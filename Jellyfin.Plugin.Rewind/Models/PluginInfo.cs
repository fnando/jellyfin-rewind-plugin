using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Rewind.Models;

/// <summary>
/// Public identification of the plugin, returned by the unauthenticated info endpoint so a
/// client can probe whether the server has this plugin installed and enabled.
/// </summary>
/// <param name="Id">The plugin's unique id.</param>
/// <param name="Name">The plugin name.</param>
/// <param name="RepositoryUrl">The plugin's source repository URL.</param>
/// <param name="Version">The installed plugin version.</param>
public sealed record PluginInfo(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("repositoryUrl")] string RepositoryUrl,
    [property: JsonPropertyName("version")] string Version);
