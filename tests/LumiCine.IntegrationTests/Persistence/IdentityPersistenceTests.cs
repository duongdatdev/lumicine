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

    [Fact]
    public async Task Account_roles_are_seeded_and_normalized_names_are_unique()
    {
        await using var db = fixture.CreateDbContext();
        var roles = await db.Roles.AsNoTracking().OrderBy(role => role.Name).ToArrayAsync();

        Assert.Equal(3, roles.Length);
        Assert.Collection(
            roles,
            role => Assert.Equal(("Admin", "ADMIN"), (role.Name, role.NormalizedName)),
            role => Assert.Equal(("Customer", "CUSTOMER"), (role.Name, role.NormalizedName)),
            role => Assert.Equal(("Staff", "STAFF"), (role.Name, role.NormalizedName)));
        Assert.Equal(Guid.Parse("71000000-0000-0000-0000-000000000003"), roles[0].Id);
        Assert.Equal(Guid.Parse("71000000-0000-0000-0000-000000000001"), roles[1].Id);
        Assert.Equal(Guid.Parse("71000000-0000-0000-0000-000000000002"), roles[2].Id);

        FormattableString duplicateNormalizedName = $"""
            INSERT INTO "Roles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
            VALUES ({Guid.NewGuid()}, {"Other"}, {"CUSTOMER"}, {"unique-test-stamp"})
            """;

        await DatabaseAssertions.ThrowsSqlStateAsync(
            () => db.Database.ExecuteSqlInterpolatedAsync(duplicateNormalizedName),
            PostgresErrorCodes.UniqueViolation);
    }
}
