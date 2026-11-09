using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Rewind.Models;

/// <summary>
/// The body of a failed request.
/// </summary>
/// <param name="Error">What went wrong.</param>
public sealed record ErrorResponse([property: JsonPropertyName("error")] string Error);
