using Jellyfin.Plugin.Rewind.Configuration;

namespace Jellyfin.Plugin.Rewind.Services;

/// <summary>
/// Hands out the current plugin settings (read on every use, so changes apply without a restart).
/// </summary>
public interface ISettings
{
    /// <summary>
    /// Gets the current settings.
    /// </summary>
    PluginConfiguration Current { get; }
}
