using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LumiCine.Infrastructure.Persistence;

internal static class AdvisoryLocks
{
    public static async Task LockRowAsync(this LumiCineDbContext db, string table, Guid id, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Row locks require an active database transaction.");
        command.CommandText = $"SELECT 1 FROM \"{table}\" WHERE \"Id\" = @id FOR UPDATE";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = id;
        command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync(ct);
    }
}
