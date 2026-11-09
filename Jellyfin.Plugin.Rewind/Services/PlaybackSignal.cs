using System;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Rewind.Services;

/// <summary>
/// One playback event for one user, stripped of the server's event types.
/// </summary>
public sealed record PlaybackSignal
{
    /// <summary>
    /// Gets the user id.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Gets the item id.
    /// </summary>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// Gets the item, when the event carried it.
    /// </summary>
    public BaseItem? Item { get; init; }

    /// <summary>
    /// Gets Jellyfin's play session id.
    /// </summary>
    public string? PlaySessionId { get; init; }

    /// <summary>
    /// Gets the server session id (the connection), used when there is no play session id.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the reported position.
    /// </summary>
    public long? PositionTicks { get; init; }

    /// <summary>
    /// Gets a value indicating whether playback is paused.
    /// </summary>
    public bool IsPaused { get; init; }

    /// <summary>
    /// Gets a value indicating whether the server considers the item played to completion (stop events only).
    /// </summary>
    public bool PlayedToCompletion { get; init; }

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

    /// <summary>
    /// Gets the play method.
    /// </summary>
    public string? PlayMethod { get; init; }

    /// <summary>
    /// Gets the key that ties this event to a record.
    /// </summary>
    public string SessionKey => string.IsNullOrEmpty(PlaySessionId)
        ? FormattableString.Invariant($"{UserId:N}:{ItemId:N}:device:{DeviceId ?? SessionId ?? "unknown"}")
        : FormattableString.Invariant($"{UserId:N}:{ItemId:N}:{PlaySessionId}");
}
