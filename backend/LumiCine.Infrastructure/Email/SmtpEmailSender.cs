using LumiCine.Application.Accounts;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Hosting;
using MimeKit;

namespace LumiCine.Infrastructure.Email;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly bool _allowLocalPlaintext;

    public SmtpEmailSender(EmailOptions options, IHostEnvironment environment)
    {
        _options = options;
        _allowLocalPlaintext = environment.IsDevelopment() && IsLoopback(options.Host);
        ValidateOptions();
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateMessage(message);

        if (string.IsNullOrWhiteSpace(_options.Username) ||
            string.IsNullOrWhiteSpace(_options.Password) ||
            string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            throw new EmailDeliveryException("Email delivery is not configured.");
        }

        var mail = new MimeMessage();
        mail.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        mail.To.Add(MailboxAddress.Parse(message.To));
        mail.Subject = message.Subject;
        mail.Body = new TextPart("plain") { Text = message.Body };

        try
        {
            using var client = new SmtpClient
            {
                Timeout = checked(_options.TimeoutSeconds * 1000)
            };
            await client.ConnectAsync(_options.Host, _options.Port, _options.Security, cancellationToken);
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
            await client.SendAsync(mail, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new EmailDeliveryException("Email delivery failed.");
        }
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.Host) || _options.Port is < 1 or > 65535 ||
            _options.TimeoutSeconds is < 1 or > 300 || !Enum.IsDefined(_options.Security))
        {
            throw new InvalidOperationException("Email delivery options are invalid.");
        }

        if (_options.Security == SecureSocketOptions.None)
        {
            if (!_allowLocalPlaintext)
            {
                throw new InvalidOperationException("Plaintext SMTP is only allowed for a loopback host in Development.");
            }
        }
        else if (_options.Security is not SecureSocketOptions.StartTls and not SecureSocketOptions.SslOnConnect)
        {
            throw new InvalidOperationException("Email delivery requires TLS without an opportunistic downgrade.");
        }

        if (!string.IsNullOrWhiteSpace(_options.FromAddress) && !MailboxAddress.TryParse(_options.FromAddress, out _))
        {
            throw new InvalidOperationException("Email sender address is invalid.");
        }
    }

    private static void ValidateMessage(EmailMessage message)
    {
        if (!MailboxAddress.TryParse(message.To, out _) || string.IsNullOrWhiteSpace(message.Subject) || message.Body is null)
        {
            throw new ArgumentException("Email recipient, subject, and body must be valid.", nameof(message));
        }
    }

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address);
}
