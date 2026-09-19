using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Identity;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace LumiCine.IntegrationTests.Authentication;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class ForgotPasswordEndpointTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Unknown_inactive_and_unconfirmed_accounts_share_accepted_response_without_email()
    {
        var sender = new RecordingEmailSender();
        using var factory = CreateFactory(sender);
        using var client = CreateHttpsClient(factory);
        var inactive = await CreateUserAsync(factory, $"forgot-inactive-{Guid.NewGuid():N}@example.test", active: false);
        var unconfirmed = await CreateUserAsync(factory, $"forgot-unconfirmed-{Guid.NewGuid():N}@example.test", confirmed: false);

        var responses = new[]
        {
            await PostWithCsrfAsync(client, new { email = "unknown@example.test" }),
            await PostWithCsrfAsync(client, new { email = inactive.Email }),
            await PostWithCsrfAsync(client, new { email = unconfirmed.Email })
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        Assert.All(responses, response => Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.All(responses, response => Assert.Equal(0, response.Content.Headers.ContentLength ?? 0));
        Assert.Empty(sender.Messages);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Eligible_account_delivery_failure_stays_generic_and_logs_only_error_type()
    {
        const string email = "forgot-eligible@example.test";
        const string privateReply = "secret SMTP response with raw reset code";
        var sender = new RecordingEmailSender(new EmailDeliveryException(privateReply));
        using var logs = new CapturingLoggerProvider(LogLevel.Warning);
        using var factory = CreateFactory(sender, logs);
        using var client = CreateHttpsClient(factory);
        var user = await CreateUserAsync(factory, email);

        using var response = await PostWithCsrfAsync(client, new { email = $" {email.ToUpperInvariant()} " });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, response.Content.Headers.ContentLength ?? 0);
        var sent = Assert.Single(sender.Messages);
        Assert.Equal(email, sent.To);
        Assert.Contains("/account/reset-password", sent.Body, StringComparison.Ordinal);
        var endpointLogs = logs.Entries.Where(entry => entry.Category == "LumiCine.API.Controllers.ForgotPasswordController").ToArray();
        var log = Assert.Single(endpointLogs);
        Assert.Contains(nameof(EmailDeliveryException), log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(email, log.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(privateReply, log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("code", log.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Forgot_password_rejects_invalid_email_and_requires_https_and_csrf()
    {
        var sender = new RecordingEmailSender();
        using var factory = CreateFactory(sender);
        using var httpsClient = CreateHttpsClient(factory);
        using var invalid = await PostWithCsrfAsync(httpsClient, new { email = "not-an-email" });
        using var missingCsrf = await httpsClient.PostAsJsonAsync("/api/auth/forgot-password", new { email = "user@example.test" });
        using var httpClient = factory.CreateClient();
        using var httpResponse = await httpClient.PostAsJsonAsync("/api/auth/forgot-password", new { email = "user@example.test" });

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, httpResponse.StatusCode);
        using var missingBody = await JsonDocument.ParseAsync(await missingCsrf.Content.ReadAsStreamAsync());
        using var httpBody = await JsonDocument.ParseAsync(await httpResponse.Content.ReadAsStreamAsync());
        Assert.Equal("Invalid CSRF token", missingBody.RootElement.GetProperty("title").GetString());
        Assert.Equal("HTTPS required", httpBody.RootElement.GetProperty("title").GetString());
        Assert.Empty(sender.Messages);
    }

    private WebApplicationFactory<Program> CreateFactory(RecordingEmailSender sender, ILoggerProvider? logs = null) =>
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
            });
        });

    private static HttpClient CreateHttpsClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<HttpResponseMessage> PostWithCsrfAsync(HttpClient client, object body)
    {
        using var tokenResponse = await client.GetAsync("/api/auth/csrf");
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenBody = await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot-password") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", tokenBody.RootElement.GetProperty("requestToken").GetString());
        return await client.SendAsync(request);
    }

    private async Task<ApplicationUser> CreateUserAsync(
        WebApplicationFactory<Program> factory,
        string email,
        bool confirmed = true,
        bool active = true)
    {
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = email, Email = email,
            EmailConfirmed = confirmed, IsActive = active, DisplayName = "Forgot Password Test"
        };
        var result = await manager.CreateAsync(user, "SecurePass123");
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        return user;
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
