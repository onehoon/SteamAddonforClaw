using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.FrontendTransport;

public static class FrontendLaunchArguments
{
    public const string LogDirectoryOption = "--log-directory";
    public const string LogLevelOption = "--log-level";

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

    public static FrontendLogLevel ResolveLogLevel(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(args[index], LogLevelOption, StringComparison.OrdinalIgnoreCase)) continue;
            return args[index + 1].ToLowerInvariant() switch
            {
                "debug" => FrontendLogLevel.Debug,
                "info" => FrontendLogLevel.Info,
                "off" => FrontendLogLevel.Off,
                _ => FrontendLogLevel.Off,
            };
        }

        return FrontendLogLevel.Off;
    }

    private static bool IsValidAbsolutePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        try { return string.Equals(Path.GetFullPath(path), path, StringComparison.Ordinal); }
        catch (ArgumentException) { return false; }
    }
}
