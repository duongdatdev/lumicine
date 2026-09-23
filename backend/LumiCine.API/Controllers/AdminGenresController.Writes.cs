using System.ComponentModel.DataAnnotations;
using LumiCine.Application.Accounts;
using LumiCine.Application.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

public sealed partial class AdminGenresController
{
    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<GenreSummary>> Create([FromBody] GenreRequest request, CancellationToken cancellationToken)
    {
        var genre = await movies.CreateGenreAsync(request.Name, request.Slug, cancellationToken);
        return Created("/api/admin/genres", genre);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<GenreSummary>> Update(Guid id, [FromBody] GenreRequest request, CancellationToken cancellationToken)
    {
        var genre = await movies.UpdateGenreAsync(id, request.Name, request.Slug, cancellationToken);
        return genre is null ? NotFound() : Ok(genre);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await movies.DeleteGenreAsync(id, cancellationToken) ? NoContent() : NotFound();

    public sealed record GenreRequest([Required, StringLength(100, MinimumLength = 1)] string Name,
        [Required, StringLength(120, MinimumLength = 1)] string Slug);
}
