using LumiCine.Application.Accounts;
using LumiCine.Domain.Cinemas;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Cinemas;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.IntegrationTests.Cinemas;

[Collection(PostgresDatabaseCollection.Name)]
public sealed partial class CinemaServiceTests(PostgresDatabaseFixture fixture)
{
    private PostgresDatabaseFixture Fixture { get; } = fixture;

    private static async Task<(Guid CinemaId, string Token)> CreateAdminAsync(ApiWebApplicationFactory factory)
    {
        var email = $"cinema-admin-{Guid.NewGuid():N}@example.test";
        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Cinema Admin", CreatedAtUtc = DateTime.UtcNow };
        var created = await manager.CreateAsync(user);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(x => x.Description)));
        var role = await manager.AddToRoleAsync(user, RoleNames.Admin);
        Assert.True(role.Succeeded, string.Join("; ", role.Errors.Select(x => x.Description)));
        var session = new AuthSession { Id = Guid.NewGuid(), UserId = user.Id, CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(1) };
        var context = scope.ServiceProvider.GetRequiredService<LumiCineDbContext>();
        context.AuthSessions.Add(session);
        var cinema = new Cinema(Guid.NewGuid(), $"C-{Guid.NewGuid():N}"[..22], "HTTP Cinema", "Address", "City", DateTime.UtcNow);
        context.Cinemas.Add(cinema);
        await context.SaveChangesAsync();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(user.Id, session.Id, [RoleNames.Admin]).Value;
        return (cinema.Id, token);
    }
}
