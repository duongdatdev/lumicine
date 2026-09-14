using System.Collections.Concurrent;
using System.Text;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LumiCine.IntegrationTests.Persistence;

namespace LumiCine.IntegrationTests.AccountEmail;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class AccountEmailServiceTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Confirmation_email_contains_escaped_link_with_a_valid_confirmation_token()
    {
        using var keys = new TemporaryKeyRing();
        var sender = new RecordingEmailSender();
        using var services = BuildServices(keys.Path, sender, fixture.ConnectionString);
        var email = $"jöhn+tag-{Guid.NewGuid():N}@example.test";
        var user = await CreateUserAsync(services, email, confirmed: false);

        await services.GetRequiredService<IAccountEmailService>().SendConfirmationAsync(user.Id);

        var message = Assert.Single(sender.Messages);
        Assert.Equal(user.Email, message.To);
        Assert.Equal("Confirm your LumiCine email", message.Subject);
        var link = ExtractLink(message.Body);
        Assert.Equal("https://localhost:7005/account/confirm-email", link.GetLeftPart(UriPartial.Path));
        var query = ParseQuery(link);
        Assert.Equal(user.Id.ToString("D"), query["userId"]);
        var token = DecodeToken(query["code"]);
        var manager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var result = await manager.ConfirmEmailAsync(user, token);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Confirmation_and_reset_tokens_are_purpose_separated_with_distinct_lifetimes()
    {
        using var keys = new TemporaryKeyRing();
        var sender = new RecordingEmailSender();
        using var services = BuildServices(keys.Path, sender, fixture.ConnectionString);
        var user = await CreateUserAsync(services, "user@example.test", confirmed: false);
        var manager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var accountEmail = services.GetRequiredService<IAccountEmailService>();

        await accountEmail.SendConfirmationAsync(user.Id);
        var confirmationCode = DecodeToken(ParseQuery(ExtractLink(Assert.Single(sender.Messages).Body))["code"]);
        Assert.True((await manager.ConfirmEmailAsync(user, confirmationCode)).Succeeded);

        await accountEmail.SendPasswordResetAsync(user.Id);
        var resetCode = DecodeToken(ParseQuery(ExtractLink(sender.Messages.Last().Body))["code"]);

        Assert.False((await manager.ConfirmEmailAsync(user, resetCode)).Succeeded);
        Assert.Equal(TimeSpan.FromHours(24), services.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailConfirmationTokenOptions>>().Value.TokenLifespan);
        Assert.Equal(TimeSpan.FromHours(1), services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PasswordResetTokenOptions>>().Value.TokenLifespan);
    }

    [Fact]
    public async Task Reset_link_round_trips_encoded_email_and_password_change_invalidates_token()
    {
        using var keys = new TemporaryKeyRing();
        var sender = new RecordingEmailSender();
        using var services = BuildServices(keys.Path, sender, fixture.ConnectionString);
        var email = $"jöhn+tag-{Guid.NewGuid():N}@example.test";
        var user = await CreateUserAsync(services, email, confirmed: true);

        await services.GetRequiredService<IAccountEmailService>().SendPasswordResetAsync(user.Id);

        var message = Assert.Single(sender.Messages);
        Assert.Equal("Reset your LumiCine password", message.Subject);
        var query = ParseQuery(ExtractLink(message.Body));
        Assert.Equal(email, query["email"]);
        Assert.Equal("https://localhost:7005/account/reset-password", ExtractLink(message.Body).GetLeftPart(UriPartial.Path));
        var manager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var token = DecodeToken(query["code"]);
        var resetResult = await manager.ResetPasswordAsync(user, token, "NewPassword123!");
        Assert.True(resetResult.Succeeded, string.Join("; ", resetResult.Errors.Select(error => error.Description)));
        Assert.False((await manager.ResetPasswordAsync(user, token, "AnotherPassword123!")).Succeeded);
    }

    [Fact]
    public async Task Missing_inactive_and_ineligible_accounts_do_not_receive_email()
    {
        using var keys = new TemporaryKeyRing();
        var sender = new RecordingEmailSender();
        using var services = BuildServices(keys.Path, sender, fixture.ConnectionString);
        var inactive = await CreateUserAsync(services, "inactive@example.test", confirmed: false, active: false);
        var confirmed = await CreateUserAsync(services, "confirmed@example.test", confirmed: true);
        var service = services.GetRequiredService<IAccountEmailService>();

        await service.SendConfirmationAsync(inactive.Id);
        await service.SendConfirmationAsync(confirmed.Id);
        await service.SendPasswordResetAsync(inactive.Id);
        await service.SendPasswordResetAsync(Guid.NewGuid());

        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task Persisted_key_ring_keeps_confirmation_tokens_valid_after_service_restart()
    {
        using var keys = new TemporaryKeyRing();
        var firstSender = new RecordingEmailSender();
        Guid userId;
        string token;
        using (var first = BuildServices(keys.Path, firstSender, fixture.ConnectionString))
        {
            var user = await CreateUserAsync(first, "restart@example.test", confirmed: false);
            userId = user.Id;
            await first.GetRequiredService<IAccountEmailService>().SendConfirmationAsync(userId);
            token = DecodeToken(ParseQuery(ExtractLink(Assert.Single(firstSender.Messages).Body))["code"]);
        }

        using var second = BuildServices(keys.Path, new RecordingEmailSender(), fixture.ConnectionString);
        var manager = second.GetRequiredService<UserManager<ApplicationUser>>();
        var userAfterRestart = await manager.FindByIdAsync(userId.ToString("D"));
        Assert.NotNull(userAfterRestart);
        Assert.True((await manager.ConfirmEmailAsync(userAfterRestart, token)).Succeeded);
    }

    [Fact]
    public async Task Email_sender_failure_propagates_without_logging_token_or_message_body()
    {
        using var keys = new TemporaryKeyRing();
        var logs = new CapturingLoggerProvider();
        var failure = new InvalidOperationException("Email delivery failed.");
        var sender = new RecordingEmailSender(failure);
        using var services = BuildServices(keys.Path, sender, fixture.ConnectionString, loggerProvider: logs);
        var user = await CreateUserAsync(services, "log-check@example.test", confirmed: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.GetRequiredService<IAccountEmailService>().SendConfirmationAsync(user.Id));
        var code = ParseQuery(ExtractLink(Assert.Single(sender.Messages).Body))["code"];

        Assert.Same(failure, exception);
        Assert.DoesNotContain(code, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains("log-check", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains("Confirm your email", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains(code, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Expired_confirmation_and_reset_tokens_are_rejected()
    {
        using var keys = new TemporaryKeyRing();
        var sender = new RecordingEmailSender();
        using var services = BuildServices(keys.Path, sender, fixture.ConnectionString, expireTokens: true);
        var user = await CreateUserAsync(services, $"expired-{Guid.NewGuid():N}@example.test", confirmed: false);
        var service = services.GetRequiredService<IAccountEmailService>();
        var manager = services.GetRequiredService<UserManager<ApplicationUser>>();

        await service.SendConfirmationAsync(user.Id);
        var confirmationCode = DecodeToken(ParseQuery(ExtractLink(Assert.Single(sender.Messages).Body))["code"]);
        Assert.False((await manager.ConfirmEmailAsync(user, confirmationCode)).Succeeded);

        user.EmailConfirmed = true;
        Assert.True((await manager.UpdateAsync(user)).Succeeded);
        await service.SendPasswordResetAsync(user.Id);
        var resetCode = DecodeToken(ParseQuery(ExtractLink(sender.Messages.Last().Body))["code"]);
        Assert.False((await manager.ResetPasswordAsync(user, resetCode, "NewPassword123!")).Succeeded);
    }

    [Theory]
    [InlineData("http://accounts.example.test")]
    [InlineData("https://accounts.example.test/?next=/home")]
    [InlineData("https://user@accounts.example.test")]
    public void Invalid_account_link_origins_fail_options_validation(string baseUrl)
    {
        using var keys = new TemporaryKeyRing();
        using var services = BuildServices(keys.Path, new RecordingEmailSender(), fixture.ConnectionString, baseUrl: baseUrl);

        Assert.Throws<OptionsValidationException>(
            () => services.GetRequiredService<IAccountEmailService>());
    }

    private static ServiceProvider BuildServices(
        string keyRingPath,
        RecordingEmailSender sender,
        string connectionString,
        ILoggerProvider? loggerProvider = null,
        string baseUrl = "https://localhost:7005",
        bool expireTokens = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AccountLinks:BaseUrl"] = baseUrl,
                ["DataProtection:KeyRingPath"] = keyRingPath
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            if (loggerProvider is not null)
            {
                builder.AddProvider(loggerProvider);
            }
        });
        services.AddDbContext<LumiCineDbContext>(options => options.UseNpgsql(connectionString));
        services.AddIdentityCore<ApplicationUser>()
            .AddEntityFrameworkStores<LumiCineDbContext>();
        services.AddSingleton<IEmailSender>(sender);
        services.AddAccountEmailTokens(configuration);
        if (expireTokens)
        {
            services.Configure<EmailConfirmationTokenOptions>(options => options.TokenLifespan = TimeSpan.FromSeconds(-1));
            services.Configure<PasswordResetTokenOptions>(options => options.TokenLifespan = TimeSpan.FromSeconds(-1));
        }

        return services.BuildServiceProvider();
    }

    private static async Task<ApplicationUser> CreateUserAsync(
        IServiceProvider services,
        string email,
        bool confirmed,
        bool active = true)
    {
        var user = new ApplicationUser
        {
            UserName = $"email-test-{Guid.NewGuid():N}",
            Email = email,
            EmailConfirmed = confirmed,
            IsActive = active,
            DisplayName = "Email Test User"
        };
        var result = await services.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(user);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        return user;
    }

    private static Uri ExtractLink(string body) => new(body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last().Trim(), UriKind.Absolute);

    private static Dictionary<string, string> ParseQuery(Uri uri) => uri.Query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);

    private static string DecodeToken(string encoded) => Encoding.UTF8.GetString(Convert.FromBase64String(
        encoded.Replace('-', '+').Replace('_', '/') + new string('=', (4 - encoded.Length % 4) % 4)));

    private sealed class RecordingEmailSender(Exception? failure = null) : IEmailSender
    {
        private readonly ConcurrentQueue<EmailMessage> _messages = new();
        public IReadOnlyCollection<EmailMessage> Messages => _messages.ToArray();

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _messages.Enqueue(message);
            if (failure is not null)
            {
                throw failure;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryKeyRing : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lumicine-keyring-{Guid.NewGuid():N}");

        public void Dispose()
        {
            var fullPath = System.IO.Path.GetFullPath(Path);
            var tempRoot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
            if (Directory.Exists(fullPath) &&
                Directory.GetParent(fullPath)?.FullName == tempRoot &&
                System.IO.Path.GetFileName(fullPath).StartsWith("lumicine-keyring-", StringComparison.Ordinal))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
    }
}
