using System.ComponentModel.DataAnnotations;
using LumiCine.Application.Accounts;
using LumiCine.Application.Catalog;
using LumiCine.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Authorize(Roles = RoleNames.Admin + "," + RoleNames.Staff)]
[Route("api/admin/movies")]
public sealed partial class AdminMoviesController(IMovieCatalogService movies) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<MovieSummary>>> Browse([FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20, [FromQuery] string? search = null,
        [FromQuery] Guid? genreId = null, CancellationToken cancellationToken = default) =>
        Ok(await movies.BrowseAsync(new(page, pageSize), search, genreId, true, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MovieSummary>> Get(Guid id, CancellationToken cancellationToken)
    {
        var movie = await movies.GetAsync(id, true, cancellationToken);
        return movie is null ? NotFound() : Ok(movie);
    }
}
