using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Recovery;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class MsiClawStartupBaselineService(MsiClawNativeStateManager nativeState, RecoveryManager recovery) : IStartupNativeBaselineValidator
{
    public async Task<StartupNativeBaselineResult> ValidateAsync(CancellationToken cancellationToken)
    {
        var residue = await recovery.CleanStartupResidueAsync(cancellationToken).ConfigureAwait(false);
        var baseline = await nativeState.EnsureStartupXInputBaselineAsync(cancellationToken).ConfigureAwait(false);
        if (!baseline.IsSafeToContinue)
            return baseline;

        var nativeRetirement = recovery.RetireNativeStateForStartupBaseline(nativeState.DeviceId);
        if (nativeRetirement.Status is RecoveryStatus.Failure)
            return new StartupNativeBaselineResult(false, nativeRetirement.Reason);

        var finalJournal = recovery.LoadJournal();
        if (!residue.IsJournalValid || !residue.IsNonNativeResidueResolved || finalJournal.Status != RecoveryStatus.NoRecoveryNeeded)
            return new StartupNativeBaselineResult(false, $"Startup residue remains unresolved: {residue.Reason}");

        return new StartupNativeBaselineResult(true, "Startup residue retired and XInput baseline verified.");
    }
}
