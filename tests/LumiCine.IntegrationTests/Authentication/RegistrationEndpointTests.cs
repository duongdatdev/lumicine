using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace LumiCine.IntegrationTests.Authentication;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class RegistrationEndpointTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Register_creates_unconfirmed_customer_and_sends_confirmation_email()
    {
        var emailSender = new RecordingEmailSender();
        using var factory = CreateFactory(emailSender);
        using var client = CreateHttpsClient(factory);
        using var response = await RegisterAsync(client, new
        {
            email = "  Jane.Customer@example.test ",
            displayName = "  Jane Customer  ",
            password = "SecurePass123",
            role = "Admin"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var responseBody = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var id = responseBody.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("Jane.Customer@example.test", responseBody.RootElement.GetProperty("email").GetString());
        Assert.Equal("Jane Customer", responseBody.RootElement.GetProperty("displayName").GetString());
        Assert.True(responseBody.RootElement.GetProperty("confirmationRequired").GetBoolean());
        Assert.False(responseBody.RootElement.TryGetProperty("role", out _));

        await using var db = fixture.CreateDbContext();
        var user = await db.Users.SingleAsync(item => item.Id == id);
        Assert.False(user.EmailConfirmed);
        Assert.True(user.IsActive);
        Assert.Equal("Jane.Customer@example.test", user.Email);
        Assert.NotEqual("SecurePass123", user.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<ApplicationUser>().VerifyHashedPassword(user, user.PasswordHash!, "SecurePass123"));
        var assignment = await db.UserRoles.SingleAsync(item => item.UserId == user.Id);
        var role = await db.Roles.SingleAsync(item => item.Id == assignment.RoleId);
        Assert.Equal(RoleNames.Customer, role.Name);
        var message = Assert.Single(emailSender.Messages);
        Assert.Equal(user.Email, message.To);
        Assert.Equal("Confirm your LumiCine email", message.Subject);
        Assert.Contains("https://localhost:5173/account/confirm-email", message.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("SecurePass123", message.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_rejects_weak_password_with_validation_problem()
    {
        var emailSender = new RecordingEmailSender();
        using var factory = CreateFactory(emailSender);
        using var client = CreateHttpsClient(factory);
        using var response = await RegisterAsync(client, new
        {
            email = "weak-password@example.test",
            displayName = "Weak Password",
            password = "weakpassword"
        });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, expectedTitle: null);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Users.AnyAsync(user => user.NormalizedEmail == "WEAK-PASSWORD@EXAMPLE.TEST"));
        Assert.Empty(emailSender.Messages);
    }

    [Fact]
    public async Task Register_trims_email_and_returns_conflict_for_case_insensitive_duplicate()
    {
        var emailSender = new RecordingEmailSender();
        using var factory = CreateFactory(emailSender);
        using var client = CreateHttpsClient(factory);
        using var first = await RegisterAsync(client, new
        {
            email = "duplicate@example.test",
            displayName = "Original",
            password = "SecurePass123"
        });
        using var second = await RegisterAsync(client, new
        {
            email = "  DUPLICATE@EXAMPLE.TEST ",
            displayName = "Duplicate",
            password = "SecurePass123"
        });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        await AssertProblemAsync(second, HttpStatusCode.Conflict, "Email already registered");
        Assert.Single(emailSender.Messages);
    }

    [Fact]
    public async Task Concurrent_duplicate_registrations_create_only_one_account()
    {
        var emailSender = new RecordingEmailSender();
        using var factory = CreateFactory(emailSender);
        using var client = CreateHttpsClient(factory);
        var token = await GetCsrfTokenAsync(client);
        var first = CreateRegistrationRequest(token, new
        {
            email = "  concurrent@example.test ",
            displayName = "Concurrent One",
            password = "SecurePass123"
        });
        var second = CreateRegistrationRequest(token, new
        {
            email = "CONCURRENT@example.test",
            displayName = "Concurrent Two",
            password = "SecurePass123"
        });

        var responses = await Task.WhenAll(client.SendAsync(first), client.SendAsync(second));
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            await using var db = fixture.CreateDbContext();
            Assert.Equal(1, await db.Users.CountAsync(user => user.NormalizedEmail == "CONCURRENT@EXAMPLE.TEST"));
            Assert.Single(emailSender.Messages);
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
    public async Task Email_delivery_failure_keeps_account_pending_returns_created_and_logs_no_sensitive_data()
    {
        const string email = "pending-after-mail-failure@example.test";
        const string password = "SecurePass123";
        using var logs = new CapturingLoggerProvider(LogLevel.Warning);
        var emailSender = new RecordingEmailSender(new EmailDeliveryException("private SMTP reply and token material"));
        using var factory = CreateFactory(emailSender, logs);
        using var client = CreateHttpsClient(factory);

        using var response = await RegisterAsync(client, new
        {
            email,
            displayName = "Pending User",
            password
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var responseBody = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(responseBody.RootElement.GetProperty("confirmationRequired").GetBoolean());
        await using var db = fixture.CreateDbContext();
        var user = await db.Users.SingleAsync(item => item.NormalizedEmail == email.ToUpperInvariant());
        Assert.False(user.EmailConfirmed);
        var registrationLogs = logs.Entries.Where(entry => entry.Category == "LumiCine.API.Controllers.RegistrationController").ToArray();
        Assert.Single(registrationLogs);
        Assert.Contains(nameof(EmailDeliveryException), registrationLogs[0].Message, StringComparison.Ordinal);
        Assert.DoesNotContain(email, registrationLogs[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(password, registrationLogs[0].Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private SMTP reply", registrationLogs[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Role_assignment_failure_rolls_back_user_creation()
    {
        var emailSender = new RecordingEmailSender();
        var interceptor = new FailUserRoleInsertInterceptor();
        using var factory = CreateFactory(emailSender, interceptor: interceptor);
        using var client = CreateHttpsClient(factory);
        using var response = await RegisterAsync(client, new
        {
            email = "role-rollback@example.test",
            displayName = "Role Rollback",
            password = "SecurePass123"
        });

        var problemJson = await AssertProblemAsync(response, HttpStatusCode.InternalServerError, "An unexpected error occurred.");
        Assert.DoesNotContain("injected user role failure", problemJson, StringComparison.OrdinalIgnoreCase);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Users.AnyAsync(user => user.NormalizedEmail == "ROLE-ROLLBACK@EXAMPLE.TEST"));
        Assert.Empty(emailSender.Messages);
    }

    private WebApplicationFactory<Program> CreateFactory(
        RecordingEmailSender sender,
        ILoggerProvider? loggerProvider = null,
        IInterceptor? interceptor = null) =>
        new ApiWebApplicationFactory(connectionString: fixture.ConnectionString).WithWebHostBuilder(builder =>
        {
            if (loggerProvider is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(loggerProvider));
            }

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
                if (interceptor is not null)
                {
                    services.ConfigureDbContext<LumiCineDbContext>((_, options) => options.AddInterceptors(interceptor));
                }
            });
        });

    private static HttpClient CreateHttpsClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, object body)
    {
        var token = await GetCsrfTokenAsync(client);
        using var request = CreateRegistrationRequest(token, body);
        return await client.SendAsync(request);
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("requestToken").GetString()!;
    }

    private static HttpRequestMessage CreateRegistrationRequest(string token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return request;
    }

    private static async Task<string> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode statusCode, string? expectedTitle)
    {
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(json);
        Assert.Equal((int)statusCode, body.RootElement.GetProperty("status").GetInt32());
        if (expectedTitle is not null)
        {
            Assert.Equal(expectedTitle, body.RootElement.GetProperty("title").GetString());
        }

        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
        return json;
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

    private sealed class FailUserRoleInsertInterceptor : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ThrowOnUserRoleInsert(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowOnUserRoleInsert(command);
            return ValueTask.FromResult(result);
        }

        private static void ThrowOnUserRoleInsert(DbCommand command)
        {
            if (command.CommandText.Contains("\"UserRoles\"", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("injected user role failure");
            }
        }
    }
}
