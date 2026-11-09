using System;

namespace Jellyfin.Plugin.Rewind.Services;

/// <summary>
/// How much history one user has.
/// </summary>
/// <param name="UserId">The user id.</param>
/// <param name="Plays">The number of recorded plays (deleted ones excluded).</param>
/// <param name="Scrobbles">How many of them count as scrobbles.</param>
/// <param name="LastPlayedAt">When the most recent play started.</param>
public sealed record UserSummary(Guid UserId, int Plays, int Scrobbles, DateTime? LastPlayedAt);
