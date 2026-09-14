using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LumiCine.API.Authentication;

public static class JwtAuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddAuthorization();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwtSigningKey, IOptions<JwtOptions>>((options, signingKey, jwtOptions) =>
            {
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey.ValidationKey,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Value.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Value.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = "role"
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var subject = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                        var session = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sid)?.Value;
                        if (!Guid.TryParse(subject, out var userId) || userId == Guid.Empty ||
                            !Guid.TryParse(session, out var sessionId) || sessionId == Guid.Empty)
                        {
                            context.Fail("Invalid token.");
                            return;
                        }

                        var validator = context.HttpContext.RequestServices.GetRequiredService<ISessionValidator>();
                        if (!await validator.IsValidAsync(userId, sessionId, context.HttpContext.RequestAborted))
                        {
                            context.Fail("Invalid token.");
                        }
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/problem+json";
                        context.Response.Headers.WWWAuthenticate = context.Options.Challenge;
                        var problem = new ProblemDetails
                        {
                            Status = StatusCodes.Status401Unauthorized,
                            Title = "Unauthorized",
                            Type = "about:blank"
                        };
                        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                        await context.Response.WriteAsync(
                            JsonSerializer.Serialize(problem, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                            context.HttpContext.RequestAborted);
                    }
                };
            });

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationProblemDetailsResultHandler>();
        return services;
    }
}
