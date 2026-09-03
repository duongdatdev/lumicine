using LumiCine.Domain.Common;

namespace LumiCine.Domain.Catalog;

public sealed class Movie
{
    private Movie() => Title = string.Empty;

    public Movie(
        Guid id,
        string title,
        int durationMinutes,
        DateTime createdAtUtc,
        string? originalTitle = null,
        string? synopsis = null,
        DateOnly? releaseDate = null,
        string? ageRating = null,
        string? posterUrl = null,
        bool isActive = true,
        DateTime? updatedAtUtc = null)
    {
        Id = EntityGuard.Id(id, nameof(id));
        Title = EntityGuard.RequiredText(title, 200, nameof(title));
        DurationMinutes = EntityGuard.Positive(durationMinutes, nameof(durationMinutes));
        CreatedAtUtc = EntityGuard.Utc(createdAtUtc, nameof(createdAtUtc));
        OriginalTitle = EntityGuard.OptionalText(originalTitle, 200, nameof(originalTitle));
        Synopsis = synopsis;
        ReleaseDate = releaseDate;
        AgeRating = EntityGuard.OptionalText(ageRating, 20, nameof(ageRating));
        PosterUrl = EntityGuard.OptionalText(posterUrl, 2048, nameof(posterUrl));
        IsActive = isActive;
        UpdatedAtUtc = EntityGuard.OptionalUtc(updatedAtUtc, nameof(updatedAtUtc));
    }

    public Guid Id { get; private set; }
    public string Title { get; private set; }
    public string? OriginalTitle { get; private set; }
    public string? Synopsis { get; private set; }
    public int DurationMinutes { get; private set; }
    public DateOnly? ReleaseDate { get; private set; }
    public string? AgeRating { get; private set; }
    public string? PosterUrl { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
}