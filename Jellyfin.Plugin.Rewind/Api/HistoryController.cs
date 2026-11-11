using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Rewind.Data;
using Jellyfin.Plugin.Rewind.Models;
using Jellyfin.Plugin.Rewind.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Rewind.Api;

/// <summary>
/// The play history of the authenticated user (or, for administrators, of any user).
/// </summary>
[ApiController]
[Authorize]
[Route("Rewind")]
[Produces(MediaTypeNames.Application.Json)]
public class HistoryController : ControllerBase
{
    /// <summary>
    /// The default page size.
    /// </summary>
    public const int DefaultLimit = 100;

    /// <summary>
    /// The largest page size.
    /// </summary>
    public const int MaxLimit = 1000;

    private readonly IHistoryStore _store;
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="HistoryController"/> class.
    /// </summary>
    /// <param name="store">The history store.</param>
    /// <param name="userManager">The user manager.</param>
    public HistoryController(IHistoryStore store, IUserManager userManager)
    {
        _store = store;
        _userManager = userManager;
    }

    /// <summary>
    /// Lists plays, newest first by default. Every filter is optional. List filters take
    /// comma-separated values (or a repeated parameter) and match any of them. Different
    /// filters must all match.
    /// </summary>
    /// <param name="userId">Whose history to read (administrators only; defaults to the caller).</param>
    /// <param name="types">Item kinds, e.g. <c>Movie,Episode,Audio</c>.</param>
    /// <param name="mediaTypes">Media types: <c>Video</c>, <c>Audio</c>, <c>Photo</c>, <c>Book</c>.</param>
    /// <param name="itemIds">Item ids.</param>
    /// <param name="seriesId">A series id.</param>
    /// <param name="albumId">An album id.</param>
    /// <param name="libraryId">A library id.</param>
    /// <param name="artist">An artist or album artist name (exact, case-insensitive).</param>
    /// <param name="genres">Genres (exact, case-insensitive).</param>
    /// <param name="providerId">A provider id as <c>provider:value</c>, e.g. <c>imdb:tt0133093</c>; also matches an episode's series.</param>
    /// <param name="startedAfter">Plays started at or after this ISO 8601 time.</param>
    /// <param name="startedBefore">Plays started before this ISO 8601 time.</param>
    /// <param name="scrobbled"><c>true</c> (default), <c>false</c> or <c>any</c>.</param>
    /// <param name="status">Statuses: <c>active</c>, <c>completed</c>, <c>abandoned</c>.</param>
    /// <param name="source">Sources: <c>playback</c>, <c>manual</c>, <c>scrobble</c>.</param>
    /// <param name="client">A client name.</param>
    /// <param name="deviceId">A device id.</param>
    /// <param name="order"><c>desc</c> (default) or <c>asc</c>, by start time.</param>
    /// <param name="limit">The page size (1..1000, default 100).</param>
    /// <param name="cursor">The <c>nextCursor</c> of the previous page.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A page of plays.</returns>
    [HttpGet("History")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HistoryPage>> GetHistory(
        [FromQuery] string? userId,
        [FromQuery] string[]? types,
        [FromQuery] string[]? mediaTypes,
        [FromQuery] string[]? itemIds,
        [FromQuery] string? seriesId,
        [FromQuery] string? albumId,
        [FromQuery] string? libraryId,
        [FromQuery] string? artist,
        [FromQuery] string[]? genres,
        [FromQuery] string? providerId,
        [FromQuery] string? startedAfter,
        [FromQuery] string? startedBefore,
        [FromQuery] string? scrobbled,
        [FromQuery] string[]? status,
        [FromQuery] string[]? source,
        [FromQuery] string? client,
        [FromQuery] string? deviceId,
        [FromQuery] string? order,
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        var descending = order?.Trim().ToUpperInvariant() switch
        {
            null or "" or "DESC" => true,
            "ASC" => false,
            _ => Error(errors, "order must be asc or desc.", true),
        };

        HistoryCursor? after = null;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            if (!HistoryCursor.TryParse(cursor, out after) || after!.Descending != descending)
            {
                errors.Add("cursor is invalid for this order.");
            }
        }

        bool? scrobbledFilter = scrobbled?.Trim().ToUpperInvariant() switch
        {
            null or "" or "TRUE" => true,
            "FALSE" => false,
            "ANY" => null,
            _ => Error<bool?>(errors, "scrobbled must be true, false or any.", null),
        };

        var query = new HistoryQuery
        {
            UserId = Guid.Empty,
            Types = ParseList(types, "types", errors, ParseKind),
            MediaTypes = ParseList(mediaTypes, "mediaTypes", errors, ParseMediaType),
            ItemIds = ParseList(itemIds, "itemIds", errors, ParseGuid),
            SeriesId = ParseOptionalGuid(seriesId, "seriesId", errors),
            AlbumId = ParseOptionalGuid(albumId, "albumId", errors),
            LibraryId = ParseOptionalGuid(libraryId, "libraryId", errors),
            Artist = string.IsNullOrWhiteSpace(artist) ? null : artist.Trim(),
            Genres = ParseList(genres, "genres", errors, value => (string?)value),
            ProviderId = ParseProviderId(providerId, errors),
            StartedAfter = ParseTime(startedAfter, "startedAfter", errors),
            StartedBefore = ParseTime(startedBefore, "startedBefore", errors),
            Scrobbled = scrobbledFilter,
            Statuses = ParseList(status, "status", errors, ParseStatus),
            Sources = ParseList(source, "source", errors, ParseSource),
            Client = string.IsNullOrWhiteSpace(client) ? null : client.Trim(),
            DeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim(),
            Descending = descending,
            After = after,
            Limit = ParseLimit(limit, errors),
        };

        if (errors.Count > 0)
        {
            return BadRequest(new ErrorResponse(string.Join(" ", errors)));
        }

        var user = RequestUser.Resolve(User, userId, _userManager);
        if (!user.Succeeded)
        {
            return Fail(user);
        }

        query = query with { UserId = user.UserId };
        var (items, hasMore) = await _store.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        var next = hasMore && items.Count > 0
            ? new HistoryCursor(descending, items[^1].StartedAt.Ticks, items[^1].Id).Encode()
            : null;

        return new HistoryPage(items.Select(HistoryEntry.From).ToList(), next);
    }

    /// <summary>
    /// The incremental sync feed: every play of the user that was created or changed after
    /// <paramref name="since"/>, in change order, including deletions (<c>deleted: true</c>) and
    /// plays still in progress. Store <c>nextSince</c> and send it on the next call; nothing is
    /// ever skipped.
    /// </summary>
    /// <param name="userId">Whose history to read (administrators only; defaults to the caller).</param>
    /// <param name="since">The last <c>nextSince</c> received (0 for a full sync).</param>
    /// <param name="types">Item kinds, e.g. <c>Movie,Episode</c>.</param>
    /// <param name="limit">The page size (1..1000, default 100).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A page of changes.</returns>
    [HttpGet("History/Changes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChangesPage>> GetChanges(
        [FromQuery] string? userId,
        [FromQuery] long? since,
        [FromQuery] string[]? types,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (since is < 0)
        {
            errors.Add("since must not be negative.");
        }

        var kinds = ParseList(types, "types", errors, ParseKind);
        var size = ParseLimit(limit, errors);
        if (errors.Count > 0)
        {
            return BadRequest(new ErrorResponse(string.Join(" ", errors)));
        }

        var user = RequestUser.Resolve(User, userId, _userManager);
        if (!user.Succeeded)
        {
            return Fail(user);
        }

        var from = since ?? 0;
        var (items, hasMore) = await _store.ChangesAsync(user.UserId, from, size, kinds, cancellationToken).ConfigureAwait(false);
        return new ChangesPage(items.Select(HistoryEntry.From).ToList(), items.Count > 0 ? items[^1].Seq : from, hasMore);
    }

    /// <summary>
    /// Gets one play.
    /// </summary>
    /// <param name="id">The play id.</param>
    /// <param name="userId">Whose history to read (administrators only; defaults to the caller).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The play.</returns>
    [HttpGet("History/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HistoryEntry>> GetEntry([FromRoute] string id, [FromQuery] string? userId, CancellationToken cancellationToken)
    {
        var user = RequestUser.Resolve(User, userId, _userManager);
        if (!user.Succeeded)
        {
            return Fail(user);
        }

        var play = NormalizeId(id) is { } normalized
            ? await _store.GetAsync(user.UserId, normalized, cancellationToken).ConfigureAwait(false)
            : null;
        return play is null ? NotFound(new ErrorResponse("Play not found.")) : HistoryEntry.From(play);
    }

    /// <summary>
    /// Deletes one play. It disappears from the history and shows up in the changes feed as
    /// deleted.
    /// </summary>
    /// <param name="id">The play id.</param>
    /// <param name="userId">Whose history to change (administrators only; defaults to the caller).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>No content.</returns>
    [HttpDelete("History/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteEntry([FromRoute] string id, [FromQuery] string? userId, CancellationToken cancellationToken)
    {
        var user = RequestUser.Resolve(User, userId, _userManager);
        if (!user.Succeeded)
        {
            return Fail(user);
        }

        var deleted = NormalizeId(id) is { } normalized
            && await _store.DeleteAsync(user.UserId, normalized, cancellationToken).ConfigureAwait(false);
        return deleted ? NoContent() : NotFound(new ErrorResponse("Play not found."));
    }

    private static string? NormalizeId(string id) => Guid.TryParse(id, out var guid) ? guid.ToString("N") : null;

    private static T Error<T>(List<string> errors, string message, T fallback)
    {
        errors.Add(message);
        return fallback;
    }

    private static int ParseLimit(int? limit, List<string> errors)
    {
        if (limit is { } value && (value < 1 || value > MaxLimit))
        {
            errors.Add(string.Format(CultureInfo.InvariantCulture, "limit must be between 1 and {0}.", MaxLimit));
        }

        return limit ?? DefaultLimit;
    }

    private static List<T>? ParseList<T>(string[]? raw, string name, List<string> errors, Func<string, T?> parse)
        where T : struct
    {
        var values = raw?
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
        if (values is null || values.Count == 0)
        {
            return null;
        }

        var parsed = new List<T>();
        foreach (var value in values)
        {
            if (parse(value) is { } item)
            {
                parsed.Add(item);
            }
            else
            {
                errors.Add(string.Format(CultureInfo.InvariantCulture, "{0} has an unknown value: {1}.", name, value));
            }
        }

        return parsed.Distinct().ToList();
    }

    private static List<string>? ParseList(string[]? raw, string name, List<string> errors, Func<string, string?> parse)
    {
        var values = raw?
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
        if (values is null || values.Count == 0)
        {
            return null;
        }

        var parsed = new List<string>();
        foreach (var value in values)
        {
            if (parse(value) is { } item)
            {
                parsed.Add(item);
            }
            else
            {
                errors.Add(string.Format(CultureInfo.InvariantCulture, "{0} has an unknown value: {1}.", name, value));
            }
        }

        return parsed.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string? ParseKind(string value)
        => Enum.TryParse<BaseItemKind>(value, ignoreCase: true, out var kind) && !int.TryParse(value, out _) ? kind.ToString() : null;

    private static string? ParseMediaType(string value)
        => Enum.TryParse<MediaType>(value, ignoreCase: true, out var type) && type != MediaType.Unknown && !int.TryParse(value, out _) ? type.ToString() : null;

    private static Guid? ParseGuid(string value) => Guid.TryParse(value, out var guid) ? guid : null;

    private static PlayStatus? ParseStatus(string value) => value.ToUpperInvariant() switch
    {
        "ACTIVE" => PlayStatus.Active,
        "COMPLETED" => PlayStatus.Completed,
        "ABANDONED" => PlayStatus.Abandoned,
        _ => null,
    };

    private static PlaySource? ParseSource(string value) => value.ToUpperInvariant() switch
    {
        "PLAYBACK" => PlaySource.Playback,
        "MANUAL" => PlaySource.Manual,
        "SCROBBLE" => PlaySource.Scrobble,
        _ => null,
    };

    private static Guid? ParseOptionalGuid(string? value, string name, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Guid.TryParse(value, out var guid))
        {
            return guid;
        }

        errors.Add(string.Format(CultureInfo.InvariantCulture, "{0} must be an id.", name));
        return null;
    }

    private static string? ParseProviderId(string? value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var separator = value.IndexOfAny([':', '=']);
        if (separator <= 0 || separator == value.Length - 1)
        {
            errors.Add("providerId must look like provider:value, e.g. imdb:tt0133093.");
            return null;
        }

        return DelimitedList.Pair(value[..separator].Trim(), value[(separator + 1)..].Trim());
    }

    private static DateTime? ParseTime(string? value, string name, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (IsoTime.TryParse(value, out var time))
        {
            return time;
        }

        errors.Add(string.Format(CultureInfo.InvariantCulture, "{0} must be an ISO 8601 time.", name));
        return null;
    }

    private ObjectResult Fail(UserResolution resolution)
        => StatusCode(resolution.StatusCode, new ErrorResponse(resolution.Error!));
}
