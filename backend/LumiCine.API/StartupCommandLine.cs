namespace LumiCine.API;

using Npgsql;
using System.Text.RegularExpressions;

public static class StartupCommandLine
{
    private static readonly Regex AcceptanceDatabase = new("^lumi_e2e_[a-zA-Z0-9_]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool ConsumeSeedAdminFlag(string[] args, out string[] builderArgs)
    {
        return ConsumeSeedFlags(args, out builderArgs) == SeedMode.Admin;
    }

    public static SeedMode ConsumeSeedFlags(string[] args, out string[] builderArgs)
    {
        var seedAdmin = args.Contains("--seed-admin", StringComparer.Ordinal);
        var seedDemo = args.Contains("--seed-demo", StringComparer.Ordinal);
        var seedAcceptance = args.Contains("--seed-acceptance", StringComparer.Ordinal);
        if (new[] { seedAdmin, seedDemo, seedAcceptance }.Count(selected => selected) > 1)
            throw new ArgumentException("Seed flags cannot be used together.", nameof(args));
        builderArgs = args.Where(argument => !string.Equals(argument, "--seed-admin", StringComparison.Ordinal) &&
                                             !string.Equals(argument, "--seed-demo", StringComparison.Ordinal) &&
                                             !string.Equals(argument, "--seed-acceptance", StringComparison.Ordinal)).ToArray();
        return seedAdmin ? SeedMode.Admin : seedDemo ? SeedMode.Demo : seedAcceptance ? SeedMode.Acceptance : SeedMode.None;
    }

    public static bool IsSafeAcceptanceConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return false;
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(connectionString);
            return connection.Host == "127.0.0.1" &&
                   connection.Port == 55439 && AcceptanceDatabase.IsMatch(connection.Database);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

public enum SeedMode { None, Admin, Demo, Acceptance }
