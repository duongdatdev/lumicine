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
        Title = EntityGuard.RequiredText(title?.Trim() ?? string.Empty, 200, nameof(title));
        DurationMinutes = ValidateDuration(durationMinutes);
        CreatedAtUtc = EntityGuard.Utc(createdAtUtc, nameof(createdAtUtc));
        OriginalTitle = NormalizeOptional(originalTitle, 200, nameof(originalTitle));
        Synopsis = NormalizeSynopsis(synopsis);
        ReleaseDate = releaseDate;
        AgeRating = NormalizeOptional(ageRating, 20, nameof(ageRating));
        PosterUrl = NormalizeOptional(posterUrl, 2048, nameof(posterUrl));
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

    public void Update(string title, string? originalTitle, string? synopsis, int durationMinutes,
        DateOnly? releaseDate, string? ageRating, DateTime updatedAtUtc)
    {
        Title = EntityGuard.RequiredText(title?.Trim() ?? string.Empty, 200, nameof(title));
        OriginalTitle = NormalizeOptional(originalTitle, 200, nameof(originalTitle));
        Synopsis = NormalizeSynopsis(synopsis);
        DurationMinutes = ValidateDuration(durationMinutes);
        ReleaseDate = releaseDate;
        AgeRating = NormalizeOptional(ageRating, 20, nameof(ageRating));
        UpdatedAtUtc = EntityGuard.Utc(updatedAtUtc, nameof(updatedAtUtc));
    }

    public void SetActive(bool isActive, DateTime updatedAtUtc)
    {
        IsActive = isActive;
        UpdatedAtUtc = EntityGuard.Utc(updatedAtUtc, nameof(updatedAtUtc));
    }

    public void SetPosterUrl(string? posterUrl, DateTime updatedAtUtc)
    {
        PosterUrl = NormalizeOptional(posterUrl, 2048, nameof(posterUrl));
        UpdatedAtUtc = EntityGuard.Utc(updatedAtUtc, nameof(updatedAtUtc));
    }

    private static int ValidateDuration(int value) => value is >= 1 and <= 600
        ? value
        : throw new ArgumentOutOfRangeException(nameof(value), "Duration must be between 1 and 600 minutes.");

    private static string? NormalizeSynopsis(string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return value is null || value.Length <= 10_000
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Synopsis must be at most 10000 characters.");
    }

    private static string? NormalizeOptional(string? value, int maxLength, string name) =>
        EntityGuard.OptionalText(string.IsNullOrWhiteSpace(value) ? null : value.Trim(), maxLength, name);
}
