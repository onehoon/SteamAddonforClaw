using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Devices.Abstractions;
using System.Text.Json;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class MsiClawNativeModeSessionCoordinator : IAsyncDisposable
{
    private readonly MsiClawNativeStateManager _nativeState;
    private readonly RecoveryManager _recovery;
    private readonly PowerMutationGate _powerGate;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DeviceNativeStateSnapshot? _snapshot;
    private bool _active;

    internal MsiClawNativeModeSessionCoordinator(MsiClawNativeStateManager nativeState, RecoveryManager recovery, PowerMutationGate powerGate)
    { _nativeState = nativeState; _recovery = recovery; _powerGate = powerGate; }

    internal Task ObserveAsync(SteamSessionState state, CancellationToken cancellationToken = default) => state.IsActive ? StartAsync(cancellationToken) : StopAsync(cancellationToken);

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_active) return;
            if (!_powerGate.TryAcquire(out var token)) return;
            var captured = _nativeState.CaptureSnapshot();
            if (!captured.AllowsMutation || captured.Snapshot is null) return;
            var journal = _recovery.BeginDeviceNativeStateMutation(captured);
            if (journal.Status != RecoveryStatus.Success) return;
            var original = captured.Snapshot.Payload.Deserialize<MsiClawNativeStatePayload>();
            if (original is null || original.Mode != MsiClawNativeMode.XInput) return;
            if (!_powerGate.IsCurrent(token)) return;
            var identity = new MsiClawPhysicalIdentity(original.ContainerId, original.ParentInstanceId, original.InstanceId ?? string.Empty, MsiClawHardware.VendorId, original.ProductId, original.IdentityConfidence);
            var result = await _nativeState.SwitchModeAsync(MsiClawNativeMode.DirectInput, identity, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded || !_powerGate.IsCurrent(token)) return;
            _snapshot = captured.Snapshot; _active = true;
        }
        finally { _gate.Release(); }
    }

    private async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_active || _snapshot is null) return;
            if (!_powerGate.TryAcquire(out var token)) return;
            var restored = await _nativeState.RestoreSnapshotAsync(_snapshot, cancellationToken).ConfigureAwait(false);
            if (!restored.Restored || !_powerGate.IsCurrent(token)) return;
            var completed = _recovery.CompleteRecoverySession();
            if (completed.Status == RecoveryStatus.Success) { _snapshot = null; _active = false; }
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    { try { await StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { } _gate.Dispose(); }
}
