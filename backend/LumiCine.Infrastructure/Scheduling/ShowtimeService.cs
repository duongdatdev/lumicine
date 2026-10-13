using System.Globalization;
using System.Text.RegularExpressions;
using LumiCine.Domain.Bookings;
using LumiCine.Application.Common;
using LumiCine.Application.Scheduling;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace LumiCine.Infrastructure.Scheduling;

public sealed class ShowtimeService(
    LumiCineDbContext db,
    TimeProvider clock,
    IShowtimeAvailabilityNotifier? notifier = null) : IShowtimeService
{
    public Task<PagedResult<ShowtimeSummary>> GetPublicAsync(ShowtimeQuery query, CancellationToken ct) => ListAsync(query, false, ct);
    public Task<PagedResult<ShowtimeSummary>> GetManagementAsync(ShowtimeQuery query, CancellationToken ct) => ListAsync(query, true, ct);

    private async Task<PagedResult<ShowtimeSummary>> ListAsync(ShowtimeQuery request, bool management, CancellationToken ct)
    {
        var paging = request.Paging.Validate();
        var offset = (long)(paging.Page - 1) * paging.PageSize;
        if (offset > int.MaxValue) throw new ApiException(400, "Page is too large.");
        var today = request.Date ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(7));
        if (today < DateOnly.MinValue.AddDays(1) || today > DateOnly.MaxValue.AddDays(-1))
            throw new ApiException(400, "Date is outside the supported range.");
        DateTime from;
        DateTime to;
        try
        {
            from = DateTime.SpecifyKind(today.ToDateTime(TimeOnly.MinValue).AddHours(-7), DateTimeKind.Utc);
            to = from.AddDays(1);
        }
        catch (ArgumentOutOfRangeException) { throw new ApiException(400, "Date is outside the supported range."); }
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = from s in db.Showtimes.AsNoTracking()
                   join m in db.Movies.AsNoTracking() on s.MovieId equals m.Id
                   join r in db.Rooms.AsNoTracking() on s.RoomId equals r.Id
                   join c in db.Cinemas.AsNoTracking() on r.CinemaId equals c.Id
                   select new { s, m, r, c };
        if (request.MovieId is Guid movieId) rows = rows.Where(x => x.s.MovieId == movieId);
        if (request.CinemaId is Guid cinemaId) rows = rows.Where(x => x.c.Id == cinemaId);
        if (request.Date is not null || !management) rows = rows.Where(x => x.s.StartAtUtc >= from && x.s.StartAtUtc < to);
        if (!management) rows = rows.Where(x => x.s.Status == ShowtimeStatus.Scheduled && x.s.StartAtUtc > now && x.m.IsActive && x.r.IsActive && x.c.IsActive);
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderBy(x => x.s.StartAtUtc).ThenBy(x => x.s.Id)
            .Skip((int)offset).Take(paging.PageSize)
            .Select(x => new ShowtimeSummary(x.s.Id, x.m.Id, x.m.Title, x.c.Id, x.c.Name, x.r.Id, x.r.Name,
                x.s.StartAtUtc, x.s.EndAtUtc, x.s.BasePrice, x.s.Status.ToString())).ToListAsync(ct);
        return new(items, total, paging.Page, paging.PageSize);
    }

    public Task<ShowtimeSummary?> GetPublicAsync(Guid id, CancellationToken ct) => GetSummaryAsync(id, false, ct);
    public Task<ShowtimeSummary?> GetManagementAsync(Guid id, CancellationToken ct) => GetSummaryAsync(id, true, ct);

    private async Task<ShowtimeSummary?> GetSummaryAsync(Guid id, bool management, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = from s in db.Showtimes.AsNoTracking()
                   join m in db.Movies.AsNoTracking() on s.MovieId equals m.Id
                   join r in db.Rooms.AsNoTracking() on s.RoomId equals r.Id
                   join c in db.Cinemas.AsNoTracking() on r.CinemaId equals c.Id
                   where s.Id == id
                   select new { s, m, r, c };
        if (!management) rows = rows.Where(x => x.s.Status == ShowtimeStatus.Scheduled && x.s.StartAtUtc > now && x.m.IsActive && x.r.IsActive && x.c.IsActive);
        return await rows.Select(x => new ShowtimeSummary(x.s.Id, x.m.Id, x.m.Title, x.c.Id, x.c.Name, x.r.Id, x.r.Name,
            x.s.StartAtUtc, x.s.EndAtUtc, x.s.BasePrice, x.s.Status.ToString())).SingleOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ShowtimeSeatSummary>?> GetSeatsAsync(Guid id, bool management, CancellationToken ct)
    {
        var showtime = await db.Showtimes.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Id, x.RoomId, x.Status, x.StartAtUtc }).SingleOrDefaultAsync(ct);
        if (showtime is null) return null;
        if (!management)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            if (showtime.Status != ShowtimeStatus.Scheduled || showtime.StartAtUtc <= now ||
                !await db.Showtimes.Where(x => x.Id == id).Join(db.Rooms, x => x.RoomId, x => x.Id, (x, r) => new { x, r })
                    .Join(db.Cinemas, x => x.r.CinemaId, x => x.Id, (x, c) => new { x, c })
                    .Join(db.Movies, x => x.x.x.MovieId, x => x.Id, (x, m) => x.x.r.IsActive && x.c.IsActive && m.IsActive).SingleAsync(ct)) return null;
        }
        var projectionNow = clock.GetUtcNow().UtcDateTime;
        var rows = await (from ss in db.ShowtimeSeats.AsNoTracking()
                          join seat in db.Seats.AsNoTracking() on new { ss.SeatId, ss.RoomId } equals new { SeatId = seat.Id, seat.RoomId }
                          join booking in db.Bookings.AsNoTracking() on ss.CurrentBookingId equals booking.Id into bookings
                          from booking in bookings.DefaultIfEmpty()
                          where ss.ShowtimeId == id && (management || seat.IsActive)
                          orderby seat.RowLabel, seat.Number
                          select new { seat, ss, booking })
            .ToListAsync(ct);

        return rows.Select(row =>
        {
            var status = row.ss.Status;
            if (!management && status == ShowtimeSeatStatus.Held && row.booking is not null &&
                (row.booking.Status == BookingStatus.Expired ||
                 (row.booking.Status == BookingStatus.Pending && row.booking.HoldExpiresAtUtc <= projectionNow)))
                status = ShowtimeSeatStatus.Available;
            return new ShowtimeSeatSummary(row.seat.Id, row.seat.RowLabel, row.seat.Number,
                row.seat.SeatType, status.ToString(), row.ss.Price);
        }).ToList();
    }

    public async Task<ShowtimeSummary> CreateAsync(ShowtimeRequest request, CancellationToken ct)
        => await CreateCoreAsync(Guid.NewGuid(), request, ct);

    public Task<ShowtimeSummary> CreateWithIdAsync(Guid id, ShowtimeRequest request, CancellationToken ct) =>
        CreateCoreAsync(id, request, ct);

    private async Task<ShowtimeSummary> CreateCoreAsync(Guid id, ShowtimeRequest request, CancellationToken ct)
    {
        if (request.MovieId == Guid.Empty || request.RoomId == Guid.Empty || id == Guid.Empty)
            throw new ApiException(400, "Movie, room, and showtime identifiers must be valid.");
        var start = ParseStart(request.StartAt);
        var now = clock.GetUtcNow().UtcDateTime;
        if (start <= now) throw new ApiException(400, "Showtime start must be in the future.");
        if (!ValidPrice(request.BasePrice)) throw new ApiException(400, "Base price must be nonnegative and have at most two decimal places.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var roomHint = await db.Rooms.AsNoTracking().Where(x => x.Id == request.RoomId).Select(x => new { x.Id, x.CinemaId }).SingleOrDefaultAsync(ct)
                ?? throw new ApiException(404, "Room was not found.");
            await LockAsync("Movies", request.MovieId, ct);
            var movie = await db.Movies.SingleOrDefaultAsync(x => x.Id == request.MovieId, ct) ?? throw new ApiException(404, "Movie was not found.");
            await LockAsync("Cinemas", roomHint.CinemaId, ct);
            var cinema = await db.Cinemas.SingleOrDefaultAsync(x => x.Id == roomHint.CinemaId, ct) ?? throw new ApiException(404, "Cinema was not found.");
            await LockAsync("Rooms", request.RoomId, ct);
            var room = await db.Rooms.SingleOrDefaultAsync(x => x.Id == request.RoomId, ct) ?? throw new ApiException(404, "Room was not found.");
            now = clock.GetUtcNow().UtcDateTime;
            if (start <= now) throw new ApiException(400, "Showtime start must be in the future.");
            if (!movie.IsActive || !cinema.IsActive || !room.IsActive) throw new ApiException(409, "Movie, cinema, and room must be active.");
            var seats = await db.Seats.Where(x => x.RoomId == room.Id && x.IsActive).ToListAsync(ct);
            if (seats.Count == 0) throw new ApiException(409, "The room must have at least one active seat.");
            var duration = TimeSpan.FromMinutes(movie.DurationMinutes);
            if (start.Ticks > DateTime.MaxValue.Ticks - duration.Ticks) throw new ApiException(400, "Showtime end is outside the supported range.");
            var showtime = new Showtime(id, movie.Id, room.Id, start, start.Add(duration), request.BasePrice, now);
            db.Showtimes.Add(showtime);
            db.ShowtimeSeats.AddRange(seats.Select(seat => new ShowtimeSeat(Guid.NewGuid(), showtime.Id, room.Id, seat.Id, request.BasePrice, now)));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            if (notifier is not null) await notifier.NotifyAsync(showtime.Id, ct);
            return await GetSummaryAsync(showtime.Id, true, ct) ?? throw new InvalidOperationException("Created showtime disappeared.");
        }
        catch (DbUpdateException ex) when (FindPostgres(ex)?.SqlState == PostgresErrorCodes.ExclusionViolation)
        {
            await tx.RollbackAsync(ct);
            throw new ApiException(409, "The room already has a showtime in that period.");
        }
    }

    public async Task<ShowtimeSummary?> RescheduleAsync(Guid id, RescheduleShowtimeRequest request, CancellationToken ct)
    {
        var start = ParseStart(request.StartAt);
        var now = clock.GetUtcNow().UtcDateTime;
        if (start <= now || !ValidPrice(request.BasePrice)) throw new ApiException(400, "Start must be in the future and price must be nonnegative with at most two decimal places.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var hint = await db.Showtimes.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.MovieId, x.RoomId }).SingleOrDefaultAsync(ct);
        if (hint is null) return null;
        var parent = await GetRoomCinemaAsync(hint.RoomId, ct);
        if (parent is null) return null;
        await LockAsync("Movies", hint.MovieId, ct);
        await LockAsync("Cinemas", parent.Value, ct);
        await LockAsync("Rooms", hint.RoomId, ct);
        await LockAsync("Showtimes", id, ct);
        var showtime = await db.Showtimes.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (showtime is null) return null;
        now = clock.GetUtcNow().UtcDateTime;
        if (showtime.Status != ShowtimeStatus.Scheduled || showtime.StartAtUtc <= now) throw new ApiException(409, "Only upcoming scheduled showtimes can be rescheduled.");
        if (await db.Bookings.AnyAsync(x => x.ShowtimeId == id, ct) || await db.BookingItems.AnyAsync(x => x.ShowtimeId == id, ct) || await db.ShowtimeSeats.AnyAsync(x => x.ShowtimeId == id && x.Status != ShowtimeSeatStatus.Available, ct))
            throw new ApiException(409, "A showtime with bookings or unavailable seats cannot be rescheduled.");
        var runtime = showtime.EndAtUtc - showtime.StartAtUtc;
        if (start.Ticks > DateTime.MaxValue.Ticks - runtime.Ticks) throw new ApiException(400, "Showtime end is outside the supported range.");
        if (start <= now) throw new ApiException(400, "Showtime start must be in the future.");
        showtime.Reschedule(start, request.BasePrice, now);
        foreach (var seat in await db.ShowtimeSeats.Where(x => x.ShowtimeId == id).ToListAsync(ct)) seat.SetPrice(request.BasePrice);
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateException ex) when (FindPostgres(ex)?.SqlState == PostgresErrorCodes.ExclusionViolation) { throw new ApiException(409, "The room already has a showtime in that period."); }
        if (notifier is not null) await notifier.NotifyAsync(id, ct);
        return await GetSummaryAsync(id, true, ct);
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var hint = await db.Showtimes.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.MovieId, x.RoomId }).SingleOrDefaultAsync(ct);
        if (hint is null) return false;
        var parent = await GetRoomCinemaAsync(hint.RoomId, ct);
        if (parent is null) return false;
        await LockAsync("Movies", hint.MovieId, ct);
        await LockAsync("Cinemas", parent.Value, ct);
        await LockAsync("Rooms", hint.RoomId, ct);
        await LockAsync("Showtimes", id, ct);
        var showtime = await db.Showtimes.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (showtime is null) return false;
        if (showtime.Status != ShowtimeStatus.Scheduled || showtime.StartAtUtc <= clock.GetUtcNow().UtcDateTime) throw new ApiException(409, "Only upcoming scheduled showtimes can be cancelled.");
        if (await db.Bookings.AnyAsync(x => x.ShowtimeId == id, ct) || await db.BookingItems.AnyAsync(x => x.ShowtimeId == id, ct) || await db.ShowtimeSeats.AnyAsync(x => x.ShowtimeId == id && x.Status != ShowtimeSeatStatus.Available, ct))
            throw new ApiException(409, "A showtime with bookings or unavailable seats cannot be cancelled.");
        showtime.Cancel(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (notifier is not null) await notifier.NotifyAsync(id, ct);
        return true;
    }

    private async Task<Guid?> GetRoomCinemaAsync(Guid roomId, CancellationToken ct) =>
        await db.Rooms.AsNoTracking().Where(x => x.Id == roomId).Select(x => (Guid?)x.CinemaId).SingleOrDefaultAsync(ct);

    private async Task LockAsync(string table, Guid id, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT 1 FROM \"{table}\" WHERE \"Id\" = @id FOR UPDATE";
        var parameter = command.CreateParameter(); parameter.ParameterName = "id"; parameter.Value = id; command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync(ct);
    }

    private static DateTime ParseStart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T.+(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase) ||
            !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new ApiException(400, "StartAt must be an ISO 8601 timestamp with Z or an explicit offset.");
        return parsed.UtcDateTime;
    }

    private static bool ValidPrice(decimal value) => value is >= 0 and <= 9999999999999999.99m && decimal.Round(value, 2) == value;

    private static PostgresException? FindPostgres(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
            if (current is PostgresException postgres) return postgres;
        return null;
    }
}
