using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LumiCine.IntegrationTests.Persistence;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class DatabaseMigrationTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Initial_migration_can_move_down_and_up()
    {
        await using var db = fixture.CreateDbContext();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync("0");
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());

        await migrator.MigrateAsync();

        Assert.Contains("20260906160211_InitialCreate", await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Role_seed_migration_can_move_down_and_up()
    {
        await using var db = fixture.CreateDbContext();
        var migrator = db.GetService<IMigrator>();

        Assert.Equal(3, await db.Roles.CountAsync());

        await migrator.MigrateAsync("20260915130621_AddIdentity");
        Assert.Empty(await db.Roles.ToArrayAsync());

        await migrator.MigrateAsync();
        Assert.Equal(
            new[] { "Admin", "Customer", "Staff" },
            await db.Roles.OrderBy(role => role.Name).Select(role => role.Name!).ToArrayAsync());
    }
}
