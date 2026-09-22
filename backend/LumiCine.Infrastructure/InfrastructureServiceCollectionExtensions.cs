using LumiCine.Infrastructure.Persistence;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LumiCine.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(options => options.IsValid(), "Jwt configuration requires an issuer, audience, and valid private RSA PKCS#8 key of at least 2048 bits.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<JwtSigningKey>();
        services.AddScoped<ISessionValidator, DatabaseSessionValidator>();
        services.AddScoped<IAccountRegistrationService, AccountRegistrationService>();
        services.AddScoped<IAccountLoginService, AccountLoginService>();
        services.AddScoped<IAccountRefreshService, AccountRefreshService>();
        services.AddScoped<IAccountLogoutService, AccountLogoutService>();
        services.AddScoped<ICurrentUserProfileService, CurrentUserProfileService>();
        services.AddScoped<IAccountPasswordService, AccountPasswordService>();
        services.AddScoped<IAccountPasswordResetService, AccountPasswordResetService>();
        services.AddScoped<IEmailConfirmationService, EmailConfirmationService>();
        services.AddEmailDelivery(configuration);
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<AdminBootstrapper>();

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequiredUniqueChars = 1;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<LumiCineDbContext>()
            .AddSignInManager();
        services.AddAccountEmailTokens(configuration);

        services.AddDbContext<LumiCineDbContext>((serviceProvider, options) =>
        {
            var currentConfiguration = serviceProvider.GetService<IConfiguration>() ?? configuration;
            var connectionString = currentConfiguration.GetConnectionString("LumiCine");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseNpgsql();
            }
            else
            {
                options.UseNpgsql(connectionString);
            }
        });

        return services;
    }
}
