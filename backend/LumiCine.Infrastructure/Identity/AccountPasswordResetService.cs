using System.Text;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.Infrastructure.Identity;

internal sealed class AccountPasswordResetService(
    UserManager<ApplicationUser> userManager,
    LumiCineDbContext dbContext,
    TimeProvider timeProvider) : IAccountPasswordResetService
{
    public async Task<bool> ResetAsync(string email, string encodedCode, string newPassword, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(encodedCode))
        {
            return false;
        }

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedCode));
        }
        catch (FormatException)
        {
            return false;
        }

        var normalizedEmail = userManager.NormalizeEmail(email.Trim());
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var user = (await dbContext.Users
                .FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"NormalizedEmail\" = {normalizedEmail} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (user is null || !user.IsActive || !user.EmailConfirmed)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var reset = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (!reset.Succeeded)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        var revokedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.AuthSessions
            .Where(session => session.UserId == user.Id && session.RevokedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(session => session.RevokedAtUtc, revokedAtUtc), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
