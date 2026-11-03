using LumiCine.Application.Common;

namespace LumiCine.Application.Reporting;

public enum RevenueGroupBy { Day, Movie, Cinema, Showtime }

public static class ReportExportLimits
{
    public const int MaxRows = 50_000;
}

public sealed record ReportFilter(
    DateOnly? FromDate = null, DateOnly? ToDate = null, Guid? MovieId = null, Guid? CinemaId = null,
    int Page = 1, int PageSize = 20);

public sealed record RevenueReportFilter(
    DateOnly? FromDate = null, DateOnly? ToDate = null, Guid? MovieId = null, Guid? CinemaId = null,
    int Page = 1, int PageSize = 20, RevenueGroupBy GroupBy = RevenueGroupBy.Day);

public sealed record NormalizedReportFilter(DateOnly FromDate, DateOnly ToDate, DateTime FromUtc,
    DateTime ToUtcExclusive, Guid? MovieId, Guid? CinemaId, int Page, int PageSize, DateTime GeneratedAtUtc);

public static class ReportFilterNormalizer
{
    public static NormalizedReportFilter Normalize(RevenueReportFilter filter, DateTime nowUtc) =>
        Normalize(new ReportFilter(filter.FromDate, filter.ToDate, filter.MovieId, filter.CinemaId, filter.Page, filter.PageSize), nowUtc);

    public static NormalizedReportFilter Normalize(ReportFilter filter, DateTime nowUtc)
    {
        if (filter.Page < 1 || filter.PageSize is < 1 or > 100)
            throw new ApiException(400, "page must be positive and pageSize must be between 1 and 100.");
        if (filter.FromDate.HasValue != filter.ToDate.HasValue)
            throw new ApiException(400, "fromDate and toDate must be provided together.");
        try
        {
            nowUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
            var today = filter.FromDate is null
                ? DateOnly.FromDateTime(nowUtc.AddHours(7))
                : default;
            var from = filter.FromDate ?? today.AddDays(-29);
            var to = filter.ToDate ?? today;
            if (to < from) throw new ApiException(400, "toDate must be on or after fromDate.");
            if (to.DayNumber - from.DayNumber >= 366) throw new ApiException(400, "Date range cannot exceed 366 days.");
            var fromUtc = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue).AddHours(-7), DateTimeKind.Utc);
            var toUtc = DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-7), DateTimeKind.Utc);
            return new(from, to, fromUtc, toUtc, filter.MovieId, filter.CinemaId, filter.Page,
                filter.PageSize, nowUtc);
        }
        catch (ApiException) { throw; }
        catch (ArgumentOutOfRangeException) { throw new ApiException(400, "Date range exceeds supported UTC dates."); }
    }
}

public sealed record ReportMetadata(DateTime GeneratedAtUtc, DateOnly FromDate, DateOnly ToDate,
    string TimeZone = "UTC+07:00", string Currency = "VND", string TimeBasis = "");
public sealed record AdminReportResult<TRow, TTotals>(ReportMetadata Metadata, IReadOnlyList<TRow> Rows, int TotalCount,
    int Page, int PageSize, TTotals Totals);
public sealed record RevenueReportTotals(decimal TicketSalesAmount, decimal CollectedAmount,
    decimal RecordedRefundAmount, decimal NetCollectedAmount, int SuccessfulPaymentCount, int FailedPaymentCount);
public sealed record ShowtimeReportTotals(int Capacity, int SoldSeats, int HeldSeats, int AvailableSeats,
    decimal OccupancyRate, int ValidTickets, int UsedTickets, decimal CheckInRate, decimal IssuedTicketAmount);

public sealed record AdminDashboardSummary(ReportMetadata Metadata, decimal TicketSalesAmount,
    decimal CollectedAmount, decimal RecordedRefundAmount, decimal NetCollectedAmount,
    int SuccessfulPaymentCount, int FailedPaymentCount, int PaymentsMissingRequiredTimestampCount,
    int BookingsCreatedCount, IReadOnlyDictionary<string, int> BookingStatuses,
    int TicketsIssuedCount, int TicketsUsedCount, int RejectedCheckInCount, IReadOnlyDictionary<string, int> RejectedCheckIns);

public sealed record AdminDashboardOperations(DateTime AsOfUtc, Guid? MovieId, Guid? CinemaId,
    int RefundReviewCount, decimal RefundReviewAmount, IReadOnlyList<Guid> RefundReviewPaymentIds,
    int FailedEmailCount, IReadOnlyList<Guid> FailedEmailIds, int DueEmailCount, IReadOnlyList<Guid> DueEmailIds,
    int PendingIndexCount, IReadOnlyList<Guid> PendingIndexMovieIds, int FailedIndexCount,
    IReadOnlyList<Guid> FailedIndexMovieIds, int PaymentsMissingProcessedAtCount,
    IReadOnlyList<Guid> PaymentsMissingProcessedAtIds);

public sealed record RevenueReportRow(string Key, string Label, decimal TicketSalesAmount, decimal CollectedAmount,
    decimal RecordedRefundAmount, decimal NetCollectedAmount, int SuccessfulPaymentCount, int FailedPaymentCount);
public sealed record ShowtimeReportRow(Guid ShowtimeId, Guid MovieId, string MovieTitle, Guid CinemaId,
    string CinemaName, Guid RoomId, string RoomName, DateTime StartAtUtc, DateTime EndAtUtc, string Status,
    int Capacity, int SoldSeats, int HeldSeats, int AvailableSeats, decimal OccupancyRate,
    int ValidTickets, int UsedTickets, decimal CheckInRate, decimal IssuedTicketAmount);

public interface IAdminReportingService
{
    Task<AdminDashboardSummary> GetSummaryAsync(ReportFilter filter, CancellationToken cancellationToken);
    Task<AdminDashboardOperations> GetOperationsAsync(ReportFilter filter, CancellationToken cancellationToken);
    Task<AdminReportResult<RevenueReportRow, RevenueReportTotals>> GetRevenueAsync(RevenueReportFilter filter, CancellationToken cancellationToken);
    Task<AdminReportResult<ShowtimeReportRow, ShowtimeReportTotals>> GetShowtimesAsync(ReportFilter filter, CancellationToken cancellationToken);
    Task<AdminReportResult<RevenueReportRow, RevenueReportTotals>> GetRevenueExportAsync(RevenueReportFilter filter, CancellationToken cancellationToken);
    Task<AdminReportResult<ShowtimeReportRow, ShowtimeReportTotals>> GetShowtimesExportAsync(ReportFilter filter, CancellationToken cancellationToken);
}
