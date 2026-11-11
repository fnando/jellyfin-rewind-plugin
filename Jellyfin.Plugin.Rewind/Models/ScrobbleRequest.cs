using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Rewind.Models;

/// <summary>
/// The body of <c>POST /Rewind/Scrobbles</c>: plays a client reports after the fact.
/// </summary>
public sealed class ScrobbleRequest
{
    /// <summary>
    /// Gets or sets the plays, oldest first preferably.
    /// </summary>
    [JsonPropertyName("plays")]
    public IReadOnlyList<ScrobbleRequestPlay>? Plays { get; set; }
}
