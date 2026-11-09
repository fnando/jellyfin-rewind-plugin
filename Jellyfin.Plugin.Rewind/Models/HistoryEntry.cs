using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Rewind.Data;

namespace Jellyfin.Plugin.Rewind.Models;

/// <summary>
/// One play, as served by the API.
/// </summary>
public sealed class HistoryEntry
{
    /// <summary>
    /// Gets or sets the record id.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the change sequence.
    /// </summary>
    [JsonPropertyName("seq")]
    public long Seq { get; set; }

    /// <summary>
    /// Gets or sets the user id.
    /// </summary>
    [JsonPropertyName("userId")]
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the item id.
    /// </summary>
    [JsonPropertyName("itemId")]
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the item kind (a Jellyfin <c>BaseItemKind</c> name).
    /// </summary>
    [JsonPropertyName("itemType")]
    public string ItemType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the media type.
    /// </summary>
    [JsonPropertyName("mediaType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaType { get; set; }

    /// <summary>
    /// Gets or sets the item name, as it was when played.
    /// </summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the library id.
    /// </summary>
    [JsonPropertyName("libraryId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? LibraryId { get; set; }

    /// <summary>
    /// Gets or sets the series id.
    /// </summary>
    [JsonPropertyName("seriesId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? SeriesId { get; set; }

    /// <summary>
    /// Gets or sets the series name.
    /// </summary>
    [JsonPropertyName("seriesName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SeriesName { get; set; }

    /// <summary>
    /// Gets or sets the season number.
    /// </summary>
    [JsonPropertyName("seasonNumber")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode or track number.
    /// </summary>
    [JsonPropertyName("indexNumber")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? IndexNumber { get; set; }

    /// <summary>
    /// Gets or sets the disc number.
    /// </summary>
    [JsonPropertyName("discNumber")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DiscNumber { get; set; }

    /// <summary>
    /// Gets or sets the album id.
    /// </summary>
    [JsonPropertyName("albumId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? AlbumId { get; set; }

    /// <summary>
    /// Gets or sets the album name.
    /// </summary>
    [JsonPropertyName("album")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Album { get; set; }

    /// <summary>
    /// Gets or sets the artists.
    /// </summary>
    [JsonPropertyName("artists")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Artists { get; set; }

    /// <summary>
    /// Gets or sets the album artists.
    /// </summary>
    [JsonPropertyName("albumArtists")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AlbumArtists { get; set; }

    /// <summary>
    /// Gets or sets the production year.
    /// </summary>
    [JsonPropertyName("productionYear")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ProductionYear { get; set; }

    /// <summary>
    /// Gets or sets the genres.
    /// </summary>
    [JsonPropertyName("genres")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Genres { get; set; }

    /// <summary>
    /// Gets or sets the item's provider ids (Imdb, Tmdb, Tvdb, MusicBrainz...).
    /// </summary>
    [JsonPropertyName("providerIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? ProviderIds { get; set; }

    /// <summary>
    /// Gets or sets the series' provider ids.
    /// </summary>
    [JsonPropertyName("seriesProviderIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? SeriesProviderIds { get; set; }

    /// <summary>
    /// Gets or sets when playback started (UTC).
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// Gets or sets when the last playback event arrived (UTC).
    /// </summary>
    [JsonPropertyName("lastSeenAt")]
    public DateTime LastSeenAt { get; set; }

    /// <summary>
    /// Gets or sets when playback ended (UTC).
    /// </summary>
    [JsonPropertyName("endedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Gets or sets the status: <c>active</c>, <c>completed</c> or <c>abandoned</c>.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source: <c>playback</c> or <c>manual</c>.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the last position.
    /// </summary>
    [JsonPropertyName("positionTicks")]
    public long PositionTicks { get; set; }

    /// <summary>
    /// Gets or sets the furthest position reached.
    /// </summary>
    [JsonPropertyName("maxPositionTicks")]
    public long MaxPositionTicks { get; set; }

    /// <summary>
    /// Gets or sets the runtime.
    /// </summary>
    [JsonPropertyName("runtimeTicks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? RuntimeTicks { get; set; }

    /// <summary>
    /// Gets or sets the time actually played, seeks excluded.
    /// </summary>
    [JsonPropertyName("playedTicks")]
    public long PlayedTicks { get; set; }

    /// <summary>
    /// Gets or sets the furthest position as a percentage of the runtime.
    /// </summary>
    [JsonPropertyName("progress")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Progress { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the server reported the item played to completion.
    /// </summary>
    [JsonPropertyName("playedToCompletion")]
    public bool PlayedToCompletion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the play counts as a scrobble.
    /// </summary>
    [JsonPropertyName("scrobbled")]
    public bool Scrobbled { get; set; }

    /// <summary>
    /// Gets or sets the client name.
    /// </summary>
    [JsonPropertyName("client")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Client { get; set; }

    /// <summary>
    /// Gets or sets the device name.
    /// </summary>
    [JsonPropertyName("deviceName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeviceName { get; set; }

    /// <summary>
    /// Gets or sets the device id.
    /// </summary>
    [JsonPropertyName("deviceId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeviceId { get; set; }

    /// <summary>
    /// Gets or sets the play method.
    /// </summary>
    [JsonPropertyName("playMethod")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlayMethod { get; set; }

    /// <summary>
    /// Gets or sets <c>true</c> for a deleted record (changes feed only).
    /// </summary>
    [JsonPropertyName("deleted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Deleted { get; set; }

    /// <summary>
    /// Gets or sets when the record last changed (UTC).
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Maps a stored record.
    /// </summary>
    /// <param name="play">The record.</param>
    /// <returns>The entry.</returns>
    public static HistoryEntry From(PlayRecord play)
    {
        ArgumentNullException.ThrowIfNull(play);

        var artists = DelimitedList.Decode(play.Artists);
        var albumArtists = DelimitedList.Decode(play.AlbumArtists);
        var genres = DelimitedList.Decode(play.Genres);
        return new HistoryEntry
        {
            Id = play.Id,
            Seq = play.Seq,
            UserId = play.UserId,
            ItemId = play.ItemId,
            ItemType = play.ItemType,
            MediaType = play.MediaType,
            Name = play.Name,
            LibraryId = play.LibraryId,
            SeriesId = play.SeriesId,
            SeriesName = play.SeriesName,
            SeasonNumber = play.SeasonNumber,
            IndexNumber = play.IndexNumber,
            DiscNumber = play.DiscNumber,
            AlbumId = play.AlbumId,
            Album = play.Album,
            Artists = artists.Count == 0 ? null : artists,
            AlbumArtists = albumArtists.Count == 0 ? null : albumArtists,
            ProductionYear = play.ProductionYear,
            Genres = genres.Count == 0 ? null : genres,
            ProviderIds = DelimitedList.DecodePairs(play.ProviderIds),
            SeriesProviderIds = DelimitedList.DecodePairs(play.SeriesProviderIds),
            StartedAt = play.StartedAt,
            LastSeenAt = play.LastSeenAt,
            EndedAt = play.EndedAt,
            Status = StatusName(play.Status),
            Source = SourceName(play.Source),
            PositionTicks = play.PositionTicks,
            MaxPositionTicks = play.MaxPositionTicks,
            RuntimeTicks = play.RuntimeTicks,
            PlayedTicks = play.PlayedTicks,
            Progress = play.RuntimeTicks is > 0 and var runtime ? Math.Round(Math.Min(100, play.MaxPositionTicks * 100.0 / runtime), 2) : null,
            PlayedToCompletion = play.PlayedToCompletion,
            Scrobbled = play.Scrobbled,
            Client = play.Client,
            DeviceName = play.DeviceName,
            DeviceId = play.DeviceId,
            PlayMethod = play.PlayMethod,
            Deleted = play.IsDeleted ? true : null,
            UpdatedAt = play.UpdatedAt,
        };
    }

    /// <summary>
    /// The API name of a status.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The name.</returns>
    public static string StatusName(PlayStatus status) => status switch
    {
        PlayStatus.Active => "active",
        PlayStatus.Completed => "completed",
        _ => "abandoned",
    };

    /// <summary>
    /// The API name of a source.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <returns>The name.</returns>
    public static string SourceName(PlaySource source) => source == PlaySource.Manual ? "manual" : "playback";
}
