using SteamInputAddonforClaw.Startup;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class MsiClawStartupBaselineService(MsiClawNativeStateManager nativeState) : IStartupNativeBaselineValidator
{
    public Task<StartupNativeBaselineResult> ValidateAsync(CancellationToken cancellationToken) =>
        nativeState.EnsureStartupXInputBaselineAsync(cancellationToken);
}
