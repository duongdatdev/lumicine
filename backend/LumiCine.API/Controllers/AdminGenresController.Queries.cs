using System.ComponentModel.DataAnnotations;
using LumiCine.Application.Accounts;
using LumiCine.Application.Catalog;
using LumiCine.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Route("api/admin/genres")]
[Authorize(Roles = RoleNames.Admin + "," + RoleNames.Staff)]
public sealed partial class AdminGenresController(IMovieCatalogService movies) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<GenreSummary>>> Get([FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Ok(await movies.GetGenresAsync(new(page, pageSize), cancellationToken));
}
