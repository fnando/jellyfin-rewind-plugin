using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Rewind.Configuration;

/// <summary>
/// The plugin's settings.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class with the defaults.
    /// </summary>
    public PluginConfiguration()
    {
        // Last.fm: half the track, or four minutes, actually listened to.
        Audio = new ScrobbleRule { Measure = ScrobbleMeasure.Played, Percent = 50, Minutes = 4 };

        // Trakt: 80% of the way through.
        Video = new ScrobbleRule { Measure = ScrobbleMeasure.Position, Percent = 80, Minutes = 0 };

        AudioBook = new ScrobbleRule { Measure = ScrobbleMeasure.Position, Percent = 90, Minutes = 0 };

        // Live TV has no runtime: five minutes watched.
        LiveTv = new ScrobbleRule { Measure = ScrobbleMeasure.Played, Percent = 0, Minutes = 5 };

        StaleSessionMinutes = 10;
        ProgressWriteSeconds = 30;
        PurgeHistoryOnUserDelete = false;
    }

    /// <summary>
    /// Gets or sets the rule for music (audio tracks).
    /// </summary>
    public ScrobbleRule Audio { get; set; }

    /// <summary>
    /// Gets or sets the rule for movies, episodes, music videos and every other video or
    /// item type without a rule of its own.
    /// </summary>
    public ScrobbleRule Video { get; set; }

    /// <summary>
    /// Gets or sets the rule for audiobooks.
    /// </summary>
    public ScrobbleRule AudioBook { get; set; }

    /// <summary>
    /// Gets or sets the rule for live TV channels and programs.
    /// </summary>
    public ScrobbleRule LiveTv { get; set; }

    /// <summary>
    /// Gets or sets how long an active session may go without a progress report before it is
    /// closed as abandoned (crashed client, server restart, a client that never sends stop).
    /// </summary>
    public int StaleSessionMinutes { get; set; }

    /// <summary>
    /// Gets or sets the minimum interval between progress writes for one session. Starts,
    /// stops and pause changes are always written immediately.
    /// </summary>
    public int ProgressWriteSeconds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a user's history is erased when the user is
    /// deleted from Jellyfin.
    /// </summary>
    public bool PurgeHistoryOnUserDelete { get; set; }
}
