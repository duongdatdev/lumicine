using LumiCine.Application.Cinemas;
using LumiCine.Application.Common;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace LumiCine.Infrastructure.Cinemas;

public sealed class CinemaService(LumiCineDbContext db, TimeProvider timeProvider) : ICinemaService
{
    private static readonly TimeSpan Cleanup = TimeSpan.FromMinutes(15);

    public async Task<PagedResult<CinemaSummary>> GetPublicCinemasAsync(PageQuery page, string? city, CancellationToken ct)
    {
        page.Validate();
        var query = db.Cinemas.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(city))
        {
            var normalizedCity = city.Trim().ToLowerInvariant();
            query = query.Where(x => x.City.ToLower() == normalizedCity);
        }
        return await Page(query.OrderBy(x => x.City).ThenBy(x => x.Name).ThenBy(x => x.Id), page, x => new CinemaSummary(x.Id, x.Code, x.Name, x.Address, x.City, x.IsActive), ct);
    }

    public async Task<CinemaSummary?> GetPublicCinemaAsync(Guid id, CancellationToken ct) =>
        await db.Cinemas.AsNoTracking().Where(x => x.Id == id && x.IsActive).Select(x => new CinemaSummary(x.Id, x.Code, x.Name, x.Address, x.City, x.IsActive)).SingleOrDefaultAsync(ct);

    public async Task<PagedResult<CinemaSummary>> GetAdminCinemasAsync(PageQuery page, CancellationToken ct)
    {
        page.Validate();
        return await Page(db.Cinemas.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id), page, x => new CinemaSummary(x.Id, x.Code, x.Name, x.Address, x.City, x.IsActive), ct);
    }

    public async Task<CinemaSummary?> GetAdminCinemaAsync(Guid id, CancellationToken ct) =>
        await db.Cinemas.AsNoTracking().Where(x => x.Id == id).Select(x => new CinemaSummary(x.Id, x.Code, x.Name, x.Address, x.City, x.IsActive)).SingleOrDefaultAsync(ct);

    public async Task<Guid> CreateCinemaAsync(CinemaRequest request, CancellationToken ct)
    {
        if (request is null) throw new ApiException(400, "Cinema details are required.");
        ValidateCinema(request);
        var entity = Validate(() => new Cinema(Guid.NewGuid(), request.Code, request.Name, request.Address, request.City, timeProvider.GetUtcNow().UtcDateTime));
        db.Cinemas.Add(entity);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "IX_Cinemas_Code")) { throw new ApiException(409, "Cinema code already exists."); }
        return entity.Id;
    }

    public async Task<bool> UpdateCinemaAsync(Guid id, CinemaRequest request, CancellationToken ct)
    {
        if (request is null) throw new ApiException(400, "Cinema details are required.");
        ValidateCinema(request);
        var entity = await db.Cinemas.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return false;
        Validate(() => entity.Update(request.Code, request.Name, request.Address, request.City, timeProvider.GetUtcNow().UtcDateTime));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "IX_Cinemas_Code")) { throw new ApiException(409, "Cinema code already exists."); }
        return true;
    }

    public async Task<bool> SetCinemaActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockCinema(id, ct);
        var entity = await db.Cinemas.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return false;
        if (!isActive && await HasOutstandingCinema(id, ct)) throw new ApiException(409, "Cinema has an upcoming or ongoing showtime.");
        entity.SetActive(isActive, timeProvider.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<PagedResult<RoomSummary>?> GetAdminRoomsAsync(Guid cinemaId, PageQuery page, CancellationToken ct)
    {
        page.Validate();
        if (!await db.Cinemas.AnyAsync(x => x.Id == cinemaId, ct)) return null;
        var query = db.Rooms.AsNoTracking().Where(x => x.CinemaId == cinemaId);
        return await Page(query.OrderBy(x => x.Code).ThenBy(x => x.Id), page,
            x => new RoomSummary(x.Id, x.CinemaId, x.Code, x.Name, x.IsActive, db.Seats.Count(s => s.RoomId == x.Id)), ct);
    }

    public async Task<RoomSummary?> GetAdminRoomAsync(Guid id, CancellationToken ct) =>
        await db.Rooms.AsNoTracking().Where(x => x.Id == id).Select(x => new RoomSummary(x.Id, x.CinemaId, x.Code, x.Name, x.IsActive, db.Seats.Count(s => s.RoomId == x.Id))).SingleOrDefaultAsync(ct);

    public async Task<PagedResult<SeatSummary>?> GetAdminSeatsAsync(Guid roomId, PageQuery page, CancellationToken ct)
    {
        page.Validate();
        if (!await db.Rooms.AnyAsync(x => x.Id == roomId, ct)) return null;
        var query = db.Seats.AsNoTracking().Where(x => x.RoomId == roomId).OrderBy(x => x.RowLabel).ThenBy(x => x.Number).ThenBy(x => x.Id);
        return await Page(query, page, x => new SeatSummary(x.Id, x.RoomId, x.RowLabel, x.Number, x.SeatType, x.IsActive), ct);
    }

    public async Task<Guid> CreateRoomAsync(Guid cinemaId, RoomRequest request, CancellationToken ct)
    {
        if (request is null) throw new ApiException(400, "Room details are required.");
        ValidateRoom(request);
        if (!await db.Cinemas.AnyAsync(x => x.Id == cinemaId, ct)) throw new ApiException(404, "Cinema not found.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockCinema(cinemaId, ct);
        if (!await db.Cinemas.AnyAsync(x => x.Id == cinemaId, ct)) throw new ApiException(404, "Cinema not found.");
        var entity = Validate(() => new Room(Guid.NewGuid(), cinemaId, request.Code, request.Name, timeProvider.GetUtcNow().UtcDateTime));
        db.Rooms.Add(entity);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "IX_Rooms_CinemaId_Code")) { throw new ApiException(409, "Room code already exists in this cinema."); }
        await tx.CommitAsync(ct);
        return entity.Id;
    }

    public async Task<bool> UpdateRoomAsync(Guid id, RoomRequest request, CancellationToken ct)
    {
        if (request is null) throw new ApiException(400, "Room details are required.");
        ValidateRoom(request);
        var entity = await db.Rooms.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return false;
        Validate(() => entity.Update(request.Code, request.Name, timeProvider.GetUtcNow().UtcDateTime));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "IX_Rooms_CinemaId_Code")) { throw new ApiException(409, "Room code already exists in this cinema."); }
        return true;
    }

    public async Task<bool> SetRoomActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var roomInfo = await db.Rooms.Where(x => x.Id == id).Select(x => new { x.CinemaId }).SingleOrDefaultAsync(ct);
        if (roomInfo is null) return false;
        await LockCinema(roomInfo.CinemaId, ct);
        await LockRoom(id, ct);
        var entity = await db.Rooms.SingleAsync(x => x.Id == id, ct);
        if (!isActive && await HasOutstandingRoom(id, ct)) throw new ApiException(409, "Room has an upcoming or ongoing showtime.");
        entity.SetActive(isActive, timeProvider.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> ReplaceSeatLayoutAsync(Guid roomId, IReadOnlyList<SeatLayoutRow> rows, CancellationToken ct)
    {
        if (rows is null || rows.Any(r => r is null || r.Label is null)) throw new ApiException(400, "Layout rows and labels are required.");
        if (rows.Count == 0 || rows.Count > 1000) throw new ApiException(400, "Layout must contain between 1 and 1000 seats.");
        var normalized = rows.Select(r => (Label: r.Label.Trim().ToUpperInvariant(), r.StandardSeats, r.VipSeats)).ToArray();
        var totalSeats = normalized.Sum(r => (long)r.StandardSeats + r.VipSeats);
        if (normalized.Any(r => string.IsNullOrWhiteSpace(r.Label) || r.Label.Length > 10 || r.StandardSeats < 0 || r.VipSeats < 0 || (long)r.StandardSeats + r.VipSeats == 0)
            || normalized.Select(r => r.Label).Distinct(StringComparer.Ordinal).Count() != normalized.Length
            || totalSeats > 1000)
            throw new ApiException(400, "Layout rows must have unique labels and contain at most 1000 Standard or VIP seats.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var room = await db.Rooms.Where(x => x.Id == roomId).Select(x => new { x.CinemaId }).SingleOrDefaultAsync(ct);
        if (room is null) return false;
        await LockCinema(room.CinemaId, ct);
        await LockRoom(roomId, ct);
        if (await db.ShowtimeSeats.AnyAsync(x => x.RoomId == roomId, ct)) throw new ApiException(409, "Seat layout cannot be replaced after seat snapshots exist.");
        var existingSeats = await db.Seats.Where(x => x.RoomId == roomId).ToListAsync(ct);
        db.Seats.RemoveRange(existingSeats);
        await db.SaveChangesAsync(ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var seats = normalized.SelectMany(row => Enumerable.Range(1, row.StandardSeats).Select(i => new Seat(Guid.NewGuid(), roomId, row.Label, i, "Standard", now))
            .Concat(Enumerable.Range(row.StandardSeats + 1, row.VipSeats).Select(i => new Seat(Guid.NewGuid(), roomId, row.Label, i, "VIP", now)))).ToArray();
        db.Seats.AddRange(seats);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> SetSeatActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var info = await db.Seats.Where(x => x.Id == id).Select(x => new { x.RoomId, RoomCinemaId = db.Rooms.Where(r => r.Id == x.RoomId).Select(r => r.CinemaId).First() }).SingleOrDefaultAsync(ct);
        if (info is null) return false;
        await LockCinema(info.RoomCinemaId, ct);
        await LockRoom(info.RoomId, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Seats\" WHERE \"Id\" = {id} FOR UPDATE", ct);
        var seat = await db.Seats.SingleAsync(x => x.Id == id, ct);
        if (await HasOutstandingRoom(info.RoomId, ct)) throw new ApiException(409, "Seat availability cannot change while the room has an upcoming or ongoing showtime.");
        seat.SetActive(isActive);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private Task<bool> HasOutstandingCinema(Guid id, CancellationToken ct)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.Add(-Cleanup);
        return db.Showtimes.AnyAsync(s => s.Status == ShowtimeStatus.Scheduled && s.EndAtUtc > cutoff && db.Rooms.Any(r => r.Id == s.RoomId && r.CinemaId == id), ct);
    }

    private Task<bool> HasOutstandingRoom(Guid id, CancellationToken ct)
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.Add(-Cleanup);
        return db.Showtimes.AnyAsync(s => s.RoomId == id && s.Status == ShowtimeStatus.Scheduled && s.EndAtUtc > cutoff, ct);
    }

    private Task LockCinema(Guid id, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Cinemas\" WHERE \"Id\" = {id} FOR UPDATE", ct);
    private Task LockRoom(Guid id, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Rooms\" WHERE \"Id\" = {id} FOR UPDATE", ct);

    private static async Task<PagedResult<TOut>> Page<TIn, TOut>(IQueryable<TIn> source, PageQuery page, System.Linq.Expressions.Expression<Func<TIn, TOut>> selector, CancellationToken ct)
    {
        var count = await source.CountAsync(ct);
        var offset = (long)(page.Page - 1) * page.PageSize;
        List<TOut> items = offset >= count ? [] : await source.Skip((int)offset).Take(page.PageSize).Select(selector).ToListAsync(ct);
        return new PagedResult<TOut>(items, count, page.Page, page.PageSize);
    }

    private static bool IsUniqueViolation(DbUpdateException exception, string expectedConstraint) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres &&
        string.Equals(postgres.ConstraintName, expectedConstraint, StringComparison.Ordinal);

    private static T Validate<T>(Func<T> action)
    {
        try { return action(); }
        catch (ArgumentException exception) { throw new ApiException(400, exception.Message); }
    }

    private static void Validate(Action action)
    {
        try { action(); }
        catch (ArgumentException exception) { throw new ApiException(400, exception.Message); }
    }

    private static void ValidateCinema(CinemaRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 30 ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160 ||
            string.IsNullOrWhiteSpace(request.Address) || request.Address.Trim().Length > 300 ||
            string.IsNullOrWhiteSpace(request.City) || request.City.Trim().Length > 100)
            throw new ApiException(400, "Cinema fields are required and must fit their maximum lengths.");
    }

    private static void ValidateRoom(RoomRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 30 ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
            throw new ApiException(400, "Room code and name are required and must fit their maximum lengths.");
    }
}
