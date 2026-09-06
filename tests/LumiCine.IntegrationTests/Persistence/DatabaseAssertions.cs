using Npgsql;

namespace LumiCine.IntegrationTests.Persistence;

internal static class DatabaseAssertions
{
    public static async Task ThrowsSqlStateAsync(Func<Task<int>> action, string expectedSqlState)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(expectedSqlState, exception.SqlState);
    }
}