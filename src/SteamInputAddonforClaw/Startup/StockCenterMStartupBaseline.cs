using System.Text.Json;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Startup;

internal interface IStockCenterMStartupBaseline
{
    Task<StockCenterMStartupBaselineResult> EstablishAsync(CancellationToken cancellationToken);
}

internal sealed class StockCenterMStartupBaseline(MsiClawNativeStateManager nativeState) : IStockCenterMStartupBaseline
{
    public async Task<StockCenterMStartupBaselineResult> EstablishAsync(CancellationToken cancellationToken)
    {
        var current = await nativeState.CaptureStableCurrentSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!TryGetCurrentState(current, out var payload, out var identity, out var reason))
            return Failed(reason);

        if (payload.Mode == MsiClawNativeMode.XInput)
        {
            AppLog.Info("Startup.Baseline", "Live Stock MSI Claw baseline is already XInput.", ("Action", "Continue"), ("ModeWrite", false));
            return new(true, false, "AlreadyXInput");
        }

        if (payload.Mode != MsiClawNativeMode.DirectInput)
            return Failed("UnsupportedCurrentMsiMode");

        var transition = await nativeState.SwitchModeAsync(MsiClawNativeMode.XInput, identity, cancellationToken).ConfigureAwait(false);
        if (!transition.Succeeded)
            return Failed("XInputTransitionFailed:" + transition.Reason);

        var verified = await nativeState.CaptureStableCurrentSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!TryGetCurrentState(verified, out var verifiedPayload, out _, out reason) || verifiedPayload.Mode != MsiClawNativeMode.XInput)
        {
            var failureReason = "XInputTransitionNotVerified:" + reason;
            AppLog.Warn("Startup.Baseline", "Live Stock MSI Claw XInput transition could not be verified.", null, ("Action", "Passive"), ("Reason", failureReason));
            return new(false, true, failureReason);
        }

        AppLog.Info("Startup.Baseline", "Live Stock MSI Claw baseline converged to XInput.", ("Action", "Continue"), ("ModeWrite", true));
        return new(true, true, "XInputVerified");
    }

    private static bool TryGetCurrentState(NativeStateCaptureResult capture, out MsiClawNativeStatePayload payload, out MsiClawPhysicalIdentity identity, out string reason)
    {
        payload = null!;
        identity = null!;
        if (!capture.AllowsMutation || capture.Snapshot is null)
        {
            reason = "CurrentMsiStateUnavailable:" + capture.Status;
            return false;
        }

        try { payload = capture.Snapshot.Payload.Deserialize<MsiClawNativeStatePayload>()!; }
        catch (JsonException) { reason = "MalformedCurrentMsiState"; return false; }
        if (payload is null)
        {
            reason = "MalformedCurrentMsiState";
            return false;
        }

        identity = MsiClawPhysicalIdentity.FromPayload(payload);
        if (identity.Confidence != MsiClawIdentityConfidence.Strong)
        {
            reason = "CurrentMsiIdentityIsNotStrong";
            return false;
        }

        reason = "CurrentMsiStateCaptured";
        return true;
    }

    private static StockCenterMStartupBaselineResult Failed(string reason)
    {
        AppLog.Warn("Startup.Baseline", "Live Stock MSI Claw baseline was not established.", null, ("Action", "Passive"), ("Reason", reason));
        return new(false, false, reason);
    }
}

internal sealed record StockCenterMStartupBaselineResult(bool Succeeded, bool ModeWriteIssued, string Reason);
