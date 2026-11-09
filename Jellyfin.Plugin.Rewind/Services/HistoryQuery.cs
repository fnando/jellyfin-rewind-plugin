using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Rewind.Data;

namespace Jellyfin.Plugin.Rewind.Services;

/// <summary>
/// A page request against one user's history. Every filter is optional; set filters combine
/// with AND, values inside one list filter combine with OR.
/// </summary>
public sealed record HistoryQuery
{
    /// <summary>
    /// Gets the user whose history is read.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Gets the item kinds (<c>BaseItemKind</c> names).
    /// </summary>
    public IReadOnlyCollection<string>? Types { get; init; }

    /// <summary>
    /// Gets the media types (<c>Audio</c>, <c>Video</c>, ...).
    /// </summary>
    public IReadOnlyCollection<string>? MediaTypes { get; init; }

    /// <summary>
    /// Gets the item ids.
    /// </summary>
    public IReadOnlyCollection<Guid>? ItemIds { get; init; }

    /// <summary>
    /// Gets the series id.
    /// </summary>
    public Guid? SeriesId { get; init; }

    /// <summary>
    /// Gets the album id.
    /// </summary>
    public Guid? AlbumId { get; init; }

    /// <summary>
    /// Gets the library id.
    /// </summary>
    public Guid? LibraryId { get; init; }

    /// <summary>
    /// Gets an artist or album artist name (case-insensitive, exact).
    /// </summary>
    public string? Artist { get; init; }

    /// <summary>
    /// Gets genres (case-insensitive, exact; any of them).
    /// </summary>
    public IReadOnlyCollection<string>? Genres { get; init; }

    /// <summary>
    /// Gets a provider id, as <c>provider=value</c>, matched against the item's and its series' ids.
    /// </summary>
    public string? ProviderId { get; init; }

    /// <summary>
    /// Gets the earliest start time (inclusive).
    /// </summary>
    public DateTime? StartedAfter { get; init; }

    /// <summary>
    /// Gets the latest start time (exclusive).
    /// </summary>
    public DateTime? StartedBefore { get; init; }

    /// <summary>
    /// Gets the scrobbled filter; <c>null</c> returns both.
    /// </summary>
    public bool? Scrobbled { get; init; }

    /// <summary>
    /// Gets the statuses.
    /// </summary>
    public IReadOnlyCollection<PlayStatus>? Statuses { get; init; }

    /// <summary>
    /// Gets the sources.
    /// </summary>
    public IReadOnlyCollection<PlaySource>? Sources { get; init; }

    /// <summary>
    /// Gets the client name.
    /// </summary>
    public string? Client { get; init; }

    /// <summary>
    /// Gets the device id.
    /// </summary>
    public string? DeviceId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the newest plays come first.
    /// </summary>
    public bool Descending { get; init; } = true;

    /// <summary>
    /// Gets the position to continue after.
    /// </summary>
    public HistoryCursor? After { get; init; }

    /// <summary>
    /// Gets the page size.
    /// </summary>
    public int Limit { get; init; } = 100;
}
