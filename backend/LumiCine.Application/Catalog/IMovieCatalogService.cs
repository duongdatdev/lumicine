using LumiCine.Application.Common;

namespace LumiCine.Application.Catalog;

public interface IMovieCatalogService
{
    Task<PagedResult<MovieSummary>> BrowseAsync(PageQuery page, string? search, Guid? genreId, bool includeInactive, CancellationToken cancellationToken);
    Task<MovieSummary?> GetAsync(Guid id, bool includeInactive, CancellationToken cancellationToken);
    Task<PagedResult<GenreSummary>> GetGenresAsync(PageQuery page, CancellationToken cancellationToken);
    Task<GenreSummary> CreateGenreAsync(string name, string slug, CancellationToken cancellationToken);
    Task<GenreSummary?> UpdateGenreAsync(Guid id, string name, string slug, CancellationToken cancellationToken);
    Task<bool> DeleteGenreAsync(Guid id, CancellationToken cancellationToken);
    Task<MovieSummary> CreateAsync(MovieMetadata metadata, CancellationToken cancellationToken);
    Task<MovieSummary?> UpdateAsync(Guid id, MovieMetadata metadata, CancellationToken cancellationToken);
    Task<bool> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken);
    Task<bool> AssignGenresAsync(Guid id, IReadOnlyList<Guid> genreIds, CancellationToken cancellationToken);
}

public sealed record MovieMetadata(string Title, string? OriginalTitle, string? Synopsis, int DurationMinutes,
    DateOnly? ReleaseDate, string? AgeRating, IReadOnlyList<Guid> GenreIds);
