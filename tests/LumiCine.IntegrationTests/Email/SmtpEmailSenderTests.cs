using System.Net;
using System.Net.Sockets;
using System.Text;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Email;
using MailKit.Security;
using Microsoft.Extensions.Hosting;
using MimeKit;

namespace LumiCine.IntegrationTests.Email;

public sealed class SmtpEmailSenderTests
{
    [Fact]
    public async Task SendAsync_delivers_plaintext_message_to_local_smtp_server()
    {
        await using var server = new FakeSmtpServer();
        var sender = CreateSender(server.Port);

        await sender.SendAsync(new EmailMessage("recipient@example.test", "Hello", "Message body"));

        var message = MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(await server.MessageTask)));
        Assert.Equal("recipient@example.test", message.To.Mailboxes.Single().Address);
        Assert.Equal("Hello", message.Subject);
        Assert.Equal("Message body", message.TextBody?.TrimEnd('\r', '\n'));
    }

    [Fact]
    public async Task SendAsync_without_credentials_fails_safely()
    {
        var sender = new SmtpEmailSender(new EmailOptions { FromAddress = "sender@example.test" }, new TestHostEnvironment("Development"));

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            sender.SendAsync(new EmailMessage("recipient@example.test", "Subject", "Body")));

        Assert.Equal("Email delivery is not configured.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("secret", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Production", "localhost", SecureSocketOptions.None)]
    [InlineData("Development", "smtp.example.test", SecureSocketOptions.None)]
    [InlineData("Production", "smtp.gmail.com", SecureSocketOptions.Auto)]
    [InlineData("Development", "localhost", SecureSocketOptions.Auto)]
    [InlineData("Production", "smtp.gmail.com", SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData("Development", "localhost", SecureSocketOptions.StartTlsWhenAvailable)]
    public void Unsafe_security_modes_are_rejected(string environment, string host, SecureSocketOptions security)
    {
        Assert.Throws<InvalidOperationException>(() => new SmtpEmailSender(
            new EmailOptions { Host = host, Security = security }, new TestHostEnvironment(environment)));
    }

    [Fact]
    public async Task SendAsync_maps_smtp_errors_to_safe_exception()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var sender = CreateSender(port);

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            sender.SendAsync(new EmailMessage("recipient@example.test", "Subject", "Body")));

        Assert.Equal("Email delivery failed.", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task SendAsync_does_not_expose_smtp_server_replies()
    {
        await using var server = new FakeSmtpServer(authenticationFailure: true);
        var sender = CreateSender(server.Port);

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            sender.SendAsync(new EmailMessage("recipient@example.test", "Private subject", "Private body")));

        Assert.Equal("Email delivery failed.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("local-test-secret", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Private body", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_propagates_cancellation()
    {
        await using var server = new FakeSmtpServer(waitForCancellation: true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var sender = CreateSender(server.Port);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sender.SendAsync(new EmailMessage("recipient@example.test", "Subject", "Body"), cancellation.Token));
    }

    private static SmtpEmailSender CreateSender(int port) => new(
        new EmailOptions
        {
            Host = "127.0.0.1",
            Port = port,
            Security = SecureSocketOptions.None,
            Username = "local-test-user",
            Password = "local-test-secret",
            FromAddress = "sender@example.test"
        },
        new TestHostEnvironment("Development"));

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "LumiCine.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class FakeSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _shutdown = new();
        private readonly TaskCompletionSource<string> _message = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _serverTask;

        public FakeSmtpServer(bool waitForCancellation = false, bool authenticationFailure = false)
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serverTask = RunAsync(waitForCancellation, authenticationFailure);
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

        private async Task RunAsync(bool waitForCancellation, bool authenticationFailure)
        {
            using var client = await _listener.AcceptTcpClientAsync(_shutdown.Token);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };

            if (waitForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, _shutdown.Token);
            }

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
                    await writer.WriteLineAsync(authenticationFailure
                        ? "535 local-test-secret Private body"
                        : "235 authenticated");
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
                        message.AppendLine(messageLine.StartsWith("..", StringComparison.Ordinal) ? messageLine[1..] : messageLine);
                    }

                    _message.TrySetResult(message.ToString());
                    await writer.WriteLineAsync("250 queued");
                }
                else if (line.Equals("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 bye");
                    break;
                }
                else
                {
                    await writer.WriteLineAsync("250 ok");
                }
            }
        }
    }
}
