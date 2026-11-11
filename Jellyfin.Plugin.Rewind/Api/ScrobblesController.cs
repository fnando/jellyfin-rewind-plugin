using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Rewind.Models;
using Jellyfin.Plugin.Rewind.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Rewind.Api;

/// <summary>
/// Plays that clients report after the fact, such as those played offline and buffered until
/// the client is back online. They are recorded with the <c>scrobble</c> source. Only a user's
/// access token may report, and only for that user: the plays come from a real player.
/// </summary>
[ApiController]
[Authorize]
[Route("Rewind")]
[Produces(MediaTypeNames.Application.Json)]
public class ScrobblesController : ControllerBase
{
    /// <summary>
    /// The most plays one request may report.
    /// </summary>
    public const int MaxPlays = 500;

    /// <summary>
    /// How far in the future a reported start may be, to allow for clock skew.
    /// </summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    // The claims Jellyfin's authentication handler stores the caller's client and device in.
    private const string ClientClaim = "Jellyfin-Client";
    private const string DeviceClaim = "Jellyfin-Device";
    private const string DeviceIdClaim = "Jellyfin-DeviceId";

    private readonly PlaybackTracker _tracker;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly TimeProvider _time;
    private readonly ILogger<ScrobblesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrobblesController"/> class.
    /// </summary>
    /// <param name="tracker">The tracker.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public ScrobblesController(
        PlaybackTracker tracker,
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        TimeProvider time,
        ILogger<ScrobblesController> logger)
    {
        _tracker = tracker;
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Records plays a client reports after the fact. Each play is handled on its own: a bad one
    /// is rejected without failing the others. Reporting a play again (same item and start
    /// second) returns the existing record, so a client can safely retry; a play the server
    /// already recorded as a scrobble from its own playback events is not recorded twice. The
    /// plays go to the user the access token belongs to; API keys are refused. Only items the user
    /// can access are accepted. A new play that counts as a scrobble also updates Jellyfin's own
    /// played state of the item (played, play count, last played).
    /// </summary>
    /// <param name="request">The plays.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One result per play, in request order.</returns>
    [HttpPost("Scrobbles")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ScrobbleResponse>> PostScrobbles(
        [FromBody] ScrobbleRequest? request,
        CancellationToken cancellationToken)
    {
        // An API key carries no user, and its client and device are the server's own.
        if (string.Equals(User.FindFirst(RequestUser.IsApiKeyClaim)?.Value, "true", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(User.FindFirst(RequestUser.UserIdClaim)?.Value, out var userId)
            || userId == Guid.Empty
            || _userManager.GetUserById(userId) is not { } user)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse("Plays must be reported with a user's access token, not an API key."));
        }

        if (request?.Plays is not { Count: > 0 } plays)
        {
            return BadRequest(new ErrorResponse("plays must list at least one play."));
        }

        if (plays.Count > MaxPlays)
        {
            return BadRequest(new ErrorResponse(string.Format(CultureInfo.InvariantCulture, "plays must list at most {0} plays.", MaxPlays)));
        }

        var latestStart = _time.GetUtcNow().UtcDateTime + MaxClockSkew;
        var results = new List<ScrobbleResult>(plays.Count);
        for (var index = 0; index < plays.Count; index++)
        {
            results.Add(await ReportAsync(index, plays[index], userId, user, latestStart, cancellationToken).ConfigureAwait(false));
        }

        return new ScrobbleResponse(results);
    }

    private static ScrobbleResult Reject(int index, string error) => new(index, "rejected", Error: error);

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<ScrobbleResult> ReportAsync(int index, ScrobbleRequestPlay? entry, Guid userId, User user, DateTime latestStart, CancellationToken cancellationToken)
    {
        if (entry is null)
        {
            return Reject(index, "The play is empty.");
        }

        if (!Guid.TryParse(entry.ItemId, out var itemId) || itemId == Guid.Empty)
        {
            return Reject(index, "itemId must be an id.");
        }

        if (!IsoTime.TryParse(entry.StartedAt, out var startedAt))
        {
            return Reject(index, "startedAt must be an ISO 8601 time.");
        }

        if (startedAt > latestStart)
        {
            return Reject(index, "startedAt is in the future.");
        }

        DateTime? endedAt = null;
        if (!string.IsNullOrWhiteSpace(entry.EndedAt))
        {
            if (!IsoTime.TryParse(entry.EndedAt, out var ended))
            {
                return Reject(index, "endedAt must be an ISO 8601 time.");
            }

            if (ended < startedAt)
            {
                return Reject(index, "endedAt must not be before startedAt.");
            }

            endedAt = ended;
        }

        if (entry.PlayedTicks is < 0 || entry.PositionTicks is < 0)
        {
            return Reject(index, "playedTicks and positionTicks must not be negative.");
        }

        // Items outside the user's libraries are "not found" too: the play would reveal them.
        var item = _libraryManager.GetItemById<BaseItem>(itemId, user);
        if (item is null)
        {
            return Reject(index, "Item not found.");
        }

        if (item.IsFolder)
        {
            return Reject(index, "The item is a folder, not something that plays.");
        }

        var report = new ReportedPlay
        {
            StartedAt = startedAt,
            EndedAt = endedAt,
            PlayedTicks = entry.PlayedTicks,
            PositionTicks = entry.PositionTicks,
            Client = Trimmed(entry.Client) ?? Trimmed(User.FindFirst(ClientClaim)?.Value),
            DeviceName = Trimmed(entry.DeviceName) ?? Trimmed(User.FindFirst(DeviceClaim)?.Value),
            DeviceId = Trimmed(entry.DeviceId) ?? Trimmed(User.FindFirst(DeviceIdClaim)?.Value),
        };

        var result = await _tracker.ReportAsync(userId, item, report, cancellationToken).ConfigureAwait(false);
        if (result.Outcome == ReportOutcome.Created && result.Play.Scrobbled)
        {
            MarkPlayed(user, item, result.Play.EndedAt ?? result.Play.StartedAt);
        }

        var status = result.Outcome == ReportOutcome.Created ? "created" : "duplicate";
        return new ScrobbleResult(index, status, result.Play.Id, result.Play.Scrobbled);
    }

    // Brings Jellyfin's played state in line with the reported play, as if the server had seen it.
    // A play older than the last one Jellyfin knows of counts, but does not move the last played
    // date back or clear the resume position of a later play. Saved as PlaybackFinished, which the
    // recorder ignores: only TogglePlayed is taken as a play of its own.
    private void MarkPlayed(User user, BaseItem item, DateTime playedAt)
    {
        try
        {
            var data = _userDataManager.GetUserData(user, item);
            if (data is null)
            {
                return;
            }

            data.Played = true;
            data.PlayCount++;
            if (data.LastPlayedDate is not { } last || playedAt > last)
            {
                data.LastPlayedDate = playedAt;
                data.PlaybackPositionTicks = 0;
            }

            _userDataManager.SaveUserData(user, item, data, UserDataSaveReason.PlaybackFinished, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The play is recorded; only Jellyfin's played state is behind.
            _logger.LogWarning(ex, "Rewind: unable to update the played state of {ItemId} after a reported play.", item.Id);
        }
    }
}
