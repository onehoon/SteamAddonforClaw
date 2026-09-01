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
    /// <summary>Sets HidHide's whitelist-inverse mode and verifies it by read-back. Defaults to
    /// <see langword="false"/> (fail-closed) so any client without a real, supported inverse-mode
    /// control path forces callers that need <c>Inverse == false</c> to fail closed rather than
    /// proceed on an unverified assumption (Addon HidHide baseline work order section 6).</summary>
    bool SetInverseWhitelist(bool inverse) => false;
    /// <summary>Whether this client has a real, verified in-process path for
    /// <see cref="SetInverseWhitelist"/>. Defaults to <see langword="false"/> so a caller that needs
    /// <c>Inverse == false</c> classifies an inverse-whitelist machine as an unsupported conflict up
    /// front rather than attempting a mutation that cannot work.</summary>
    bool SupportsInverseWhitelistMutation => false;
}
