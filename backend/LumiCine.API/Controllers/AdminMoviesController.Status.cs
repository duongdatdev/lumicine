using System.ComponentModel.DataAnnotations;
using LumiCine.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

public sealed partial class AdminMoviesController
{
    [HttpPut("{id:guid}/status")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] MovieStatusRequest request, CancellationToken cancellationToken) =>
        await movies.SetActiveAsync(id, request.IsActive!.Value, cancellationToken) ? NoContent() : NotFound();

    public sealed record MovieStatusRequest
    {
        [Required] public bool? IsActive { get; init; }
    }
}
