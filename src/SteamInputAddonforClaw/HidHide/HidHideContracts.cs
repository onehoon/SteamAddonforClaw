using SteamInputAddonforClaw.Diagnostics;

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
    internal static HidHideRoutingAdmissionOutcome Evaluate(HidHideInspection inspection, string executablePath, IReadOnlyCollection<string>? trustedApplicationPaths = null)
    {
        if (inspection.Status == HidHideInspectionStatus.NotInstalled) return HidHideRoutingAdmissionOutcome.Allowed;
        if (!inspection.IsConfigurationReadable || inspection.Status is HidHideInspectionStatus.AccessDenied or HidHideInspectionStatus.ConfigurationUnavailable)
            return HidHideRoutingAdmissionOutcome.InspectionUnavailable;
        if (inspection.IsInverseWhitelist || inspection.Status == HidHideInspectionStatus.InverseWhitelist || inspection.HasUnresolvedApplicationWhitelistEntries)
            return HidHideRoutingAdmissionOutcome.ForeignConfiguration;
        if ((inspection.HiddenDeviceEntries ?? []).Count > 0)
            return HidHideRoutingAdmissionOutcome.ForeignConfiguration;
        var trusted = (trustedApplicationPaths ?? []).Where(IsCanonicalPath).ToArray();
        if (inspection.ApplicationWhitelist.Any(entry => !PathEquals(entry, executablePath) && !trusted.Any(path => PathEquals(entry, path))))
            return HidHideRoutingAdmissionOutcome.ForeignConfiguration;
        return HidHideRoutingAdmissionOutcome.Allowed;
    }

    internal static void LogRejection(HidHideInspection inspection, string executablePath, IReadOnlyCollection<string>? trustedApplicationPaths, HidHideRoutingAdmissionOutcome outcome)
    {
        var trusted = (trustedApplicationPaths ?? []).Where(IsCanonicalPath).ToArray();
        var foreignWhitelistCount = inspection.ApplicationWhitelist.Count(entry => !PathEquals(entry, executablePath) && !trusted.Any(path => PathEquals(entry, path)));
        AppLog.Warn("HidHide", "HidHide routing admission rejected.", null,
            ("Outcome", outcome),
            ("HiddenDeviceCount", (inspection.HiddenDeviceEntries ?? []).Count),
            ("ForeignWhitelistCount", foreignWhitelistCount),
            ("InverseWhitelist", inspection.IsInverseWhitelist || inspection.Status == HidHideInspectionStatus.InverseWhitelist),
            ("UnresolvedWhitelist", inspection.HasUnresolvedApplicationWhitelistEntries),
            ("OfficialHidHideClientResolved", trusted.Length > 0));
    }

    private static bool IsCanonicalPath(string path)
    {
        try { return Path.IsPathFullyQualified(path) && string.Equals(path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static bool PathEquals(string left, string right)
    {
        try
        {
            return IsCanonicalPath(left) && IsCanonicalPath(right)
                && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
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
