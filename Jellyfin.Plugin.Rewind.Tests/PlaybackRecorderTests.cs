using System;
using System.Collections.Generic;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Rewind.Services;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Xunit;

namespace Jellyfin.Plugin.Rewind.Tests;

public class PlaybackRecorderTests
{
    [Fact]
    public void ToSignals_SplitsSharedSessions_PerUser()
    {
        var alice = new User("alice", "default", "default");
        var bob = new User("bob", "default", "default");
        var movie = new Movie { Id = Guid.NewGuid() };

        var signals = PlaybackRecorder.ToSignals(new PlaybackStopEventArgs
        {
            Users = new List<User> { alice, bob, alice },
            Item = movie,
            PlaySessionId = "ps",
            PlaybackPositionTicks = 42,
            ClientName = "Jellyfin Web",
            DeviceId = "dev",
            PlayedToCompletion = true,
        });

        Assert.Equal(2, signals.Count);
        Assert.Equal(alice.Id, signals[0].UserId);
        Assert.Equal(bob.Id, signals[1].UserId);
        Assert.All(signals, signal =>
        {
            Assert.Equal(movie.Id, signal.ItemId);
            Assert.Equal(42, signal.PositionTicks);
            Assert.True(signal.PlayedToCompletion);
            Assert.Equal("Jellyfin Web", signal.Client);
        });
        Assert.NotEqual(signals[0].SessionKey, signals[1].SessionKey);
    }

    [Fact]
    public void ToSignals_SkipsEventsWithoutAnItem()
    {
        Assert.Empty(PlaybackRecorder.ToSignals(new PlaybackProgressEventArgs { Users = new List<User> { new("a", "b", "c") } }));
    }

    [Fact]
    public void SessionKey_FallsBackToTheDevice()
    {
        var user = Guid.NewGuid();
        var item = Guid.NewGuid();
        var signal = new PlaybackSignal { UserId = user, ItemId = item, DeviceId = "tv" };

        Assert.Equal(FormattableString.Invariant($"{user:N}:{item:N}:device:tv"), signal.SessionKey);
    }
}
