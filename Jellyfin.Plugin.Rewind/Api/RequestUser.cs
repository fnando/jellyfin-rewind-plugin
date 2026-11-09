using System;
using System.Security.Claims;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.Rewind.Api;

/// <summary>
/// Works out whose history a request may read. A user token reads its own user's history; an
/// administrator (or a server API key, which carries no user) may name any user with
/// <c>userId</c>.
/// </summary>
public static class RequestUser
{
    /// <summary>
    /// The claim Jellyfin's authentication handler stores the token's user id in.
    /// </summary>
    public const string UserIdClaim = "Jellyfin-UserId";

    /// <summary>
    /// The role Jellyfin grants administrators and API keys.
    /// </summary>
    public const string AdministratorRole = "Administrator";

    /// <summary>
    /// Resolves the target user.
    /// </summary>
    /// <param name="principal">The authenticated caller.</param>
    /// <param name="requestedUserId">The <c>userId</c> query parameter, if any.</param>
    /// <param name="userManager">The user manager.</param>
    /// <returns>The user id, or the HTTP status and message to fail with.</returns>
    public static UserResolution Resolve(ClaimsPrincipal principal, string? requestedUserId, IUserManager userManager)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(userManager);

        var own = Guid.TryParse(principal.FindFirst(UserIdClaim)?.Value, out var parsed) ? parsed : Guid.Empty;

        if (string.IsNullOrWhiteSpace(requestedUserId))
        {
            return own == Guid.Empty
                ? UserResolution.Fail(StatusCodes.Status400BadRequest, "userId is required when authenticating with an API key.")
                : UserResolution.For(own);
        }

        if (!Guid.TryParse(requestedUserId, out var requested) || requested == Guid.Empty)
        {
            return UserResolution.Fail(StatusCodes.Status400BadRequest, "userId must be a user id.");
        }

        if (requested != own && !principal.IsInRole(AdministratorRole))
        {
            return UserResolution.Fail(StatusCodes.Status403Forbidden, "Only administrators can read another user's history.");
        }

        if (userManager.GetUserById(requested) is null)
        {
            return UserResolution.Fail(StatusCodes.Status404NotFound, "User not found.");
        }

        return UserResolution.For(requested);
    }
}
