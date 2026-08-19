namespace SteamInputAddonforClaw.Power;

/// <summary>
/// A minimal, generic capability for a runtime component that needs to run its own fresh
/// reconciliation exactly once after resume, independent of whether Steam/VIIPER routing's own
/// resume reconciliation succeeded. Deliberately narrow -- this is not a general lifecycle/module
/// framework, just the one seam <see cref="Runtime.AddonRuntimeHost"/> needs so a device-specific
/// auxiliary subsystem (e.g. the MSI Center M OEM1 lifecycle driver) can receive a resume callback
/// without <see cref="Runtime.AddonRuntimeHost"/> learning any device-specific detail.
/// </summary>
internal interface IRuntimeResumeParticipant
{
    Task ReconcileAfterResumeAsync(CancellationToken cancellationToken);
}
