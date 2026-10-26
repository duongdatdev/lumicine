using LumiCine.Application.Discovery;

namespace LumiCine.UnitTests.Discovery;

public sealed class DiscoveryTimeTests
{
    // 2026-11-07 03:30Z is 10:30 in UTC+7.
    private static readonly DateTime Now = new(2026, 11, 7, 3, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Zone_day_start_and_end_convert_back_and_forth()
    {
        Assert.Equal(new DateTime(2026, 11, 6, 17, 0, 0, DateTimeKind.Utc), DiscoveryTime.DayStartUtc(new DateOnly(2026, 11, 7)));
        Assert.Equal(new DateTime(2026, 11, 7, 17, 0, 0, DateTimeKind.Utc), DiscoveryTime.DayEndUtc(new DateOnly(2026, 11, 7)));
        Assert.Equal(new DateOnly(2026, 11, 7), DiscoveryTime.TodayInZone(Now));
    }

    [Fact]
    public void Minutes_of_day_in_zone_reflect_the_seven_hour_offset()
    {
        Assert.Equal(630, DiscoveryTime.MinutesOfDayInZone(Now)); // 10:30
        Assert.Equal(1320, DiscoveryTime.MinutesOfDayInZone(new DateTime(2026, 11, 6, 15, 0, 0, DateTimeKind.Utc))); // 22:00 local
    }

    [Fact]
    public void To_utc_converts_zone_local_wall_clock_time()
    {
        Assert.Equal(new DateTime(2026, 11, 7, 2, 0, 0, DateTimeKind.Utc), DiscoveryTime.ToUtc(new DateOnly(2026, 11, 7), 9 * 60));
        Assert.Equal(DateTimeKind.Utc, DiscoveryTime.ToUtc(new DateOnly(2026, 11, 7), 600).Kind);
    }

    [Fact]
    public void To_utc_rolls_past_midnight_into_the_next_zone_day()
    {
        // 22:00 of Nov 6 is 15:00Z; 02:00 of Nov 7 is 19:00Z of Nov 6.
        Assert.Equal(new DateTime(2026, 11, 6, 15, 0, 0, DateTimeKind.Utc), DiscoveryTime.ToUtc(new DateOnly(2026, 11, 6), 22 * 60));
        Assert.Equal(new DateTime(2026, 11, 6, 19, 0, 0, DateTimeKind.Utc), DiscoveryTime.ToUtc(new DateOnly(2026, 11, 7), 2 * 60));
    }

    [Fact]
    public void Window_defaults_to_next_seven_days_from_now()
    {
        var (startUtc, endUtc) = DiscoveryTime.ResolveWindowUtc(null, null, Now);

        Assert.Equal(Now, startUtc);
        Assert.Equal(Now.AddDays(7), endUtc);
    }

    [Fact]
    public void Window_covers_whole_zone_days_when_dates_are_given()
    {
        var (startUtc, endUtc) = DiscoveryTime.ResolveWindowUtc(new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 8), Now);

        Assert.Equal(new DateTime(2026, 11, 6, 17, 0, 0, DateTimeKind.Utc), startUtc);
        Assert.Equal(new DateTime(2026, 11, 8, 17, 0, 0, DateTimeKind.Utc), endUtc);
    }

    [Fact]
    public void Window_swaps_reversed_dates_and_fills_missing_end()
    {
        var (reversedStart, reversedEnd) = DiscoveryTime.ResolveWindowUtc(new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 7), Now);
        Assert.Equal(DiscoveryTime.DayStartUtc(new DateOnly(2026, 11, 7)), reversedStart);
        Assert.Equal(DiscoveryTime.DayEndUtc(new DateOnly(2026, 11, 9)), reversedEnd);

        var (singleStart, singleEnd) = DiscoveryTime.ResolveWindowUtc(new DateOnly(2026, 11, 9), null, Now);
        Assert.Equal(DiscoveryTime.DayStartUtc(new DateOnly(2026, 11, 9)), singleStart);
        Assert.Equal(DiscoveryTime.DayEndUtc(new DateOnly(2026, 11, 9)), singleEnd);
    }
}
