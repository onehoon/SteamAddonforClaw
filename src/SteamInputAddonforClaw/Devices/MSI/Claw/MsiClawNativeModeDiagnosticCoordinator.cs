using System.Diagnostics;
using System.Text.Json;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Power;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed record MsiClawNativeModeDiagnosticResult(bool Succeeded, string Status, string Reason, long ElapsedMs);

internal sealed class MsiClawNativeModeDiagnosticCoordinator(MsiClawNativeStateManager nativeState, RecoveryManager recoveryManager, PowerMutationGate? powerGate = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    internal bool IsRunning { get; private set; }

    internal async Task<MsiClawNativeModeDiagnosticResult> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return new(false, "Busy", "A native mode diagnostic is already running.", 0);
        var started = Stopwatch.GetTimestamp(); IsRunning = true;
        try
        {
            var powerToken = new PowerMutationToken(0);
            if (powerGate is not null && !powerGate.TryAcquire(out powerToken)) return Failure("PowerGateClosed", started);
            var captured = nativeState.CaptureSnapshot();
            if (!captured.AllowsMutation || captured.Snapshot is null) return Failure(captured.Reason, started);
            var original = captured.Snapshot.Payload.Deserialize<MsiClawNativeStatePayload>();
            if (original is null) return Failure("MalformedSnapshotPayload", started);
            var journal = recoveryManager.BeginDeviceNativeStateMutation(captured);
            if (journal.Status != RecoveryStatus.Success) return Failure(journal.Reason, started);
            var identity = new MsiClawPhysicalIdentity(original.ContainerId, original.ParentInstanceId, original.InstanceId ?? string.Empty, MsiClawHardware.VendorId, original.ProductId, original.IdentityConfidence);
            var target = original.Mode == MsiClawNativeMode.XInput ? MsiClawNativeMode.DirectInput : original.Mode == MsiClawNativeMode.DirectInput ? MsiClawNativeMode.XInput : MsiClawNativeMode.Other;
            if (target == MsiClawNativeMode.Other) return Failure("UnsupportedOriginalMode", started);
            if (powerGate is not null && !powerGate.IsCurrent(powerToken)) return Failure("StaleGeneration", started);
            var switched = await nativeState.SwitchModeAsync(target, identity, cancellationToken).ConfigureAwait(false);
            if (!switched.Succeeded) return Failure(switched.Reason, started);
            if (powerGate is not null && !powerGate.IsCurrent(powerToken)) return Failure("StaleGeneration", started);
            var restored = await nativeState.RestoreSnapshotAsync(captured.Snapshot, cancellationToken).ConfigureAwait(false);
            if (!restored.Restored) return Failure(restored.Reason, started);
            if (powerGate is not null && !powerGate.IsCurrent(powerToken)) return Failure("StaleGeneration", started);
            var completed = recoveryManager.CompleteRecoverySession();
            return completed.Status == RecoveryStatus.Success ? new(true, "Succeeded", "Native mode transition and exact restore verified.", Elapsed(started)) : Failure(completed.Reason, started);
        }
        catch (OperationCanceledException) { return Failure("Cancelled", started); }
        catch (Exception exception) { return Failure(exception.GetType().Name, started); }
        finally { IsRunning = false; _gate.Release(); }
    }

    private static MsiClawNativeModeDiagnosticResult Failure(string reason, long started) => new(false, "Failed", reason, Elapsed(started));
    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
