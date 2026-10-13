using LumiCine.Domain.Common;

namespace LumiCine.Domain.Bookings;

public sealed class TicketEmailDelivery
{
    public const int MaxAttempts = 5;

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(60)
    ];

    private TicketEmailDelivery() { }

    public TicketEmailDelivery(Guid id, Guid bookingId, DateTime createdAtUtc)
    {
        Id = EntityGuard.Id(id, nameof(id));
        BookingId = EntityGuard.Id(bookingId, nameof(bookingId));
        Status = TicketEmailDeliveryStatus.Pending;
        CreatedAtUtc = EntityGuard.Utc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = createdAtUtc;
        NextAttemptAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid BookingId { get; private set; }
    public TicketEmailDeliveryStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? NextAttemptAtUtc { get; private set; }
    public DateTime? LeaseUntilUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public bool TryAcquireLease(DateTime nowUtc, TimeSpan leaseDuration)
    {
        nowUtc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Must be greater than zero.");
        if (Status != TicketEmailDeliveryStatus.Pending)
            return false;
        if (LeaseUntilUtc is DateTime lease && lease > nowUtc)
            return false;

        LeaseUntilUtc = nowUtc.Add(leaseDuration);
        UpdatedAtUtc = nowUtc;
        return true;
    }

    public void ReleaseLease(DateTime nowUtc)
    {
        LeaseUntilUtc = null;
        UpdatedAtUtc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
    }

    public void MarkSent(DateTime nowUtc)
    {
        nowUtc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
        if (Status != TicketEmailDeliveryStatus.Pending)
            throw new InvalidOperationException("Only pending ticket email deliveries can be sent.");

        Status = TicketEmailDeliveryStatus.Sent;
        SentAtUtc = nowUtc;
        NextAttemptAtUtc = null;
        LeaseUntilUtc = null;
        LastError = null;
        UpdatedAtUtc = nowUtc;
    }

    public void ScheduleRetry(DateTime nowUtc, string error)
    {
        nowUtc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
        if (Status != TicketEmailDeliveryStatus.Pending)
            throw new InvalidOperationException("Only pending ticket email deliveries can be retried.");

        AttemptCount++;
        LeaseUntilUtc = null;
        LastError = error is null ? null : EntityGuard.OptionalText(error, 500, nameof(error));
        if (AttemptCount >= MaxAttempts)
        {
            Status = TicketEmailDeliveryStatus.Failed;
            NextAttemptAtUtc = null;
        }
        else
        {
            NextAttemptAtUtc = nowUtc.Add(RetryDelays[Math.Min(AttemptCount, RetryDelays.Length) - 1]);
        }
        UpdatedAtUtc = nowUtc;
    }

    public void ResetForResend(DateTime nowUtc)
    {
        nowUtc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
        Status = TicketEmailDeliveryStatus.Pending;
        AttemptCount = 0;
        NextAttemptAtUtc = nowUtc;
        LeaseUntilUtc = null;
        SentAtUtc = null;
        LastError = null;
        UpdatedAtUtc = nowUtc;
    }
}
