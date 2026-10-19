using LumiCine.Infrastructure.Persistence;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Payments;
using LumiCine.Application.Accounts;
using LumiCine.Application.Bookings;
using LumiCine.Application.Payments;
using LumiCine.Infrastructure.Bookings;
using LumiCine.Application.Scheduling;
using LumiCine.Infrastructure.Email;
using LumiCine.Infrastructure.Scheduling;
using LumiCine.Application.Catalog;
using LumiCine.Infrastructure.Catalog;
using LumiCine.Application.Cinemas;
using LumiCine.Infrastructure.Cinemas;
using LumiCine.Infrastructure.Seeding;
using LumiCine.Application.Media;
using LumiCine.Infrastructure.Media;
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
        // VNPay credentials arrive with the sandbox merchant registration, so they are validated
        // when a payment is created instead of blocking every startup without them.
        services.AddOptions<VnPayOptions>()
            .Bind(configuration.GetSection(VnPayOptions.SectionName));
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
        services.AddScoped<IMovieCatalogService, MovieCatalogService>();
        services.AddScoped<AdminBootstrapper>();
        services.AddScoped<IShowtimeService, ShowtimeService>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IVnPayMessageService, VnPayMessageService>();
        services.AddScoped<ICinemaService, CinemaService>();
        services.AddScoped<DemoSeeder>();
        services.AddScoped<IPosterService, LocalPosterService>();

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
