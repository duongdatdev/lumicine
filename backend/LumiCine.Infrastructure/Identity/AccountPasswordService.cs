using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.Infrastructure.Identity;

internal sealed class AccountPasswordService(
    UserManager<ApplicationUser> userManager,
    LumiCineDbContext dbContext,
    TimeProvider timeProvider) : IAccountPasswordService
{
    public async Task<AccountPasswordChangeStatus> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var user = (await dbContext.Users
                .FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (user is null || !user.IsActive || !user.EmailConfirmed)
        {
            await transaction.CommitAsync(cancellationToken);
            return AccountPasswordChangeStatus.AccountUnavailable;
        }

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            var status = result.Errors.Any(error => error.Code == "PasswordMismatch")
                ? AccountPasswordChangeStatus.InvalidCurrentPassword
                : AccountPasswordChangeStatus.InvalidNewPassword;
            await transaction.CommitAsync(cancellationToken);
            return status;
        }

        var revokedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.AuthSessions
            .Where(session => session.UserId == userId && session.RevokedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(session => session.RevokedAtUtc, revokedAtUtc), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AccountPasswordChangeStatus.Changed;
    }
}
