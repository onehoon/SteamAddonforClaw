using SteamInputAddonforClaw.Recovery;

namespace SteamInputAddonforClaw.Startup;

internal interface IStartupVirtualOutputRecoveryInspector
{
    Task<StartupVirtualOutputRecoveryAssessment> AssessAsync(
        IReadOnlyList<AddonOwnedVirtualDeviceRecoveryEntry> entries,
        CancellationToken cancellationToken);
}

internal sealed record StartupVirtualOutputRecoveryAssessment(bool SafeToRetire, string Reason);
