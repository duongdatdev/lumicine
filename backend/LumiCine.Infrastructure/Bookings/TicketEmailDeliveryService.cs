using LumiCine.Application.Bookings;
using LumiCine.Domain.Bookings;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.Infrastructure.Bookings;

public sealed class TicketEmailDeliveryService(LumiCineDbContext db, TimeProvider clock) : ITicketEmailDeliveryService
{
    public async Task EnqueueAsync(Guid bookingId, CancellationToken ct)
    {
        var now = UtcNow;
        var pending = await db.TicketEmailDeliveries.SingleOrDefaultAsync(x => x.BookingId == bookingId && x.Status == TicketEmailDeliveryStatus.Pending, ct);
        if (pending is not null)
        {
            pending.ResetForResend(now);
        }
        else
        {
            db.TicketEmailDeliveries.Add(new TicketEmailDelivery(Guid.NewGuid(), bookingId, now));
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TicketEmailDeliveryWork>> ClaimDueAsync(int batchSize, CancellationToken ct)
    {
        var now = UtcNow;
        var candidates = await db.TicketEmailDeliveries.AsNoTracking()
            .Where(x => x.Status == TicketEmailDeliveryStatus.Pending && x.NextAttemptAtUtc != null && x.NextAttemptAtUtc <= now &&
                (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now))
            .OrderBy(x => x.NextAttemptAtUtc).ThenBy(x => x.Id)
            .Take(Math.Clamp(batchSize, 1, 100))
            .Select(x => new { x.Id, x.BookingId })
            .ToListAsync(ct);
        var leaseUntil = now.Add(TimeSpan.FromMinutes(5));
        var claimed = new List<TicketEmailDeliveryWork>();
        foreach (var candidate in candidates)
        {
            // The conditional update is the atomic lease: only one worker's update affects the row.
            var affected = await db.TicketEmailDeliveries
                .Where(x => x.Id == candidate.Id && x.Status == TicketEmailDeliveryStatus.Pending &&
                    (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now))
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.LeaseUntilUtc, leaseUntil)
                    .SetProperty(x => x.UpdatedAtUtc, now), ct);
            if (affected == 1)
                claimed.Add(new TicketEmailDeliveryWork(candidate.Id, candidate.BookingId));
        }
        return claimed;
    }

    public async Task CompleteAsync(Guid deliveryId, CancellationToken ct)
    {
        var delivery = await db.TicketEmailDeliveries.SingleAsync(x => x.Id == deliveryId, ct);
        delivery.MarkSent(UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public async Task RetryAsync(Guid deliveryId, string error, CancellationToken ct)
    {
        var delivery = await db.TicketEmailDeliveries.SingleAsync(x => x.Id == deliveryId, ct);
        delivery.ScheduleRetry(UtcNow, error);
        await db.SaveChangesAsync(ct);
    }

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;
}
