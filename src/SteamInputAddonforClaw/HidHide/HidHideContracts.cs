namespace SteamInputAddonforClaw.HidHide;

internal enum HidHideInspectionStatus { Available, NotInstalled, ConfigurationUnavailable, AccessDenied, Disabled, InverseWhitelist }

internal sealed record HidHideInspection(
    HidHideInspectionStatus Status,
    IReadOnlySet<string> ApplicationWhitelist,
    IReadOnlyList<string>? HiddenDeviceEntries = null,
    IReadOnlyList<string>? RawApplicationWhitelist = null,
    bool IsActive = false,
    bool IsInverseWhitelist = false,
    string? Reason = null,
    bool HasUnresolvedApplicationWhitelistEntries = false)
{
    public bool CanAcquireWhitelistLease => Status == HidHideInspectionStatus.Available;
    public bool IsConfigurationReadable => Status is HidHideInspectionStatus.Available or HidHideInspectionStatus.Disabled or HidHideInspectionStatus.InverseWhitelist;
    public bool CanPrepareRouting => IsConfigurationReadable && Status != HidHideInspectionStatus.InverseWhitelist && !IsInverseWhitelist;
}

internal interface IHidHideClient
{
    HidHideInspection Inspect();
    bool AddApplication(string executablePath);
    bool RemoveApplication(string executablePath);
    bool AddHiddenDevice(string deviceEntry);
    bool RemoveHiddenDevice(string deviceEntry);
    bool SetActive(bool active) => true;
}
