using LumiCine.Application.Accounts;
using LumiCine.Application.Bookings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LumiCine.Infrastructure.Bookings;

public sealed class TicketEmailWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<TicketEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var deliveries = scope.ServiceProvider.GetRequiredService<ITicketEmailDeliveryService>();
                var composer = scope.ServiceProvider.GetRequiredService<ITicketEmailComposer>();
                var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                foreach (var work in await deliveries.ClaimDueAsync(20, stoppingToken))
                {
                    try
                    {
                        var message = await composer.ComposeAsync(work.BookingId, stoppingToken);
                        await sender.SendAsync(message, stoppingToken);
                        await deliveries.CompleteAsync(work.DeliveryId, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Could not deliver ticket email {DeliveryId}.", work.DeliveryId);
                        await deliveries.RetryAsync(work.DeliveryId, ex.Message, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ticket email delivery pass failed.");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(1), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
