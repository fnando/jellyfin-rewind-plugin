namespace Jellyfin.Plugin.Rewind.Data;

/// <summary>
/// The state of a play.
/// </summary>
public enum PlayStatus
{
    /// <summary>
    /// Playing (or paused) right now.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Ended with a stop event.
    /// </summary>
    Completed = 1,

    /// <summary>
    /// Closed by the plugin after the client went quiet without a stop event.
    /// </summary>
    Abandoned = 2,
}
