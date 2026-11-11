namespace Jellyfin.Plugin.Rewind.Data;

/// <summary>
/// Where a play record came from.
/// </summary>
public enum PlaySource
{
    /// <summary>
    /// Recorded from the server's playback events.
    /// </summary>
    Playback = 0,

    /// <summary>
    /// The user marked the item as played.
    /// </summary>
    Manual = 1,

    /// <summary>
    /// Reported by a client after the fact (for example, plays buffered while offline).
    /// </summary>
    Scrobble = 2,
}
