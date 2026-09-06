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
}