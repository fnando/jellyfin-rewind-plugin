using Jellyfin.Plugin.Rewind.Configuration;

namespace Jellyfin.Plugin.Rewind.Services;

/// <summary>
/// The <see cref="ISettings"/> backed by the plugin's saved configuration.
/// </summary>
public sealed class PluginSettings : ISettings
{
    private static readonly PluginConfiguration _defaults = new();

    /// <inheritdoc />
    public PluginConfiguration Current => Plugin.Instance?.Configuration ?? _defaults;
}
