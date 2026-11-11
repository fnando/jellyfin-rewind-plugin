using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Rewind.Models;

/// <summary>
/// The response of <c>POST /Rewind/Scrobbles</c>.
/// </summary>
/// <param name="Results">One result per reported play, in request order.</param>
public sealed record ScrobbleResponse([property: JsonPropertyName("results")] IReadOnlyList<ScrobbleResult> Results);
