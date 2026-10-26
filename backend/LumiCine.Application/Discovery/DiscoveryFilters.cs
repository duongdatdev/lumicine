using System.Text.Json;
using System.Text.Json.Serialization;

namespace LumiCine.Application.Discovery;

/// <summary>
/// Conversation search state. Persisted as JSON on the conversation row; conditions the user has
/// not mentioned again carry over between turns, and new values replace the matching field.
/// Dates and time windows are expressed in UTC+7 (see <see cref="DiscoveryTime"/>).
/// </summary>
public sealed record DiscoveryFilters
{
    public const int MinSeatsFloor = 1;
    public const int MinSeatsCeiling = 8;

    private static readonly JsonSerializerOptions StorageOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public string? TopicText { get; init; }
    public string? MovieNameText { get; init; }
    public IReadOnlyList<string> GenreSlugs { get; init; } = [];
    public int? MaxDurationMinutes { get; init; }
    public string? City { get; init; }
    public Guid? CinemaId { get; init; }
    public string? CinemaName { get; init; }
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }
    public int? TimeWindowStartMinutes { get; init; }
    public int? TimeWindowEndMinutes { get; init; }
    public decimal? MaxPrice { get; init; }
    public int? MinSeats { get; init; }

    public static DiscoveryFilters Empty() => new();

    /// <summary>Default state for a brand-new search: upcoming showtimes over the next 7 days.</summary>
    public static DiscoveryFilters Default(DateTime nowUtc) => new();

    /// <summary>Applies a parsed intent to the current state, honoring intent type, clears and clamps.</summary>
    public DiscoveryFilters Apply(ParsedDiscoveryIntent patch, DateTime nowUtc, List<string> warnings)
    {
        var current = patch.Intent == DiscoveryIntentTypes.NewSearch ? Empty() : this;
        var clear = patch.ClearFields ?? [];

        foreach (var field in clear)
        {
            current = field switch
            {
                DiscoveryFilterFields.Topic => current with { TopicText = null },
                DiscoveryFilterFields.MovieName => current with { MovieNameText = null },
                DiscoveryFilterFields.Genres => current with { GenreSlugs = [] },
                DiscoveryFilterFields.Duration => current with { MaxDurationMinutes = null },
                DiscoveryFilterFields.Cinema => current with { CinemaId = null, CinemaName = null },
                DiscoveryFilterFields.City => current with { City = null },
                DiscoveryFilterFields.DateRange => current with { DateFrom = null, DateTo = null },
                DiscoveryFilterFields.TimeWindow => current with { TimeWindowStartMinutes = null, TimeWindowEndMinutes = null },
                DiscoveryFilterFields.MaxPrice => current with { MaxPrice = null },
                DiscoveryFilterFields.MinSeats => current with { MinSeats = null },
                _ => current
            };
        }

        current = current with
        {
            TopicText = patch.TopicText ?? current.TopicText,
            MovieNameText = patch.MovieNameText ?? current.MovieNameText,
            GenreSlugs = patch.GenreSlugs is { Length: > 0 } ? patch.GenreSlugs : current.GenreSlugs,
            MaxDurationMinutes = patch.MaxDurationMinutes ?? current.MaxDurationMinutes,
            City = patch.City ?? current.City,
            DateFrom = patch.DateFrom ?? current.DateFrom,
            DateTo = patch.DateTo ?? current.DateTo,
            TimeWindowStartMinutes = patch.TimeWindowStartMinutes ?? current.TimeWindowStartMinutes,
            TimeWindowEndMinutes = patch.TimeWindowEndMinutes ?? current.TimeWindowEndMinutes,
            MaxPrice = patch.MaxPrice ?? current.MaxPrice,
            MinSeats = patch.MinSeats ?? current.MinSeats
        };
        if (patch.CinemaName is not null)
        {
            // Cinema identity is resolved against stored cinemas by the caller; keep the name only.
            current = current with { CinemaId = null, CinemaName = patch.CinemaName };
        }
        return current.Normalize(patch, nowUtc, warnings);
    }

    private DiscoveryFilters Normalize(ParsedDiscoveryIntent patch, DateTime nowUtc, List<string> warnings)
    {
        var normalized = this with
        {
            TopicText = ClampText(TopicText, 500),
            MovieNameText = ClampText(MovieNameText, 200),
            GenreSlugs = GenreSlugs.Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Distinct().Take(10).ToList(),
            MaxDurationMinutes = MaxDurationMinutes is > 0 and <= 600 ? MaxDurationMinutes : null,
            City = ClampText(City, 100),
            CinemaName = ClampText(CinemaName, 160),
            MaxPrice = MaxPrice is >= 0 ? MaxPrice : null,
            MinSeats = MinSeats is >= MinSeatsFloor and <= MinSeatsCeiling ? MinSeats : null,
            TimeWindowStartMinutes = ClampMinuteOfDay(TimeWindowStartMinutes),
            TimeWindowEndMinutes = ClampMinuteOfDay(TimeWindowEndMinutes)
        };

        if (MaxPrice is < 0 or > 100_000_000)
            warnings.Add("Giá tối đa không hợp lệ nên đã bị bỏ qua.");

        var today = DiscoveryTime.TodayInZone(nowUtc);
        if (patch.Intent == DiscoveryIntentTypes.NewSearch && patch.DateFrom is null && patch.DateTo is null)
        {
            normalized = normalized with { DateFrom = null, DateTo = null };
        }
        else if (normalized.DateFrom is not null || normalized.DateTo is not null)
        {
            var from = normalized.DateFrom ?? today;
            var to = normalized.DateTo ?? from;
            if (to < from) (from, to) = (to, from);
            if ((to.DayNumber - from.DayNumber) + 1 > DiscoveryTime.MaxDateRangeDays)
            {
                to = from.AddDays(DiscoveryTime.MaxDateRangeDays - 1);
                warnings.Add("Khoảng ngày vượt quá 31 ngày nên đã được rút gọn.");
            }
            if (from < today.AddDays(-1))
            {
                from = today;
                if (to < from) to = from;
                warnings.Add("Ngày bắt đầu trong quá khứ nên đã được đặt lại thành hôm nay.");
            }
            normalized = normalized with { DateFrom = from, DateTo = to };
        }
        return normalized;
    }

    private static int? ClampMinuteOfDay(int? minutes) =>
        minutes is >= 0 and <= 24 * 60 - 1 ? minutes : null;

    private static string? ClampText(string? value, int maxLength) =>
        value is null ? null : value.Trim().Length == 0 ? null : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];

    public string Serialize() => JsonSerializer.Serialize(this, StorageOptions);

    public static DiscoveryFilters Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty();
        try
        {
            return JsonSerializer.Deserialize<DiscoveryFilters>(json, StorageOptions) ?? Empty();
        }
        catch (JsonException)
        {
            return Empty();
        }
    }
}
