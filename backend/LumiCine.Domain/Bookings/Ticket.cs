using LumiCine.Domain.Common;

namespace LumiCine.Domain.Bookings;

public sealed class Ticket
{
    private Ticket() => Code = string.Empty;

    public Ticket(
        Guid id,
        Guid bookingItemId,
        string code,
        DateTime issuedAtUtc,
        DateTime createdAtUtc,
        TicketStatus status = TicketStatus.Issued,
        DateTime? usedAtUtc = null)
    {
        Id = EntityGuard.Id(id, nameof(id));
        BookingItemId = EntityGuard.Id(bookingItemId, nameof(bookingItemId));
        Code = EntityGuard.RequiredText(code, 60, nameof(code));
        Status = EntityGuard.Defined(status, nameof(status));
        IssuedAtUtc = EntityGuard.Utc(issuedAtUtc, nameof(issuedAtUtc));
        UsedAtUtc = EntityGuard.OptionalUtc(usedAtUtc, nameof(usedAtUtc));
        CreatedAtUtc = EntityGuard.Utc(createdAtUtc, nameof(createdAtUtc));
    }

    public Guid Id { get; private set; }
    public Guid BookingItemId { get; private set; }
    public string Code { get; private set; }
    public TicketStatus Status { get; private set; }
    public DateTime IssuedAtUtc { get; private set; }
    public DateTime? UsedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public void MarkUsed(DateTime nowUtc)
    {
        nowUtc = EntityGuard.Utc(nowUtc, nameof(nowUtc));
        if (Status != TicketStatus.Issued)
            throw new InvalidOperationException("Only issued tickets can be used.");

        Status = TicketStatus.Used;
        UsedAtUtc = nowUtc;
    }
}