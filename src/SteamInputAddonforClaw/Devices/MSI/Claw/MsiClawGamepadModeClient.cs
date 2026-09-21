using System.Diagnostics;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

/// <summary>
/// The narrow MSI firmware GamepadMode protocol client. It deliberately does not infer the
/// firmware mode from PID alone: PID1902 is shared by multiple firmware modes, so callers must use
/// the bounded 0x26/0x27 readback when the distinction matters.
/// </summary>
internal sealed class MsiClawGamepadModeClient(
    IControllerDeviceEnumerator deviceEnumerator,
    MsiClawControlHidResolver resolver,
    IMsiClawGamepadModeIo io,
    TimeSpan? readbackTimeout = null,
    TimeSpan? switchSettleDelay = null) : IMsiClawGamepadModeClient
{
    private const int MaxReadReports = 4;
    private static readonly TimeSpan DefaultReadbackTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan DefaultSwitchSettleDelay = TimeSpan.FromMilliseconds(20);
    private readonly TimeSpan _readbackTimeout = readbackTimeout ?? DefaultReadbackTimeout;
    private readonly TimeSpan _switchSettleDelay = switchSettleDelay ?? DefaultSwitchSettleDelay;

    public async Task<MsiClawGamepadModeQueryResult> QueryAsync(MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        for (var attempt = 1; attempt <= MaxReadReports; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = _readbackTimeout - Stopwatch.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
                break;

            var control = ResolveCommand(expectedIdentity);
            if (control is null)
                return MsiClawGamepadModeQueryResult.Unavailable("CommandHidNotUniquelyResolved");

            byte[]? report;
            try
            {
                report = await io.ReadGamepadModeAsync(control, remaining, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                report = null;
            }

            if (report is not null && MsiClawModeCommand.TryParseGamepadModeAck(report, out var mode))
            {
                AppLog.Info("NativeMode", "MSI Claw GamepadMode readback completed.",
                    ("Event", "GamepadModeQueryCompleted"), ("Mode", mode), ("Attempts", attempt),
                    ("ElapsedMs", (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds));
                return new(true, mode, "GamepadModeAckVerified");
            }
        }

        AppLog.Debug("NativeMode", "MSI Claw GamepadMode readback was unavailable.",
            ("Event", "GamepadModeQueryCompleted"), ("Succeeded", false),
            ("Reason", "ReadbackTimeoutOrUnrelatedReports"));
        return MsiClawGamepadModeQueryResult.Unavailable("ReadbackTimeoutOrUnrelatedReports");
    }

    public async Task<MsiClawGamepadModeWriteResult> SwitchAndVerifyAsync(
        MsiClawPhysicalIdentity expectedIdentity,
        MsiClawGamepadMode targetMode,
        CancellationToken cancellationToken)
    {
        var control = ResolveCommand(expectedIdentity);
        if (control is null)
            return new(false, null, false, false, "CommandHidNotUniquelyResolved");

        if (!await io.WriteGamepadModeAsync(control, targetMode, cancellationToken).ConfigureAwait(false))
        {
            AppLog.Warn("NativeMode", "MSI Claw GamepadMode write failed.", null,
                ("Event", "GamepadModeWriteCompleted"), ("Succeeded", false), ("TargetMode", targetMode));
            return new(false, null, true, false, "GamepadModeWriteFailed");
        }

        AppLog.Info("NativeMode", "MSI Claw GamepadMode write completed.",
            ("Event", "GamepadModeWriteCompleted"), ("Succeeded", true), ("TargetMode", targetMode));
        await Task.Delay(_switchSettleDelay, cancellationToken).ConfigureAwait(false);

        var readback = await QueryAsync(expectedIdentity, cancellationToken).ConfigureAwait(false);
        var verified = readback.Succeeded && readback.Mode == targetMode;
        AppLog.Info("NativeMode", "MSI Claw GamepadMode target verification completed.",
            ("Event", "GamepadModeVerified"), ("Succeeded", verified), ("TargetMode", targetMode),
            ("ObservedMode", readback.Mode), ("Reason", readback.Reason));
        return new(verified, readback.Mode, true, verified,
            verified ? "GamepadModeVerified" : "GamepadModeReadbackMismatch:" + readback.Reason);
    }

    private MsiClawControlHidDevice? ResolveCommand(MsiClawPhysicalIdentity expectedIdentity)
    {
        try
        {
            return resolver.ResolveCommand(deviceEnumerator.EnumeratePresentDevices(), expectedIdentity);
        }
        catch (Exception exception)
        {
            AppLog.Debug("NativeMode", "MSI Claw GamepadMode command HID resolution failed.",
                ("Exception", exception.GetType().Name));
            return null;
        }
    }
}
