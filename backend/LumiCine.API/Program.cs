using LumiCine.API.Exceptions;
using LumiCine.API.Authentication;
using LumiCine.API;
using LumiCine.Infrastructure;
using LumiCine.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

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
builder.Services.AddOpenApi();
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
