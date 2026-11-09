using Jellyfin.Plugin.Rewind.Configuration;
using Jellyfin.Plugin.Rewind.Services;

namespace Jellyfin.Plugin.Rewind.Tests;

public sealed class TestSettings : ISettings
{
    public PluginConfiguration Current { get; } = new();
}
