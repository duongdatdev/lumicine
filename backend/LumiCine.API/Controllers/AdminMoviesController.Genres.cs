using System.ComponentModel.DataAnnotations;
using LumiCine.Application.Accounts;
using LumiCine.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

public sealed partial class AdminMoviesController
{
    [HttpPut("{id:guid}/genres")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> AssignGenres(Guid id, [FromBody] GenreAssignment request, CancellationToken cancellationToken) =>
        await movies.AssignGenresAsync(id, request.RequiredGenreIds, cancellationToken) ? NoContent() : NotFound();

    public sealed record GenreAssignment([Required] IReadOnlyList<Guid>? GenreIds)
    {
        public IReadOnlyList<Guid> RequiredGenreIds => GenreIds ?? [];
    }
}
