namespace SteamInputAddonforClaw.Prerequisites;

internal enum PrerequisiteComponentAction { Install, AlreadyReady, RestartRequired, Blocked }
internal sealed record PrerequisitePostInstallOutcome(bool IsProvisioned, bool RequiresRestart, string Reason);

internal static class PrerequisiteSetupExecutionPolicy
{
    public static PrerequisiteComponentAction SelectAction(bool packageInstalled, PrerequisiteStatus prerequisiteStatus, bool addonReceiptPendingReboot, bool unresolvedInstallStarted)
    {
        if (unresolvedInstallStarted) return PrerequisiteComponentAction.Blocked;
        if (!packageInstalled) return addonReceiptPendingReboot ? PrerequisiteComponentAction.Blocked : PrerequisiteComponentAction.Install;
        if (prerequisiteStatus == PrerequisiteStatus.Ready) return PrerequisiteComponentAction.AlreadyReady;
        return addonReceiptPendingReboot ? PrerequisiteComponentAction.RestartRequired : PrerequisiteComponentAction.Blocked;
    }

    public static PrerequisitePostInstallOutcome EvaluatePostInstall(int exitCode, bool inspectionSucceeded, bool packageInstalled, string? observedVersion, string expectedVersion, PrerequisiteStatus prerequisiteStatus, bool allowControlDeviceEvidence = false)
    {
        if (exitCode == 3010) return new(false, true, "InstallerRequestedRestart");
        if (exitCode != 0) return new(false, false, "InstallerExitCode" + exitCode);
        if (!inspectionSucceeded) return new(false, false, "PostInstallPackageInspectionFailed");
        if (!packageInstalled && !(allowControlDeviceEvidence && prerequisiteStatus == PrerequisiteStatus.Unusable)) return new(false, false, "PostInstallPackageMissing");
        if (packageInstalled && !string.Equals(observedVersion, expectedVersion, StringComparison.OrdinalIgnoreCase)) return new(false, false, "PostInstallVersionMismatch");
        if (prerequisiteStatus is not (PrerequisiteStatus.Ready or PrerequisiteStatus.Unusable)) return new(false, false, "PostInstallPrerequisiteNotReady");
        if (allowControlDeviceEvidence && prerequisiteStatus == PrerequisiteStatus.Unusable) return new(true, false, "InstalledControlDeviceNotRuntimeReady");
        return new(true, false, "Provisioned");
    }
}
