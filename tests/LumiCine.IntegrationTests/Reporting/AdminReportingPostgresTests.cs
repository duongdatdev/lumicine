using LumiCine.Application.Reporting;
using LumiCine.Application.Accounts;
using LumiCine.Domain.Bookings;
using LumiCine.Domain.Catalog;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.Infrastructure.Reporting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace LumiCine.IntegrationTests.Persistence;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class AdminReportingPostgresTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Revenue_uses_real_postgres_and_separates_payment_and_refund_local_dates()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, cinema, room) = await DatabaseTestData.CreateBaseAsync(db);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var booking = new Booking(Guid.NewGuid(), $"B-{Guid.NewGuid():N}", showtime.Id, 200m,
            new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc), BookingStatus.Pending,
            new DateTime(2026, 11, 15, 12, 30, 0, DateTimeKind.Utc));
        await db.Bookings.AddAsync(booking);

        var processedAt = new DateTime(2026, 11, 15, 16, 59, 59, DateTimeKind.Utc);
        var refundedAt = new DateTime(2026, 11, 15, 17, 0, 0, DateTimeKind.Utc);
        var fulfilled = CreatePayment(booking.Id, 100m, PaymentProcessingStatus.Fulfilled, processedAt);
        var refund = CreatePayment(booking.Id, 100m, PaymentProcessingStatus.RequiresRefundReview, processedAt);
        refund.RecordRefund(refundedAt, Guid.NewGuid(), "refund-1", null);
        var failed = new Payment(Guid.NewGuid(), booking.Id, Guid.NewGuid(), $"P-{Guid.NewGuid():N}", 50m,
            "VND", processedAt.AddHours(1), processedAt.AddHours(-1), PaymentStatus.Pending);
        failed.MarkFailed(processedAt, "24", "Failed");
        await db.Payments.AddRangeAsync(fulfilled, refund, failed);
        await db.SaveChangesAsync();

        var service = new AdminReportingService(db, new FixedTimeProvider(new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero)));
        var filter = new RevenueReportFilter(new DateOnly(2026, 11, 14), new DateOnly(2026, 11, 17),
            MovieId: movie.Id, GroupBy: RevenueGroupBy.Day);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET TIME ZONE 'Asia/Bangkok'");
        var result = await service.GetRevenueAsync(filter, CancellationToken.None);

        Assert.Equal(new[] { "2026-11-14", "2026-11-15", "2026-11-16", "2026-11-17" }, result.Rows.Select(x => x.Key));
        Assert.Equal(4, result.TotalCount);
        Assert.Equal(0m, result.Rows[0].CollectedAmount);
        Assert.Equal(100m, result.Rows[1].TicketSalesAmount);
        Assert.Equal(200m, result.Rows[1].CollectedAmount);
        Assert.Equal(0m, result.Rows[1].RecordedRefundAmount);
        Assert.Equal(0m, result.Rows[2].CollectedAmount);
        Assert.Equal(100m, result.Rows[2].RecordedRefundAmount);
        Assert.Equal(-100m, result.Rows[2].NetCollectedAmount);
        Assert.Equal(0m, result.Rows[3].NetCollectedAmount);
        Assert.Equal(new RevenueReportTotals(100m, 200m, 100m, 100m, 2, 1), result.Totals);
        Assert.Contains("ProcessedAtUtc", result.Metadata.TimeBasis, StringComparison.Ordinal);
        Assert.Contains("RefundRecordedAtUtc", result.Metadata.TimeBasis, StringComparison.Ordinal);

        var paged = await service.GetRevenueAsync(filter with { Page = 2, PageSize = 2 }, CancellationToken.None);
        Assert.Equal(4, paged.TotalCount);
        Assert.Equal(new[] { "2026-11-16", "2026-11-17" }, paged.Rows.Select(x => x.Key));
        Assert.Equal(result.Totals, paged.Totals);
        var sessionTimeZone = await db.Database.SqlQueryRaw<string>("SELECT current_setting('TimeZone') AS \"Value\"").SingleAsync();
        Assert.Equal("Asia/Bangkok", sessionTimeZone);
        await db.Database.CloseConnectionAsync();

        var independent = await ReadIndependentPaymentTotalsAsync(db, booking.Id, processedAt, refundedAt);
        Assert.Equal((2, 200m, 100m), independent);
    }

    [Fact]
    public async Task Revenue_groups_by_movie_cinema_and_showtime_with_fixture_local_keys()
    {
        await using var db = fixture.CreateDbContext();
        var (firstMovie, cinema, room) = await DatabaseTestData.CreateBaseAsync(db);
        var timestamp = DateTime.UtcNow;
        firstMovie.Update("Alpha", null, null, 90, null, null, timestamp);
        cinema.Update(cinema.Code, "Scoped Cinema", cinema.Address, cinema.City, timestamp);
        await db.SaveChangesAsync();
        var firstShowtime = await DatabaseTestData.CreateShowtimeAsync(db, firstMovie.Id, room.Id, 0);
        var firstBooking = await DatabaseTestData.CreateBookingAsync(db, firstShowtime.Id);
        var firstPayment = CreatePayment(firstBooking.Id, 100m, PaymentProcessingStatus.Fulfilled,
            new DateTime(2026, 11, 15, 5, 0, 0, DateTimeKind.Utc));
        var secondMovie = new Movie(Guid.NewGuid(), "Beta", 90, timestamp);
        await db.Movies.AddAsync(secondMovie);
        await db.SaveChangesAsync();
        var secondShowtime = await DatabaseTestData.CreateShowtimeAsync(db, secondMovie.Id, room.Id, 120);
        var secondBooking = await DatabaseTestData.CreateBookingAsync(db, secondShowtime.Id);
        var secondPayment = CreatePayment(secondBooking.Id, 200m, PaymentProcessingStatus.Fulfilled,
            new DateTime(2026, 11, 15, 5, 0, 0, DateTimeKind.Utc));
        await db.Payments.AddRangeAsync(firstPayment, secondPayment);
        await db.SaveChangesAsync();
        var service = new AdminReportingService(db,
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero)));
        var commonFilter = new RevenueReportFilter(new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 15),
            CinemaId: cinema.Id, PageSize: 2);

        var byMovie = await service.GetRevenueAsync(commonFilter with { GroupBy = RevenueGroupBy.Movie }, CancellationToken.None);
        Assert.Equal(2, byMovie.TotalCount);
        Assert.Equal(new[] { "Beta", "Alpha" }, byMovie.Rows.Select(x => x.Label));
        Assert.Equal(secondMovie.Id.ToString(), byMovie.Rows[0].Key);
        Assert.Equal(200m, byMovie.Rows[0].TicketSalesAmount);
        Assert.Equal(100m, byMovie.Rows[1].TicketSalesAmount);
        Assert.Equal(300m, byMovie.Totals.CollectedAmount);

        var byCinema = await service.GetRevenueAsync(commonFilter with { GroupBy = RevenueGroupBy.Cinema }, CancellationToken.None);
        Assert.Equal(1, byCinema.TotalCount);
        Assert.Equal(cinema.Id.ToString(), byCinema.Rows[0].Key);
        Assert.Equal("Scoped Cinema", byCinema.Rows[0].Label);
        Assert.Equal(300m, byCinema.Rows[0].TicketSalesAmount);

        var byShowtime = await service.GetRevenueAsync(commonFilter with { GroupBy = RevenueGroupBy.Showtime }, CancellationToken.None);
        Assert.Equal(2, byShowtime.TotalCount);
        Assert.Equal(secondShowtime.Id.ToString(), byShowtime.Rows[0].Key);
        Assert.Equal($"Beta · Scoped Cinema · {secondShowtime.StartAtUtc.AddHours(7).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}",
            byShowtime.Rows[0].Label);
        Assert.Equal(100m, byShowtime.Rows[1].TicketSalesAmount);
        Assert.Equal(firstShowtime.Id.ToString(), byShowtime.Rows[1].Key);
    }

    [Fact]
    public async Task Showtime_rows_use_seat_and_ticket_snapshots_and_correct_expired_holds_across_pages()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var earlier = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id, 0);
        var later = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id, 120);
        var now = DateTime.UtcNow.AddDays(2);
        var expiredBooking = new Booking(Guid.NewGuid(), $"B-{Guid.NewGuid():N}", earlier.Id, 120m,
            now.AddDays(-3), BookingStatus.Pending, now.AddMinutes(-1));
        var currentBooking = new Booking(Guid.NewGuid(), $"B-{Guid.NewGuid():N}", earlier.Id, 80m,
            now.AddDays(-2), BookingStatus.Pending, now.AddMinutes(30));
        var soldBooking = new Booking(Guid.NewGuid(), $"B-{Guid.NewGuid():N}", earlier.Id, 200m,
            now.AddDays(-1), BookingStatus.Confirmed);
        await db.Bookings.AddRangeAsync(expiredBooking, currentBooking, soldBooking);
        await db.SaveChangesAsync();

        var soldSeat = await DatabaseTestData.CreateSeatAsync(db, room.Id, "B", 1);
        var expiredSeat = await DatabaseTestData.CreateSeatAsync(db, room.Id, "B", 2);
        var currentSeat = await DatabaseTestData.CreateSeatAsync(db, room.Id, "B", 3);
        var availableSeat = await DatabaseTestData.CreateSeatAsync(db, room.Id, "B", 4);
        var soldSnapshot = await DatabaseTestData.CreateShowtimeSeatAsync(db, earlier, soldSeat);
        soldSnapshot.Hold(soldBooking.Id);
        soldSnapshot.Sell(soldBooking.Id);
        var expiredSnapshot = await DatabaseTestData.CreateShowtimeSeatAsync(db, earlier, expiredSeat);
        expiredSnapshot.Hold(expiredBooking.Id);
        var currentSnapshot = await DatabaseTestData.CreateShowtimeSeatAsync(db, earlier, currentSeat);
        currentSnapshot.Hold(currentBooking.Id);
        var availableSnapshot = await DatabaseTestData.CreateShowtimeSeatAsync(db, earlier, availableSeat);
        await db.SaveChangesAsync();

        var soldItem = new BookingItem(Guid.NewGuid(), soldBooking.Id, earlier.Id, soldSnapshot.Id, 120m, now.AddDays(-1));
        var expiredItem = new BookingItem(Guid.NewGuid(), expiredBooking.Id, earlier.Id, expiredSnapshot.Id, 80m, now.AddDays(-3));
        var voidedItem = new BookingItem(Guid.NewGuid(), soldBooking.Id, earlier.Id, availableSnapshot.Id, 999m, now.AddDays(-1));
        await db.BookingItems.AddRangeAsync(soldItem, expiredItem, voidedItem);
        await db.SaveChangesAsync();
        await db.Tickets.AddRangeAsync(
            new Ticket(Guid.NewGuid(), soldItem.Id, $"T-{Guid.NewGuid():N}", now.AddDays(-1), now.AddDays(-1)),
            new Ticket(Guid.NewGuid(), expiredItem.Id, $"T-{Guid.NewGuid():N}", now.AddDays(-3), now.AddDays(-3), TicketStatus.Used, now.AddDays(-2)),
            new Ticket(Guid.NewGuid(), voidedItem.Id, $"T-{Guid.NewGuid():N}", now.AddDays(-1), now.AddDays(-1), TicketStatus.Voided));
        await db.SaveChangesAsync();

        var localStartDay = DateOnly.FromDateTime(earlier.StartAtUtc.AddHours(7));
        var service = new AdminReportingService(db, new FixedTimeProvider(new DateTimeOffset(now, TimeSpan.Zero)));
        var filter = new ReportFilter(localStartDay, localStartDay.AddDays(1), MovieId: movie.Id, Page: 1, PageSize: 1);
        var firstPage = await service.GetShowtimesAsync(filter, CancellationToken.None);
        var secondPage = await service.GetShowtimesAsync(filter with { Page = 2 }, CancellationToken.None);

        Assert.Equal(2, firstPage.TotalCount);
        Assert.Single(firstPage.Rows);
        Assert.Equal(later.Id, firstPage.Rows[0].ShowtimeId);
        Assert.Equal(0, firstPage.Rows[0].Capacity);
        Assert.Equal(0m, firstPage.Rows[0].OccupancyRate);
        Assert.Equal(2, secondPage.Page);
        Assert.Equal(earlier.Id, secondPage.Rows[0].ShowtimeId);
        Assert.Equal(4, secondPage.Rows[0].Capacity);
        Assert.Equal(1, secondPage.Rows[0].SoldSeats);
        Assert.Equal(1, secondPage.Rows[0].HeldSeats);
        Assert.Equal(2, secondPage.Rows[0].AvailableSeats);
        Assert.Equal(25m, secondPage.Rows[0].OccupancyRate);
        Assert.Equal(2, secondPage.Rows[0].ValidTickets);
        Assert.Equal(1, secondPage.Rows[0].UsedTickets);
        Assert.Equal(50m, secondPage.Rows[0].CheckInRate);
        Assert.Equal(200m, secondPage.Rows[0].IssuedTicketAmount);
        Assert.Equal(new ShowtimeReportTotals(4, 1, 1, 2, 25m, 2, 1, 50m, 200m), secondPage.Totals);
    }

    [Fact]
    public async Task Summary_and_operations_expose_successful_payments_missing_processing_timestamps()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, cinema, room) = await DatabaseTestData.CreateBaseAsync(db);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var booking = await DatabaseTestData.CreateBookingAsync(db, showtime.Id);
        var payment = new Payment(Guid.NewGuid(), booking.Id, Guid.NewGuid(), $"MISSING-{Guid.NewGuid():N}", 250m,
            "VND", DateTime.UtcNow.AddHours(1), DateTime.UtcNow, PaymentStatus.Succeeded,
            PaymentProcessingStatus.RequiresRefundReview);
        await db.Payments.AddAsync(payment);
        await db.SaveChangesAsync();

        var service = new AdminReportingService(db, new FixedTimeProvider(DateTimeOffset.UtcNow.AddMinutes(1)));
        var summary = await service.GetSummaryAsync(new ReportFilter(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)), CancellationToken.None);
        var operations = await service.GetOperationsAsync(new ReportFilter(MovieId: movie.Id, CinemaId: cinema.Id), CancellationToken.None);

        Assert.Equal(1, summary.PaymentsMissingRequiredTimestampCount);
        Assert.Equal(1, operations.PaymentsMissingProcessedAtCount);
        Assert.Equal(new[] { payment.Id }, operations.PaymentsMissingProcessedAtIds);
        Assert.Equal(1, operations.RefundReviewCount);
        Assert.Equal(250m, operations.RefundReviewAmount);
        Assert.Equal(new[] { payment.Id }, operations.RefundReviewPaymentIds);
    }

    [Fact]
    public async Task Admin_can_follow_filtered_dashboard_to_report_and_csv_over_https_origin()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        var email = $"report-admin-{Guid.NewGuid():N}@example.test";
        var sessionId = Guid.NewGuid();
        Guid userId;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true,
                DisplayName = "Reporting Admin", CreatedAtUtc = DateTime.UtcNow };
            var created = await users.CreateAsync(user);
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));
            var role = await users.AddToRoleAsync(user, RoleNames.Admin);
            Assert.True(role.Succeeded, string.Join("; ", role.Errors.Select(error => error.Description)));
            userId = user.Id;
            var db = scope.ServiceProvider.GetRequiredService<LumiCineDbContext>();
            db.AuthSessions.Add(new AuthSession { Id = sessionId, UserId = userId, CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1) });
            await db.SaveChangesAsync();
        }

        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(userId, sessionId, [RoleNames.Admin]).Value;
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var movieId = Guid.NewGuid();
        var cinemaId = Guid.NewGuid();
        const string range = "fromDate=2026-11-01&toDate=2026-11-02";

        using var summary = await client.GetAsync($"/api/admin/dashboard/summary?{range}&movieId={movieId:D}&cinemaId={cinemaId:D}");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        Assert.Contains("no-store", summary.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        using var revenue = await client.GetAsync($"/api/admin/reports/revenue?{range}&groupBy=movie&movieId={movieId:D}&cinemaId={cinemaId:D}");
        Assert.Equal(HttpStatusCode.OK, revenue.StatusCode);
        using var showtimes = await client.GetAsync($"/api/admin/reports/showtimes?{range}&movieId={movieId:D}&cinemaId={cinemaId:D}");
        Assert.Equal(HttpStatusCode.OK, showtimes.StatusCode);
        using var export = await client.GetAsync($"/api/admin/reports/revenue/export?{range}&groupBy=movie&movieId={movieId:D}&cinemaId={cinemaId:D}");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("0", export.Headers.GetValues("X-Report-Row-Count").Single());
        var csv = await export.Content.ReadAsByteArrayAsync();
        Assert.True(csv.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    [Fact]
    public async Task Summary_keeps_totals_on_one_repeatable_read_snapshot_during_concurrent_refund_insert()
    {
        await using var seedDb = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(seedDb);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(seedDb, movie.Id, room.Id);
        var booking = await DatabaseTestData.CreateBookingAsync(seedDb, showtime.Id);
        var now = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        var pause = new PauseAfterPaymentAggregateInterceptor();
        var options = new DbContextOptionsBuilder<LumiCineDbContext>().UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(pause).Options;
        await using var reportDb = new LumiCineDbContext(options);
        var service = new AdminReportingService(reportDb, new FixedTimeProvider(new DateTimeOffset(now, TimeSpan.Zero)));
        var reportFilter = new ReportFilter(new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 16), MovieId: movie.Id);
        var reportTask = service.GetSummaryAsync(reportFilter, CancellationToken.None);

        try
        {
            await pause.Paused.WaitAsync(TimeSpan.FromSeconds(10));
            var concurrent = CreatePayment(booking.Id, 100m, PaymentProcessingStatus.RequiresRefundReview,
                new DateTime(2026, 11, 16, 1, 0, 0, DateTimeKind.Utc));
            concurrent.RecordRefund(new DateTime(2026, 11, 16, 2, 0, 0, DateTimeKind.Utc), Guid.NewGuid(), "race-refund", null);
            await using var writer = fixture.CreateDbContext();
            await writer.Payments.AddAsync(concurrent);
            await writer.SaveChangesAsync();
        }
        finally
        {
            pause.Resume();
        }

        var result = await reportTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0m, result.CollectedAmount);
        Assert.Equal(0m, result.RecordedRefundAmount);
        Assert.Equal(0m, result.NetCollectedAmount);
        await using var verifyDb = fixture.CreateDbContext();
        var afterCommit = await new AdminReportingService(verifyDb, new FixedTimeProvider(new DateTimeOffset(now, TimeSpan.Zero)))
            .GetSummaryAsync(reportFilter, CancellationToken.None);
        Assert.Equal(100m, afterCommit.CollectedAmount);
        Assert.Equal(100m, afterCommit.RecordedRefundAmount);
        Assert.Equal(0m, afterCommit.NetCollectedAmount);
    }

    [Fact]
    public async Task Reporting_timestamp_indexes_exist_and_are_selected_by_postgres()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, room) = await DatabaseTestData.CreateBaseAsync(db);
        var showtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, room.Id);
        var booking = await DatabaseTestData.CreateBookingAsync(db, showtime.Id);
        var events = Enumerable.Range(0, 5_000).Select(index =>
        {
            var at = new DateTime(2025, 11, 16, 0, 0, 0, DateTimeKind.Utc).AddDays(index % 365).AddMinutes(index % 1440);
            var payment = new Payment(Guid.NewGuid(), booking.Id, Guid.NewGuid(), $"PLAN-{Guid.NewGuid():N}", 1m,
                "VND", at.AddHours(1), at.AddMinutes(-1));
            payment.MarkFailed(at, "24", "Failed");
            return payment;
        }).ToArray();
        await db.Payments.AddRangeAsync(events);
        await db.SaveChangesAsync();

        var names = await db.Database.SqlQueryRaw<string>("""
            SELECT indexname AS "Value" FROM pg_indexes
            WHERE schemaname = current_schema()
              AND indexname IN (
                'IX_Payments_ProcessedAtUtc', 'IX_Payments_RefundRecordedAtUtc',
                'IX_Bookings_CreatedAtUtc', 'IX_Tickets_IssuedAtUtc',
                'IX_Tickets_UsedAtUtc', 'IX_CheckInLogs_LoggedAtUtc')
            """).ToListAsync();

        Assert.Equal(6, names.Count);
        await db.Database.ExecuteSqlRawAsync("ANALYZE \"Payments\"");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            EXPLAIN (ANALYZE, FORMAT TEXT)
            SELECT "Id" FROM "Payments"
            WHERE "ProcessedAtUtc" >= TIMESTAMPTZ '2026-11-15 00:00:00+00'
              AND "ProcessedAtUtc" < TIMESTAMPTZ '2026-11-16 00:00:00+00'
            """;
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var reader = await command.ExecuteReaderAsync();
            var plan = new List<string>();
            while (await reader.ReadAsync()) plan.Add(reader.GetString(0));
            Assert.Contains(plan, line => line.Contains("IX_Payments_ProcessedAtUtc", StringComparison.Ordinal));
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    [Fact]
    public async Task Reporting_index_migration_moves_down_and_up_without_model_drift()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, _, _) = await DatabaseTestData.CreateBaseAsync(db);
        var migrator = db.GetService<IMigrator>();
        const string previousMigration = "20261114175152_AddPaymentRefunds";

        await migrator.MigrateAsync(previousMigration);
        var removedCount = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::integer AS "Value" FROM pg_indexes
            WHERE schemaname = current_schema()
              AND indexname IN (
                'IX_Payments_ProcessedAtUtc', 'IX_Payments_RefundRecordedAtUtc',
                'IX_Bookings_CreatedAtUtc', 'IX_Tickets_IssuedAtUtc',
                'IX_Tickets_UsedAtUtc', 'IX_CheckInLogs_LoggedAtUtc')
            """).SingleAsync();
        Assert.Equal(0, removedCount);

        await migrator.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        var restoredCount = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::integer AS "Value" FROM pg_indexes
            WHERE schemaname = current_schema()
              AND indexname IN (
                'IX_Payments_ProcessedAtUtc', 'IX_Payments_RefundRecordedAtUtc',
                'IX_Bookings_CreatedAtUtc', 'IX_Tickets_IssuedAtUtc',
                'IX_Tickets_UsedAtUtc', 'IX_CheckInLogs_LoggedAtUtc')
            """).SingleAsync();
        Assert.Equal(6, restoredCount);
        Assert.True(await db.Movies.AnyAsync(x => x.Id == movie.Id));
    }

    private static Payment CreatePayment(Guid bookingId, decimal amount, PaymentProcessingStatus processingStatus, DateTime processedAt)
    {
        var payment = new Payment(Guid.NewGuid(), bookingId, Guid.NewGuid(), $"P-{Guid.NewGuid():N}", amount,
            "VND", processedAt.AddHours(1), processedAt.AddHours(-1), PaymentStatus.Pending);
        payment.MarkSucceeded(processedAt, null, "00", "00", processingStatus);
        return payment;
    }

    private static async Task<(int Count, decimal Collected, decimal Refunded)> ReadIndependentPaymentTotalsAsync(
        LumiCineDbContext db, Guid bookingId, DateTime processedAt, DateTime refundedAt)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FILTER (WHERE "Status" = 'Succeeded' AND "ProcessedAtUtc" = @processed),
                   COALESCE(SUM("Amount") FILTER (WHERE "Status" = 'Succeeded' AND "ProcessedAtUtc" = @processed), 0),
                   COALESCE(SUM("Amount") FILTER (WHERE "RefundRecordedAtUtc" = @refunded), 0)
            FROM "Payments" WHERE "BookingId" = @booking
            """;
        var booking = command.CreateParameter(); booking.ParameterName = "booking"; booking.Value = bookingId;
        var processed = command.CreateParameter(); processed.ParameterName = "processed"; processed.Value = processedAt;
        var refunded = command.CreateParameter(); refunded.ParameterName = "refunded"; refunded.Value = refundedAt;
        command.Parameters.Add(booking); command.Parameters.Add(processed); command.Parameters.Add(refunded);
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetInt32(0), reader.GetDecimal(1), reader.GetDecimal(2));
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class PauseAfterPaymentAggregateInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _captured;

        public Task Paused => _paused.Task;
        public void Resume() => _resume.TrySetResult();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"Payments\"", StringComparison.Ordinal) &&
                command.CommandText.Contains("SUM(", StringComparison.OrdinalIgnoreCase) &&
                !command.CommandText.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref _captured, 1) == 0)
            {
                _paused.TrySetResult();
                await _resume.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
