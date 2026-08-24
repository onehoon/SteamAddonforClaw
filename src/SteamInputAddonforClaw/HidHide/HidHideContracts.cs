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

internal enum HidHideRoutingAdmissionOutcome { Allowed, ForeignConfiguration, InspectionUnavailable }

internal static class HidHideRoutingAdmissionPolicy
{
    internal static HidHideRoutingAdmissionOutcome Evaluate(HidHideInspection inspection, string executablePath)
    {
        if (inspection.Status == HidHideInspectionStatus.NotInstalled) return HidHideRoutingAdmissionOutcome.Allowed;
        if (!inspection.IsConfigurationReadable || inspection.Status is HidHideInspectionStatus.AccessDenied or HidHideInspectionStatus.ConfigurationUnavailable)
            return HidHideRoutingAdmissionOutcome.InspectionUnavailable;
        if (inspection.IsInverseWhitelist || inspection.Status == HidHideInspectionStatus.InverseWhitelist || inspection.HasUnresolvedApplicationWhitelistEntries)
            return HidHideRoutingAdmissionOutcome.ForeignConfiguration;
        if ((inspection.HiddenDeviceEntries ?? []).Count > 0)
            return HidHideRoutingAdmissionOutcome.ForeignConfiguration;
        if (inspection.ApplicationWhitelist.Any(entry => !string.Equals(entry, executablePath, StringComparison.OrdinalIgnoreCase)))
            return HidHideRoutingAdmissionOutcome.ForeignConfiguration;
        return HidHideRoutingAdmissionOutcome.Allowed;
    }
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
