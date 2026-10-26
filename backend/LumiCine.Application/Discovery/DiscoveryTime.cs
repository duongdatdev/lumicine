namespace LumiCine.Application.Discovery;

/// <summary>
/// Discovery interprets all user-facing dates and times in UTC+7, matching cinema schedules.
/// Stored filters use zone-local dates plus minute-of-day windows; queries convert back to UTC.
/// </summary>
public static class DiscoveryTime
{
    public static readonly TimeSpan ZoneOffset = new(7, 0, 0);

    /// <summary>Number of days ahead a conversation searches when the user gives no date range.</summary>
    public const int DefaultSearchDays = 7;

    /// <summary>Maximum span a single request may cover, inclusive of both endpoints.</summary>
    public const int MaxDateRangeDays = 31;

    public static DateOnly TodayInZone(DateTime utcNow) => DateOnly.FromDateTime(utcNow + ZoneOffset);

    /// <summary>Start of the local day expressed in UTC.</summary>
    public static DateTime DayStartUtc(DateOnly date) =>
        DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue).AddHours(-ZoneOffset.TotalHours), DateTimeKind.Utc);

    /// <summary>Exclusive end of the local day expressed in UTC.</summary>
    public static DateTime DayEndUtc(DateOnly date) => DayStartUtc(date.AddDays(1));

    /// <summary>Converts a zone-local wall-clock time to UTC; a window past midnight rolls into the next day.</summary>
    public static DateTime ToUtc(DateOnly date, int minutesFromMidnight)
    {
        var local = date.ToDateTime(TimeOnly.MinValue).AddMinutes(minutesFromMidnight);
        return DateTime.SpecifyKind(local.Add(-ZoneOffset), DateTimeKind.Utc);
    }

    /// <summary>Minute of day, in the zone, of a UTC instant.</summary>
    public static int MinutesOfDayInZone(DateTime utc) =>
        (int)TimeOnly.FromDateTime(utc + ZoneOffset).ToTimeSpan().TotalMinutes;

    /// <summary>
    /// Resolves the effective UTC window for a request: an explicit range covers whole zone-local
    /// days, otherwise the default runs from <paramref name="nowUtc"/> for <see cref="DefaultSearchDays"/> days.
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc) ResolveWindowUtc(
        DateOnly? dateFrom, DateOnly? dateTo, DateTime nowUtc)
    {
        if (dateFrom is null && dateTo is null)
        {
            return (DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), DateTime.SpecifyKind(nowUtc.AddDays(DefaultSearchDays), DateTimeKind.Utc));
        }
        var today = TodayInZone(nowUtc);
        var from = dateFrom ?? today;
        var to = dateTo ?? from;
        if (to < from) (from, to) = (to, from);
        return (DayStartUtc(from), DayEndUtc(to));
    }
}
