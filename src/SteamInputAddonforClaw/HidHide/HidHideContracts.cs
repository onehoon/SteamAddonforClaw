namespace SteamInputAddonforClaw.HidHide;

internal enum HidHideInspectionStatus { Available, NotInstalled, ConfigurationUnavailable, Disabled, InverseWhitelist }

internal sealed record HidHideInspection(
    HidHideInspectionStatus Status,
    IReadOnlySet<string> ApplicationWhitelist,
    IReadOnlyList<string>? HiddenDeviceEntries = null,
    IReadOnlyList<string>? RawApplicationWhitelist = null,
    string? Reason = null)
{
    public bool CanAcquireWhitelistLease => Status == HidHideInspectionStatus.Available;
    public bool IsConfigurationReadable => Status is HidHideInspectionStatus.Available or HidHideInspectionStatus.Disabled or HidHideInspectionStatus.InverseWhitelist;
}

internal interface IHidHideClient
{
    HidHideInspection Inspect();
    bool AddApplication(string executablePath);
    bool RemoveApplication(string executablePath);
}
