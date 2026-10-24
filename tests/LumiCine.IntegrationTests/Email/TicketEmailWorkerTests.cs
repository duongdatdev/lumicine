using System.Net;
using System.Net.Sockets;
using System.Text;
using LumiCine.Domain.Bookings;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LumiCine.IntegrationTests.Email;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class TicketEmailWorkerTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Worker_delivers_the_confirmation_email_and_marks_it_sent()
    {
        await using var server = new MultiMessageFakeSmtpServer();
        var logs = new CapturingLoggerProvider(LogLevel.Warning);
        using var factory = CreateFactory(server.Port, logs);
        await using var db = fixture.CreateDbContext();
        await db.TicketEmailDeliveries.ExecuteDeleteAsync();
        var (deliveryId, email) = await InsertFulfilledBookingAsync(db);

        await WaitForDeliveryStatusAsync(db, deliveryId, TicketEmailDeliveryStatus.Sent, TimeSpan.FromSeconds(45));

        // The SMTP completion lands when the worker's DATA round trip finishes.
        using var smtpTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (server.Messages.Count == 0)
            await Task.Delay(200, smtpTimeout.Token);
        var message = MimeKit.MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(await server.Messages[0].Task)));
        Assert.Equal(email, message.To.Mailboxes.Single().Address);
        Assert.NotNull(message.HtmlBody);
        Assert.Contains("cid:qr-0", message.HtmlBody);
        Assert.Equal(2, message.BodyParts.OfType<MimeKit.MimePart>()
            .Count(part => part.ContentDisposition?.Disposition == MimeKit.ContentDisposition.Inline));
        Assert.Equal(1, await db.TicketEmailDeliveries.CountAsync(x => x.Status == TicketEmailDeliveryStatus.Sent));
    }

    [Fact]
    public async Task Failed_deliveries_back_off_then_fail_after_five_attempts()
    {
        var deadPort = GetFreePort();
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var logs = new CapturingLoggerProvider(LogLevel.Warning);
        using var baseFactory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Host"] = "127.0.0.1",
                ["Email:Port"] = deadPort.ToString(),
                ["Email:Security"] = "None",
                ["Email:Username"] = "user",
                ["Email:Password"] = "pass",
                ["Email:FromAddress"] = "tickets@lumicine.test"
            }));
            builder.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)));
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(logs));
        });
        _ = factory.Services;
        await using var db = fixture.CreateDbContext();
        await db.TicketEmailDeliveries.ExecuteDeleteAsync();
        var (deliveryId, _) = await InsertFulfilledBookingAsync(db);
        // The worker parks on a clock-based delay after each pass; advancing the clock lets it run again.
        clock.Advance(TimeSpan.FromSeconds(2));
        deliveryId = await WaitForAttemptCountAsync(db, deliveryId, 1);

        var expectedDelays = new[] { 1, 5, 15, 60 };
        var attempt = 1;
        while (attempt < 5)
        {
            var stored = await db.TicketEmailDeliveries.AsNoTracking().SingleAsync(x => x.Id == deliveryId);
            Assert.Equal(attempt, stored.AttemptCount);
            Assert.Equal(TicketEmailDeliveryStatus.Pending, stored.Status);
            Assert.Equal(expectedDelays[attempt - 1], (int)((stored.NextAttemptAtUtc!.Value - clock.GetUtcNow().UtcDateTime).TotalMinutes + 0.5));
            clock.Advance(stored.NextAttemptAtUtc!.Value.AddSeconds(1) - clock.GetUtcNow());
            await WaitForAttemptCountAsync(db, deliveryId, attempt + 1);
            attempt++;
        }

        foreach (var entry in logs.Entries.Take(6))
            System.Console.WriteLine("RETRY-LOG: " + entry.Level + " [" + entry.Category + "] " + entry.Message);
        var failed = await db.TicketEmailDeliveries.AsNoTracking().SingleAsync(x => x.Id == deliveryId);
        Assert.Equal(TicketEmailDeliveryStatus.Failed, failed.Status);
        Assert.Equal(5, failed.AttemptCount);
        Assert.Equal("Email delivery failed.", failed.LastError);
    }

    [Fact]
    public async Task Two_instances_deliver_each_email_once()
    {
        await using var server = new MultiMessageFakeSmtpServer();
        using var first = CreateFactory(server.Port);
        using var second = CreateFactory(server.Port);
        await using var db = fixture.CreateDbContext();
        await db.TicketEmailDeliveries.ExecuteDeleteAsync();
        var (deliveryId, _) = await InsertFulfilledBookingAsync(db);

        await WaitForDeliveryStatusAsync(db, deliveryId, TicketEmailDeliveryStatus.Sent, TimeSpan.FromSeconds(45));
        await Task.Delay(500);

        Assert.True(server.Messages.Count <= 1, $"The queue delivered {server.Messages.Count} messages for one booking.");
        Assert.Equal(1, await db.TicketEmailDeliveries.CountAsync(x => x.Id == deliveryId && x.Status == TicketEmailDeliveryStatus.Sent));
        Assert.Equal(1, await db.TicketEmailDeliveries.CountAsync());
    }

    [Fact]
    public async Task Restart_does_not_resend_sent_emails()
    {
        await using var server = new MultiMessageFakeSmtpServer();
        await using var db = fixture.CreateDbContext();
        await db.TicketEmailDeliveries.ExecuteDeleteAsync();
        var (sentDeliveryId, _) = await InsertFulfilledBookingAsync(db, markDeliverySent: true);
        var sentBefore = await db.TicketEmailDeliveries.AsNoTracking().SingleAsync(x => x.Id == sentDeliveryId);

        using var factory = CreateFactory(server.Port);
        await Task.Delay(2000);

        Assert.Empty(server.Messages);
        var sentAfter = await db.TicketEmailDeliveries.AsNoTracking().SingleAsync(x => x.Id == sentDeliveryId);
        Assert.Equal(sentBefore.SentAtUtc, sentAfter.SentAtUtc);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateFactory(int smtpPort, CapturingLoggerProvider? logs = null)
    {
        var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString)
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:Host"] = "127.0.0.1",
                    ["Email:Port"] = smtpPort.ToString(),
                    ["Email:Security"] = "None",
                    ["Email:Username"] = "user",
                    ["Email:Password"] = "pass",
                    ["Email:FromAddress"] = "tickets@lumicine.test"
                }));
                if (logs is not null)
                    builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(logs));
            });
        // Building the host starts the hosted workers; without it the worker never runs.
        _ = factory.Services;
        return factory;
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task<(Guid DeliveryId, string Email)> InsertFulfilledBookingAsync(LumiCineDbContext db, bool markDeliverySent = false)
    {
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var email = $"{userId:N}@example.com";
        db.Users.Add(new ApplicationUser
        {
            Id = userId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true, IsActive = true, DisplayName = "Worker test"
        });
        var movie = new Movie(Guid.NewGuid(), $"Film-{Guid.NewGuid():N}", 100, now);
        var cinema = new Cinema(Guid.NewGuid(), $"C-{Guid.NewGuid():N}"[..22], "Test Cinema", "Test Address", "Test City", now);
        var room = new Room(Guid.NewGuid(), cinema.Id, $"R-{Guid.NewGuid():N}"[..22], "Test Room", now);
        var start = now.AddDays(1);
        var showtime = new Showtime(Guid.NewGuid(), movie.Id, room.Id, start, start.AddMinutes(100), 100, now);
        var booking = new Booking(Guid.NewGuid(), $"LC{Guid.NewGuid():N}", showtime.Id, 200, now.AddMinutes(-1),
            holdExpiresAtUtc: now.AddMinutes(5), userId: userId, requestId: Guid.NewGuid());
        db.AddRange(movie, cinema, room, showtime, booking);
        booking.Confirm(now);
        for (var i = 0; i < 2; i++)
        {
            var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id, number: i + 1);
            var showtimeSeat = new ShowtimeSeat(Guid.NewGuid(), showtime.Id, room.Id, seat.Id, 100, now);
            await db.ShowtimeSeats.AddAsync(showtimeSeat);
            showtimeSeat.Hold(booking.Id);
            showtimeSeat.Sell(booking.Id);
            var item = new BookingItem(Guid.NewGuid(), booking.Id, showtime.Id, showtimeSeat.Id, 100, now);
            await db.BookingItems.AddAsync(item);
            await db.SaveChangesAsync();
            db.Tickets.Add(new Ticket(Guid.NewGuid(), item.Id, TicketCode.New(), now, now));
        }
        var delivery = new TicketEmailDelivery(Guid.NewGuid(), booking.Id, now);
        if (markDeliverySent) delivery.MarkSent(now);
        db.TicketEmailDeliveries.Add(delivery);
        await db.SaveChangesAsync();
        return (delivery.Id, email);
    }

        private static async Task<Guid> WaitForAttemptCountAsync(LumiCineDbContext db, Guid deliveryId, int attempt)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (true)
        {
            var count = await db.TicketEmailDeliveries.AsNoTracking()
                .Where(x => x.Id == deliveryId).Select(x => (int?)x.AttemptCount).SingleOrDefaultAsync(timeout.Token);
            if (count >= attempt)
                return deliveryId;
            await Task.Delay(200, timeout.Token);
        }
    }

    private static async Task WaitForDeliveryStatusAsync(LumiCineDbContext db, Guid deliveryId, TicketEmailDeliveryStatus status, TimeSpan limit)
    {
        using var timeout = new CancellationTokenSource(limit);
        while (true)
        {
            var stored = await db.TicketEmailDeliveries.AsNoTracking()
                .Where(x => x.Id == deliveryId).Select(x => x.Status).SingleOrDefaultAsync(timeout.Token);
            if (stored == status)
                return;
            await Task.Delay(200, timeout.Token);
        }
    }

private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public void Advance(TimeSpan delta) => Now = Now.Add(delta);

        private DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class MultiMessageFakeSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _shutdown = new();
        private readonly List<TaskCompletionSource<string>> _messages = [];
        private readonly List<Task> _connections = [];
        private readonly Task _serverTask;

        public MultiMessageFakeSmtpServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serverTask = AcceptLoopAsync();
        }

        public int Port { get; }
        public IReadOnlyList<TaskCompletionSource<string>> Messages => _messages;

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            _shutdown.Cancel();
            try { await _serverTask.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception) { }
        }

        private async Task AcceptLoopAsync()
        {
            while (!_shutdown.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_shutdown.Token); }
                catch (OperationCanceledException) { return; }
                catch (ObjectDisposedException) { return; }
                var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                _messages.Add(completion);
                _connections.Add(HandleAsync(client, completion));
            }
        }

        private static async Task HandleAsync(TcpClient client, TaskCompletionSource<string> completion)
        {
            using var _ = client;
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };

            await writer.WriteLineAsync("220 localhost ESMTP ready");
            while (await reader.ReadLineAsync() is { } line)
            {
                if (line.StartsWith("EHLO ", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250-localhost");
                    await writer.WriteLineAsync("250 AUTH PLAIN LOGIN");
                }
                else if (line.StartsWith("AUTH ", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("235 authenticated");
                }
                else if (line.StartsWith("MAIL FROM:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("RCPT TO:", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250 accepted");
                }
                else if (line.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("354 send message");
                    var message = new StringBuilder();
                    while (await reader.ReadLineAsync() is { } messageLine && messageLine != ".")
                    {
                        if (messageLine == "..") message.AppendLine(".");
                        else message.AppendLine(messageLine);
                    }
                    await writer.WriteLineAsync("250 accepted message");
                    completion.TrySetResult(message.ToString());
                }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 bye");
                    return;
                }
            }
        }
    }
}
