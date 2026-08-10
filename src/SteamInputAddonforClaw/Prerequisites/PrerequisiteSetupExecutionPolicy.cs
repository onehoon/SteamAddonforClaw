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

    public static PrerequisitePostInstallOutcome EvaluatePostInstall(int exitCode, bool inspectionSucceeded, bool packageInstalled, string? observedVersion, string expectedVersion, PrerequisiteStatus prerequisiteStatus)
    {
        if (exitCode == 3010) return new(false, true, "InstallerRequestedRestart");
        if (exitCode != 0) return new(false, false, "InstallerExitCode" + exitCode);
        if (!inspectionSucceeded) return new(false, false, "PostInstallPackageInspectionFailed");
        if (!packageInstalled) return new(false, false, "PostInstallPackageMissing");
        if (!string.Equals(observedVersion, expectedVersion, StringComparison.OrdinalIgnoreCase)) return new(false, false, "PostInstallVersionMismatch");
        if (prerequisiteStatus != PrerequisiteStatus.Ready) return new(false, false, "PostInstallPrerequisiteNotReady");
        return new(true, false, "Provisioned");
    }
}
