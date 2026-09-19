using System.Security.Cryptography;
using System.Text;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.Infrastructure.Identity;

internal sealed class AccountLogoutService(LumiCineDbContext dbContext, TimeProvider timeProvider) : IAccountLogoutService
{
    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
        var tokenInfo = await dbContext.RefreshTokens.AsNoTracking()
            .Where(token => token.Hash == hash)
            .Select(token => new { token.Id, token.SessionId })
            .SingleOrDefaultAsync(cancellationToken);
        if (tokenInfo is null)
        {
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var userId = await dbContext.AuthSessions.AsNoTracking()
            .Where(session => session.Id == tokenInfo.SessionId)
            .Select(session => session.UserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (userId == Guid.Empty)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await dbContext.Users
            .FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        var session = (await dbContext.AuthSessions
                .FromSqlInterpolated($"SELECT * FROM \"AuthSessions\" WHERE \"Id\" = {tokenInfo.SessionId} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (session is not null)
        {
            session.RevokedAtUtc ??= timeProvider.GetUtcNow().UtcDateTime;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
