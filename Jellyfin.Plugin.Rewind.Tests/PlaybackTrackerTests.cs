using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Rewind.Data;
using Jellyfin.Plugin.Rewind.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jellyfin.Plugin.Rewind.Tests;

public sealed class PlaybackTrackerTests : IDisposable
{
    private static readonly long Minute = TimeSpan.FromMinutes(1).Ticks;
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private readonly Harness _h = new();
    private readonly Movie _movie = new() { Id = Guid.NewGuid(), Name = "The Matrix", RunTimeTicks = 100 * Minute, ProductionYear = 1999 };

    public PlaybackTrackerTests()
    {
        _movie.ProviderIds["Imdb"] = "tt0133093";
    }

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task StartProgressStop_RecordsOnePlay_WithTheItemSnapshot()
    {
        await _h.Tracker.StartAsync(Signal(0), CancellationToken.None);

        var started = Assert.Single(await All());
        Assert.Equal(PlayStatus.Active, started.Status);
        Assert.Equal("Movie", started.ItemType);
        Assert.Equal("Video", started.MediaType);
        Assert.Equal("The Matrix", started.Name);
        Assert.Equal("|Imdb=tt0133093|", started.ProviderIds);
        Assert.Equal(Harness.T0, started.StartedAt);

        for (var minute = 1; minute <= 85; minute++)
        {
            _h.Time.Advance(TimeSpan.FromMinutes(1));
            await _h.Tracker.ProgressAsync(Signal(minute * Minute), CancellationToken.None);
        }

        await _h.Tracker.StopAsync(Signal(85 * Minute), CancellationToken.None);

        var play = Assert.Single(await All());
        Assert.Equal(PlayStatus.Completed, play.Status);
        Assert.Equal(85 * Minute, play.PlayedTicks);
        Assert.Equal(85 * Minute, play.MaxPositionTicks);
        Assert.Equal(Harness.T0.AddMinutes(85), play.EndedAt);
        Assert.True(play.Scrobbled);
    }

    [Fact]
    public async Task SeekingForward_DoesNotCountAsPlayed_ButMovesTheFurthestPosition()
    {
        await _h.Tracker.StartAsync(Signal(0), CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromSeconds(10));
        await _h.Tracker.ProgressAsync(Signal(TimeSpan.FromSeconds(10).Ticks), CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromSeconds(10));
        await _h.Tracker.StopAsync(Signal(90 * Minute), CancellationToken.None);

        var play = Assert.Single(await All());
        Assert.True(play.PlayedTicks <= TimeSpan.FromSeconds(30).Ticks);
        Assert.Equal(90 * Minute, play.MaxPositionTicks);
        Assert.True(play.Scrobbled); // Video measures the furthest position.
    }

    [Fact]
    public async Task PausedTime_DoesNotCountAsPlayed()
    {
        await _h.Tracker.StartAsync(Signal(0), CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromMinutes(1));
        await _h.Tracker.ProgressAsync(Signal(Minute, paused: true), CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromMinutes(30));
        await _h.Tracker.ProgressAsync(Signal(Minute + 1, paused: false), CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromMinutes(1));
        await _h.Tracker.StopAsync(Signal((2 * Minute) + 1), CancellationToken.None);

        var play = Assert.Single(await All());
        Assert.Equal(2 * Minute, play.PlayedTicks);
    }

    [Fact]
    public async Task ProgressWithoutAStart_StillRecordsThePlay()
    {
        await _h.Tracker.ProgressAsync(Signal(40 * Minute), CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromMinutes(45));
        await _h.Tracker.StopAsync(Signal(85 * Minute), CancellationToken.None);

        var play = Assert.Single(await All());
        Assert.Equal(PlayStatus.Completed, play.Status);
        Assert.Equal(45 * Minute, play.PlayedTicks);
        Assert.True(play.Scrobbled);
    }

    [Fact]
    public async Task StopWithoutAnythingElse_StillRecordsThePlay()
    {
        await _h.Tracker.StopAsync(Signal(99 * Minute, completed: true), CancellationToken.None);

        var play = Assert.Single(await All());
        Assert.True(play.PlayedToCompletion);
        Assert.True(play.Scrobbled);
    }

    [Fact]
    public async Task RepeatedStart_ForTheSameSession_KeepsOneRecord()
    {
        await _h.Tracker.StartAsync(Signal(0), CancellationToken.None);
        await _h.Tracker.StartAsync(Signal(0), CancellationToken.None);

        Assert.Single(await All());
    }

    [Fact]
    public async Task AFalseStart_RetriedWithANewPlaySession_IsOnePlay()
    {
        // What JellySound does: start, stop a second later at 0, start again with a new id.
        await _h.Tracker.StartAsync(Signal(0, playSession: "first"), CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromSeconds(1));
        await _h.Tracker.StopAsync(Signal(0, playSession: "first"), CancellationToken.None);
        await _h.Tracker.StartAsync(Signal(0, playSession: "second"), CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromMinutes(1));
        await _h.Tracker.ProgressAsync(Signal(Minute, playSession: "second"), CancellationToken.None);

        var play = Assert.Single(await All());
        Assert.Equal(PlayStatus.Active, play.Status);
        Assert.Equal("second", play.PlaySessionId);
        Assert.Equal(Harness.T0, play.StartedAt);
        Assert.Null(play.EndedAt);
        Assert.Equal(Minute, play.PlayedTicks);

        await _h.Tracker.StopAsync(Signal(Minute, playSession: "second"), CancellationToken.None);
        Assert.Equal(PlayStatus.Completed, Assert.Single(await All()).Status);
    }

    [Fact]
    public async Task ARealPlay_FollowedByAReplay_IsTwoPlays()
    {
        await _h.Tracker.StartAsync(Signal(0, playSession: "first"), CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromSeconds(20));
        await _h.Tracker.StopAsync(Signal(TimeSpan.FromSeconds(20).Ticks, playSession: "first"), CancellationToken.None);
        await _h.Tracker.StartAsync(Signal(0, playSession: "second"), CancellationToken.None);

        Assert.Equal(2, (await All()).Count);
    }

    [Theory]
    [InlineData(true)] // the server reports it played to completion
    [InlineData(false)] // the scrobble rule alone counts it
    public async Task ATrackShorterThanTheFalseStartLimit_PlayedTwice_IsTwoScrobbles(bool reportedComplete)
    {
        var jingle = new Movie { Id = Guid.NewGuid(), Name = "Jingle", RunTimeTicks = TimeSpan.FromSeconds(8).Ticks };
        var eight = TimeSpan.FromSeconds(8).Ticks;

        for (var round = 1; round <= 2; round++)
        {
            var session = "round-" + round;
            await _h.Tracker.StartAsync(Signal(0, playSession: session) with { ItemId = jingle.Id, Item = jingle }, CancellationToken.None);
            _h.Time.Advance(TimeSpan.FromSeconds(8));
            await _h.Tracker.StopAsync(Signal(eight, completed: reportedComplete, playSession: session) with { ItemId = jingle.Id, Item = jingle }, CancellationToken.None);
            _h.Time.Advance(TimeSpan.FromSeconds(1));
        }

        var plays = await All();
        Assert.Equal(2, plays.Count);
        Assert.All(plays, play =>
        {
            Assert.Equal(PlayStatus.Completed, play.Status);
            Assert.Equal(eight, play.PlayedTicks);
            Assert.True(play.Scrobbled);
        });
    }

    [Fact]
    public async Task AnOldFalseStart_IsNotContinued()
    {
        await _h.Tracker.StartAsync(Signal(0, playSession: "first"), CancellationToken.None);
        await _h.Tracker.StopAsync(Signal(0, playSession: "first"), CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromMinutes(1));
        await _h.Tracker.StartAsync(Signal(0, playSession: "second"), CancellationToken.None);

        Assert.Equal(2, (await All()).Count);
    }

    [Fact]
    public async Task EveryUserOfASharedSession_GetsTheirOwnPlay()
    {
        await _h.Tracker.StartAsync(Signal(0, user: Alice), CancellationToken.None);
        await _h.Tracker.StartAsync(Signal(0, user: Bob), CancellationToken.None);

        var plays = await All();
        Assert.Equal(2, plays.Count);
        Assert.Equal(new[] { Alice, Bob }.OrderBy(id => id), plays.Select(play => play.UserId).OrderBy(id => id));
    }

    [Fact]
    public async Task StartingSomethingElseOnTheDevice_ClosesTheOldPlay()
    {
        await _h.Tracker.StartAsync(Signal(0, playSession: "one"), CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromMinutes(1));
        await _h.Tracker.StartAsync(Signal(0, playSession: "two", item: Guid.NewGuid()), CancellationToken.None);

        var plays = (await All()).OrderBy(play => play.StartedAt).ToList();
        Assert.Equal(PlayStatus.Abandoned, plays[0].Status);
        Assert.Equal(PlayStatus.Active, plays[1].Status);
    }

    [Fact]
    public async Task QuietSessions_AreAbandoned_AndScrobbledOnWhatWasPlayed()
    {
        await _h.Tracker.StartAsync(Signal(0), CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromMinutes(90));
        await _h.Tracker.ProgressAsync(Signal(90 * Minute), CancellationToken.None);

        _h.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(0, await _h.Tracker.ReapAsync(CancellationToken.None));

        _h.Time.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(1, await _h.Tracker.ReapAsync(CancellationToken.None));

        var play = Assert.Single(await All());
        Assert.Equal(PlayStatus.Abandoned, play.Status);
        Assert.Equal(Harness.T0.AddMinutes(90), play.EndedAt);
        Assert.True(play.Scrobbled);
    }

    [Fact]
    public async Task ReaperUsesUnwrittenProgress_InsteadOfTheStaleRow()
    {
        _h.Settings.Current.ProgressWriteSeconds = 3600;
        await _h.Tracker.StartAsync(Signal(0), CancellationToken.None);
        for (var minute = 1; minute <= 15; minute++)
        {
            _h.Time.Advance(TimeSpan.FromMinutes(1));
            await _h.Tracker.ProgressAsync(Signal(minute * Minute), CancellationToken.None);
        }

        // The row on disk says "last seen at T0", but the session is alive.
        Assert.Equal(0, await _h.Tracker.ReapAsync(CancellationToken.None));
        Assert.Equal(PlayStatus.Active, Assert.Single(await All()).Status);
    }

    [Fact]
    public async Task ProgressAfterAbandonment_ReopensTheSameSession()
    {
        await _h.Tracker.StartAsync(Signal(0), CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromMinutes(20));
        await _h.Tracker.ReapAsync(CancellationToken.None);

        _h.Time.Advance(TimeSpan.FromMinutes(1));
        await _h.Tracker.ProgressAsync(Signal(21 * Minute), CancellationToken.None);

        var play = Assert.Single(await All());
        Assert.Equal(PlayStatus.Active, play.Status);
        Assert.Null(play.EndedAt);
    }

    [Fact]
    public async Task AfterARestart_ProgressContinuesTheStoredSession()
    {
        await _h.Tracker.StartAsync(Signal(0), CancellationToken.None);

        // A fresh tracker has nothing in memory, as after a server restart.
        using var restarted = new PlaybackTracker(_h.Store, new ItemSnapshotter(_h.Library.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<ItemSnapshotter>.Instance), _h.Settings, _h.Time, Microsoft.Extensions.Logging.Abstractions.NullLogger<PlaybackTracker>.Instance);
        _h.Time.Advance(TimeSpan.FromMinutes(2));
        await restarted.ProgressAsync(Signal(2 * Minute), CancellationToken.None);
        await restarted.StopAsync(Signal(2 * Minute), CancellationToken.None);

        var play = Assert.Single(await All());
        Assert.Equal(PlayStatus.Completed, play.Status);
        Assert.Equal(2 * Minute, play.PlayedTicks);
    }

    [Fact]
    public async Task MarkPlayed_RecordsAManualScrobble_UnlessThePlayJustEnded()
    {
        Assert.True(await _h.Tracker.MarkPlayedAsync(Alice, _movie, Harness.T0.AddDays(-3), CancellationToken.None));

        var manual = Assert.Single(await All());
        Assert.Equal(PlaySource.Manual, manual.Source);
        Assert.True(manual.Scrobbled);
        Assert.Equal(Harness.T0.AddDays(-3), manual.StartedAt);

        await _h.Tracker.StopAsync(Signal(99 * Minute, completed: true), CancellationToken.None);
        Assert.False(await _h.Tracker.MarkPlayedAsync(Alice, _movie, null, CancellationToken.None));
        Assert.Equal(2, (await All()).Count);
    }

    [Fact]
    public async Task MarkPlayed_IgnoresFolders()
    {
        Assert.False(await _h.Tracker.MarkPlayedAsync(Alice, new Folder { Id = Guid.NewGuid() }, null, CancellationToken.None));
        Assert.Empty(await All());
    }

    [Fact]
    public async Task ProgressWrites_AreThrottled_ButPausesWriteAtOnce()
    {
        _h.Settings.Current.ProgressWriteSeconds = 30;
        await _h.Tracker.StartAsync(Signal(0), CancellationToken.None);

        _h.Time.Advance(TimeSpan.FromSeconds(10));
        await _h.Tracker.ProgressAsync(Signal(TimeSpan.FromSeconds(10).Ticks), CancellationToken.None);
        Assert.Equal(0, Assert.Single(await All()).PositionTicks);

        _h.Time.Advance(TimeSpan.FromSeconds(5));
        await _h.Tracker.ProgressAsync(Signal(TimeSpan.FromSeconds(15).Ticks, paused: true), CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(15).Ticks, Assert.Single(await All()).PositionTicks);
    }

    [Fact]
    public async Task SequenceNumbers_GrowOnMeaningfulChangesOnly()
    {
        await _h.Tracker.StartAsync(Signal(0), CancellationToken.None);
        var created = Assert.Single(await All()).Seq;

        _h.Time.Advance(TimeSpan.FromMinutes(1));
        await _h.Tracker.ProgressAsync(Signal(Minute), CancellationToken.None);
        Assert.Equal(created, Assert.Single(await All()).Seq);

        await _h.Tracker.StopAsync(Signal(Minute), CancellationToken.None);
        Assert.True(Assert.Single(await All()).Seq > created);
    }

    private PlaybackSignal Signal(long position, bool paused = false, bool completed = false, Guid? user = null, string playSession = "ps-1", Guid? item = null)
        => new()
        {
            UserId = user ?? Alice,
            ItemId = item ?? _movie.Id,
            Item = item is null ? _movie : null,
            PlaySessionId = playSession,
            PositionTicks = position,
            IsPaused = paused,
            PlayedToCompletion = completed,
            Client = "Jellyfin Web",
            DeviceName = "Firefox",
            DeviceId = "device-" + (user ?? Alice).ToString("N"),
            PlayMethod = "DirectPlay",
        };

    private async Task<System.Collections.Generic.List<PlayRecord>> All()
    {
        await using var context = _h.Database.CreateDbContext();
        return await context.Plays.AsNoTracking().ToListAsync();
    }
}
