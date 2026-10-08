namespace LumiCine.API;

public static class StartupCommandLine
{
    public static bool ConsumeSeedAdminFlag(string[] args, out string[] builderArgs)
    {
        return ConsumeSeedFlags(args, out builderArgs) == SeedMode.Admin;
    }

    public static SeedMode ConsumeSeedFlags(string[] args, out string[] builderArgs)
    {
        var seedAdmin = args.Contains("--seed-admin", StringComparer.Ordinal);
        var seedDemo = args.Contains("--seed-demo", StringComparer.Ordinal);
        if (seedAdmin && seedDemo) throw new ArgumentException("--seed-admin and --seed-demo cannot be used together.", nameof(args));
        builderArgs = args.Where(argument => !string.Equals(argument, "--seed-admin", StringComparison.Ordinal) &&
                                             !string.Equals(argument, "--seed-demo", StringComparison.Ordinal)).ToArray();
        return seedAdmin ? SeedMode.Admin : seedDemo ? SeedMode.Demo : SeedMode.None;
    }
}

public enum SeedMode { None, Admin, Demo }
