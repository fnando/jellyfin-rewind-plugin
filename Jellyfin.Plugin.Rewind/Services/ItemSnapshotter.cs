using System;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Rewind.Data;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Rewind.Services;

/// <summary>
/// Copies what a history entry needs to know about an item onto the record, so the entry stays
/// meaningful after the item is renamed or removed from the library.
/// </summary>
public class ItemSnapshotter
{
    /// <summary>
    /// The item type stored when the server no longer knows the item.
    /// </summary>
    public const string UnknownType = "Unknown";

    /// <summary>
    /// The snapshot version new records get. Version 1 added the disc number and genres;
    /// version 2 takes a track's genres from its album (and an episode's from its series) when
    /// the item has none of its own, which is how Jellyfin usually stores them.
    /// </summary>
    public const int CurrentVersion = 2;

    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<ItemSnapshotter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemSnapshotter"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="logger">The logger.</param>
    public ItemSnapshotter(ILibraryManager libraryManager, ILogger<ItemSnapshotter> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Fills the item fields of a record.
    /// </summary>
    /// <param name="play">The record.</param>
    /// <param name="itemId">The item id.</param>
    /// <param name="item">The item, when the server still knows it.</param>
    public void Apply(PlayRecord play, Guid itemId, BaseItem? item)
    {
        ArgumentNullException.ThrowIfNull(play);

        play.ItemId = itemId;
        item ??= _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            play.ItemType = UnknownType;
            return;
        }

        play.ItemType = item.GetBaseItemKind().ToString();
        play.MediaType = item.MediaType == MediaType.Unknown ? null : item.MediaType.ToString();
        play.Name = item.Name;
        play.RuntimeTicks = item.RunTimeTicks is > 0 ? item.RunTimeTicks : null;
        play.ProductionYear = item.ProductionYear;
        play.IndexNumber = item.IndexNumber;
        play.ProviderIds = DelimitedList.EncodePairs(item.ProviderIds);
        play.LibraryId = LibraryOf(item);

        if (item is Episode episode)
        {
            play.SeriesId = episode.SeriesId == Guid.Empty ? null : episode.SeriesId;
            play.SeriesName = episode.SeriesName;
            play.SeasonNumber = episode.ParentIndexNumber;
            play.SeriesProviderIds = DelimitedList.EncodePairs(episode.Series?.ProviderIds);
        }

        if (item is IHasArtist withArtists)
        {
            play.Artists = DelimitedList.Encode(withArtists.Artists);
        }

        if (item is IHasAlbumArtist withAlbumArtists)
        {
            play.AlbumArtists = DelimitedList.Encode(withAlbumArtists.AlbumArtists);
        }

        if (item is Audio audio)
        {
            play.Album = audio.Album;
            var albumId = audio.AlbumEntity?.Id;
            play.AlbumId = albumId is { } id && id != Guid.Empty ? id : null;
        }
        else if (item is MusicVideo musicVideo)
        {
            play.Album = musicVideo.Album;
        }

        ApplyLaterFields(play, item);
        play.SnapshotVersion = CurrentVersion;
    }

    /// <summary>
    /// Brings an older record up to <see cref="CurrentVersion"/> by filling the fields added since
    /// it was recorded. Fields it already has are left alone: they describe the item as it was played.
    /// </summary>
    /// <param name="play">The record.</param>
    /// <returns><c>true</c> when a field was filled in.</returns>
    public bool Upgrade(PlayRecord play)
    {
        ArgumentNullException.ThrowIfNull(play);

        if (play.SnapshotVersion >= CurrentVersion)
        {
            return false;
        }

        play.SnapshotVersion = CurrentVersion;
        var item = play.ItemId == Guid.Empty ? null : _libraryManager.GetItemById(play.ItemId);
        if (item is null)
        {
            return false;
        }

        var before = (play.DiscNumber, play.Genres);
        ApplyLaterFields(play, item);
        return before != (play.DiscNumber, play.Genres);
    }

    // Version 1: disc number (tracks keep it where episodes keep the season) and genres.
    // Version 2: genres fall back to the album's or the series'.
    private static void ApplyLaterFields(PlayRecord play, BaseItem item)
    {
        if (item is Audio)
        {
            play.DiscNumber = item.ParentIndexNumber;
        }

        var genres = item.Genres;
        if (genres is null || genres.Length == 0)
        {
            genres = item switch
            {
                Audio audio => audio.AlbumEntity?.Genres,
                Episode episode => episode.Series?.Genres,
                _ => null,
            };
        }

        play.Genres = DelimitedList.Encode(genres) ?? play.Genres;
    }

    private Guid? LibraryOf(BaseItem item)
    {
        try
        {
            return _libraryManager.GetCollectionFolders(item)?.FirstOrDefault()?.Id;
        }
        catch (Exception ex)
        {
            // The library is optional context; never lose a play over it.
            _logger.LogDebug(ex, "Rewind: unable to resolve the library of {ItemId}.", item.Id);
            return null;
        }
    }
}
