using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace LumiCine.IntegrationTests.Authentication;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class EmailConfirmationEndpointTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Registered_user_can_confirm_email_using_link_code_and_repeat_idempotently()
    {
        var sender = new RecordingEmailSender();
        using var factory = CreateFactory(sender);
        using var client = CreateHttpsClient(factory);
        using var registration = await RegisterAsync(client);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var registrationBody = await JsonDocument.ParseAsync(await registration.Content.ReadAsStreamAsync());
        var userId = registrationBody.RootElement.GetProperty("id").GetGuid();
        var message = Assert.Single(sender.Messages);
        var link = new Uri(message.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last().Trim());
        var code = ParseQuery(link)["code"];

        using var first = await PostWithCsrfAsync(client, "/api/auth/confirm-email", new { userId, code });
        using var repeated = await PostWithCsrfAsync(client, "/api/auth/confirm-email", new { userId, code });

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.True((await db.Users.SingleAsync(user => user.Id == userId)).EmailConfirmed);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("wrong-user")]
    [InlineData("wrong-purpose")]
    [InlineData("expired")]
    [InlineData("unknown-user")]
    public async Task Invalid_confirmation_code_returns_generic_bad_request(string kind)
    {
        var sender = new RecordingEmailSender();
        using var factory = CreateFactory(sender, expireConfirmationTokens: kind == "expired");
        using var client = CreateHttpsClient(factory);
        var user = await CreateUserAsync(factory, $"{kind}-{Guid.NewGuid():N}@example.test");
        var code = kind switch
        {
            "malformed" => "not***base64",
            "wrong-purpose" => await GenerateCodeAsync(factory, user, passwordReset: true),
            _ => await GenerateCodeAsync(factory, user)
        };
        var userId = kind == "unknown-user" ? Guid.NewGuid() : user.Id;
        if (kind == "wrong-user")
        {
            userId = (await CreateUserAsync(factory, $"other-{Guid.NewGuid():N}@example.test")).Id;
        }

        using var response = await PostWithCsrfAsync(client, "/api/auth/confirm-email", new { userId, code });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "Invalid confirmation token");
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Users.AnyAsync(candidate => candidate.Id == userId && candidate.EmailConfirmed));
    }

    [Fact]
    public async Task Resend_confirmation_returns_same_accepted_response_for_unknown_confirmed_and_inactive_users()
    {
        var sender = new RecordingEmailSender();
        using var factory = CreateFactory(sender);
        using var client = CreateHttpsClient(factory);
        var confirmed = await CreateUserAsync(factory, $"confirmed-{Guid.NewGuid():N}@example.test", confirmed: true);
        var inactive = await CreateUserAsync(factory, $"inactive-{Guid.NewGuid():N}@example.test", active: false);
        var addresses = new[] { "unknown@example.test", confirmed.Email!, inactive.Email! };
        var responses = new List<HttpResponseMessage>();

        try
        {
            foreach (var email in addresses)
            {
                responses.Add(await PostWithCsrfAsync(client, "/api/auth/resend-confirmation", new { email }));
            }

            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
            Assert.All(responses, response => Assert.Equal(0, response.Content.Headers.ContentLength ?? 0));
            Assert.Empty(sender.Messages);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Resend_confirmation_sends_to_eligible_pending_user_but_returns_accepted_on_email_failure()
    {
        const string email = "resend-pending@example.test";
        const string password = "PrivatePassword123";
        using var logs = new CapturingLoggerProvider(LogLevel.Warning);
        var sender = new RecordingEmailSender(new EmailDeliveryException("private SMTP reply and token material"));
        using var factory = CreateFactory(sender, logs: logs);
        using var client = CreateHttpsClient(factory);
        var user = await CreateUserAsync(factory, email);

        using var response = await PostWithCsrfAsync(client, "/api/auth/resend-confirmation", new { email = $" {email.ToUpperInvariant()} " });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(email, Assert.Single(sender.Messages).To);
        var logsForEndpoint = logs.Entries.Where(entry => entry.Category == "LumiCine.API.Controllers.EmailConfirmationController").ToArray();
        Assert.Single(logsForEndpoint);
        Assert.Contains(nameof(EmailDeliveryException), logsForEndpoint[0].Message, StringComparison.Ordinal);
        Assert.DoesNotContain(email, logsForEndpoint[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(password, logsForEndpoint[0].Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private SMTP reply", logsForEndpoint[0].Message, StringComparison.Ordinal);
        await using var db = fixture.CreateDbContext();
        Assert.False((await db.Users.SingleAsync(candidate => candidate.Id == user.Id)).EmailConfirmed);
    }

    [Fact]
    public async Task Confirmation_and_resend_require_https_and_csrf_tokens()
    {
        var sender = new RecordingEmailSender();
        using var factory = CreateFactory(sender);
        using var httpsClient = CreateHttpsClient(factory);
        using var missingCsrf = await httpsClient.PostAsJsonAsync("/api/auth/confirm-email", new { userId = Guid.NewGuid(), code = "x" });
        using var httpClient = factory.CreateClient();
        using var httpResend = await httpClient.PostAsJsonAsync("/api/auth/resend-confirmation", new { email = "user@example.test" });

        await AssertProblemAsync(missingCsrf, HttpStatusCode.BadRequest, "Invalid CSRF token");
        await AssertProblemAsync(httpResend, HttpStatusCode.BadRequest, "HTTPS required");
    }

    private WebApplicationFactory<Program> CreateFactory(
        RecordingEmailSender sender,
        ILoggerProvider? logs = null,
        bool expireConfirmationTokens = false) =>
        new ApiWebApplicationFactory(connectionString: fixture.ConnectionString).WithWebHostBuilder(builder =>
        {
            if (logs is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(logs));
            }

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
                if (expireConfirmationTokens)
                {
                    services.Configure<EmailConfirmationTokenOptions>(options => options.TokenLifespan = TimeSpan.FromSeconds(-1));
                }
            });
        });

    private static HttpClient CreateHttpsClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client)
    {
        var email = $"confirmation-{Guid.NewGuid():N}@example.test";
        return await PostWithCsrfAsync(client, "/api/auth/register", new
        {
            email,
            displayName = "Confirmation Test",
            password = "SecurePass123"
        });
    }

    private static async Task<HttpResponseMessage> PostWithCsrfAsync(HttpClient client, string path, object body)
    {
        using var tokenResponse = await client.GetAsync("/api/auth/csrf");
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenBody = await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync());
        var token = tokenBody.RootElement.GetProperty("requestToken").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static async Task<ApplicationUser> CreateUserAsync(
        WebApplicationFactory<Program> factory,
        string email,
        bool confirmed = false,
        bool active = true)
    {
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = confirmed,
            IsActive = active,
            DisplayName = "Email Confirmation Test"
        };
        var result = await manager.CreateAsync(user);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        return user;
    }

    private static async Task<string> GenerateCodeAsync(WebApplicationFactory<Program> factory, ApplicationUser user, bool passwordReset = false)
    {
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var token = passwordReset
            ? await manager.GeneratePasswordResetTokenAsync(user)
            : await manager.GenerateEmailConfirmationTokenAsync(user);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    }

    private static Dictionary<string, string> ParseQuery(Uri uri) => uri.Query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode statusCode, string title)
    {
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal((int)statusCode, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(title, body.RootElement.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }

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
}
