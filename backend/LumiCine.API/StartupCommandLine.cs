namespace LumiCine.API;

public static class StartupCommandLine
{
    public static bool ConsumeSeedAdminFlag(string[] args, out string[] builderArgs)
    {
        var seedAdmin = args.Contains("--seed-admin", StringComparer.Ordinal);
        builderArgs = args.Where(argument => !string.Equals(argument, "--seed-admin", StringComparison.Ordinal)).ToArray();
        return seedAdmin;
    }
}
