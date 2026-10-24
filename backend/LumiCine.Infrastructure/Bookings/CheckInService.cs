using LumiCine.Application.Bookings;
using LumiCine.Application.Common;
using LumiCine.Domain.Bookings;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LumiCine.Infrastructure.Bookings;

public sealed class CheckInService(LumiCineDbContext db, TimeProvider clock) : ICheckInService
{
    public async Task<CheckInResult> CheckInAsync(Guid staffUserId, CheckInRequest request, CancellationToken ct)
    {
        ValidateInput(staffUserId, request);
        var code = request.TicketCode.Trim();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // A retry with the same staff identity and request id replays the recorded outcome.
            var replay = await db.CheckInLogs.AsNoTracking()
                .SingleOrDefaultAsync(x => x.StaffUserId == staffUserId && x.RequestId == request.RequestId, ct);
            if (replay is not null)
                return await ReplayAsync(replay, request.RequestId, ct);

            var now = UtcNow;
            var ticketId = await db.Tickets.AsNoTracking().Where(x => x.Code == code).Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(ct);

            Ticket? ticket = null;
            Booking? booking = null;
            Showtime? showtime = null;
            if (ticketId is Guid id)
            {
                ticket = await db.Tickets.SingleAsync(x => x.Id == id, ct);
                var item = await db.BookingItems.AsNoTracking().SingleAsync(x => x.Id == ticket.BookingItemId, ct);
                booking = await db.Bookings.SingleAsync(x => x.Id == item.BookingId, ct);
                showtime = await db.Showtimes.AsNoTracking().SingleAsync(x => x.Id == item.ShowtimeId, ct);
                // Serialise concurrent scans of the same ticket before judging its status.
                await db.LockRowAsync("Tickets", ticket.Id, ct);
                await db.Entry(ticket).ReloadAsync(ct);
                await db.Entry(booking).ReloadAsync(ct);
            }

            var result = Classify(ticket, booking, showtime, now);
            db.CheckInLogs.Add(new CheckInLog(Guid.NewGuid(), staffUserId, request.RequestId, now, result,
                ticket?.Id, booking?.Id, showtime?.Id, code));
            if (result == CheckInLogResult.Success)
                ticket!.MarkUsed(now);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return BuildResult(request.RequestId, result, ticket, booking, showtime, now);
        }
        catch (DbUpdateException ex) when (FindPostgres(ex)?.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Two requests from the same staff landed together: answer with the recorded scan.
            var replay = await db.CheckInLogs.AsNoTracking()
                .SingleOrDefaultAsync(x => x.StaffUserId == staffUserId && x.RequestId == request.RequestId, ct);
            if (replay is not null)
                return await ReplayAsync(replay, request.RequestId, ct);
            throw new ApiException(409, "The check-in request conflicts with a concurrent scan.");
        }
    }

    private async Task<CheckInResult> ReplayAsync(CheckInLog replay, Guid requestId, CancellationToken ct)
    {
        var now = UtcNow;
        Ticket? ticket = null;
        Booking? booking = null;
        Showtime? showtime = null;
        if (replay.TicketId is Guid ticketId)
        {
            ticket = await db.Tickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ticketId, ct);
            if (ticket is not null)
            {
                var item = await db.BookingItems.AsNoTracking().SingleAsync(x => x.Id == ticket.BookingItemId, ct);
                booking = await db.Bookings.AsNoTracking().SingleAsync(x => x.Id == item.BookingId, ct);
                showtime = await db.Showtimes.AsNoTracking().SingleAsync(x => x.Id == item.ShowtimeId, ct);
            }
        }
        // The replay answers with the recorded outcome exactly as it was logged.
        var result = replay.Result;
        return BuildResult(requestId, result, ticket, booking, showtime, now);
    }

    private static CheckInLogResult Classify(Ticket? ticket, Booking? booking, Showtime? showtime, DateTime now)
    {
        if (ticket is null)
            return CheckInLogResult.TicketNotFound;
        if (ticket.Status == TicketStatus.Voided)
            return CheckInLogResult.TicketVoided;
        if (booking!.Status != BookingStatus.Confirmed)
            return CheckInLogResult.BookingNotConfirmed;
        if (showtime!.Status != ShowtimeStatus.Scheduled)
            return CheckInLogResult.ShowtimeCancelled;
        if (now < showtime.StartAtUtc.AddMinutes(-30) || now >= showtime.EndAtUtc)
            return CheckInLogResult.OutsideWindow;
        if (ticket.Status == TicketStatus.Used)
            return CheckInLogResult.DuplicateScan;
        return CheckInLogResult.Success;
    }

    private static CheckInResult BuildResult(
        Guid requestId, CheckInLogResult result, Ticket? ticket, Booking? booking, Showtime? showtime, DateTime now) =>
        new(requestId,
            MapOutcome(result),
            ticket?.Id,
            ticket?.Code,
            booking?.Id,
            booking?.Code,
            showtime?.Id,
            result is CheckInLogResult.Success or CheckInLogResult.DuplicateScan ? ticket?.UsedAtUtc : null,
            showtime is null ? null : showtime.StartAtUtc.AddMinutes(-30),
            showtime?.EndAtUtc,
            now);

    private static string MapOutcome(CheckInLogResult result) => result switch
    {
        CheckInLogResult.Success => CheckInOutcomes.Success,
        CheckInLogResult.DuplicateScan => CheckInOutcomes.DuplicateScan,
        CheckInLogResult.TicketNotFound => CheckInOutcomes.TicketNotFound,
        CheckInLogResult.BookingNotConfirmed => CheckInOutcomes.BookingNotConfirmed,
        CheckInLogResult.ShowtimeCancelled => CheckInOutcomes.ShowtimeCancelled,
        CheckInLogResult.OutsideWindow => CheckInOutcomes.OutsideWindow,
        CheckInLogResult.TicketVoided => CheckInOutcomes.TicketVoided,
        _ => throw new InvalidOperationException("Unknown check-in result.")
    };

    private static void ValidateInput(Guid staffUserId, CheckInRequest request)
    {
        if (staffUserId == Guid.Empty || request is null || request.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.TicketCode))
            throw new ApiException(400, "A valid staff account, request id, and ticket code are required.");
        if (request.TicketCode.Length > 60)
            throw new ApiException(400, "The scanned code is too long.");
    }

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    private static PostgresException? FindPostgres(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
            if (current is PostgresException postgres) return postgres;
        return null;
    }
}
