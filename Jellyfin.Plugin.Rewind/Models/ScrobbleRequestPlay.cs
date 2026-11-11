using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Rewind.Models;

/// <summary>
/// One reported play. Fields are read leniently, so a bad entry is rejected alone instead of
/// failing the whole batch.
/// </summary>
public sealed class ScrobbleRequestPlay
{
    /// <summary>
    /// Gets or sets the item id.
    /// </summary>
    [JsonPropertyName("itemId")]
    public string? ItemId { get; set; }

    /// <summary>
    /// Gets or sets when playback started (ISO 8601).
    /// </summary>
    [JsonPropertyName("startedAt")]
    public string? StartedAt { get; set; }

    /// <summary>
    /// Gets or sets when playback ended (ISO 8601).
    /// </summary>
    [JsonPropertyName("endedAt")]
    public string? EndedAt { get; set; }

    /// <summary>
    /// Gets or sets the time actually played, in ticks.
    /// </summary>
    [JsonPropertyName("playedTicks")]
    public long? PlayedTicks { get; set; }

    /// <summary>
    /// Gets or sets the furthest position reached, in ticks.
    /// </summary>
    [JsonPropertyName("positionTicks")]
    public long? PositionTicks { get; set; }

    /// <summary>
    /// Gets or sets the client name.
    /// </summary>
    [JsonPropertyName("client")]
    public string? Client { get; set; }

    /// <summary>
    /// Gets or sets the device name.
    /// </summary>
    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; set; }

    /// <summary>
    /// Gets or sets the device id.
    /// </summary>
    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }
}
