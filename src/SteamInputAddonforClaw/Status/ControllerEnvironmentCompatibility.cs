using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Status;

internal enum ControllerEnvironmentCompatibilityStatus { Supported, Unsupported, Indeterminate }
internal enum ControllerEnvironmentCompatibilityReason
{
    StockCenterMOnlySupported,
    ClawTweaksNotSupportedByCurrentVersion,
    HandheldCompanionNotSupportedByCurrentVersion,
    MultipleThirdPartyControllerManagersNotSupportedByCurrentVersion,
    MsiCenterMRequired,
    MsiCenterMNotOperational,
    MsiCenterMStarting,
    ControllerSoftwareStateIndeterminate
}

internal sealed record ControllerEnvironmentCompatibilityAssessment(ControllerEnvironmentCompatibilityStatus Status, ControllerEnvironmentCompatibilityReason Reason)
{
    public bool AllowsMutation => Status == ControllerEnvironmentCompatibilityStatus.Supported;
}

internal sealed record ControllerSoftwareSnapshot(ControllerSoftwareStatus MsiCenterM, ControllerSoftwareStatus ClawTweaks, ControllerSoftwareStatus HandheldCompanion)
{
    public static bool TryCreate(IReadOnlyList<ControllerSoftwareStatus> statuses, out ControllerSoftwareSnapshot? snapshot)
    {
        snapshot = null;
        if (statuses is null || statuses.Count != 3) return false;
        var grouped = statuses.GroupBy(status => status.Kind).ToDictionary(group => group.Key, group => group.ToArray());
        if (!grouped.TryGetValue(ControllerSoftwareKind.MsiCenterM, out var centerM) || centerM.Length != 1
            || !grouped.TryGetValue(ControllerSoftwareKind.ClawTweaks, out var clawTweaks) || clawTweaks.Length != 1
            || !grouped.TryGetValue(ControllerSoftwareKind.HandheldCompanion, out var handheldCompanion) || handheldCompanion.Length != 1)
            return false;
        snapshot = new(centerM[0], clawTweaks[0], handheldCompanion[0]);
        return true;
    }
}

internal interface IControllerEnvironmentCompatibilityPolicy
{
    ControllerEnvironmentCompatibilityAssessment Evaluate(IReadOnlyList<ControllerSoftwareStatus> software);
}

internal sealed class CurrentControllerEnvironmentCompatibilityPolicy : IControllerEnvironmentCompatibilityPolicy
{
    public ControllerEnvironmentCompatibilityAssessment Evaluate(IReadOnlyList<ControllerSoftwareStatus> software)
    {
        var result = !ControllerSoftwareSnapshot.TryCreate(software, out var snapshot)
            ? new(ControllerEnvironmentCompatibilityStatus.Indeterminate, ControllerEnvironmentCompatibilityReason.ControllerSoftwareStateIndeterminate)
            : Evaluate(snapshot!);
        AppLog.Info("Compatibility", "Controller environment compatibility assessed.", ("Status", result.Status), ("Reason", result.Reason), ("AllowsMutation", result.AllowsMutation));
        return result;
    }

    private static ControllerEnvironmentCompatibilityAssessment Evaluate(ControllerSoftwareSnapshot software)
    {
        var clawTweaksPresent = IsPresent(software.ClawTweaks);
        var handheldCompanionPresent = IsPresent(software.HandheldCompanion);
        if (clawTweaksPresent && handheldCompanionPresent) return Unsupported(ControllerEnvironmentCompatibilityReason.MultipleThirdPartyControllerManagersNotSupportedByCurrentVersion);
        if (clawTweaksPresent) return Unsupported(ControllerEnvironmentCompatibilityReason.ClawTweaksNotSupportedByCurrentVersion);
        if (handheldCompanionPresent) return Unsupported(ControllerEnvironmentCompatibilityReason.HandheldCompanionNotSupportedByCurrentVersion);
        if (IsUnresolved(software.ClawTweaks) || IsUnresolved(software.HandheldCompanion)) return Indeterminate();
        if (software.MsiCenterM.Installation == SoftwareInstallationStatus.Indeterminate || software.MsiCenterM.Runtime == SoftwareRuntimeStatus.Indeterminate) return Indeterminate();
        if (software.MsiCenterM.Runtime == SoftwareRuntimeStatus.Starting) return new(ControllerEnvironmentCompatibilityStatus.Indeterminate, ControllerEnvironmentCompatibilityReason.MsiCenterMStarting);
        if (software.MsiCenterM.Installation == SoftwareInstallationStatus.NotInstalled) return Unsupported(ControllerEnvironmentCompatibilityReason.MsiCenterMRequired);
        if (software.MsiCenterM.Runtime != SoftwareRuntimeStatus.Running) return Unsupported(ControllerEnvironmentCompatibilityReason.MsiCenterMNotOperational);
        return new(ControllerEnvironmentCompatibilityStatus.Supported, ControllerEnvironmentCompatibilityReason.StockCenterMOnlySupported);
    }

    private static bool IsPresent(ControllerSoftwareStatus status) => status.Installation == SoftwareInstallationStatus.Installed || status.Runtime == SoftwareRuntimeStatus.Running;
    private static bool IsUnresolved(ControllerSoftwareStatus status) => status.Installation == SoftwareInstallationStatus.Indeterminate || status.Runtime is SoftwareRuntimeStatus.Starting or SoftwareRuntimeStatus.Indeterminate;
    private static ControllerEnvironmentCompatibilityAssessment Unsupported(ControllerEnvironmentCompatibilityReason reason) => new(ControllerEnvironmentCompatibilityStatus.Unsupported, reason);
    private static ControllerEnvironmentCompatibilityAssessment Indeterminate() => new(ControllerEnvironmentCompatibilityStatus.Indeterminate, ControllerEnvironmentCompatibilityReason.ControllerSoftwareStateIndeterminate);
}
