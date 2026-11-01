namespace LumiCine.Application.Discovery;

/// <summary>Filter state projected to clients; mirrors <see cref="DiscoveryFilters"/>.</summary>
public sealed record DiscoveryFiltersView(
    string? TopicText,
    string? MovieNameText,
    IReadOnlyList<string> GenreSlugs,
    int? MaxDurationMinutes,
    string? City,
    string? CinemaName,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    int? TimeWindowStartMinutes,
    int? TimeWindowEndMinutes,
    decimal? MaxPrice,
    int? MinSeats);

/// <summary>One qualifying screening offered to the user; IDs feed the seat-selection flow.</summary>
public sealed record DiscoveryShowtimeResult(
    Guid ShowtimeId,
    Guid MovieId,
    Guid CinemaId,
    string CinemaName,
    Guid RoomId,
    string RoomName,
    DateTime StartAtUtc,
    decimal Price,
    int AvailableSeats);

/// <summary>One movie in the answer, ordered showtimes and its fused relevance score.</summary>
public sealed record DiscoveryMovieResult(
    Guid MovieId,
    string Title,
    string? OriginalTitle,
    int DurationMinutes,
    string? AgeRating,
    string? PosterUrl,
    IReadOnlyList<string> Genres,
    IReadOnlyList<DiscoveryShowtimeResult> Showtimes,
    double Score);

/// <summary>Search modes the response can report.</summary>
public static class DiscoveryModes
{
    /// <summary>Semantic plus keyword ranking was fused.</summary>
    public const string Hybrid = "hybrid";
    /// <summary>Keyword ranking only, e.g. AI embedding unavailable or candidate cap exceeded.</summary>
    public const string Keyword = "keyword";
    /// <summary>Concrete schedule filters only; results follow showtime order.</summary>
    public const string Schedule = "schedule";
    /// <summary>The server asks the user to disambiguate instead of searching.</summary>
    public const string Clarification = "clarification";
}

/// <summary>Full response for one discovery turn; also the payload replayed for duplicate requests.</summary>
public sealed record DiscoveryChatResponse(
    Guid ConversationId,
    int Version,
    Guid RequestId,
    int TurnNumber,
    string Answer,
    DiscoveryFiltersView AppliedFilters,
    IReadOnlyList<DiscoveryMovieResult> Movies,
    string Mode,
    bool IsDegraded,
    IReadOnlyList<string> Warnings,
    DateTime GeneratedAtUtc,
    DateTime AvailabilityAsOfUtc);

/// <summary>Showtime evidence handed to the answer writer; prices, times and seats come from the server.</summary>
public sealed record DiscoveryEvidenceShowtime(
    Guid ShowtimeId,
    Guid CinemaId,
    string CinemaName,
    string RoomName,
    DateTime StartAtUtc,
    decimal Price,
    int AvailableSeats);

/// <summary>Movie evidence handed to the answer writer.</summary>
public sealed record DiscoveryEvidenceMovie(
    Guid MovieId,
    string Title,
    int DurationMinutes,
    string? AgeRating,
    IReadOnlyList<string> Genres,
    IReadOnlyList<DiscoveryEvidenceShowtime> Showtimes);

public sealed record DiscoveryAnswerResult(string? Answer, string? Error)
{
    public static DiscoveryAnswerResult Ok(string answer) => new(answer, null);
    public static DiscoveryAnswerResult Fail(string error) => new(null, error);
}

/// <summary>Writes the natural-language part of the answer; structured facts stay server-owned.</summary>
public interface IDiscoveryAnswerWriter
{
    Task<DiscoveryAnswerResult> WriteAsync(string message, IReadOnlyList<DiscoveryFiltersView> contextFilters, IReadOnlyList<DiscoveryEvidenceMovie> movies, bool noResults, DateTime nowUtc, CancellationToken cancellationToken);
}

public sealed record DiscoverySearchInput(
    Guid UserId,
    DiscoveryFilters Filters,
    string MessageText,
    DateTime NowUtc,
    bool PreferMessageKeywords = false);

public sealed record DiscoverySearchOutput(
    IReadOnlyList<DiscoveryMovieResult> Movies,
    string Mode,
    bool IsDegraded,
    IReadOnlyList<string> Warnings,
    DateTime AvailabilityAsOfUtc);

/// <summary>Retrieves candidates honoring filters, keyword relevance and, when possible, semantics.</summary>
public interface IDiscoverySearchService
{
    Task<DiscoverySearchOutput> SearchAsync(DiscoverySearchInput input, CancellationToken cancellationToken);
}
