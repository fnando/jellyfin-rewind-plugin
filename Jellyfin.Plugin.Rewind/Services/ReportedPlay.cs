using System;

namespace Jellyfin.Plugin.Rewind.Services;

/// <summary>
/// A play a client reports after the fact, such as one buffered while offline.
/// </summary>
public sealed record ReportedPlay
{
    /// <summary>
    /// Gets when playback started (UTC).
    /// </summary>
    public required DateTime StartedAt { get; init; }

    /// <summary>
    /// Gets when playback ended (UTC), if the client knows it.
    /// </summary>
    public DateTime? EndedAt { get; init; }

    /// <summary>
    /// Gets the time actually played, if the client measured it.
    /// </summary>
    public long? PlayedTicks { get; init; }

    /// <summary>
    /// Gets the furthest position reached, if the client knows it.
    /// </summary>
    public long? PositionTicks { get; init; }

    /// <summary>
    /// Gets the client name.
    /// </summary>
    public string? Client { get; init; }

    /// <summary>
    /// Gets the device name.
    /// </summary>
    public string? DeviceName { get; init; }

    /// <summary>
    /// Gets the device id.
    /// </summary>
    public string? DeviceId { get; init; }
}
