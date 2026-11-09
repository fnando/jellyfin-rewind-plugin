using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Rewind.Models;

/// <summary>
/// A page of history.
/// </summary>
/// <param name="Items">The plays.</param>
/// <param name="NextCursor">The cursor for the next page; absent on the last page.</param>
public sealed record HistoryPage(
    [property: JsonPropertyName("items")] IReadOnlyList<HistoryEntry> Items,
    [property: JsonPropertyName("nextCursor"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor);
