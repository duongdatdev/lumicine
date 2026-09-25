using System.ComponentModel.DataAnnotations;
using LumiCine.Application.Accounts;
using LumiCine.Application.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LumiCine.API.Controllers;

public sealed partial class AdminMoviesController
{
    [HttpPost]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<MovieSummary>> Create([FromBody] MovieRequest request, CancellationToken cancellationToken)
    {
        var movie = await movies.CreateAsync(request.ToMetadata(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = movie.Id }, movie);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = RoleNames.Admin)]
    public async Task<ActionResult<MovieSummary>> Update(Guid id, [FromBody] MovieRequest request, CancellationToken cancellationToken)
    {
        var movie = await movies.UpdateAsync(id, request.ToMetadata(), cancellationToken);
        return movie is null ? NotFound() : Ok(movie);
    }

    public sealed record MovieRequest
    {
        [Required, StringLength(200, MinimumLength = 1)] public string Title { get; init; } = string.Empty;
        [StringLength(200)] public string? OriginalTitle { get; init; }
        [StringLength(10_000)] public string? Synopsis { get; init; }
        [Range(1, 600)] public int DurationMinutes { get; init; }
        public DateOnly? ReleaseDate { get; init; }
        [StringLength(20)] public string? AgeRating { get; init; }
        [Required] public IReadOnlyList<Guid>? GenreIds { get; init; } = [];
        public MovieMetadata ToMetadata() => new(Title, OriginalTitle, Synopsis, DurationMinutes, ReleaseDate, AgeRating, GenreIds ?? []);
    }
}
