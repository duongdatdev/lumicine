using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LumiCine.IntegrationTests.Persistence;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class IdentityPersistenceTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Identity_users_preserve_inactive_state_and_reject_duplicate_normalized_emails()
    {
        var userId = Guid.NewGuid();
        const string email = "identity-test@example.com";
        await using (var db = fixture.CreateDbContext())
        {
            db.Users.Add(new ApplicationUser
            {
                Id = userId,
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                DisplayName = "Inactive test user",
                IsActive = false,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            Assert.False((await db.Users.SingleAsync(user => user.Id == userId)).IsActive);

            FormattableString duplicateEmail = $"""
                INSERT INTO "Users" ("Id", "DisplayName", "IsActive", "CreatedAtUtc", "Email", "NormalizedEmail", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
                VALUES ({Guid.NewGuid()}, {"Second user"}, TRUE, {DateTime.UtcNow}, {"second@example.com"}, {email.ToUpperInvariant()}, FALSE, FALSE, FALSE, FALSE, 0)
                """;

            await DatabaseAssertions.ThrowsSqlStateAsync(
                () => db.Database.ExecuteSqlInterpolatedAsync(duplicateEmail),
                PostgresErrorCodes.UniqueViolation);
        }
    }

    [Fact]
    public async Task Identity_tables_are_included_in_the_latest_migration()
    {
        await using var db = fixture.CreateDbContext();

        Assert.Contains("20260915130621_AddIdentity", await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
