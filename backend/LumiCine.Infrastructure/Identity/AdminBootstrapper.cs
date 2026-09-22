using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LumiCine.Infrastructure.Identity;

public sealed class AdminBootstrapper(
    UserManager<ApplicationUser> userManager,
    LumiCineDbContext dbContext)
{
    public async Task<bool> SeedAsync(IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var email = configuration["BootstrapAdmin:Email"]?.Trim();
        var password = configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
        {
            throw new InvalidOperationException("BootstrapAdmin:Email must be a valid email address of at most 254 characters.");
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length > 128)
        {
            throw new InvalidOperationException("BootstrapAdmin:Password must be configured and at most 128 characters.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            if (!await userManager.IsInRoleAsync(existingUser, RoleNames.Admin))
            {
                throw new InvalidOperationException("The configured bootstrap email belongs to a non-admin account.");
            }

            await transaction.RollbackAsync(CancellationToken.None);
            return false;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsActive = true,
            DisplayName = "Administrator",
            CreatedAtUtc = DateTime.UtcNow
        };
        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new InvalidOperationException($"Could not create bootstrap admin: {FormatErrors(createResult)}");
        }

        var roleResult = await userManager.AddToRoleAsync(user, RoleNames.Admin);
        if (!roleResult.Succeeded)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new InvalidOperationException($"Could not assign the admin role: {FormatErrors(roleResult)}");
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static string FormatErrors(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(error => error.Description));
}
