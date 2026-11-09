using System.Net.Mime;
using Jellyfin.Plugin.Rewind.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Rewind.Api;

/// <summary>
/// Plugin identification.
/// </summary>
[ApiController]
[Route("Rewind")]
[Produces(MediaTypeNames.Application.Json)]
public class InfoController : ControllerBase
{
    /// <summary>
    /// Identifies the plugin. Public (no authentication), so a client can check that the server
    /// has Rewind before signing in.
    /// </summary>
    /// <returns>The plugin id, name, repository URL and version.</returns>
    [AllowAnonymous]
    [HttpGet("Info")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PluginInfo> GetInfo()
        => new PluginInfo(
            Plugin.PluginId,
            Plugin.Instance?.Name ?? "Rewind",
            Plugin.RepositoryUrl,
            Plugin.Instance?.Version?.ToString() ?? "0.0.0.0");
}
