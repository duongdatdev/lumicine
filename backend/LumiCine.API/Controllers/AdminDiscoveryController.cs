using LumiCine.Application.Accounts;
using LumiCine.Application.Discovery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Route("api/admin/discovery")]
[Authorize(Roles = RoleNames.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminDiscoveryController(IDiscoveryIndexAdminService index) : ControllerBase
{
    [HttpGet("index")]
    [ProducesResponseType<DiscoveryIndexStatus>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DiscoveryIndexStatus>> GetIndex(CancellationToken cancellationToken) =>
        Ok(await index.GetStatusAsync(cancellationToken));

    [HttpPost("index/rebuild")]
    [ProducesResponseType<DiscoveryRebuildResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DiscoveryRebuildResult>> RebuildIndex(CancellationToken cancellationToken) =>
        Ok(await index.RebuildAsync(cancellationToken));
}
