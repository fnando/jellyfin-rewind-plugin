namespace Jellyfin.Plugin.Rewind.Configuration;

/// <summary>
/// When a play counts as a scrobble: <see cref="Percent"/> of the runtime by
/// <see cref="Measure"/>, or <see cref="Minutes"/> actually played, whichever comes first. A
/// zero disables that half of the rule.
/// </summary>
public class ScrobbleRule
{
    /// <summary>
    /// Gets or sets what the percentage is measured on.
    /// </summary>
    public ScrobbleMeasure Measure { get; set; } = ScrobbleMeasure.Position;

    /// <summary>
    /// Gets or sets the percentage of the runtime (0 disables it).
    /// </summary>
    public int Percent { get; set; }

    /// <summary>
    /// Gets or sets the minutes actually played (0 disables it).
    /// </summary>
    public int Minutes { get; set; }
}
