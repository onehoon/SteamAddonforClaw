namespace SteamInputAddonforClaw.HidHide;

internal enum HidHideInspectionStatus { Available, NotInstalled, ConfigurationUnavailable, Disabled, InverseWhitelist }

internal sealed record HidHideInspection(
    HidHideInspectionStatus Status,
    IReadOnlySet<string> ApplicationWhitelist,
    IReadOnlyList<string>? HiddenDeviceEntries = null,
    string? Reason = null)
{
    public bool IsUsable => Status == HidHideInspectionStatus.Available;
}

internal interface IHidHideClient
{
    HidHideInspection Inspect();
    bool AddApplication(string executablePath);
    bool RemoveApplication(string executablePath);
}

internal interface IHidHideCommandRunner
{
    HidHideCommandResult Run(string executablePath, params string[] arguments);
}

internal sealed record HidHideCommandResult(int ExitCode, string StandardOutput, string StandardError);
