namespace SteamInputAddonforClaw.Status;

internal enum ControllerManagerKind { None, ClawTweaks, HandheldCompanion, Winhanced, Multiple, Indeterminate }

internal enum ControllerManagerClassificationReason
{
    NoThirdPartyControllerManager,
    ClawTweaksDetected,
    HandheldCompanionDetected,
    WinhancedDetected,
    MultipleThirdPartyControllerManagersDetected,
    ControllerManagerStateIndeterminate
}

internal sealed record ControllerManagerClassification(ControllerManagerKind Kind, ControllerManagerClassificationReason Reason);

internal static class ControllerManagerClassifier
{
    public static ControllerManagerClassification Classify(ControllerSoftwareSnapshot snapshot)
    {
        var present = new List<ControllerManagerKind>();
        if (IsPresent(snapshot.ClawTweaks)) present.Add(ControllerManagerKind.ClawTweaks);
        if (IsPresent(snapshot.HandheldCompanion)) present.Add(ControllerManagerKind.HandheldCompanion);
        if (snapshot.Winhanced is not null && IsPresent(snapshot.Winhanced)) present.Add(ControllerManagerKind.Winhanced);
        if (present.Count >= 2) return new(ControllerManagerKind.Multiple, ControllerManagerClassificationReason.MultipleThirdPartyControllerManagersDetected);
        if (present.Count == 1) return present[0] switch
        {
            ControllerManagerKind.ClawTweaks => new(ControllerManagerKind.ClawTweaks, ControllerManagerClassificationReason.ClawTweaksDetected),
            ControllerManagerKind.HandheldCompanion => new(ControllerManagerKind.HandheldCompanion, ControllerManagerClassificationReason.HandheldCompanionDetected),
            ControllerManagerKind.Winhanced => new(ControllerManagerKind.Winhanced, ControllerManagerClassificationReason.WinhancedDetected),
            _ => new(ControllerManagerKind.Indeterminate, ControllerManagerClassificationReason.ControllerManagerStateIndeterminate)
        };
        if (IsUnresolved(snapshot.ClawTweaks) || IsUnresolved(snapshot.HandheldCompanion) || (snapshot.Winhanced is not null && IsUnresolved(snapshot.Winhanced)))
            return new(ControllerManagerKind.Indeterminate, ControllerManagerClassificationReason.ControllerManagerStateIndeterminate);
        return new(ControllerManagerKind.None, ControllerManagerClassificationReason.NoThirdPartyControllerManager);
    }

    private static bool IsPresent(ControllerSoftwareStatus status) => status.Installation == SoftwareInstallationStatus.Installed || status.Runtime == SoftwareRuntimeStatus.Running;
    private static bool IsUnresolved(ControllerSoftwareStatus status) => status.Installation == SoftwareInstallationStatus.Indeterminate || status.Runtime is SoftwareRuntimeStatus.Starting or SoftwareRuntimeStatus.Indeterminate;
}
