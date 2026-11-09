using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Rewind.Data;
using Jellyfin.Plugin.Rewind.Services;
using MediaBrowser.Controller.Entities.Audio;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Rewind.Tests;

/// <summary>
/// Plugin updates and server restarts happen while people are playing things. Each test plays
/// on one tracker, flushes it (graceful shutdown), and continues on a fresh tracker over the
/// same database, as the server does after a restart.
/// </summary>
public sealed class RestartTests : IDisposable
{
    private static readonly Guid User = Guid.NewGuid();

    private readonly Harness _h = new();
    private readonly Audio _short = new() { Id = Guid.NewGuid(), Name = "Short", RunTimeTicks = Seconds(8) };
    private readonly Audio _song = new() { Id = Guid.NewGuid(), Name = "Song", RunTimeTicks = Seconds(180) };
    private readonly Audio _next = new() { Id = Guid.NewGuid(), Name = "Next", RunTimeTicks = Seconds(180) };

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task AShortTrack_ThatEndsWhileTheServerIsDown_StillScrobbles()
    {
        await _h.Tracker.StartAsync(Signal(_short, 0), CancellationToken.None);
        await _h.Tracker.FlushAsync(CancellationToken.None);

        // Down for 30 seconds: the track ends, its stop is lost, the next one starts.
        _h.Time.Advance(TimeSpan.FromSeconds(30));
        using var restarted = Restarted();
        await restarted.StartAsync(Signal(_next, 0), CancellationToken.None);

        var play = await Find(_short);
        Assert.Equal(PlayStatus.Abandoned, play.Status);
        Assert.Equal(Seconds(8), play.PlayedTicks);
        Assert.Equal(Harness.T0.AddSeconds(8), play.EndedAt);
        Assert.True(play.Scrobbled);
    }

    [Fact]
    public async Task APausedTrack_StaysPaused_AcrossARestart()
    {
        await _h.Tracker.StartAsync(Signal(_song, 0), CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromSeconds(10));
        await _h.Tracker.ProgressAsync(Signal(_song, Seconds(10)) with { IsPaused = true }, CancellationToken.None);
        await _h.Tracker.FlushAsync(CancellationToken.None);

        // After the restart the user stops (no position) two minutes later: still 10 seconds.
        _h.Time.Advance(TimeSpan.FromMinutes(2));
        using var restarted = Restarted();
        await restarted.StopAsync(Signal(_song, null), CancellationToken.None);

        var play = await Find(_song);
        Assert.Equal(Seconds(10), play.PlayedTicks);
        Assert.False(play.Scrobbled);
    }

    [Fact]
    public async Task APausedTrack_AbandonedForTheNextOne_AfterARestart_GetsNoCredit()
    {
        await _h.Tracker.StartAsync(Signal(_song, 0), CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromSeconds(10));
        await _h.Tracker.ProgressAsync(Signal(_song, Seconds(10)) with { IsPaused = true }, CancellationToken.None);
        await _h.Tracker.FlushAsync(CancellationToken.None);

        _h.Time.Advance(TimeSpan.FromMinutes(2));
        using var restarted = Restarted();
        await restarted.StartAsync(Signal(_next, 0), CancellationToken.None);

        Assert.Equal(Seconds(10), (await Find(_song)).PlayedTicks);
    }

    [Fact]
    public async Task PlayingThroughARestart_CountsTheWholeSong()
    {
        await _h.Tracker.StartAsync(Signal(_song, 0), CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromSeconds(60));
        await _h.Tracker.ProgressAsync(Signal(_song, Seconds(60)), CancellationToken.None);
        await _h.Tracker.FlushAsync(CancellationToken.None);

        // The client kept playing during a 20-second restart and reports again.
        _h.Time.Advance(TimeSpan.FromSeconds(20));
        using var restarted = Restarted();
        await restarted.ProgressAsync(Signal(_song, Seconds(80)), CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromSeconds(100));
        await restarted.StopAsync(Signal(_song, Seconds(180)), CancellationToken.None);

        var play = await Find(_song);
        Assert.Equal(Seconds(180), play.PlayedTicks);
        Assert.True(play.Scrobbled);
    }

    private static long Seconds(int seconds) => TimeSpan.FromSeconds(seconds).Ticks;

    private static PlaybackSignal Signal(Audio track, long? position) => new()
    {
        UserId = User,
        ItemId = track.Id,
        Item = track,
        PlaySessionId = "ps-" + track.Id.ToString("N"),
        PositionTicks = position,
        Client = "JellySound",
        DeviceId = "razorblade",
    };

    private PlaybackTracker Restarted()
        => new(_h.Store, new ItemSnapshotter(_h.Library.Object, NullLogger<ItemSnapshotter>.Instance), _h.Settings, _h.Time, NullLogger<PlaybackTracker>.Instance);

    private async Task<PlayRecord> Find(Audio track)
    {
        await using var context = _h.Database.CreateDbContext();
        return await context.Plays.AsNoTracking().SingleAsync(play => play.ItemId == track.Id);
    }
}
