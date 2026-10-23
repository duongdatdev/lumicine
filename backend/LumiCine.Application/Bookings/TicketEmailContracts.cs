using LumiCine.Application.Accounts;

namespace LumiCine.Application.Bookings;

public interface ITicketEmailComposer
{
    Task<EmailMessage> ComposeAsync(Guid bookingId, CancellationToken cancellationToken);
}

public interface ITicketEmailDeliveryService
{
    Task EnqueueAsync(Guid bookingId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> ClaimDueAsync(int batchSize, CancellationToken cancellationToken);
    Task CompleteAsync(Guid deliveryId, CancellationToken cancellationToken);
    Task RetryAsync(Guid deliveryId, string error, CancellationToken cancellationToken);
}
