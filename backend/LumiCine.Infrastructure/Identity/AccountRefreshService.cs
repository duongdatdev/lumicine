using System.Security.Cryptography;
using System.Text;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.Infrastructure.Identity;

internal sealed class AccountRefreshService(
    UserManager<ApplicationUser> userManager,
    LumiCineDbContext dbContext,
    IAccessTokenIssuer accessTokenIssuer,
    TimeProvider timeProvider) : IAccountRefreshService
{
    public async Task<AccountRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
        var tokenIdentity = await dbContext.RefreshTokens.AsNoTracking()
            .Where(token => token.Hash == tokenHash)
            .Select(token => new { token.Id, token.SessionId })
            .SingleOrDefaultAsync(cancellationToken);
        if (tokenIdentity is null)
        {
            return null;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Keep account mutations and refresh operations serialized in user, session, token order.
        var sessionOwner = await dbContext.AuthSessions.AsNoTracking()
            .Where(session => session.Id == tokenIdentity.SessionId)
            .Select(session => session.UserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (sessionOwner == Guid.Empty)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var user = (await dbContext.Users
                .FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {sessionOwner} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (user is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var session = (await dbContext.AuthSessions
                .FromSqlInterpolated($"SELECT * FROM \"AuthSessions\" WHERE \"Id\" = {tokenIdentity.SessionId} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (session is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var token = (await dbContext.RefreshTokens
                .FromSqlInterpolated($"SELECT * FROM \"RefreshTokens\" WHERE \"Id\" = {tokenIdentity.Id} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (token is null || token.SessionId != session.Id)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (token.ConsumedAtUtc is not null)
        {
            session.RevokedAtUtc ??= now;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        if (!user.IsActive || !user.EmailConfirmed || session.RevokedAtUtc is not null ||
            session.ExpiresAtUtc <= now || token.ExpiresAtUtc <= now || token.Hash != tokenHash)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var roles = (await userManager.GetRolesAsync(user)).ToArray();
        var nextRefreshToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        token.ConsumedAtUtc = now;
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nextRefreshToken))),
            CreatedAtUtc = now,
            ExpiresAtUtc = session.ExpiresAtUtc
        });
        var accessToken = accessTokenIssuer.Issue(user.Id, session.Id, roles);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AccountRefreshResult(
            user.Id,
            user.Email!,
            user.DisplayName,
            roles,
            accessToken.Value,
            accessToken.ExpiresInSeconds,
            nextRefreshToken,
            session.ExpiresAtUtc);
    }
}
