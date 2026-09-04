using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
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
