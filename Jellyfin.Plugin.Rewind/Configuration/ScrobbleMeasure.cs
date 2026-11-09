namespace Jellyfin.Plugin.Rewind.Configuration;

/// <summary>
/// What a scrobble percentage is measured on.
/// </summary>
public enum ScrobbleMeasure
{
    /// <summary>
    /// The furthest position reached. Resuming a half-watched movie and finishing it counts.
    /// </summary>
    Position = 0,

    /// <summary>
    /// The time actually played in this session, seeks excluded.
    /// </summary>
    Played = 1,
}
