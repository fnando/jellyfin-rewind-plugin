using System;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Rewind.Configuration;
using Jellyfin.Plugin.Rewind.Data;

namespace Jellyfin.Plugin.Rewind.Services;

/// <summary>
/// Decides whether a play counts as a scrobble. Pure: it only reads the record's stored
/// metrics, so it can be re-applied to the whole history when the thresholds change.
/// </summary>
public static class ScrobblePolicy
{
    /// <summary>
    /// Evaluates a record against the configured rules.
    /// </summary>
    /// <param name="play">The play.</param>
    /// <param name="configuration">The settings.</param>
    /// <returns><c>true</c> when the play counts.</returns>
    public static bool Evaluate(PlayRecord play, PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(play);
        ArgumentNullException.ThrowIfNull(configuration);

        if (play.Source == PlaySource.Manual)
        {
            return true;
        }

        // Jellyfin's PlayedToCompletion is not a signal: anything shorter than five minutes is
        // "played" once past 5%, and a stop without a position is always "played". It stays on
        // the record as information; the plugin's own measurements decide.
        var rule = RuleFor(play.ItemType, configuration);
        if (rule.Minutes > 0 && play.PlayedTicks >= TimeSpan.FromMinutes(rule.Minutes).Ticks)
        {
            return true;
        }

        if (rule.Percent > 0 && play.RuntimeTicks is > 0 and var runtime)
        {
            var measured = rule.Measure == ScrobbleMeasure.Played ? play.PlayedTicks : play.MaxPositionTicks;
            return measured * 100.0 / runtime >= rule.Percent;
        }

        return false;
    }

    /// <summary>
    /// Picks the rule for an item kind.
    /// </summary>
    /// <param name="itemType">The <see cref="BaseItemKind"/> name.</param>
    /// <param name="configuration">The settings.</param>
    /// <returns>The rule.</returns>
    public static ScrobbleRule RuleFor(string itemType, PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!Enum.TryParse<BaseItemKind>(itemType, ignoreCase: false, out var kind))
        {
            return configuration.Video;
        }

        return kind switch
        {
            BaseItemKind.Audio => configuration.Audio,
            BaseItemKind.AudioBook => configuration.AudioBook,
            BaseItemKind.LiveTvChannel or BaseItemKind.TvChannel or BaseItemKind.LiveTvProgram or BaseItemKind.TvProgram or BaseItemKind.Program => configuration.LiveTv,
            _ => configuration.Video,
        };
    }
}
