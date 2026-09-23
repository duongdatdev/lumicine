namespace LumiCine.Application.Catalog;

public sealed record MovieSummary(Guid Id, string Title, string? OriginalTitle, string? Synopsis, int DurationMinutes,
    DateOnly? ReleaseDate, string? AgeRating, string? PosterUrl, bool IsActive, IReadOnlyList<GenreSummary> Genres);
