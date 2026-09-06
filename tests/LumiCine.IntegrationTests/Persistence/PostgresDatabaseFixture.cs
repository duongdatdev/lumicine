using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace LumiCine.IntegrationTests.Persistence;

public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    private const string DatabasePrefix = "lumi_test_";
    private string? _adminConnectionString;
    private string? _createdDatabaseName;

    public string ConnectionString { get; private set; } = string.Empty;

    public LumiCineDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LumiCineDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new LumiCineDbContext(options);
    }

    public async Task InitializeAsync()
    {
        var suppliedConnectionString = Environment.GetEnvironmentVariable("LUMICINE_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(suppliedConnectionString))
        {
            throw new InvalidOperationException("Set LUMICINE_TEST_CONNECTION_STRING to a dedicated PostgreSQL admin connection string targeting the postgres database.");
        }

        NpgsqlConnectionStringBuilder adminBuilder;
        try
        {
            adminBuilder = new NpgsqlConnectionStringBuilder(suppliedConnectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("LUMICINE_TEST_CONNECTION_STRING must be a valid PostgreSQL connection string targeting the postgres database.");
        }

        if (!string.Equals(adminBuilder.Database, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("LUMICINE_TEST_CONNECTION_STRING must target the postgres database; the fixture creates and drops only its own lumi_test_<guid> database.");
        }

        adminBuilder.Pooling = false;
        _adminConnectionString = adminBuilder.ConnectionString;
        var databaseName = $"{DatabasePrefix}{Guid.NewGuid():N}";
        var testBuilder = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = databaseName,
            Pooling = false
        };
        ConnectionString = testBuilder.ConnectionString;

        try
        {
            await using var adminConnection = new NpgsqlConnection(_adminConnectionString);
            await adminConnection.OpenAsync();
            await using (var createCommand = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", adminConnection))
            {
                await createCommand.ExecuteNonQueryAsync();
            }

            _createdDatabaseName = databaseName;
            await using var context = CreateDbContext();
            await context.Database.MigrateAsync();
        }
        catch
        {
            await DropCreatedDatabaseAsync();
            throw;
        }
    }

    public async Task DisposeAsync() => await DropCreatedDatabaseAsync();

    private async Task DropCreatedDatabaseAsync()
    {
        if (_adminConnectionString is null || !IsOwnedDatabaseName(_createdDatabaseName))
        {
            return;
        }

        var databaseName = _createdDatabaseName!;
        await using var adminConnection = new NpgsqlConnection(_adminConnectionString);
        await adminConnection.OpenAsync();
        await using var dropCommand = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\"", adminConnection);
        await dropCommand.ExecuteNonQueryAsync();
        _createdDatabaseName = null;
    }

    private static bool IsOwnedDatabaseName(string? name) =>
        name is not null &&
        name.StartsWith(DatabasePrefix, StringComparison.Ordinal) &&
        name.Length == DatabasePrefix.Length + 32 &&
        Guid.TryParseExact(name[DatabasePrefix.Length..], "N", out _);
}