using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Rewind.Models;

/// <summary>
/// What became of one reported play.
/// </summary>
/// <param name="Index">The play's position in the request.</param>
/// <param name="Status"><c>created</c>, <c>duplicate</c> (already recorded) or <c>rejected</c>.</param>
/// <param name="Id">The new or existing play id, unless rejected.</param>
/// <param name="Scrobbled">Whether that play counts as a scrobble, unless rejected.</param>
/// <param name="Error">Why the play was rejected.</param>
public sealed record ScrobbleResult(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("id")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id = null,
    [property: JsonPropertyName("scrobbled")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Scrobbled = null,
    [property: JsonPropertyName("error")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error = null);
