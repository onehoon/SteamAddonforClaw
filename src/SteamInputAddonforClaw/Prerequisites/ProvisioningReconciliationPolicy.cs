namespace SteamInputAddonforClaw.Prerequisites;

internal enum ProvisioningReconciliationAction { Preserve, Provisioned, PendingReboot }

internal sealed record ProvisioningReconciliationDecision(ProvisioningReconciliationAction Action, string Reason);

internal static class ProvisioningReconciliationPolicy
{
    public static ProvisioningReconciliationDecision Evaluate(
        bool packageInspectionSucceeded,
        bool packageInstalled,
        string? observedVersion,
        string expectedVersion,
        PrerequisiteStatus prerequisiteStatus,
        bool receiptWasInstallStarted)
    {
        if (!packageInspectionSucceeded) return new(ProvisioningReconciliationAction.Preserve, "PackageInspectionFailed");
        if (!packageInstalled) return new(ProvisioningReconciliationAction.Preserve, "PackageMissing");
        if (!string.Equals(observedVersion, expectedVersion, StringComparison.OrdinalIgnoreCase)) return new(ProvisioningReconciliationAction.Preserve, "ProvisioningVersionMismatch");
        if (prerequisiteStatus == PrerequisiteStatus.Ready) return new(ProvisioningReconciliationAction.Provisioned, "Provisioned");
        return receiptWasInstallStarted
            ? new(ProvisioningReconciliationAction.PendingReboot, "PrerequisiteNotReady")
            : new(ProvisioningReconciliationAction.Preserve, "PrerequisiteNotReady");
    }
}
