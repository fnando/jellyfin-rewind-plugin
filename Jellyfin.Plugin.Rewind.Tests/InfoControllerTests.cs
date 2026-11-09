using System;
using System.Reflection;
using System.Text.Json;
using Jellyfin.Plugin.Rewind.Api;
using Jellyfin.Plugin.Rewind.Models;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Jellyfin.Plugin.Rewind.Tests;

public class InfoControllerTests
{
    [Fact]
    public void GetInfo_ReturnsThePluginIdentity()
    {
        var info = new InfoController().GetInfo().Value;

        Assert.NotNull(info);
        Assert.Equal(Guid.Parse("6bcfddb4-c3fb-48f6-996f-5b02d07e3e62"), info.Id);
        Assert.Equal("Rewind", info.Name);
        Assert.Equal("https://github.com/fnando/jellyfin-rewind-plugin", info.RepositoryUrl);
        Assert.False(string.IsNullOrEmpty(info.Version));
    }

    [Fact]
    public void GetInfo_IsPublic()
    {
        var method = typeof(InfoController).GetMethod(nameof(InfoController.GetInfo))!;

        Assert.NotNull(method.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(typeof(InfoController).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void GetInfo_SerializesCamelCase_UnderJellyfinsOptions()
    {
        // Jellyfin's own options keep property names as declared.
        var json = JsonSerializer.Serialize(new PluginInfo(Guid.Empty, "Rewind", "https://example.com", "1.0.0.0"));

        Assert.Contains("\"repositoryUrl\":", json, StringComparison.Ordinal);
        Assert.Contains("\"version\":", json, StringComparison.Ordinal);
    }
}
