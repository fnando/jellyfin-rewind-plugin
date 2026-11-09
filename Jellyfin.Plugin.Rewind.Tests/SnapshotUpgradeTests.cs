using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Rewind.Data;
using Jellyfin.Plugin.Rewind.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Rewind.Tests;

public sealed class SnapshotUpgradeTests : IDisposable
{
    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task OldPlays_GetDiscNumberAndGenres_FromTheLibrary_AndAreAnnounced()
    {
        var track = new Audio { Id = Guid.NewGuid(), Name = "Is It a Lie", ParentIndexNumber = 2, Genres = ["Punk", "Rock"] };
        _h.Library.Setup(l => l.GetItemById(track.Id)).Returns(track);

        var old = await Insert(track.Id, version: 0, name: "Old name");
        var gone = await Insert(Guid.NewGuid(), version: 0);
        var current = await Insert(track.Id, version: ItemSnapshotter.CurrentVersion);
        var active = await Insert(track.Id, version: 0, status: PlayStatus.Active);

        var snapshotter = new ItemSnapshotter(_h.Library.Object, NullLogger<ItemSnapshotter>.Instance);
        var changed = await _h.Store.UpgradeSnapshotsAsync(ItemSnapshotter.CurrentVersion, snapshotter.Upgrade, null, CancellationToken.None);

        Assert.Equal(1, changed);

        var upgraded = await Load(old.Id);
        Assert.Equal(2, upgraded.DiscNumber);
        Assert.Equal("|Punk|Rock|", upgraded.Genres);
        Assert.Equal("Old name", upgraded.Name); // what was recorded at play time stays
        Assert.Equal(ItemSnapshotter.CurrentVersion, upgraded.SnapshotVersion);
        Assert.True(upgraded.Seq > old.Seq);

        // An item gone from the library is marked done without changes, so it is not retried.
        var missing = await Load(gone.Id);
        Assert.Equal(ItemSnapshotter.CurrentVersion, missing.SnapshotVersion);
        Assert.Null(missing.Genres);
        Assert.Equal(gone.Seq, missing.Seq);

        Assert.Null((await Load(current.Id)).Genres);
        Assert.Equal(0, (await Load(active.Id)).SnapshotVersion);

        // A second run has nothing left to do.
        Assert.Equal(0, await _h.Store.UpgradeSnapshotsAsync(ItemSnapshotter.CurrentVersion, snapshotter.Upgrade, null, CancellationToken.None));
    }

    [Fact]
    public async Task ATrackWithoutGenres_TakesItsAlbums()
    {
        var album = new MusicAlbum { Id = Guid.NewGuid(), Name = "Plastic Skies", Genres = ["hardcore punk", "pop punk"] };
        var track = new Audio { Id = Guid.NewGuid(), Name = "Is It a Lie", ParentId = album.Id, ParentIndexNumber = 1 };
        _h.Library.Setup(l => l.GetItemById(album.Id)).Returns(album);
        _h.Library.Setup(l => l.GetItemById(track.Id)).Returns(track);

        // Jellyfin resolves an item's parents through this static.
        var previous = BaseItem.LibraryManager;
        BaseItem.LibraryManager = _h.Library.Object;
        try
        {
            // Recorded by version 1, which only read the track's own (empty) genres.
            var old = await Insert(track.Id, version: 1);
            var snapshotter = new ItemSnapshotter(_h.Library.Object, NullLogger<ItemSnapshotter>.Instance);

            Assert.Equal(1, await _h.Store.UpgradeSnapshotsAsync(ItemSnapshotter.CurrentVersion, snapshotter.Upgrade, null, CancellationToken.None));
            Assert.Equal("|hardcore punk|pop punk|", (await Load(old.Id)).Genres);

            // New plays get them straight away.
            var fresh = new PlayRecord();
            snapshotter.Apply(fresh, track.Id, track);
            Assert.Equal("|hardcore punk|pop punk|", fresh.Genres);
            Assert.Equal(1, fresh.DiscNumber);
        }
        finally
        {
            BaseItem.LibraryManager = previous;
        }
    }

    [Fact]
    public async Task APlayStartedBeforeAnUpdate_IsUpgradedWhenItCloses()
    {
        var track = new Audio { Id = Guid.NewGuid(), Name = "Too Drunk to Drive", ParentIndexNumber = 1, Genres = ["Punk"] };
        _h.Library.Setup(l => l.GetItemById(track.Id)).Returns(track);

        var old = await Insert(track.Id, version: 0, status: PlayStatus.Active);
        await using (var context = _h.Database.CreateDbContext())
        {
            var row = await context.Plays.SingleAsync(play => play.Id == old.Id);
            row.SessionKey = new PlaybackSignal { UserId = old.UserId, ItemId = track.Id, PlaySessionId = "ps" }.SessionKey;
            await context.SaveChangesAsync();
        }

        await _h.Tracker.StopAsync(new PlaybackSignal { UserId = old.UserId, ItemId = track.Id, Item = track, PlaySessionId = "ps", PositionTicks = 10 }, CancellationToken.None);

        var closed = await Load(old.Id);
        Assert.Equal(PlayStatus.Completed, closed.Status);
        Assert.Equal(1, closed.DiscNumber);
        Assert.Equal("|Punk|", closed.Genres);
        Assert.Equal(ItemSnapshotter.CurrentVersion, closed.SnapshotVersion);
    }

    [Fact]
    public async Task Initializer_AddsTheNewColumns_ToAnExistingDatabase()
    {
        await using (var context = _h.Database.CreateDbContext())
        {
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE Plays DROP COLUMN DiscNumber; ALTER TABLE Plays DROP COLUMN Genres; ALTER TABLE Plays DROP COLUMN SnapshotVersion; ALTER TABLE Plays DROP COLUMN IsPaused;");
        }

        var initializer = new DatabaseInitializer(_h.Database, NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitializeAsync(CancellationToken.None);
        await initializer.InitializeAsync(CancellationToken.None); // idempotent

        var play = await Insert(Guid.NewGuid(), version: 1);
        Assert.Equal(1, (await Load(play.Id)).SnapshotVersion);
    }

    private async Task<PlayRecord> Insert(Guid itemId, int version, string name = "Track", PlayStatus status = PlayStatus.Completed)
    {
        var play = new PlayRecord
        {
            Id = Guid.CreateVersion7().ToString("N"),
            UserId = Guid.NewGuid(),
            ItemId = itemId,
            SessionKey = Guid.NewGuid().ToString("N"),
            ItemType = "Audio",
            Name = name,
            Status = status,
            StartedAt = _h.Time.Now,
            LastSeenAt = _h.Time.Now,
            SnapshotVersion = version,
        };
        await _h.Store.InsertAsync(play, CancellationToken.None);
        return play;
    }

    private async Task<PlayRecord> Load(string id)
    {
        await using var context = _h.Database.CreateDbContext();
        return await context.Plays.AsNoTracking().SingleAsync(play => play.Id == id);
    }
}
