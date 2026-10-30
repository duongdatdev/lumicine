using LumiCine.API.Exceptions;
using LumiCine.API.Authentication;
using LumiCine.API.Realtime;
using LumiCine.API;
using LumiCine.Application.Scheduling;
using LumiCine.Infrastructure;
using LumiCine.Infrastructure.Bookings;
using LumiCine.Infrastructure.Discovery;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Seeding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

var seedMode = StartupCommandLine.ConsumeSeedFlags(args, out var builderArgs);
var builder = WebApplication.CreateBuilder(builderArgs);
if (seedMode == SeedMode.Demo && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("--seed-demo is available only in the Development environment.");
}
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest
        };
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

        var result = new BadRequestObjectResult(problem);
        result.ContentTypes.Add("application/problem+json");
        return result;
    };
});
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-LumiCine.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
});
builder.Services.AddScoped<CsrfValidationFilter>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOptions<AuthRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(AuthRateLimitOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var duration)
            ? Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds))
            : context.HttpContext.RequestServices.GetRequiredService<IOptions<AuthRateLimitOptions>>().Value.WindowSeconds;
        context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var problem = new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Title = "Too many requests", Type = "about:blank" };
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        await context.HttpContext.Response.WriteAsJsonAsync(problem, typeof(ProblemDetails), options: null, contentType: "application/problem+json", cancellationToken);
    };
});
builder.Services.AddOptions<RateLimiterOptions>().Configure<IOptions<AuthRateLimitOptions>>((options, rateLimitOptions) =>
    options.AddPolicy("AuthPerIp", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitOptions.Value.PermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitOptions.Value.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            })));
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<OpenApiAuthTransformer>();
    options.AddOperationTransformer<OpenApiAuthTransformer>();
});
builder.Services.AddInfrastructure(builder.Configuration);
if (seedMode == SeedMode.None)
{
    builder.Services.AddJwtAuthentication();
    builder.Services.AddSignalR();
    builder.Services.AddSingleton<IShowtimeAvailabilityNotifier, SignalRShowtimeAvailabilityNotifier>();
    if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("LumiCine")))
    {
        builder.Services.AddHostedService<BookingExpirationWorker>();
        builder.Services.AddSingleton<TicketEmailWorker>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<TicketEmailWorker>());
        builder.Services.AddSingleton<MovieEmbeddingWorker>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<MovieEmbeddingWorker>());
    }
}
else
{
    builder.Services.AddAuthentication();
}

if (seedMode != SeedMode.None)
{
    using var bootstrapApp = builder.Build();
    using var scope = bootstrapApp.Services.CreateScope();
    if (seedMode == SeedMode.Admin)
    {
        var created = await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>().SeedAsync(builder.Configuration);
        Console.WriteLine(created ? "Bootstrap admin created." : "Bootstrap admin already exists.");
    }
    else
    {
        await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync();
        Console.WriteLine("Demo catalog and upcoming showtimes seeded.");
    }
    return;
}

var app = builder.Build();
app.UseExceptionHandler();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ShowtimeHub>("/hubs/showtimes");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "LumiCine API v1"));
}

app.Run();

public partial class Program { }
