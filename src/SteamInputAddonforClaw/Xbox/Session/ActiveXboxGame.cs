namespace SteamInputAddonforClaw.Xbox.Session;

internal sealed record ActiveXboxGame(string Key, string DisplayName);

internal sealed record XboxGameProcessMatch(
    XboxGameIdentity Identity,
    uint ProcessId,
    string RunningProcessPath,
    string RunningExecutableName,
    string PackageFullName);
