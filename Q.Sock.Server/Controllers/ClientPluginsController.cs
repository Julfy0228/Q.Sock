using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Q.Sock.Core.Plugins;
using Q.Sock.Server.Services;

namespace Q.Sock.Server.Controllers;

[ApiController, Authorize, Route("api/ui/plugins")]
public sealed class ClientPluginsController(ClientPluginCatalog catalog) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<PluginPackage>> List([FromQuery] string target = "avalonia") =>
        catalog.List(target);

    [HttpGet("assets/{id}/{target}/{hash}")]
    public async Task<IActionResult> Asset(string id, string target, string hash) =>
        File(await catalog.Asset(id, target, hash), "application/octet-stream");
}
