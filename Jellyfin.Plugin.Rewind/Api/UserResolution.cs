using System;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.Rewind.Api;

/// <summary>
/// The outcome of <see cref="RequestUser.Resolve"/>.
/// </summary>
/// <param name="UserId">The user id, on success.</param>
/// <param name="StatusCode">The failure status, on failure.</param>
/// <param name="Error">The failure message, on failure.</param>
public sealed record UserResolution(Guid UserId, int StatusCode, string? Error)
{
    /// <summary>
    /// Gets a value indicating whether the user was resolved.
    /// </summary>
    public bool Succeeded => Error is null;

    internal static UserResolution For(Guid userId) => new(userId, StatusCodes.Status200OK, null);

    internal static UserResolution Fail(int statusCode, string error) => new(Guid.Empty, statusCode, error);
}
