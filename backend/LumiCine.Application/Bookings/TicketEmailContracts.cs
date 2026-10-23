using LumiCine.Application.Accounts;

namespace LumiCine.Application.Bookings;

public interface ITicketEmailComposer
{
    Task<EmailMessage> ComposeAsync(Guid bookingId, CancellationToken cancellationToken);
}

public sealed record TicketEmailDeliveryWork(Guid DeliveryId, Guid BookingId);

public interface ITicketEmailDeliveryService
{
    Task EnqueueAsync(Guid bookingId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TicketEmailDeliveryWork>> ClaimDueAsync(int batchSize, CancellationToken cancellationToken);
    Task CompleteAsync(Guid deliveryId, CancellationToken cancellationToken);
    Task RetryAsync(Guid deliveryId, string error, CancellationToken cancellationToken);
}
