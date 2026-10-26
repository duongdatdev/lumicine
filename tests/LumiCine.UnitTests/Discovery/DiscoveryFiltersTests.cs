using LumiCine.Application.Discovery;

namespace LumiCine.UnitTests.Discovery;

public sealed class DiscoveryFiltersTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 3, 30, 0, DateTimeKind.Utc);

    private static ParsedDiscoveryIntent Adjust(
        string? topic = null, string? movieName = null, string[]? genres = null, int? duration = null,
        string? city = null, string? cinemaName = null, DateOnly? from = null, DateOnly? to = null,
        int? windowStart = null, int? windowEnd = null, decimal? maxPrice = null, int? minSeats = null,
        string[]? clear = null, string intent = DiscoveryIntentTypes.Adjust) => new(
        intent, topic, movieName, genres, duration, city, cinemaName, from, to,
        windowStart, windowEnd, maxPrice, minSeats, clear);

    [Fact]
    public void Adjust_inherits_untouched_fields_and_replaces_mentioned_ones()
    {
        var current = new DiscoveryFilters
        {
            TopicText = "phim gia đình",
            MaxPrice = 120000m,
            MinSeats = 3,
            GenreSlugs = ["ha-noi"]
        };

        var updated = current.Apply(Adjust(maxPrice: 90_000m), Now, []);

        Assert.Equal("phim gia đình", updated.TopicText);
        Assert.Equal(3, updated.MinSeats);
        Assert.Equal(90_000m, updated.MaxPrice);
    }

    [Fact]
    public void New_search_discards_previous_conditions()
    {
        var current = new DiscoveryFilters { TopicText = "old", MaxPrice = 100_000m, City = "Hà Nội" };

        var updated = current.Apply(Adjust(movieName: "Interstellar", intent: DiscoveryIntentTypes.NewSearch), Now, []);

        Assert.Null(updated.TopicText);
        Assert.Null(updated.MaxPrice);
        Assert.Null(updated.City);
        Assert.Equal("Interstellar", updated.MovieNameText);
    }

    [Fact]
    public void Clear_fields_remove_only_matching_conditions()
    {
        var current = new DiscoveryFilters { MaxPrice = 120_000m, MinSeats = 2, GenreSlugs = ["hanh-dong"], CinemaName = "Beta Mỹ Đình", CinemaId = Guid.NewGuid() };

        var updated = current.Apply(Adjust(clear: [DiscoveryFilterFields.MaxPrice, DiscoveryFilterFields.Cinema]), Now, []);

        Assert.Null(updated.MaxPrice);
        Assert.Equal(2, updated.MinSeats);
        Assert.Equal(["hanh-dong"], updated.GenreSlugs);
        Assert.Null(updated.CinemaId);
        Assert.Null(updated.CinemaName);
    }

    [Fact]
    public void Date_range_over_31_days_is_clamped_with_warning()
    {
        var warnings = new List<string>();

        var updated = DiscoveryFilters.Empty().Apply(Adjust(from: new DateOnly(2026, 11, 7), to: new DateOnly(2027, 3, 1)), Now, warnings);

        Assert.Equal(DiscoveryTime.MaxDateRangeDays, updated.DateTo!.Value.DayNumber - updated.DateFrom!.Value.DayNumber + 1);
        Assert.Contains(warnings, w => w.Contains("31 ngày"));
    }

    [Fact]
    public void Past_start_date_is_moved_to_today_with_warning()
    {
        var warnings = new List<string>();

        var updated = DiscoveryFilters.Empty().Apply(Adjust(from: new DateOnly(2026, 11, 1)), Now, warnings);

        Assert.Equal(new DateOnly(2026, 11, 7), updated.DateFrom);
        Assert.Contains(warnings, w => w.Contains("hôm nay"));
    }

    [Fact]
    public void Seats_outside_1_to_8_are_dropped()
    {
        var tooFew = DiscoveryFilters.Empty().Apply(Adjust(minSeats: 0), Now, []);
        var tooMany = DiscoveryFilters.Empty().Apply(Adjust(minSeats: 9), Now, []);

        Assert.Null(tooFew.MinSeats);
        Assert.Null(tooMany.MinSeats);
    }

    [Fact]
    public void Time_window_past_midnight_is_kept_as_wrapping_range()
    {
        var updated = DiscoveryFilters.Empty().Apply(Adjust(windowStart: 22 * 60, windowEnd: 2 * 60), Now, []);

        Assert.Equal(1320, updated.TimeWindowStartMinutes);
        Assert.Equal(120, updated.TimeWindowEndMinutes);
    }

    [Fact]
    public void Genre_slugs_are_normalized_and_deduplicated()
    {
        var updated = DiscoveryFilters.Empty().Apply(Adjust(genres: [" Hài ", "hai", "", "KINH-DỊ"]), Now, []);

        Assert.Equal(["hài", "hai", "kinh-dị"], updated.GenreSlugs);
    }

    [Fact]
    public void Cinema_name_replaces_previous_cinema_identity()
    {
        var current = new DiscoveryFilters { CinemaId = Guid.NewGuid(), CinemaName = "Beta Mỹ Đình" };

        var updated = current.Apply(Adjust(cinemaName: "Lotte Hà Đông"), Now, []);

        Assert.Null(updated.CinemaId);
        Assert.Equal("Lotte Hà Đông", updated.CinemaName);
    }

    [Fact]
    public void Serialize_and_parse_round_trip_preserves_conditions()
    {
        var filters = new DiscoveryFilters
        {
            TopicText = "du hành thời gian",
            GenreSlugs = ["khoa-hoc"],
            DateFrom = new DateOnly(2026, 11, 14),
            DateTo = new DateOnly(2026, 11, 15),
            TimeWindowStartMinutes = 1320,
            TimeWindowEndMinutes = 120,
            MaxPrice = 120_000m,
            MinSeats = 3
        };

        var parsed = DiscoveryFilters.Parse(filters.Serialize());

        Assert.Equivalent(filters, parsed);
    }

    [Fact]
    public void Parse_returns_empty_state_for_invalid_json()
    {
        Assert.NotNull(DiscoveryFilters.Parse("not-json"));
        Assert.NotNull(DiscoveryFilters.Parse(null));
    }
}
