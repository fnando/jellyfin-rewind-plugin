namespace Jellyfin.Plugin.Rewind.Services;

/// <summary>
/// What became of a <see cref="ReportedPlay"/>.
/// </summary>
public enum ReportOutcome
{
    /// <summary>
    /// A new record was written.
    /// </summary>
    Created = 0,

    /// <summary>
    /// The play was already recorded: the same report sent again, or a scrobbled play the server
    /// recorded itself at the same time.
    /// </summary>
    Duplicate = 1,
}
