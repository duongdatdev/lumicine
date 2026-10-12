using LumiCine.Application.Bookings;
using LumiCine.Application.Common;
using LumiCine.Application.Scheduling;
using LumiCine.Domain.Bookings;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LumiCine.Infrastructure.Bookings;

public sealed class BookingService(
    LumiCineDbContext db,
    TimeProvider clock,
    IShowtimeAvailabilityNotifier notifier,
    ILogger<BookingService> logger) : IBookingService
{
    private static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(5);

    public async Task<(BookingDetails Booking, bool Created)> CreateAsync(Guid userId, CreateBookingRequest request, CancellationToken ct)
    {
        ValidateRequest(userId, request);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var changedShowtimes = new HashSet<Guid>();
        try
        {
            await LockAsync("Users", userId, ct);
            var user = await db.Users.OfType<ApplicationUser>().SingleOrDefaultAsync(x => x.Id == userId, ct);
            if (user is null || !user.IsActive || !user.EmailConfirmed)
                throw new ApiException(403, "An active account with a confirmed email is required.");

            var prior = await db.Bookings.SingleOrDefaultAsync(x => x.UserId == userId && x.RequestId == request.RequestId, ct);
            if (prior is not null)
            {
                if (prior.ShowtimeId != request.ShowtimeId || !await SameSeatSetAsync(prior.Id, request.SeatIds, ct))
                    throw new ApiException(409, "This requestId was already used with a different booking request.");
                var existing = await GetRequiredAsync(userId, prior.Id, ct);
                await tx.CommitAsync(ct);
                return (existing, false);
            }

            var pending = await db.Bookings.Where(x => x.UserId == userId && x.Status == BookingStatus.Pending)
                .Select(x => new { x.Id, x.ShowtimeId, x.HoldExpiresAtUtc }).ToListAsync(ct);
            var showtimesToLock = pending.Select(x => x.ShowtimeId).Append(request.ShowtimeId).Distinct().Order().ToArray();
            foreach (var showtimeId in showtimesToLock) await LockAsync("Showtimes", showtimeId, ct);

            var now = UtcNow;
            foreach (var item in pending.Where(x => x.HoldExpiresAtUtc is DateTime expiry && now >= expiry))
            {
                if (await ExpireLockedAsync(item.Id, item.ShowtimeId, now, ct)) changedShowtimes.Add(item.ShowtimeId);
            }
            var dueOnShowtime = await db.Bookings.AsNoTracking()
                .Where(x => x.ShowtimeId == request.ShowtimeId && x.Status == BookingStatus.Pending && x.HoldExpiresAtUtc != null && x.HoldExpiresAtUtc <= now)
                .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
            foreach (var bookingId in dueOnShowtime)
                if (await ExpireLockedAsync(bookingId, request.ShowtimeId, now, ct)) changedShowtimes.Add(request.ShowtimeId);
            if (await db.Bookings.AnyAsync(x => x.UserId == userId && x.Status == BookingStatus.Pending, ct))
                throw new ApiException(409, "You already have a pending booking.");

            var showtime = await (from s in db.Showtimes
                                  join m in db.Movies on s.MovieId equals m.Id
                                  join r in db.Rooms on s.RoomId equals r.Id
                                  join c in db.Cinemas on r.CinemaId equals c.Id
                                  where s.Id == request.ShowtimeId
                                  select new { Showtime = s, Movie = m, Room = r, Cinema = c }).SingleOrDefaultAsync(ct);
            now = UtcNow;
            if (showtime is null || showtime.Showtime.Status != ShowtimeStatus.Scheduled || showtime.Showtime.StartAtUtc <= now ||
                !showtime.Movie.IsActive || !showtime.Room.IsActive || !showtime.Cinema.IsActive)
                throw new ApiException(409, "The showtime is unavailable for booking.");

            var seats = await db.ShowtimeSeats.AsNoTracking().Where(x => x.ShowtimeId == request.ShowtimeId && request.SeatIds.Contains(x.SeatId))
                .Join(db.Seats, x => new { x.SeatId, x.RoomId }, x => new { SeatId = x.Id, x.RoomId }, (showtimeSeat, seat) => new { ShowtimeSeat = showtimeSeat, Seat = seat })
                .OrderBy(x => x.ShowtimeSeat.Id).ToListAsync(ct);
            if (seats.Count != request.SeatIds.Count || seats.Any(x => !x.Seat.IsActive || x.ShowtimeSeat.Status != ShowtimeSeatStatus.Available || x.ShowtimeSeat.CurrentBookingId is not null))
                throw new ApiException(409, "One or more selected seats are unavailable.");
            foreach (var row in seats) await LockAsync("ShowtimeSeats", row.ShowtimeSeat.Id, ct);
            await db.Entry(showtime.Showtime).ReloadAsync(ct);
            seats = await db.ShowtimeSeats.AsNoTracking().Where(x => x.ShowtimeId == request.ShowtimeId && request.SeatIds.Contains(x.SeatId))
                .Join(db.Seats, x => new { x.SeatId, x.RoomId }, x => new { SeatId = x.Id, x.RoomId }, (showtimeSeat, seat) => new { ShowtimeSeat = showtimeSeat, Seat = seat })
                .OrderBy(x => x.ShowtimeSeat.Id).ToListAsync(ct);
            now = UtcNow;
            if (showtime.Showtime.Status != ShowtimeStatus.Scheduled || showtime.Showtime.StartAtUtc <= now ||
                seats.Count != request.SeatIds.Count || seats.Any(x => !x.Seat.IsActive || x.ShowtimeSeat.Status != ShowtimeSeatStatus.Available || x.ShowtimeSeat.CurrentBookingId is not null))
                throw new ApiException(409, "The showtime or one or more selected seats are unavailable.");

            var id = Guid.NewGuid();
            var expiresAt = Min(now.Add(HoldDuration), showtime.Showtime.StartAtUtc);
            var total = seats.Sum(x => x.ShowtimeSeat.Price);
            var booking = new Booking(id, $"LC{id:N}", request.ShowtimeId, total, now, holdExpiresAtUtc: expiresAt, userId: userId, requestId: request.RequestId);
            db.Bookings.Add(booking);
            db.BookingItems.AddRange(seats.Select(x => new BookingItem(Guid.NewGuid(), id, request.ShowtimeId, x.ShowtimeSeat.Id, x.ShowtimeSeat.Price, now)));
            await db.SaveChangesAsync(ct);
            var snapshotIds = seats.Select(x => x.ShowtimeSeat.Id).ToArray();
            var affected = await db.ShowtimeSeats.Where(x => snapshotIds.Contains(x.Id) && x.ShowtimeId == request.ShowtimeId &&
                    x.Status == ShowtimeSeatStatus.Available && x.CurrentBookingId == null)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, ShowtimeSeatStatus.Held)
                    .SetProperty(x => x.CurrentBookingId, id), ct);
            if (affected != snapshotIds.Length) throw new ApiException(409, "One or more selected seats are unavailable.");
            var details = await GetRequiredAsync(userId, id, ct);
            await tx.CommitAsync(ct);
            changedShowtimes.Add(request.ShowtimeId);
            await NotifyAsync(changedShowtimes, ct);
            return (details, true);
        }
        catch (DbUpdateException ex) when (FindPostgres(ex)?.SqlState == PostgresErrorCodes.UniqueViolation || FindPostgres(ex)?.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new ApiException(409, "The booking request conflicts with a concurrent change.");
        }
    }

    public async Task<PagedResult<BookingDetails>> ListAsync(Guid userId, BookingQuery query, CancellationToken ct)
    {
        var page = query.Paging.Validate();
        var rows = db.Bookings.AsNoTracking().Where(x => x.UserId == userId);
        var now = UtcNow;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<BookingStatus>(query.Status, true, out var status) ||
                !string.Equals(query.Status.Trim(), status.ToString(), StringComparison.OrdinalIgnoreCase) ||
                !Enum.IsDefined(status))
                throw new ApiException(400, "Status filter is invalid.");
            rows = status == BookingStatus.Expired
                ? rows.Where(x => x.Status == BookingStatus.Expired || (x.Status == BookingStatus.Pending && x.HoldExpiresAtUtc != null && x.HoldExpiresAtUtc <= now))
                : status == BookingStatus.Pending
                    ? rows.Where(x => x.Status == BookingStatus.Pending && (x.HoldExpiresAtUtc == null || x.HoldExpiresAtUtc > now))
                    : rows.Where(x => x.Status == status);
        }
        var total = await rows.CountAsync(ct);
        var offset = page.Offset;
        if (offset > int.MaxValue) throw new ApiException(400, "Page is too large.");
        var bookings = await rows.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((int)offset).Take(page.PageSize).Select(x => x.Id).ToListAsync(ct);
        var details = await GetManyAsync(userId, bookings, ct);
        var byId = details.ToDictionary(x => x.Id);
        var items = bookings.Select(id => byId[id]).ToArray();
        return new(items, total, page.Page, page.PageSize);
    }

    public async Task<BookingDetails?> GetAsync(Guid userId, Guid bookingId, CancellationToken ct)
    {
        return (await GetManyAsync(userId, [bookingId], ct)).SingleOrDefault();
    }

    public async Task CancelAsync(Guid userId, Guid bookingId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var hint = await db.Bookings.AsNoTracking().Where(x => x.Id == bookingId && x.UserId == userId)
            .Select(x => new { x.Id, x.ShowtimeId }).SingleOrDefaultAsync(ct);
        if (hint is null) throw new ApiException(404, "Booking was not found.");
        await LockAsync("Showtimes", hint.ShowtimeId, ct);
        await LockAsync("Bookings", bookingId, ct);
        var booking = await db.Bookings.SingleAsync(x => x.Id == bookingId && x.UserId == userId, ct);
        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Expired)
        {
            await tx.CommitAsync(ct);
            return;
        }
        if (booking.Status != BookingStatus.Pending) throw new ApiException(409, "Only pending bookings can be cancelled.");
        await ReleaseSeatsAsync(booking.Id, booking.ShowtimeId, ct);
        var now = UtcNow;
        if (booking.HoldExpiresAtUtc is DateTime expiresAt && now >= expiresAt)
            booking.Expire(now);
        else
            booking.Cancel(now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await NotifyAsync([booking.ShowtimeId], ct);
    }

    public async Task<int> ExpireDueAsync(int batchSize, CancellationToken ct)
    {
        var now = UtcNow;
        var due = await db.Bookings.AsNoTracking().Where(x => x.Status == BookingStatus.Pending && x.HoldExpiresAtUtc != null && x.HoldExpiresAtUtc <= now)
            .OrderBy(x => x.HoldExpiresAtUtc).ThenBy(x => x.Id).Take(Math.Clamp(batchSize, 1, 100)).Select(x => new { x.Id, x.ShowtimeId }).ToListAsync(ct);
        var expired = 0;
        foreach (var item in due)
        {
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await LockAsync("Showtimes", item.ShowtimeId, ct);
                if (await ExpireLockedAsync(item.Id, item.ShowtimeId, UtcNow, ct))
                {
                    await tx.CommitAsync(ct);
                    expired++;
                    await NotifyAsync([item.ShowtimeId], ct);
                }
                else await tx.CommitAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Could not expire booking {BookingId}.", item.Id);
            }
        }
        return expired;
    }

    private async Task<bool> ExpireLockedAsync(Guid bookingId, Guid showtimeId, DateTime now, CancellationToken ct)
    {
        await LockAsync("Bookings", bookingId, ct);
        var booking = await db.Bookings.SingleOrDefaultAsync(x => x.Id == bookingId && x.ShowtimeId == showtimeId, ct);
        if (booking is null || booking.Status != BookingStatus.Pending || booking.HoldExpiresAtUtc is not DateTime expiry || now < expiry) return false;
        await ReleaseSeatsAsync(bookingId, showtimeId, ct);
        booking.Expire(now);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task ReleaseSeatsAsync(Guid bookingId, Guid showtimeId, CancellationToken ct)
    {
        var heldSeats = db.ShowtimeSeats.Where(x => x.ShowtimeId == showtimeId &&
            x.Status == ShowtimeSeatStatus.Held && x.CurrentBookingId == bookingId);
        var ids = await heldSeats.OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
        foreach (var id in ids) await LockAsync("ShowtimeSeats", id, ct);
        await heldSeats.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(update =>
            update.SetProperty(x => x.Status, ShowtimeSeatStatus.Available).SetProperty(x => x.CurrentBookingId, (Guid?)null), ct);
    }

    private async Task<bool> SameSeatSetAsync(Guid bookingId, IReadOnlyList<Guid> requested, CancellationToken ct)
    {
        var existing = await (from item in db.BookingItems
                              join showtimeSeat in db.ShowtimeSeats on item.ShowtimeSeatId equals showtimeSeat.Id
                              where item.BookingId == bookingId
                              select showtimeSeat.SeatId).Order().ToListAsync(ct);
        return existing.SequenceEqual(requested.Order());
    }

    private async Task<BookingDetails> GetRequiredAsync(Guid userId, Guid bookingId, CancellationToken ct)
    {
        return (await GetManyAsync(userId, [bookingId], ct)).SingleOrDefault()
            ?? throw new ApiException(404, "Booking was not found.");
    }

    private async Task<IReadOnlyList<BookingDetails>> GetManyAsync(Guid userId, IReadOnlyList<Guid> bookingIds, CancellationToken ct)
    {
        if (bookingIds.Count == 0) return [];
        var rows = await (from b in db.Bookings.AsNoTracking()
                          join s in db.Showtimes.AsNoTracking() on b.ShowtimeId equals s.Id
                          join m in db.Movies.AsNoTracking() on s.MovieId equals m.Id
                          join r in db.Rooms.AsNoTracking() on s.RoomId equals r.Id
                          join c in db.Cinemas.AsNoTracking() on r.CinemaId equals c.Id
                          where b.UserId == userId && bookingIds.Contains(b.Id)
                          select new { Booking = b, Showtime = s, Movie = m, Room = r, Cinema = c }).ToListAsync(ct);
        var seats = await (from item in db.BookingItems.AsNoTracking()
                           join ss in db.ShowtimeSeats.AsNoTracking() on item.ShowtimeSeatId equals ss.Id
                           join seat in db.Seats.AsNoTracking() on new { ss.SeatId, ss.RoomId } equals new { SeatId = seat.Id, seat.RoomId }
                           where bookingIds.Contains(item.BookingId)
                           orderby seat.RowLabel, seat.Number, seat.Id
                           select new { item.BookingId, Seat = new BookingSeat(seat.Id, seat.RowLabel, seat.Number, seat.SeatType, item.UnitPrice) }).ToListAsync(ct);
        var seatsByBooking = seats.GroupBy(x => x.BookingId).ToDictionary(x => x.Key, x => (IReadOnlyList<BookingSeat>)x.Select(y => y.Seat).ToArray());
        var now = UtcNow;
        return rows.Select(row =>
        {
            var status = row.Booking.Status == BookingStatus.Pending && row.Booking.HoldExpiresAtUtc is DateTime expiry && now >= expiry
                ? BookingStatus.Expired : row.Booking.Status;
            return new BookingDetails(row.Booking.Id, row.Booking.Code, row.Booking.ShowtimeId, row.Showtime.MovieId, row.Movie.Title,
                row.Cinema.Id, row.Cinema.Name, row.Room.Id, row.Room.Name, status.ToString(), row.Booking.HoldExpiresAtUtc,
                row.Booking.TotalAmount, row.Booking.CreatedAtUtc, now,
                seatsByBooking.GetValueOrDefault(row.Booking.Id, Array.Empty<BookingSeat>()));
        }).ToArray();
    }

    private async Task LockAsync(string table, Guid id, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT 1 FROM \"{table}\" WHERE \"Id\" = @id FOR UPDATE";
        var parameter = command.CreateParameter(); parameter.ParameterName = "id"; parameter.Value = id; command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync(ct);
    }

    private async Task NotifyAsync(IEnumerable<Guid> showtimeIds, CancellationToken ct)
    {
        foreach (var showtimeId in showtimeIds.Distinct())
        {
            try { await notifier.NotifyAsync(showtimeId, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not publish availability for showtime {ShowtimeId}.", showtimeId); }
        }
    }

    private static void ValidateRequest(Guid userId, CreateBookingRequest request)
    {
        if (userId == Guid.Empty || request is null || request.RequestId == Guid.Empty || request.ShowtimeId == Guid.Empty)
            throw new ApiException(400, "Valid account, request, and showtime identifiers are required.");
        if (request.SeatIds is null || request.SeatIds.Count is < 1 or > 8 || request.SeatIds.Any(x => x == Guid.Empty) || request.SeatIds.Distinct().Count() != request.SeatIds.Count)
            throw new ApiException(400, "Choose between one and eight distinct valid seats.");
    }

    private static DateTime Min(DateTime first, DateTime second) => first < second ? first : second;
    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private static PostgresException? FindPostgres(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
            if (current is PostgresException postgres) return postgres;
        return null;
    }
}
