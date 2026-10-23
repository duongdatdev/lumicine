using System.Net;
using System.Net.Sockets;
using System.Text;
using LumiCine.Application.Accounts;
using LumiCine.Application.Bookings;
using LumiCine.Domain.Bookings;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using LumiCine.Infrastructure.Email;
using LumiCine.IntegrationTests.Persistence;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MimeKit;

namespace LumiCine.IntegrationTests.Email;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class TicketEmailQueueTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Composer_builds_html_and_plain_body_with_inline_qr_images()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var scope = factory.Services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<ITicketEmailComposer>();
        await using var db = fixture.CreateDbContext();
        var (bookingId, email) = await CreateFulfilledBookingAsync(db, movieTitle: "<Film> & \"Fun\"");

        var message = await composer.ComposeAsync(bookingId, CancellationToken.None);

        Assert.Equal(email, message.To);
        Assert.Contains(bookingCode(await GetBookingAsync(bookingId)), message.Subject);
        Assert.Contains("Booking <strong>", message.HtmlBody);
        Assert.Contains("&lt;Film&gt; &amp; &quot;Fun&quot;", message.HtmlBody);
        Assert.DoesNotContain("<Film>", message.HtmlBody);
        var cidCount = message.InlineAttachments.Count;
        Assert.Equal(2, cidCount);
        Assert.All(message.InlineAttachments, attachment => Assert.Equal("image/png", attachment.ContentType));
        Assert.Contains("cid:qr-0", message.HtmlBody);
        Assert.Contains("cid:qr-1", message.HtmlBody);
        var plainCodes = ExtractCodes(message.Body);
        Assert.Equal(cidCount, plainCodes.Count);
        Assert.All(message.InlineAttachments, attachment => Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], attachment.Data[..4]));
    }

    [Fact]
    public async Task Smtp_delivers_the_ticket_email_as_multipart_with_inline_images()
    {
        await using var server = new FakeSmtpServer();
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var scope = factory.Services.CreateScope();
        var composer = scope.ServiceProvider.GetRequiredService<ITicketEmailComposer>();
        var sender = new SmtpEmailSender(new EmailOptions
        {
            Host = "127.0.0.1", Port = server.Port, Security = SecureSocketOptions.None,
            Username = "user", Password = "pass", FromAddress = "tickets@lumicine.test"
        }, new TestHostEnvironment("Development"));
        await using var db = fixture.CreateDbContext();
        var (bookingId, _) = await CreateFulfilledBookingAsync(db);
        var message = await composer.ComposeAsync(bookingId, CancellationToken.None);

        await sender.SendAsync(message);

        var mime = MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(await server.MessageTask)));
        Assert.Equal(message.To, mime.To.Mailboxes.Single().Address);
        Assert.NotNull(mime.HtmlBody);
        Assert.Contains("cid:qr-0", mime.HtmlBody);
        Assert.Equal(message.Body.TrimEnd('\r', '\n'), mime.TextBody?.TrimEnd('\r', '\n'));
        var inlineImages = mime.BodyParts.OfType<MimePart>()
            .Where(part => part.ContentDisposition?.Disposition == ContentDisposition.Inline).ToArray();
        Assert.Equal(2, inlineImages.Length);
        Assert.Contains(inlineImages, part => part.ContentId == "qr-0");
        Assert.Contains(inlineImages, part => part.ContentId == "qr-1");
    }

    [Fact]
    public async Task Claims_lease_rows_once_and_completing_marks_them_sent()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITicketEmailDeliveryService>();
        await using var db = fixture.CreateDbContext();
        await db.TicketEmailDeliveries.ExecuteDeleteAsync();
        var (booking, _) = await CreateBookingAsync(db);
        var delivery = new TicketEmailDelivery(Guid.NewGuid(), booking.Id, DateTime.UtcNow);
        db.TicketEmailDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        var claimed = await service.ClaimDueAsync(10, CancellationToken.None);
        var second = await service.ClaimDueAsync(10, CancellationToken.None);

        Assert.Equal([delivery.Id], claimed);
        Assert.Empty(second);
        Assert.Equal(delivery.Id, Assert.Single(await db.TicketEmailDeliveries.Where(x => x.LeaseUntilUtc != null).Select(x => x.Id).ToListAsync()));

        await service.CompleteAsync(delivery.Id, CancellationToken.None);
        await using var verify = fixture.CreateDbContext();
        var sent = await verify.TicketEmailDeliveries.SingleAsync(x => x.Id == delivery.Id);
        Assert.Equal(TicketEmailDeliveryStatus.Sent, sent.Status);
        Assert.NotNull(sent.SentAtUtc);
        Assert.Null(sent.LeaseUntilUtc);
    }

    [Fact]
    public async Task Expired_leases_are_reclaimed_and_retries_clear_the_lease()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITicketEmailDeliveryService>();
        await using var db = fixture.CreateDbContext();
        await db.TicketEmailDeliveries.ExecuteDeleteAsync();
        var (booking, _) = await CreateBookingAsync(db);
        var delivery = new TicketEmailDelivery(Guid.NewGuid(), booking.Id, DateTime.UtcNow);
        db.TicketEmailDeliveries.Add(delivery);
        await db.SaveChangesAsync();
        _ = await service.ClaimDueAsync(10, CancellationToken.None);

        await using (var db2 = fixture.CreateDbContext())
        {
            await db2.TicketEmailDeliveries.Where(x => x.Id == delivery.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.LeaseUntilUtc, DateTime.UtcNow.AddMinutes(-1)));
        }
        var reclaimed = await service.ClaimDueAsync(10, CancellationToken.None);
        Assert.Equal([delivery.Id], reclaimed);

        await service.RetryAsync(delivery.Id, "SMTP unavailable", CancellationToken.None);
        await using var verify = fixture.CreateDbContext();
        var retried = await verify.TicketEmailDeliveries.SingleAsync(x => x.Id == delivery.Id);
        Assert.Equal(TicketEmailDeliveryStatus.Pending, retried.Status);
        Assert.Equal(1, retried.AttemptCount);
        Assert.Null(retried.LeaseUntilUtc);
        Assert.NotNull(retried.NextAttemptAtUtc);
        Assert.True(retried.NextAttemptAtUtc > DateTime.UtcNow.AddSeconds(30));
        Assert.Contains("SMTP unavailable", retried.LastError);
    }

    [Fact]
    public async Task Enqueue_resets_a_pending_delivery_or_creates_a_fresh_one()
    {
        using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITicketEmailDeliveryService>();
        await using var db = fixture.CreateDbContext();
        var (booking, _) = await CreateBookingAsync(db);
        var sentDelivery = new TicketEmailDelivery(Guid.NewGuid(), booking.Id, DateTime.UtcNow);
        sentDelivery.MarkSent(DateTime.UtcNow);
        db.TicketEmailDeliveries.Add(sentDelivery);
        await db.SaveChangesAsync();

        await service.EnqueueAsync(booking.Id, CancellationToken.None);

        await using var verify = fixture.CreateDbContext();
        var rows = await verify.TicketEmailDeliveries.Where(x => x.BookingId == booking.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        var pending = Assert.Single(rows, x => x.Status == TicketEmailDeliveryStatus.Pending);
        Assert.Equal(0, pending.AttemptCount);

        await service.EnqueueAsync(booking.Id, CancellationToken.None);
        Assert.Equal(2, await verify.TicketEmailDeliveries.CountAsync(x => x.BookingId == booking.Id));
    }

    private static string bookingCode(LumiCine.Domain.Bookings.Booking booking) => booking.Code;

    private async Task<LumiCine.Domain.Bookings.Booking> GetBookingAsync(Guid bookingId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Bookings.SingleAsync(x => x.Id == bookingId);
    }

    private static IReadOnlyList<string> ExtractCodes(string text)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(text, "[A-Za-z0-9_-]{43}");
        return matches.Select(match => match.Value).ToArray();
    }

    private async Task<(Guid BookingId, string Email)> CreateFulfilledBookingAsync(LumiCineDbContext db, string? movieTitle = null)
    {
        var (booking, email) = await CreateBookingAsync(db, movieTitle);
        var items = await db.BookingItems.Where(x => x.BookingId == booking.Id).ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var item in items)
            db.Tickets.Add(new Ticket(Guid.NewGuid(), item.Id, Domain.Bookings.TicketCode.New(), now, now));
        await db.SaveChangesAsync();
        return (booking.Id, email);
    }

    private async Task<(LumiCine.Domain.Bookings.Booking Booking, string Email)> CreateBookingAsync(LumiCineDbContext db, string? movieTitle = null)
    {
        var now = DateTime.UtcNow;
        var movie = new Movie(Guid.NewGuid(), movieTitle ?? $"Film-{Guid.NewGuid():N}", 100, now);
        var cinema = new Cinema(Guid.NewGuid(), $"C-{Guid.NewGuid():N}"[..22], "Test Cinema", "Test Address", "Test City", now);
        var room = new Room(Guid.NewGuid(), cinema.Id, $"R-{Guid.NewGuid():N}"[..22], "Test Room", now);
        await db.AddRangeAsync(movie, cinema, room);
        await db.SaveChangesAsync();
        var start = DateTime.UtcNow.AddDays(1);
        var showtime = new Showtime(Guid.NewGuid(), movie.Id, room.Id, start, start.AddMinutes(100), 100, now);
        await db.Showtimes.AddAsync(showtime);
        var userId = Guid.NewGuid();
        var email = $"{userId:N}@example.com";
        db.Users.Add(new ApplicationUser
        {
            Id = userId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true, IsActive = true, DisplayName = "Email test"
        });
        var booking = new LumiCine.Domain.Bookings.Booking(Guid.NewGuid(), $"LC{Guid.NewGuid():N}", showtime.Id, 200,
            now.AddMinutes(-1), holdExpiresAtUtc: now.AddMinutes(5), userId: userId, requestId: Guid.NewGuid());
        db.Bookings.Add(booking);
        var seats = new List<LumiCine.Domain.Scheduling.ShowtimeSeat>();
        for (var i = 0; i < 2; i++)
        {
            var seat = await DatabaseTestData.CreateSeatAsync(db, room.Id, number: i + 1);
            var showtimeSeat = new ShowtimeSeat(Guid.NewGuid(), showtime.Id, room.Id, seat.Id, 100, now);
            await db.ShowtimeSeats.AddAsync(showtimeSeat);
            seats.Add(showtimeSeat);
            db.BookingItems.Add(new BookingItem(Guid.NewGuid(), booking.Id, showtime.Id, showtimeSeat.Id, 100, DateTime.UtcNow));
        }
        await db.SaveChangesAsync();
        foreach (var showtimeSeat in seats)
            showtimeSeat.Hold(booking.Id);
        await db.SaveChangesAsync();
        return (booking, email);
    }

    private sealed class TestHostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "LumiCine";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(AppContext.BaseDirectory);
        public System.IO.FileInfo ContentRootFile { get; set; } = new(System.IO.Path.Combine(".", "LumiCine"));
    }

    private sealed class FakeSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _shutdown = new();
        private readonly TaskCompletionSource<string> _message = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _serverTask;

        public FakeSmtpServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serverTask = RunAsync();
        }

        public int Port { get; }
        public Task<string> MessageTask => _message.Task;

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            _shutdown.Cancel();
            try { await _serverTask.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception) { }
        }

        private async Task RunAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync(_shutdown.Token);
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
                    _message.TrySetResult(message.ToString());
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
