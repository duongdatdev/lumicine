using LumiCine.Application.Reporting;
using LumiCine.Domain.Bookings;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace LumiCine.Infrastructure.Reporting;

public sealed class AdminReportingService(LumiCineDbContext db, TimeProvider clock) : IAdminReportingService
{
    private const int ExportLimit = ReportExportLimits.MaxRows;
    private static readonly string[] BookingStatusNames = ["Pending", "Confirmed", "Cancelled", "Expired"];

    public async Task<AdminDashboardSummary> GetSummaryAsync(ReportFilter filter, CancellationToken ct)
    {
        var f = ReportFilterNormalizer.Normalize(filter, clock.GetUtcNow().UtcDateTime);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await SetSessionUtcAsync(ct);
        var bookingScope = FilterShowtimes(db.Bookings.AsNoTracking().Where(x => x.CreatedAtUtc >= f.FromUtc && x.CreatedAtUtc < f.ToUtcExclusive), f);
        var paymentScope = FilterPaymentBookings(db.Payments.AsNoTracking(), f);
        var payments = await paymentScope.Where(x => x.ProcessedAtUtc >= f.FromUtc && x.ProcessedAtUtc < f.ToUtcExclusive)
            .GroupBy(x => new { x.Status, x.ProcessingStatus })
            .Select(g => new { g.Key.Status, g.Key.ProcessingStatus, Count = g.Count(), Amount = g.Sum(x => x.Amount) })
            .ToListAsync(ct);
        var refunds = await paymentScope.Where(x => x.RefundRecordedAtUtc >= f.FromUtc && x.RefundRecordedAtUtc < f.ToUtcExclusive)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var missing = await paymentScope.CountAsync(x =>
            ((x.Status == PaymentStatus.Succeeded || x.Status == PaymentStatus.Failed) && x.ProcessedAtUtc == null) ||
            (x.ProcessingStatus == PaymentProcessingStatus.RefundRecorded && x.RefundRecordedAtUtc == null), ct);
        var bookingStatusesRaw = await bookingScope.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var bookingsByStatus = BookingStatusNames.ToDictionary(x => x, _ => 0);
        foreach (var s in bookingStatusesRaw) bookingsByStatus[s.Status.ToString()] = s.Count;
        var expiredPending = await bookingScope.CountAsync(x => x.Status == BookingStatus.Pending && x.HoldExpiresAtUtc <= f.GeneratedAtUtc, ct);
        bookingsByStatus["Pending"] -= expiredPending;
        bookingsByStatus["Expired"] += expiredPending;
        var issued = FilterTicketDates(db.Tickets.AsNoTracking().Where(x => x.IssuedAtUtc >= f.FromUtc && x.IssuedAtUtc < f.ToUtcExclusive), f);
        var issuedCount = await issued.CountAsync(ct);
        var usedCount = await FilterTicketDates(db.Tickets.AsNoTracking().Where(x => x.UsedAtUtc >= f.FromUtc && x.UsedAtUtc < f.ToUtcExclusive), f).CountAsync(ct);
        var rejects = await FilterCheckIns(db.CheckInLogs.AsNoTracking().Where(x => x.Result != CheckInLogResult.Success && x.LoggedAtUtc >= f.FromUtc && x.LoggedAtUtc < f.ToUtcExclusive), f)
            .GroupBy(x => x.Result).Select(g => new { Reason = g.Key, Count = g.Count() }).ToListAsync(ct);
        await tx.CommitAsync(ct);
        var succeeded = payments.Where(x => x.Status == PaymentStatus.Succeeded).ToArray();
        var failed = payments.Where(x => x.Status == PaymentStatus.Failed).Sum(x => x.Count);
        var collected = succeeded.Sum(x => x.Amount);
        var ticketSales = succeeded.Where(x => x.ProcessingStatus == PaymentProcessingStatus.Fulfilled).Sum(x => x.Amount);
        return new(Metadata(f, "payment ProcessedAtUtc; refunds RefundRecordedAtUtc; bookings CreatedAtUtc; tickets IssuedAtUtc/UsedAtUtc; check-ins LoggedAtUtc"),
            ticketSales, collected, refunds, collected - refunds, succeeded.Sum(x => x.Count), failed, missing,
            bookingsByStatus.Values.Sum(), bookingsByStatus, issuedCount, usedCount, rejects.Sum(x => x.Count),
            rejects.ToDictionary(x => x.Reason.ToString(), x => x.Count));
    }

    public async Task<AdminDashboardOperations> GetOperationsAsync(ReportFilter filter, CancellationToken ct)
    {
        var f = ReportFilterNormalizer.Normalize(new ReportFilter(MovieId: filter.MovieId, CinemaId: filter.CinemaId), clock.GetUtcNow().UtcDateTime);
        var now = f.GeneratedAtUtc;
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await SetSessionUtcAsync(ct);
        var reviewQuery = FilterPaymentBookings(db.Payments.AsNoTracking().Where(x => x.Status == PaymentStatus.Succeeded && x.ProcessingStatus == PaymentProcessingStatus.RequiresRefundReview), f);
        var reviewCount = await reviewQuery.CountAsync(ct);
        var reviewAmount = await reviewQuery.SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var reviewIds = await reviewQuery.OrderByDescending(x => x.ProcessedAtUtc ?? x.CreatedAtUtc).ThenBy(x => x.Id).Take(5).Select(x => x.Id).ToListAsync(ct);
        var email = db.TicketEmailDeliveries.AsNoTracking();
        email = from e in email join b in db.Bookings.AsNoTracking() on e.BookingId equals b.Id
                join s in db.Showtimes.AsNoTracking() on b.ShowtimeId equals s.Id
                join r in db.Rooms.AsNoTracking() on s.RoomId equals r.Id
                where (!f.MovieId.HasValue || s.MovieId == f.MovieId) && (!f.CinemaId.HasValue || r.CinemaId == f.CinemaId)
                select e;
        var failedQuery = email.Where(x => x.Status == TicketEmailDeliveryStatus.Failed);
        var failedCount = await failedQuery.CountAsync(ct);
        var failedEmails = await failedQuery.OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.Id).Take(5).Select(x => x.Id).ToListAsync(ct);
        var dueQuery = email.Where(x => x.Status == TicketEmailDeliveryStatus.Pending && x.NextAttemptAtUtc <= now && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now));
        var dueCount = await dueQuery.CountAsync(ct);
        var dueEmails = await dueQuery.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Take(5).Select(x => x.Id).ToListAsync(ct);
        var documents = db.MovieSearchDocuments.AsNoTracking().Where(x => !f.MovieId.HasValue || x.MovieId == f.MovieId);
        if (f.CinemaId.HasValue) documents = documents.Where(x => db.Showtimes.Any(s => s.MovieId == x.MovieId && db.Rooms.Any(r => r.Id == s.RoomId && r.CinemaId == f.CinemaId)));
        var pendingIndexQuery = documents.Where(x => x.Status == MovieSearchDocumentStatus.Pending).GroupBy(x => x.MovieId)
            .Select(g => new { MovieId = g.Key, Latest = g.Max(x => x.CreatedAtUtc) });
        var failedIndexQuery = documents.Where(x => x.Status == MovieSearchDocumentStatus.Failed).GroupBy(x => x.MovieId)
            .Select(g => new { MovieId = g.Key, Latest = g.Max(x => x.UpdatedAtUtc ?? x.CreatedAtUtc) });
        var pendingIndexCount = await pendingIndexQuery.CountAsync(ct);
        var failedIndexCount = await failedIndexQuery.CountAsync(ct);
        var pendingIndex = await pendingIndexQuery.OrderByDescending(x => x.Latest).ThenBy(x => x.MovieId).Take(5).Select(x => x.MovieId).ToListAsync(ct);
        var failedIndex = await failedIndexQuery.OrderByDescending(x => x.Latest).ThenBy(x => x.MovieId).Take(5).Select(x => x.MovieId).ToListAsync(ct);
        var missingQuery = FilterPaymentBookings(db.Payments.AsNoTracking().Where(x => x.Status == PaymentStatus.Succeeded && x.ProcessedAtUtc == null), f);
        var missingCount = await missingQuery.CountAsync(ct);
        var missingPayments = await missingQuery.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Take(5).Select(x => x.Id).ToListAsync(ct);
        await tx.CommitAsync(ct);
        return new(now, f.MovieId, f.CinemaId, reviewCount, reviewAmount, reviewIds,
            failedCount, failedEmails, dueCount, dueEmails,
            pendingIndexCount, pendingIndex, failedIndexCount, failedIndex,
            missingCount, missingPayments);
    }

    public Task<AdminReportResult<RevenueReportRow, RevenueReportTotals>> GetRevenueAsync(RevenueReportFilter filter, CancellationToken ct) => Revenue(filter, true, ct);
    public Task<AdminReportResult<RevenueReportRow, RevenueReportTotals>> GetRevenueExportAsync(RevenueReportFilter filter, CancellationToken ct) => Revenue(filter, false, ct);

    private sealed class RevenueSqlRow
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public decimal Sales { get; set; }
        public decimal Collected { get; set; }
        public decimal Refund { get; set; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
    }
    private sealed class RevenueSqlTotals
    {
        public decimal Sales { get; set; }
        public decimal Collected { get; set; }
        public decimal Refund { get; set; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
    }

    private async Task<AdminReportResult<RevenueReportRow, RevenueReportTotals>> Revenue(RevenueReportFilter filter, bool page, CancellationToken ct)
    {
        if (!Enum.IsDefined(filter.GroupBy)) throw new LumiCine.Application.Common.ApiException(400, "Invalid groupBy.");
        var f = ReportFilterNormalizer.Normalize(filter, clock.GetUtcNow().UtcDateTime);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var offset = (int)Math.Min(int.MaxValue, ((long)f.Page - 1) * f.PageSize);
        var (keyExpression, labelExpression, dayExpression) = filter.GroupBy switch
        {
            RevenueGroupBy.Movie => ("s.\"MovieId\"::text", "m.\"Title\"", "NULL::date"),
            RevenueGroupBy.Cinema => ("c.\"Id\"::text", "c.\"Name\"", "NULL::date"),
            RevenueGroupBy.Showtime => ("s.\"Id\"::text", "m.\"Title\" || ' · ' || c.\"Name\" || ' · ' || to_char((s.\"StartAtUtc\" AT TIME ZONE 'UTC') + INTERVAL '7 hours', 'YYYY-MM-DD HH24:MI')", "NULL::date"),
            _ => ("to_char((e.\"EventAtUtc\" AT TIME ZONE 'UTC' + INTERVAL '7 hours')::date, 'YYYY-MM-DD')", "to_char((e.\"EventAtUtc\" AT TIME ZONE 'UTC' + INTERVAL '7 hours')::date, 'YYYY-MM-DD')", "((e.\"EventAtUtc\" AT TIME ZONE 'UTC' + INTERVAL '7 hours')::date)")
        };
        var eventSql = $$"""
            WITH Events AS (
                SELECT p."BookingId", p."ProcessedAtUtc" AS "EventAtUtc",
                    CASE WHEN p."Status" = 'Succeeded' AND p."ProcessingStatus" = 'Fulfilled' THEN p."Amount" ELSE 0 END AS "Sales",
                    CASE WHEN p."Status" = 'Succeeded' THEN p."Amount" ELSE 0 END AS "Collected",
                    0::numeric AS "Refund",
                    CASE WHEN p."Status" = 'Succeeded' THEN 1 ELSE 0 END AS "Succeeded",
                    CASE WHEN p."Status" = 'Failed' THEN 1 ELSE 0 END AS "Failed"
                FROM "Payments" p
                WHERE p."ProcessedAtUtc" >= @fromUtc AND p."ProcessedAtUtc" < @toUtc
                    AND p."Status" IN ('Succeeded', 'Failed')
                UNION ALL
                SELECT p."BookingId", p."RefundRecordedAtUtc", 0::numeric, 0::numeric, p."Amount", 0, 0
                FROM "Payments" p
                WHERE p."RefundRecordedAtUtc" >= @fromUtc AND p."RefundRecordedAtUtc" < @toUtc
            ), Dimensioned AS (
                SELECT {{keyExpression}} AS "Key", {{labelExpression}} AS "Label", {{dayExpression}} AS "Day",
                    e."Sales", e."Collected", e."Refund", e."Succeeded", e."Failed"
                FROM Events e
                JOIN "Bookings" b ON e."BookingId" = b."Id"
                JOIN "Showtimes" s ON b."ShowtimeId" = s."Id"
                JOIN "Movies" m ON s."MovieId" = m."Id"
                JOIN "Rooms" r ON s."RoomId" = r."Id"
                JOIN "Cinemas" c ON r."CinemaId" = c."Id"
                WHERE (@movieId IS NULL OR s."MovieId" = @movieId)
                    AND (@cinemaId IS NULL OR c."Id" = @cinemaId)
            ), Grouped AS (
                SELECT "Key", "Label", "Day", SUM("Sales") AS "Sales", SUM("Collected") AS "Collected",
                    SUM("Refund") AS "Refund", SUM("Succeeded")::int AS "Succeeded", SUM("Failed")::int AS "Failed"
                FROM Dimensioned
                GROUP BY "Key", "Label", "Day"
            )
            """;
        var parameters = new object[]
        {
            new NpgsqlParameter("movieId", NpgsqlDbType.Uuid) { Value = f.MovieId is Guid movieId ? movieId : DBNull.Value },
            new NpgsqlParameter("cinemaId", NpgsqlDbType.Uuid) { Value = f.CinemaId is Guid cinemaId ? cinemaId : DBNull.Value },
            new NpgsqlParameter("fromUtc", NpgsqlDbType.TimestampTz) { Value = f.FromUtc },
            new NpgsqlParameter("toUtc", NpgsqlDbType.TimestampTz) { Value = f.ToUtcExclusive }
        };
        // SQL fragments above come only from the closed RevenueGroupBy switch; all request values use parameters.
#pragma warning disable EF1002
        var count = await db.Database.SqlQueryRaw<int>($"""
            {eventSql}
            SELECT COUNT(*)::int AS "Value" FROM Grouped
            """, parameters).SingleAsync(ct);
        var total = filter.GroupBy == RevenueGroupBy.Day
            ? count == 0 && (f.MovieId.HasValue || f.CinemaId.HasValue) ? 0 : f.ToDate.DayNumber - f.FromDate.DayNumber + 1
            : count;
        if (!page && total > ExportLimit) throw new LumiCine.Application.Common.ApiException(422, $"Report exceeds the {ExportLimit} row export limit.");
        var rawTotals = await db.Database.SqlQueryRaw<RevenueSqlTotals>($"""
            {eventSql}
            SELECT COALESCE(SUM("Sales"), 0) AS "Sales", COALESCE(SUM("Collected"), 0) AS "Collected",
                COALESCE(SUM("Refund"), 0) AS "Refund", COALESCE(SUM("Succeeded"), 0)::int AS "Succeeded",
                COALESCE(SUM("Failed"), 0)::int AS "Failed"
            FROM Grouped
            """, parameters).SingleAsync(ct);
        var ordering = filter.GroupBy == RevenueGroupBy.Day ? "\"Day\" ASC" : "\"Sales\" DESC, \"Key\" ASC";
        var paging = page && filter.GroupBy != RevenueGroupBy.Day ? " OFFSET @offset LIMIT @limit" : string.Empty;
        var rowParameters = parameters.Concat(page && filter.GroupBy != RevenueGroupBy.Day ? [new NpgsqlParameter("offset", Math.Max(0, offset)), new NpgsqlParameter("limit", f.PageSize)] : []).ToArray();
        var rawRows = await db.Database.SqlQueryRaw<RevenueSqlRow>($"""
            {eventSql}
            SELECT "Key", "Label", "Sales", "Collected", "Refund", "Succeeded", "Failed"
            FROM Grouped
            ORDER BY {ordering}{paging}
            """, rowParameters).ToListAsync(ct);
#pragma warning restore EF1002
        await tx.CommitAsync(ct);
        var reportRows = rawRows.Select(x => new RevenueReportRow(x.Key, x.Label, x.Sales, x.Collected, x.Refund, x.Collected - x.Refund, x.Succeeded, x.Failed)).ToList();
        if (filter.GroupBy == RevenueGroupBy.Day && total > 0)
        {
            var byDay = reportRows.ToDictionary(x => x.Key);
            reportRows = Enumerable.Range(0, total).Select(i => { var d = f.FromDate.AddDays(i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); return byDay.GetValueOrDefault(d) ?? new RevenueReportRow(d, d, 0, 0, 0, 0, 0, 0); }).ToList();
            if (page) reportRows = reportRows.Skip(offset).Take(f.PageSize).ToList();
        }
        var totals = new RevenueReportTotals(rawTotals.Sales, rawTotals.Collected, rawTotals.Refund,
            rawTotals.Collected - rawTotals.Refund, rawTotals.Succeeded, rawTotals.Failed);
        return new(Metadata(f, "payment ProcessedAtUtc; refunds RefundRecordedAtUtc"), reportRows, total, page ? f.Page : 1, page ? f.PageSize : total, totals);
    }

    public Task<AdminReportResult<ShowtimeReportRow, ShowtimeReportTotals>> GetShowtimesAsync(ReportFilter filter, CancellationToken ct) => Showtimes(filter, true, ct);
    public Task<AdminReportResult<ShowtimeReportRow, ShowtimeReportTotals>> GetShowtimesExportAsync(ReportFilter filter, CancellationToken ct) => Showtimes(filter, false, ct);

    private async Task<AdminReportResult<ShowtimeReportRow, ShowtimeReportTotals>> Showtimes(ReportFilter filter, bool page, CancellationToken ct)
    {
        var f = ReportFilterNormalizer.Normalize(filter, clock.GetUtcNow().UtcDateTime);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await SetSessionUtcAsync(ct);
        var q = from s in db.Showtimes.AsNoTracking()
                join m in db.Movies.AsNoTracking() on s.MovieId equals m.Id
                join r in db.Rooms.AsNoTracking() on s.RoomId equals r.Id
                join c in db.Cinemas.AsNoTracking() on r.CinemaId equals c.Id
                where s.StartAtUtc >= f.FromUtc && s.StartAtUtc < f.ToUtcExclusive &&
                    (!f.MovieId.HasValue || s.MovieId == f.MovieId) && (!f.CinemaId.HasValue || c.Id == f.CinemaId)
                select new { s, m, r, c };
        var total = await q.CountAsync(ct);
        if (!page && total > ReportExportLimits.MaxRows) throw new LumiCine.Application.Common.ApiException(422, $"Report exceeds the {ReportExportLimits.MaxRows} row export limit.");
        var offset = (int)Math.Min(int.MaxValue, ((long)f.Page - 1) * f.PageSize);
        var baseRows = await q.OrderByDescending(x => x.s.StartAtUtc).ThenBy(x => x.s.Id)
            .Skip(page ? offset : 0).Take(page ? f.PageSize : int.MaxValue)
            .Select(x => new { x.s.Id, x.s.MovieId, MovieTitle = x.m.Title, CinemaId = x.c.Id, CinemaName = x.c.Name,
                RoomId = x.r.Id, RoomName = x.r.Name, x.s.StartAtUtc, x.s.EndAtUtc, Status = x.s.Status.ToString() }).ToListAsync(ct);
        var ids = baseRows.Select(x => x.Id).ToArray();
        var seats = await (from seat in db.ShowtimeSeats.AsNoTracking().Where(x => ids.Contains(x.ShowtimeId))
                           join booking in db.Bookings.AsNoTracking() on seat.CurrentBookingId equals booking.Id into bookings
                           from booking in bookings.DefaultIfEmpty()
                           group seat by new { seat.ShowtimeId, Status = seat.Status == ShowtimeSeatStatus.Held && booking != null &&
                               booking.Status == BookingStatus.Pending && booking.HoldExpiresAtUtc <= f.GeneratedAtUtc
                               ? ShowtimeSeatStatus.Available : seat.Status } into g
                           select new { g.Key.ShowtimeId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
        var tickets = await (from t in db.Tickets.AsNoTracking() join i in db.BookingItems.AsNoTracking() on t.BookingItemId equals i.Id
                             where ids.Contains(i.ShowtimeId) && t.Status != TicketStatus.Voided
                             group new { t, i } by i.ShowtimeId into g
                             select new { ShowtimeId = g.Key, Valid = g.Count(), Used = g.Count(x => x.t.Status == TicketStatus.Used), Amount = g.Sum(x => x.i.UnitPrice) }).ToListAsync(ct);
        var seatLookup = seats.ToDictionary(x => (x.ShowtimeId, x.Status), x => x.Count);
        var capacityLookup = seats.GroupBy(x => x.ShowtimeId).ToDictionary(g => g.Key, g => g.Sum(x => x.Count));
        var ticketLookup = tickets.ToDictionary(x => x.ShowtimeId);
        var seatTotals = await (from seat in db.ShowtimeSeats.AsNoTracking()
            where q.Any(x => x.s.Id == seat.ShowtimeId)
            join booking in db.Bookings.AsNoTracking() on seat.CurrentBookingId equals booking.Id into bookings
            from booking in bookings.DefaultIfEmpty()
            group seat by seat.Status == ShowtimeSeatStatus.Held && booking != null && booking.Status == BookingStatus.Pending &&
                booking.HoldExpiresAtUtc <= f.GeneratedAtUtc ? ShowtimeSeatStatus.Available : seat.Status into g
            select new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var allTicketTotals = await (from ticket in db.Tickets.AsNoTracking()
            join item in db.BookingItems.AsNoTracking() on ticket.BookingItemId equals item.Id
            where q.Any(x => x.s.Id == item.ShowtimeId) && ticket.Status != TicketStatus.Voided
            group new { ticket, item } by 1 into g
            select new { Valid = g.Count(), Used = g.Count(x => x.ticket.Status == TicketStatus.Used), Amount = g.Sum(x => x.item.UnitPrice) }).SingleOrDefaultAsync(ct);
        await tx.CommitAsync(ct);
        var output = baseRows.Select(x => {
            int Count(ShowtimeSeatStatus status) => seatLookup.GetValueOrDefault((x.Id, status));
            var capacity = capacityLookup.GetValueOrDefault(x.Id);
            ticketLookup.TryGetValue(x.Id, out var ticket);
            var valid = ticket?.Valid ?? 0; var used = ticket?.Used ?? 0;
            return new ShowtimeReportRow(x.Id, x.MovieId, x.MovieTitle, x.CinemaId, x.CinemaName, x.RoomId, x.RoomName,
                x.StartAtUtc, x.EndAtUtc, x.Status, capacity, Count(ShowtimeSeatStatus.Sold), Count(ShowtimeSeatStatus.Held),
                Count(ShowtimeSeatStatus.Available), Rate(Count(ShowtimeSeatStatus.Sold), capacity), valid, used, Rate(used, valid), ticket?.Amount ?? 0m);
        }).ToArray();
        int Total(ShowtimeSeatStatus status) => seatTotals.Where(x => x.Status == status).Sum(x => x.Count);
        var capacityTotal = seatTotals.Sum(x => x.Count);
        var validTotal = allTicketTotals?.Valid ?? 0;
        var usedTotal = allTicketTotals?.Used ?? 0;
        var totals = new ShowtimeReportTotals(capacityTotal, Total(ShowtimeSeatStatus.Sold), Total(ShowtimeSeatStatus.Held),
            Total(ShowtimeSeatStatus.Available), Rate(Total(ShowtimeSeatStatus.Sold), capacityTotal), validTotal, usedTotal,
            Rate(usedTotal, validTotal), allTicketTotals?.Amount ?? 0m);
        return new(Metadata(f, "showtime StartAtUtc"), output, total, page ? f.Page : 1, page ? f.PageSize : total, totals);
    }

    private static decimal Rate(int numerator, int denominator) => denominator == 0 ? 0 : Math.Round(numerator * 100m / denominator, 2, MidpointRounding.AwayFromZero);
    private Task<int> SetSessionUtcAsync(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("SET LOCAL TIME ZONE 'UTC'", ct);
    private static ReportMetadata Metadata(NormalizedReportFilter f, string basis) => new(f.GeneratedAtUtc, f.FromDate, f.ToDate, TimeBasis: basis);

    private IQueryable<Payment> FilterPaymentBookings(IQueryable<Payment> query, NormalizedReportFilter f) =>
        from p in query join b in db.Bookings.AsNoTracking() on p.BookingId equals b.Id
        join s in db.Showtimes.AsNoTracking() on b.ShowtimeId equals s.Id join r in db.Rooms.AsNoTracking() on s.RoomId equals r.Id
        where (!f.MovieId.HasValue || s.MovieId == f.MovieId) && (!f.CinemaId.HasValue || r.CinemaId == f.CinemaId) select p;

    private IQueryable<Booking> FilterShowtimes(IQueryable<Booking> q, NormalizedReportFilter f) =>
        from b in q join s in db.Showtimes.AsNoTracking() on b.ShowtimeId equals s.Id
        join r in db.Rooms.AsNoTracking() on s.RoomId equals r.Id
        where (!f.MovieId.HasValue || s.MovieId == f.MovieId) && (!f.CinemaId.HasValue || r.CinemaId == f.CinemaId) select b;
    private IQueryable<Ticket> FilterTicketDates(IQueryable<Ticket> q, NormalizedReportFilter f) =>
        from t in q join i in db.BookingItems.AsNoTracking() on t.BookingItemId equals i.Id
        join s in db.Showtimes.AsNoTracking() on i.ShowtimeId equals s.Id join r in db.Rooms.AsNoTracking() on s.RoomId equals r.Id
        where (!f.MovieId.HasValue || s.MovieId == f.MovieId) && (!f.CinemaId.HasValue || r.CinemaId == f.CinemaId) select t;
    private IQueryable<CheckInLog> FilterCheckIns(IQueryable<CheckInLog> q, NormalizedReportFilter f)
    {
        if (f.MovieId is null && f.CinemaId is null) return q;
        return
        from l in q join s in db.Showtimes.AsNoTracking() on l.ShowtimeId equals s.Id
        join r in db.Rooms.AsNoTracking() on s.RoomId equals r.Id
        where (!f.MovieId.HasValue || s.MovieId == f.MovieId) && (!f.CinemaId.HasValue || r.CinemaId == f.CinemaId) select l;
    }

}
