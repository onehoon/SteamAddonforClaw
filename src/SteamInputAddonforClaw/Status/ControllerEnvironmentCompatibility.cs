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
    ControllerSoftwareStateIndeterminate,
    WinhancedNotSupportedByCurrentVersion
}

internal sealed record ControllerEnvironmentCompatibilityAssessment(ControllerEnvironmentCompatibilityStatus Status, ControllerEnvironmentCompatibilityReason Reason)
{
    public bool AllowsMutation => Status == ControllerEnvironmentCompatibilityStatus.Supported;
}

internal sealed record ControllerSoftwareSnapshot(
    ControllerSoftwareStatus MsiCenterM,
    ControllerSoftwareStatus ClawTweaks,
    ControllerSoftwareStatus HandheldCompanion,
    ControllerSoftwareStatus? Winhanced = null)
{
    public static bool TryCreate(IReadOnlyList<ControllerSoftwareStatus> statuses, out ControllerSoftwareSnapshot? snapshot)
    {
        snapshot = null;
        if (statuses is null) return false;
        var grouped = statuses.GroupBy(status => status.Kind).ToDictionary(group => group.Key, group => group.ToArray());
        if (!grouped.TryGetValue(ControllerSoftwareKind.MsiCenterM, out var centerM) || centerM.Length != 1
            || !grouped.TryGetValue(ControllerSoftwareKind.ClawTweaks, out var clawTweaks) || clawTweaks.Length != 1
            || !grouped.TryGetValue(ControllerSoftwareKind.HandheldCompanion, out var handheldCompanion) || handheldCompanion.Length != 1)
            return false;
        if (grouped.Keys.Any(kind => kind is not (ControllerSoftwareKind.MsiCenterM or ControllerSoftwareKind.ClawTweaks or ControllerSoftwareKind.HandheldCompanion or ControllerSoftwareKind.Winhanced))
            || (grouped.TryGetValue(ControllerSoftwareKind.Winhanced, out var winhanced) && winhanced.Length > 1))
            return false;
        snapshot = new(centerM[0], clawTweaks[0], handheldCompanion[0], winhanced?[0]);
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
        var classification = ControllerManagerClassifier.Classify(software);
        return classification.Kind switch
        {
            ControllerManagerKind.Multiple => Unsupported(ControllerEnvironmentCompatibilityReason.MultipleThirdPartyControllerManagersNotSupportedByCurrentVersion),
            ControllerManagerKind.ClawTweaks => Unsupported(ControllerEnvironmentCompatibilityReason.ClawTweaksNotSupportedByCurrentVersion),
            ControllerManagerKind.HandheldCompanion => Unsupported(ControllerEnvironmentCompatibilityReason.HandheldCompanionNotSupportedByCurrentVersion),
            ControllerManagerKind.Winhanced => Unsupported(ControllerEnvironmentCompatibilityReason.WinhancedNotSupportedByCurrentVersion),
            ControllerManagerKind.Indeterminate => Indeterminate(),
            _ => EvaluateStockCenterM(software.MsiCenterM)
        };
    }

    private static ControllerEnvironmentCompatibilityAssessment EvaluateStockCenterM(ControllerSoftwareStatus centerM)
    {
        if (centerM.Installation == SoftwareInstallationStatus.Indeterminate || centerM.Runtime == SoftwareRuntimeStatus.Indeterminate) return Indeterminate();
        if (centerM.Runtime == SoftwareRuntimeStatus.Starting) return new(ControllerEnvironmentCompatibilityStatus.Indeterminate, ControllerEnvironmentCompatibilityReason.MsiCenterMStarting);
        if (centerM.Installation == SoftwareInstallationStatus.NotInstalled) return Unsupported(ControllerEnvironmentCompatibilityReason.MsiCenterMRequired);
        if (centerM.Runtime != SoftwareRuntimeStatus.Running) return Unsupported(ControllerEnvironmentCompatibilityReason.MsiCenterMNotOperational);
        return new(ControllerEnvironmentCompatibilityStatus.Supported, ControllerEnvironmentCompatibilityReason.StockCenterMOnlySupported);
    }
    private static ControllerEnvironmentCompatibilityAssessment Unsupported(ControllerEnvironmentCompatibilityReason reason) => new(ControllerEnvironmentCompatibilityStatus.Unsupported, reason);
    private static ControllerEnvironmentCompatibilityAssessment Indeterminate() => new(ControllerEnvironmentCompatibilityStatus.Indeterminate, ControllerEnvironmentCompatibilityReason.ControllerSoftwareStateIndeterminate);
}
