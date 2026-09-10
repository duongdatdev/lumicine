using LumiCine.Infrastructure.Authentication;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LumiCine.IntegrationTests.Persistence;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class AuthenticationSessionPersistenceTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Session_and_hashed_refresh_token_round_trip_and_hash_is_unique()
    {
        await using var db = fixture.CreateDbContext();
        var user = await CreateUserAsync(db);
        var createdAt = DateTime.UtcNow;
        var session = new AuthSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CreatedAtUtc = createdAt,
            ExpiresAtUtc = createdAt.AddDays(7)
        };
        const string tokenHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            Hash = tokenHash,
            CreatedAtUtc = createdAt,
            ExpiresAtUtc = createdAt.AddDays(7)
        };
        db.AuthSessions.Add(session);
        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync();

        var savedSession = await db.AuthSessions.AsNoTracking().SingleAsync(item => item.Id == session.Id);
        var savedToken = await db.RefreshTokens.AsNoTracking().SingleAsync(item => item.Id == refreshToken.Id);
        Assert.Equal(user.Id, savedSession.UserId);
        var expectedExpiry = new DateTime(session.ExpiresAtUtc.Ticks / 10 * 10, DateTimeKind.Utc);
        Assert.Equal(expectedExpiry, savedSession.ExpiresAtUtc);
        Assert.Null(savedSession.RevokedAtUtc);
        Assert.Equal(session.Id, savedToken.SessionId);
        Assert.Equal(tokenHash, savedToken.Hash);
        Assert.Null(savedToken.ConsumedAtUtc);

        FormattableString duplicateHash = $"""
            INSERT INTO "RefreshTokens" ("Id", "SessionId", "Hash", "CreatedAtUtc", "ExpiresAtUtc")
            VALUES ({Guid.NewGuid()}, {session.Id}, {tokenHash}, {createdAt}, {createdAt.AddDays(1)})
            """;
        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(duplicateHash),
            PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Session_and_token_reject_missing_foreign_keys_and_invalid_expiry()
    {
        await using var db = fixture.CreateDbContext();
        var user = await CreateUserAsync(db);
        var now = DateTime.UtcNow;
        var session = new AuthSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(7)
        };
        db.AuthSessions.Add(session);
        await db.SaveChangesAsync();

        FormattableString missingSessionUser = $"""
            INSERT INTO "AuthSessions" ("Id", "UserId", "CreatedAtUtc", "ExpiresAtUtc")
            VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {now}, {now.AddDays(1)})
            """;
        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(missingSessionUser),
            PostgresErrorCodes.ForeignKeyViolation);

        FormattableString expiredSession = $"""
            INSERT INTO "AuthSessions" ("Id", "UserId", "CreatedAtUtc", "ExpiresAtUtc")
            VALUES ({Guid.NewGuid()}, {user.Id}, {now}, {now})
            """;
        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(expiredSession),
            PostgresErrorCodes.CheckViolation);

        FormattableString missingTokenSession = $"""
            INSERT INTO "RefreshTokens" ("Id", "SessionId", "Hash", "CreatedAtUtc", "ExpiresAtUtc")
            VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {new string('b', 64)}, {now}, {now.AddDays(1)})
            """;
        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(missingTokenSession),
            PostgresErrorCodes.ForeignKeyViolation);

        FormattableString expiredToken = $"""
            INSERT INTO "RefreshTokens" ("Id", "SessionId", "Hash", "CreatedAtUtc", "ExpiresAtUtc")
            VALUES ({Guid.NewGuid()}, {session.Id}, {new string('c', 64)}, {now}, {now})
            """;
        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(expiredToken),
            PostgresErrorCodes.CheckViolation);

        FormattableString invalidHash = $"""
            INSERT INTO "RefreshTokens" ("Id", "SessionId", "Hash", "CreatedAtUtc", "ExpiresAtUtc")
            VALUES ({Guid.NewGuid()}, {session.Id}, {"not-a-hash"}, {now}, {now.AddDays(1)})
            """;
        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(invalidHash),
            PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Session_and_user_deletes_are_restricted_while_tokens_and_sessions_reference_them()
    {
        await using var db = fixture.CreateDbContext();
        var user = await CreateUserAsync(db);
        var now = DateTime.UtcNow;
        var session = new AuthSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(7)
        };
        db.AuthSessions.Add(session);
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            Hash = new string('d', 64),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(7)
        });
        await db.SaveChangesAsync();

        FormattableString deleteSession = $"DELETE FROM \"AuthSessions\" WHERE \"Id\" = {session.Id}";
        FormattableString deleteUser = $"DELETE FROM \"Users\" WHERE \"Id\" = {user.Id}";
        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(deleteSession),
            "23001");
        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(deleteUser),
            "23001");
    }

    private static async Task<ApplicationUser> CreateUserAsync(LumiCineDbContext db)
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = "Session test user"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
