using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Rewind.Models;
using Jellyfin.Plugin.Rewind.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Rewind.Api;

/// <summary>
/// Administrator views of the history.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("Rewind/Admin")]
[Produces(MediaTypeNames.Application.Json)]
public class AdminController : ControllerBase
{
    private readonly IHistoryStore _store;
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminController"/> class.
    /// </summary>
    /// <param name="store">The history store.</param>
    /// <param name="userManager">The user manager.</param>
    public AdminController(IHistoryStore store, IUserManager userManager)
    {
        _store = store;
        _userManager = userManager;
    }

    /// <summary>
    /// Summarizes the recorded history per user.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One row per user with history, most recently active first.</returns>
    [HttpGet("Summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserSummaryResponse>>> GetSummary(CancellationToken cancellationToken)
    {
        var rows = await _store.SummaryAsync(cancellationToken).ConfigureAwait(false);
        return rows
            .Select(row => new UserSummaryResponse(row.UserId, _userManager.GetUserById(row.UserId)?.Username, row.Plays, row.Scrobbles, row.LastPlayedAt))
            .ToList();
    }
}
