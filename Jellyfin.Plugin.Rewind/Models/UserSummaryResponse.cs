using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Rewind.Models;

/// <summary>
/// How much history one user has.
/// </summary>
/// <param name="UserId">The user id.</param>
/// <param name="UserName">The user name, when the user still exists.</param>
/// <param name="Plays">The recorded plays.</param>
/// <param name="Scrobbles">The plays that count as scrobbles.</param>
/// <param name="LastPlayedAt">When the most recent play started.</param>
public sealed record UserSummaryResponse(
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("userName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UserName,
    [property: JsonPropertyName("plays")] int Plays,
    [property: JsonPropertyName("scrobbles")] int Scrobbles,
    [property: JsonPropertyName("lastPlayedAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? LastPlayedAt);
