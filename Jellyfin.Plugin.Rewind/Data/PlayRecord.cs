using System;

namespace Jellyfin.Plugin.Rewind.Data;

/// <summary>
/// One playback session of one item by one user: the unit of history. The item fields are a
/// snapshot taken when the play started, so the history outlives renames and library removals.
/// </summary>
public class PlayRecord
{
    /// <summary>
    /// Gets or sets the record id: a version 7 GUID in <c>N</c> format, so ids sort by creation time.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user id.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the item id.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the key that ties the server's playback events to this record: user, item
    /// and Jellyfin's play session id (or the device when a client sends none).
    /// </summary>
    public string SessionKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets Jellyfin's play session id, when the client sent one.
    /// </summary>
    public string? PlaySessionId { get; set; }

    /// <summary>
    /// Gets or sets when playback started (UTC).
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// Gets or sets when the last playback event arrived (UTC).
    /// </summary>
    public DateTime LastSeenAt { get; set; }

    /// <summary>
    /// Gets or sets when playback ended (UTC); <c>null</c> while active.
    /// </summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Gets or sets the status.
    /// </summary>
    public PlayStatus Status { get; set; }

    /// <summary>
    /// Gets or sets where the record came from.
    /// </summary>
    public PlaySource Source { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether playback was paused at the last report. Persisted
    /// so a play picked up again after a server restart knows whether time kept running.
    /// </summary>
    public bool IsPaused { get; set; }

    /// <summary>
    /// Gets or sets the last reported position.
    /// </summary>
    public long PositionTicks { get; set; }

    /// <summary>
    /// Gets or sets the furthest position reached.
    /// </summary>
    public long MaxPositionTicks { get; set; }

    /// <summary>
    /// Gets or sets the item's runtime, when known.
    /// </summary>
    public long? RuntimeTicks { get; set; }

    /// <summary>
    /// Gets or sets the time actually played, seeks excluded.
    /// </summary>
    public long PlayedTicks { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the server reported the item as played to completion.
    /// </summary>
    public bool PlayedToCompletion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the play counts as a scrobble.
    /// </summary>
    public bool Scrobbled { get; set; }

    /// <summary>
    /// Gets or sets the client name.
    /// </summary>
    public string? Client { get; set; }

    /// <summary>
    /// Gets or sets the device name.
    /// </summary>
    public string? DeviceName { get; set; }

    /// <summary>
    /// Gets or sets the device id.
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// Gets or sets the play method (direct play, direct stream, transcode).
    /// </summary>
    public string? PlayMethod { get; set; }

    /// <summary>
    /// Gets or sets the item kind (a <c>BaseItemKind</c> name).
    /// </summary>
    public string ItemType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the media type (<c>Audio</c>, <c>Video</c>, ...).
    /// </summary>
    public string? MediaType { get; set; }

    /// <summary>
    /// Gets or sets the item name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the library (collection folder) id.
    /// </summary>
    public Guid? LibraryId { get; set; }

    /// <summary>
    /// Gets or sets the series id, for episodes.
    /// </summary>
    public Guid? SeriesId { get; set; }

    /// <summary>
    /// Gets or sets the series name, for episodes.
    /// </summary>
    public string? SeriesName { get; set; }

    /// <summary>
    /// Gets or sets the season number, for episodes.
    /// </summary>
    public int? SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode (or track) number.
    /// </summary>
    public int? IndexNumber { get; set; }

    /// <summary>
    /// Gets or sets the disc number, for tracks.
    /// </summary>
    public int? DiscNumber { get; set; }

    /// <summary>
    /// Gets or sets the album id, for tracks.
    /// </summary>
    public Guid? AlbumId { get; set; }

    /// <summary>
    /// Gets or sets the album name.
    /// </summary>
    public string? Album { get; set; }

    /// <summary>
    /// Gets or sets the album artists, encoded with <see cref="DelimitedList"/>.
    /// </summary>
    public string? AlbumArtists { get; set; }

    /// <summary>
    /// Gets or sets the artists, encoded with <see cref="DelimitedList"/>.
    /// </summary>
    public string? Artists { get; set; }

    /// <summary>
    /// Gets or sets the production year.
    /// </summary>
    public int? ProductionYear { get; set; }

    /// <summary>
    /// Gets or sets the genres, encoded with <see cref="DelimitedList"/>.
    /// </summary>
    public string? Genres { get; set; }

    /// <summary>
    /// Gets or sets which version of the item snapshot the record carries. Records below
    /// <c>ItemSnapshotter.CurrentVersion</c> are missing fields added later; the backfill task
    /// fills them in from the library.
    /// </summary>
    public int SnapshotVersion { get; set; }

    /// <summary>
    /// Gets or sets the item's provider ids, encoded with <see cref="DelimitedList"/> as <c>key=value</c>.
    /// </summary>
    public string? ProviderIds { get; set; }

    /// <summary>
    /// Gets or sets the series' provider ids, for episodes.
    /// </summary>
    public string? SeriesProviderIds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the record was deleted. Deleted records stay as
    /// tombstones so the changes feed can report the removal.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Gets or sets when the record last changed (UTC).
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the change sequence: a server-wide, strictly increasing number assigned on
    /// every meaningful change (creation, status, scrobble, deletion).
    /// </summary>
    public long Seq { get; set; }
}
