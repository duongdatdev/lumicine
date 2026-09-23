using LumiCine.API.Exceptions;
using LumiCine.API.Authentication;
using LumiCine.API;
using LumiCine.Infrastructure;
using LumiCine.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

var seedAdmin = StartupCommandLine.ConsumeSeedAdminFlag(args, out var builderArgs);
var builder = WebApplication.CreateBuilder(builderArgs);
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
if (!seedAdmin)
{
    builder.Services.AddJwtAuthentication();
}
else
{
    builder.Services.AddAuthentication();
}

if (seedAdmin)
{
    using var bootstrapApp = builder.Build();
    using var scope = bootstrapApp.Services.CreateScope();
    var created = await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>()
        .SeedAsync(builder.Configuration);
    Console.WriteLine(created ? "Bootstrap admin created." : "Bootstrap admin already exists.");
    return;
}

var app = builder.Build();
app.UseExceptionHandler();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "LumiCine API v1"));
}

app.Run();

public partial class Program { }
