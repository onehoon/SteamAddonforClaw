using SteamInputAddonforClaw.CenterM;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

/// <summary>
/// Wraps the SAME already-owned <see cref="IMsiClawNativeModeStageSession"/> (the composition's
/// <c>NativeModeSession</c>) that the real NativeMode stage uses -- never constructs a second
/// session/state manager. Purely forwards to the existing read-only
/// <see cref="IMsiClawNativeModeStageSession.InspectForPipelineAsync"/> preflight; NativeMode's own
/// Prepare step still always runs again afterward against then-current world state.
/// </summary>
internal sealed class MsiClawCenterMRoutingPreflightProbe(IMsiClawNativeModeStageSession session) : ICenterMRoutingPreflightProbe
{
    private readonly IMsiClawNativeModeStageSession _session = session ?? throw new ArgumentNullException(nameof(session));

    public async ValueTask<CenterMRoutingPreflightResult> InspectAsync(CancellationToken cancellationToken)
    {
        var result = await _session.InspectForPipelineAsync(cancellationToken).ConfigureAwait(false);
        return new CenterMRoutingPreflightResult(result.Succeeded, result.Reason);
    }
}
