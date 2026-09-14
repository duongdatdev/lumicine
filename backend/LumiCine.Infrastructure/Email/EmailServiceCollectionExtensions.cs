using LumiCine.Application.Accounts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.Infrastructure.Email;

public static class EmailServiceCollectionExtensions
{
    public static IServiceCollection AddEmailDelivery(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailOptions>(configuration.GetSection("Email"));
        services.AddSingleton<IEmailSender>(serviceProvider => new SmtpEmailSender(
            serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailOptions>>().Value,
            serviceProvider.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>()));
        return services;
    }
}
