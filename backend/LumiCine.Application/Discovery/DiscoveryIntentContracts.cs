namespace LumiCine.Application.Discovery;

/// <summary>Intent the AI parser may classify a discovery message as.</summary>
public static class DiscoveryIntentTypes
{
    public const string NewSearch = "new_search";
    public const string Adjust = "adjust";
    public const string Repeat = "repeat";

    public static bool IsDefined(string value) =>
        value is NewSearch or Adjust or Repeat;
}

/// <summary>Filter fields the parser can explicitly clear ("bỏ giới hạn giá" and friends).</summary>
public static class DiscoveryFilterFields
{
    public const string Topic = "topic";
    public const string MovieName = "movieName";
    public const string Genres = "genres";
    public const string Duration = "duration";
    public const string Cinema = "cinema";
    public const string City = "city";
    public const string DateRange = "dateRange";
    public const string TimeWindow = "timeWindow";
    public const string MaxPrice = "maxPrice";
    public const string MinSeats = "minSeats";

    public static bool IsDefined(string value) =>
        value is Topic or MovieName or Genres or Duration or Cinema or City or DateRange or TimeWindow or MaxPrice or MinSeats;
}

/// <summary>
/// Structured intent the AI returns for one discovery message. Null fields inherit from the
/// conversation state; <see cref="ClearFields"/> removes conditions the user asked to drop.
/// </summary>
public sealed record ParsedDiscoveryIntent(
    string Intent,
    string? TopicText,
    string? MovieNameText,
    string[]? GenreSlugs,
    int? MaxDurationMinutes,
    string? City,
    string? CinemaName,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    int? TimeWindowStartMinutes,
    int? TimeWindowEndMinutes,
    decimal? MaxPrice,
    int? MinSeats,
    string[]? ClearFields);

/// <summary>One prior turn fed back to the model as conversation context.</summary>
public sealed record DiscoveryIntentTurn(string Role, string Content);

/// <summary>Normalized inputs the parser needs besides the raw message.</summary>
public sealed record DiscoveryIntentInput(
    string Message,
    string FiltersJson,
    IReadOnlyList<DiscoveryIntentTurn> RecentTurns,
    DateTime NowUtc,
    IReadOnlyList<string> GenreSlugs,
    IReadOnlyList<string> CinemaNames,
    IReadOnlyList<string> CityNames);

public sealed record DiscoveryIntentParseResult(ParsedDiscoveryIntent? Intent, string? Error)
{
    public static DiscoveryIntentParseResult Ok(ParsedDiscoveryIntent intent) => new(intent, null);
    public static DiscoveryIntentParseResult Fail(string error) => new(null, error);
}

/// <summary>Turns the parsed intent into validated filters against real catalog entities.</summary>
public sealed record DiscoveryFilterResolution(
    ParsedDiscoveryIntent Intent,
    DiscoveryFilters Filters,
    IReadOnlyList<string> Warnings,
    string? ClarificationText);

/// <summary>Parser over the AI chat client; failures return <see cref="DiscoveryIntentParseResult.Error"/>.</summary>
public interface IDiscoveryIntentParser
{
    Task<DiscoveryIntentParseResult> ParseAsync(DiscoveryIntentInput input, CancellationToken cancellationToken);
}
