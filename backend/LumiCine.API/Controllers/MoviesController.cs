using System.ComponentModel.DataAnnotations;
using LumiCine.Application.Catalog;
using LumiCine.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

[ApiController]
[Route("api/movies")]
public sealed class MoviesController(IMovieCatalogService movies) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<MovieSummary>>> Browse([FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20, [FromQuery] string? search = null,
        [FromQuery] Guid? genreId = null, CancellationToken cancellationToken = default) =>
        Ok(await movies.BrowseAsync(new(page, pageSize), search, genreId, false, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MovieSummary>> Get(Guid id, CancellationToken cancellationToken)
    {
        var movie = await movies.GetAsync(id, false, cancellationToken);
        return movie is null ? NotFound() : Ok(movie);
    }
}
