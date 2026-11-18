using LumiCine.Application.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace LumiCine.Infrastructure.Identity;

public sealed class AcceptanceUserSeeder(UserManager<ApplicationUser> userManager)
{
    public async Task SeedAsync(IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var password = configuration["E2eUsers:Password"];
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("E2eUsers:Password must be configured for acceptance seeding.");

        for (var index = 1; index <= 6; index++)
        {
            await SeedUserAsync($"customer{index}@lumicine.example.test", $"E2E Customer {index}", RoleNames.Customer, password, cancellationToken);
            await SeedUserAsync($"admin{index}@lumicine.example.test", $"E2E Admin {index}", RoleNames.Admin, password, cancellationToken);
        }
        await SeedUserAsync("staff@lumicine.example.test", "E2E Staff", RoleNames.Staff, password, cancellationToken);
    }

    private async Task SeedUserAsync(string email, string displayName, string role, string password, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                IsActive = true,
                DisplayName = displayName,
                CreatedAtUtc = DateTime.UtcNow
            };
            var created = await userManager.CreateAsync(user, password);
            if (!created.Succeeded)
                throw new InvalidOperationException($"Could not create acceptance user {email}: {string.Join("; ", created.Errors.Select(error => error.Description))}");
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            var assigned = await userManager.AddToRoleAsync(user, role);
            if (!assigned.Succeeded)
                throw new InvalidOperationException($"Could not assign role {role} to acceptance user {email}: {string.Join("; ", assigned.Errors.Select(error => error.Description))}");
        }
    }
}
