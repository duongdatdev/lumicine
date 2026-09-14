using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LumiCine.Infrastructure.Identity;

public static class AccountEmailServiceCollectionExtensions
{
    public const string EmailConfirmationProviderName = "LumiCineEmailConfirmation";
    public const string PasswordResetProviderName = "LumiCinePasswordReset";

    public static IServiceCollection AddAccountEmailTokens(this IServiceCollection services, IConfiguration configuration)
    {
        var keyRingPath = configuration["DataProtection:KeyRingPath"];
        if (string.IsNullOrWhiteSpace(keyRingPath))
        {
            keyRingPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LumiCine",
                "data-protection-keys");
        }

        Directory.CreateDirectory(keyRingPath);
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("LumiCine")
            .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        if (OperatingSystem.IsWindows())
        {
            dataProtection.ProtectKeysWithDpapi();
        }

        services.AddOptions<AccountLinksOptions>()
            .Bind(configuration.GetSection(AccountLinksOptions.SectionName))
            .Validate(options => options.IsValid(), "AccountLinks:BaseUrl must be a trusted HTTPS origin without a path, query, or fragment.")
            .ValidateOnStart();

        services.Configure<EmailConfirmationTokenOptions>(options =>
        {
            options.Name = "LumiCine.EmailConfirmation";
            options.TokenLifespan = TimeSpan.FromHours(24);
        });
        services.Configure<PasswordResetTokenOptions>(options =>
        {
            options.Name = "LumiCine.PasswordReset";
            options.TokenLifespan = TimeSpan.FromHours(1);
        });

        var identityBuilder = new IdentityBuilder(typeof(ApplicationUser), services);
        identityBuilder.AddTokenProvider<EmailConfirmationTokenProvider>(EmailConfirmationProviderName);
        identityBuilder.AddTokenProvider<PasswordResetTokenProvider>(PasswordResetProviderName);
        services.Configure<IdentityOptions>(options =>
        {
            options.Tokens.EmailConfirmationTokenProvider = EmailConfirmationProviderName;
            options.Tokens.PasswordResetTokenProvider = PasswordResetProviderName;
        });
        services.AddScoped<LumiCine.Application.Accounts.IAccountEmailService, AccountEmailService>();
        return services;
    }
}
