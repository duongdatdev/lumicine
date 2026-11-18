using LumiCine.Application.Bookings;
using LumiCine.Application.Common;
using LumiCine.Domain.Bookings;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.Infrastructure.Bookings;

public sealed class TicketService(
    LumiCineDbContext db,
    TimeProvider clock,
    ITicketQrRenderer qrRenderer,
    ITicketEmailDeliveryService emailDeliveries) : ITicketService
{
    public async Task<IReadOnlyList<TicketDetails>> ListForBookingAsync(Guid userId, Guid bookingId, CancellationToken ct)
    {
        await RequireOwnedBookingAsync(userId, bookingId, ct);
        var rows = await QueryTickets(bookingId: bookingId).ToListAsync(ct);
        return rows.Select(ToDetails).ToArray();
    }

    public async Task<TicketDetails> GetOwnedAsync(Guid userId, Guid ticketId, CancellationToken ct)
    {
        var rows = await QueryTickets(ticketId: ticketId, ownerUserId: userId).ToListAsync(ct);
        return rows.Count == 0 ? throw new ApiException(404, "Ticket was not found.") : ToDetails(rows[0]);
    }

    public async Task<TicketQrImage> GetQrImageAsync(Guid userId, Guid ticketId, CancellationToken ct)
    {
        var rows = await QueryTickets(ticketId: ticketId, ownerUserId: userId).ToListAsync(ct);
        return rows.Count == 0 ? throw new ApiException(404, "Ticket was not found.") : new TicketQrImage(qrRenderer.RenderPng(rows[0].TicketCode));
    }

    public async Task ResendConfirmationEmailAsync(Guid userId, Guid bookingId, CancellationToken ct)
    {
        await RequireOwnedBookingAsync(userId, bookingId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var hasTickets = await QueryTickets(bookingId: bookingId).AnyAsync(ct);
        if (!hasTickets)
            throw new ApiException(409, "Tickets are only emailed once they are issued.");

        // Resends are limited to one request per minute per booking.
        var latest = await db.TicketEmailDeliveries.AsNoTracking()
            .Where(x => x.BookingId == bookingId)
            .OrderByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc)
            .Select(x => x.UpdatedAtUtc ?? x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (latest is DateTime lastActivity && lastActivity > now.AddSeconds(-60))
            throw new ApiException(429, "Ticket email resend is limited to once per minute.");

        await emailDeliveries.EnqueueAsync(bookingId, ct);
    }

    internal static async Task<CheckInTicketSummary?> GetCheckInSummaryAsync(LumiCineDbContext db, Guid ticketId, CancellationToken ct)
    {
        var row = await QueryTickets(db, ticketId: ticketId).SingleOrDefaultAsync(ct);
        return row is null ? null : new(row.MovieTitle, row.CinemaName, row.RoomName, row.StartAtUtc,
            row.RowLabel, row.Number, row.SeatType);
    }

    private IQueryable<TicketRow> QueryTickets(Guid? ticketId = null, Guid? ownerUserId = null, Guid? bookingId = null) =>
        QueryTickets(db, ticketId, ownerUserId, bookingId);

    private static IQueryable<TicketRow> QueryTickets(LumiCineDbContext db, Guid? ticketId = null, Guid? ownerUserId = null, Guid? bookingId = null)
    {
        // Filters stay ahead of the TicketRow projection so the whole query translates to SQL.
        return from ticket in db.Tickets.AsNoTracking()
               join item in db.BookingItems.AsNoTracking() on ticket.BookingItemId equals item.Id
               join booking in db.Bookings.AsNoTracking() on item.BookingId equals booking.Id
               join showtime in db.Showtimes.AsNoTracking() on item.ShowtimeId equals showtime.Id
               join movie in db.Movies.AsNoTracking() on showtime.MovieId equals movie.Id
               join room in db.Rooms.AsNoTracking() on showtime.RoomId equals room.Id
               join cinema in db.Cinemas.AsNoTracking() on room.CinemaId equals cinema.Id
               join showtimeSeat in db.ShowtimeSeats.AsNoTracking() on item.ShowtimeSeatId equals showtimeSeat.Id
               join seat in db.Seats.AsNoTracking() on showtimeSeat.SeatId equals seat.Id
               where (ticketId == null || ticket.Id == ticketId)
                   && (ownerUserId == null || booking.UserId == ownerUserId)
                   && (bookingId == null || item.BookingId == bookingId)
               orderby seat.RowLabel, seat.Number, seat.Id
               select new TicketRow(ticket.Id, ticket.Code, ticket.Status, ticket.IssuedAtUtc, ticket.UsedAtUtc,
                   booking.Id, booking.Code, showtime.Id, movie.Title, cinema.Name, room.Name,
                   showtime.StartAtUtc, showtime.EndAtUtc, seat.RowLabel, seat.Number, seat.SeatType, item.UnitPrice);
    }

    private async Task RequireOwnedBookingAsync(Guid userId, Guid bookingId, CancellationToken ct)
    {
        _ = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == bookingId && x.UserId == userId, ct)
            ?? throw new ApiException(404, "Booking was not found.");
    }

    private static TicketDetails ToDetails(TicketRow row) =>
        new(row.TicketId, row.BookingId, row.BookingCode, row.TicketCode, row.Status.ToString(),
            row.IssuedAtUtc, row.UsedAtUtc, row.ShowtimeId, row.MovieTitle, row.CinemaName, row.RoomName,
            row.StartAtUtc, row.EndAtUtc, row.RowLabel, row.Number, row.SeatType, row.UnitPrice);

    private sealed record TicketRow(
        Guid TicketId,
        string TicketCode,
        TicketStatus Status,
        DateTime IssuedAtUtc,
        DateTime? UsedAtUtc,
        Guid BookingId,
        string BookingCode,
        Guid ShowtimeId,
        string MovieTitle,
        string CinemaName,
        string RoomName,
        DateTime StartAtUtc,
        DateTime EndAtUtc,
        string RowLabel,
        int Number,
        string SeatType,
        decimal UnitPrice);
}
