using System;
using Jellyfin.Plugin.Rewind.Configuration;
using Jellyfin.Plugin.Rewind.Data;
using Jellyfin.Plugin.Rewind.Services;
using Xunit;

namespace Jellyfin.Plugin.Rewind.Tests;

public class ScrobblePolicyTests
{
    private static readonly long Minute = TimeSpan.FromMinutes(1).Ticks;

    [Theory]
    // Audio: half the track played, or four minutes.
    [InlineData("Audio", 10, 3.9, 9, false)]
    [InlineData("Audio", 10, 5, 5, true)]
    [InlineData("Audio", 20, 4, 4, true)]
    [InlineData("Audio", 10, 1, 10, false)] // seeked to the end: position does not count for music
    // Video: 80% of the way through, however it got there.
    [InlineData("Movie", 100, 10, 79, false)]
    [InlineData("Movie", 100, 10, 80, true)]
    [InlineData("Episode", 40, 40, 31, false)]
    [InlineData("Episode", 40, 5, 32, true)]
    [InlineData("MusicVideo", 4, 0, 4, true)]
    [InlineData("Video", 10, 9, 7, false)]
    // Unknown kinds use the video rule.
    [InlineData("Unknown", 10, 0, 9, true)]
    // Audiobooks: 90%.
    [InlineData("AudioBook", 600, 600, 539, false)]
    [InlineData("AudioBook", 600, 1, 540, true)]
    public void Evaluate_AppliesTheRuleOfTheKind(string type, double runtimeMinutes, double playedMinutes, double maxPositionMinutes, bool expected)
    {
        var play = new PlayRecord
        {
            ItemType = type,
            RuntimeTicks = (long)(runtimeMinutes * Minute),
            PlayedTicks = (long)(playedMinutes * Minute),
            MaxPositionTicks = (long)(maxPositionMinutes * Minute),
        };

        Assert.Equal(expected, ScrobblePolicy.Evaluate(play, new PluginConfiguration()));
    }

    [Theory]
    [InlineData("TvChannel", 4.9, false)]
    [InlineData("TvChannel", 5, true)]
    [InlineData("LiveTvChannel", 5, true)]
    public void LiveTv_CountsMinutes_WithoutARuntime(string type, double playedMinutes, bool expected)
    {
        var play = new PlayRecord { ItemType = type, PlayedTicks = (long)(playedMinutes * Minute) };

        Assert.Equal(expected, ScrobblePolicy.Evaluate(play, new PluginConfiguration()));
    }

    [Fact]
    public void UnknownRuntime_NeverMeetsAPercentage()
    {
        var play = new PlayRecord { ItemType = "Movie", PlayedTicks = 90 * Minute, MaxPositionTicks = 90 * Minute };

        Assert.False(ScrobblePolicy.Evaluate(play, new PluginConfiguration()));
    }

    [Fact]
    public void ManualPlays_AlwaysCount()
    {
        Assert.True(ScrobblePolicy.Evaluate(new PlayRecord { ItemType = "Movie", Source = PlaySource.Manual }, new PluginConfiguration()));
    }

    [Fact]
    public void JellyfinsPlayedFlag_DoesNotCount_OnItsOwn()
    {
        // Jellyfin calls a 3-minute song skipped at 10s "played to completion" (shorter than 5
        // minutes and past 5%).
        var skipped = new PlayRecord
        {
            ItemType = "Audio",
            RuntimeTicks = 3 * Minute,
            PlayedTicks = Minute / 6,
            MaxPositionTicks = Minute / 6,
            PlayedToCompletion = true,
        };

        Assert.False(ScrobblePolicy.Evaluate(skipped, new PluginConfiguration()));
    }

    [Fact]
    public void ZeroDisablesEitherHalf()
    {
        var configuration = new PluginConfiguration();
        configuration.Audio = new ScrobbleRule { Measure = ScrobbleMeasure.Played, Percent = 0, Minutes = 0 };

        var play = new PlayRecord { ItemType = "Audio", RuntimeTicks = 4 * Minute, PlayedTicks = 4 * Minute, MaxPositionTicks = 4 * Minute };

        Assert.False(ScrobblePolicy.Evaluate(play, configuration));
    }
}
