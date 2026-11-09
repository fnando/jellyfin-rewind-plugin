using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Rewind.Models;

/// <summary>
/// A page of the changes feed.
/// </summary>
/// <param name="Items">The changed plays in sequence order; deleted ones have <c>deleted: true</c>.</param>
/// <param name="NextSince">The <c>since</c> to send next time (the last sequence seen).</param>
/// <param name="HasMore">Whether more changes are waiting right now.</param>
public sealed record ChangesPage(
    [property: JsonPropertyName("items")] IReadOnlyList<HistoryEntry> Items,
    [property: JsonPropertyName("nextSince")] long NextSince,
    [property: JsonPropertyName("hasMore")] bool HasMore);
