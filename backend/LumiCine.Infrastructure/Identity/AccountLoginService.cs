using System.Security.Cryptography;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;

namespace LumiCine.Infrastructure.Identity;

internal sealed class AccountLoginService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    LumiCineDbContext dbContext,
    IAccessTokenIssuer accessTokenIssuer,
    TimeProvider timeProvider) : IAccountLoginService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    public async Task<AccountLoginResult?> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedEmail = userManager.NormalizeEmail(email.Trim());
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // The row lock serializes credential, status, and role reads with account updates before creating a session.
        var user = (await dbContext.Users
                .FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"NormalizedEmail\" = {normalizedEmail} FOR UPDATE")
                .ToListAsync(cancellationToken))
            .SingleOrDefault();
        if (user is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        if (!user.IsActive || !user.EmailConfirmed)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!signInResult.Succeeded)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var sessionId = Guid.NewGuid();
        var expiresAtUtc = now.Add(SessionLifetime);
        var refreshToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var refreshTokenHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(refreshToken)));
        var roleNames = roles.ToArray();
        var accessToken = accessTokenIssuer.Issue(user.Id, sessionId, roleNames);

        dbContext.AuthSessions.Add(new AuthSession
        {
            Id = sessionId,
            UserId = user.Id,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc
        });
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Hash = refreshTokenHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AccountLoginResult(
            user.Id,
            user.Email!,
            user.DisplayName,
            roleNames,
            accessToken.Value,
            accessToken.ExpiresInSeconds,
            refreshToken,
            expiresAtUtc);
    }
}
