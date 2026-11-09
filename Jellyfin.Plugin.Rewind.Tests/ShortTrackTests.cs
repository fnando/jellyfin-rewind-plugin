using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Rewind.Data;
using Jellyfin.Plugin.Rewind.Services;
using MediaBrowser.Controller.Entities.Audio;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jellyfin.Plugin.Rewind.Tests;

/// <summary>
/// Music, and especially tracks shorter than the clients' progress interval: what counts is what
/// was measured, not Jellyfin's played flag, and missing positions are estimated from the clock.
/// </summary>
public sealed class ShortTrackTests : IDisposable
{
    private static readonly Guid User = Guid.NewGuid();

    private readonly Harness _h = new();
    private readonly Audio _short = new() { Id = Guid.NewGuid(), Name = "You're Going to Jail", RunTimeTicks = Seconds(8) };
    private readonly Audio _next = new() { Id = Guid.NewGuid(), Name = "Next", RunTimeTicks = Seconds(20) };
    private readonly Audio _song = new() { Id = Guid.NewGuid(), Name = "Is It a Lie", RunTimeTicks = Seconds(180) };

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task ASkippedSong_JellyfinCallsPlayed_DoesNotScrobble()
    {
        await Start(_song);
        _h.Time.Advance(TimeSpan.FromSeconds(10));
        await Stop(_song, Seconds(10), reportedComplete: true);

        var play = Assert.Single(await All());
        Assert.True(play.PlayedToCompletion); // kept as information
        Assert.False(play.Scrobbled);
    }

    [Fact]
    public async Task AShortTrack_StoppedWithoutAPosition_IsCreditedTheTimeItPlayed()
    {
        await Start(_short);
        _h.Time.Advance(TimeSpan.FromSeconds(8));
        await Stop(_short, null);

        var play = Assert.Single(await All());
        Assert.Equal(Seconds(8), play.PlayedTicks);
        Assert.Equal(Seconds(8), play.MaxPositionTicks);
        Assert.True(play.Scrobbled);
    }

    [Fact]
    public async Task AnEstimate_StopsAtTheEndOfTheTrack()
    {
        await Start(_short);
        _h.Time.Advance(TimeSpan.FromSeconds(30));
        await Stop(_short, null);

        var play = Assert.Single(await All());
        Assert.Equal(Seconds(8), play.PlayedTicks);
        Assert.Equal(Harness.T0.AddSeconds(8), play.EndedAt);
    }

    [Fact]
    public async Task ASongStoppedEarly_WithoutAPosition_IsCreditedOnlyThatTime()
    {
        await Start(_song);
        _h.Time.Advance(TimeSpan.FromSeconds(10));
        await Stop(_song, null, reportedComplete: true); // Jellyfin assumes "played" here

        var play = Assert.Single(await All());
        Assert.Equal(Seconds(10), play.PlayedTicks);
        Assert.False(play.Scrobbled);
    }

    [Fact]
    public async Task APausedTrack_StoppedWithoutAPosition_GetsNoCredit()
    {
        await Start(_song);
        _h.Time.Advance(TimeSpan.FromSeconds(10));
        await Progress(_song, Seconds(10), paused: true);
        _h.Time.Advance(TimeSpan.FromMinutes(3));
        await Stop(_song, null);

        var play = Assert.Single(await All());
        Assert.Equal(Seconds(10), play.PlayedTicks);
        Assert.Equal(Harness.T0.AddMinutes(3).AddSeconds(10), play.EndedAt);
    }

    [Fact]
    public async Task AutoAdvanceWithoutStops_CreditsEachTrack_UpToTheNextStart()
    {
        // A client that only announces the next track: the album plays through.
        await Start(_short);
        _h.Time.Advance(TimeSpan.FromSeconds(8));
        await Start(_next);
        _h.Time.Advance(TimeSpan.FromSeconds(5)); // skipped after 5 of 20 seconds
        await Start(_song);

        var plays = (await All()).OrderBy(play => play.StartedAt).ToList();
        Assert.Equal(3, plays.Count);

        Assert.Equal(PlayStatus.Abandoned, plays[0].Status);
        Assert.Equal(Seconds(8), plays[0].PlayedTicks);
        Assert.Equal(Harness.T0.AddSeconds(8), plays[0].EndedAt);
        Assert.True(plays[0].Scrobbled);

        Assert.Equal(Seconds(5), plays[1].PlayedTicks);
        Assert.False(plays[1].Scrobbled);

        Assert.Equal(PlayStatus.Active, plays[2].Status);
    }

    [Fact]
    public async Task AShortAlbum_PlayedThroughWithStops_ScrobblesEveryTrack()
    {
        var tracks = Enumerable.Range(1, 10)
            .Select(n => new Audio { Id = Guid.NewGuid(), Name = "Track " + n, RunTimeTicks = Seconds(8 + n) })
            .ToList();

        foreach (var track in tracks)
        {
            await Start(track);
            _h.Time.Advance(TimeSpan.FromTicks(track.RunTimeTicks!.Value));
            await Stop(track, track.RunTimeTicks, reportedComplete: true);
        }

        var plays = await All();
        Assert.Equal(10, plays.Count);
        Assert.All(plays, play => Assert.True(play.Scrobbled));
    }

    private static long Seconds(int seconds) => TimeSpan.FromSeconds(seconds).Ticks;

    private Task Start(Audio track) => _h.Tracker.StartAsync(Signal(track, 0), CancellationToken.None);

    private Task Progress(Audio track, long position, bool paused = false)
        => _h.Tracker.ProgressAsync(Signal(track, position) with { IsPaused = paused }, CancellationToken.None);

    private Task Stop(Audio track, long? position, bool reportedComplete = false)
        => _h.Tracker.StopAsync(Signal(track, position) with { PlayedToCompletion = reportedComplete }, CancellationToken.None);

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

    private async Task<List<PlayRecord>> All()
    {
        await using var context = _h.Database.CreateDbContext();
        return await context.Plays.AsNoTracking().ToListAsync();
    }
}
