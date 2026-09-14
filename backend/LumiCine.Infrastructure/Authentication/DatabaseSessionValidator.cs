using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.Infrastructure.Authentication;

public sealed class DatabaseSessionValidator(LumiCineDbContext dbContext, TimeProvider timeProvider) : ISessionValidator
{
    public Task<bool> IsValidAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return (from session in dbContext.AuthSessions.AsNoTracking()
                join user in dbContext.Users.AsNoTracking() on session.UserId equals user.Id
                where session.Id == sessionId && session.UserId == userId && session.RevokedAtUtc == null &&
                      session.ExpiresAtUtc > now && user.IsActive && user.EmailConfirmed
                select session.Id).AnyAsync(cancellationToken);
    }
}
