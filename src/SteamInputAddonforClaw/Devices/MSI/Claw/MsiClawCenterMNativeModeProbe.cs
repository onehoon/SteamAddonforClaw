using System.Text.Json;
using SteamInputAddonforClaw.CenterM;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

/// <summary>
/// Wraps the SAME already-owned <see cref="MsiClawNativeStateManager"/> instance
/// <see cref="MsiClawRoutingComposition"/> uses for the real NativeMode stage -- never constructs a
/// second one. Read-only: never calls <see cref="MsiClawNativeStateManager.SwitchModeAsync"/> or any
/// other mutating member.
/// </summary>
internal sealed class MsiClawCenterMNativeModeProbe(MsiClawNativeStateManager nativeStateManager) : ICenterMNativeModeProbe
{
    private readonly MsiClawNativeStateManager _nativeStateManager = nativeStateManager ?? throw new ArgumentNullException(nameof(nativeStateManager));

    public async Task<CenterMNativeModeProbeResult> CaptureAsync(CancellationToken cancellationToken)
    {
        var result = await _nativeStateManager.CaptureStableCurrentSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!result.AllowsMutation || result.Snapshot is null) return CenterMNativeModeProbeResult.Uncertain;

        MsiClawNativeStatePayload? payload;
        try { payload = result.Snapshot.Payload.Deserialize<MsiClawNativeStatePayload>(); }
        catch (JsonException) { return CenterMNativeModeProbeResult.Uncertain; }
        if (payload is null) return CenterMNativeModeProbeResult.Uncertain;

        // A successful capture alone is not enough authority to call XInput confirmed -- it says
        // nothing about physical identity strength. The real NativeMode stage
        // (MsiClawNativeModeSessionCoordinator.InspectCoreLocked) explicitly refuses to mutate
        // unless IdentityConfidence.Strong; this probe must refuse to call the state authoritative
        // under the same weaker evidence, or retirement could terminate the real MainUI for a route
        // the canonical native preflight was always going to reject anyway.
        if (payload.IdentityConfidence != MsiClawIdentityConfidence.Strong) return CenterMNativeModeProbeResult.Uncertain;

        return payload.Mode == MsiClawNativeMode.XInput ? CenterMNativeModeProbeResult.XInput : CenterMNativeModeProbeResult.NotXInput;
    }
}
