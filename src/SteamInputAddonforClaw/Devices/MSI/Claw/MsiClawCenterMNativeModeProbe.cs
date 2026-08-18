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

        return payload.Mode == MsiClawNativeMode.XInput ? CenterMNativeModeProbeResult.XInput : CenterMNativeModeProbeResult.NotXInput;
    }
}
