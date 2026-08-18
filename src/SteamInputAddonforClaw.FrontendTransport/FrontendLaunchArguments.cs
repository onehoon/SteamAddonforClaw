namespace SteamInputAddonforClaw.FrontendTransport;

public static class FrontendLaunchArguments
{
    public const string LogDirectoryOption = "--log-directory";

    public static string ResolveLogDirectory(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(args[index], LogDirectoryOption, StringComparison.OrdinalIgnoreCase)) continue;
            var candidate = args[index + 1];
            if (IsValidAbsolutePath(candidate)) return candidate;
            break;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamInputAddonforClaw-Data", "logs");
    }

    private static bool IsValidAbsolutePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        try { return string.Equals(Path.GetFullPath(path), path, StringComparison.Ordinal); }
        catch (ArgumentException) { return false; }
    }
}
