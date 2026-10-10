using System.Diagnostics;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed record MsiClawControlHidDevice(ControllerDeviceInfo Device, ushort UsagePage, ushort Usage, MsiClawPhysicalIdentity VerifiedIdentity);
internal interface IMsiClawControlHidResolver
{
    MsiClawControlHidDevice? Resolve(IReadOnlyList<ControllerDeviceInfo> devices, MsiClawNativeMode currentMode, MsiClawPhysicalIdentity expectedIdentity);
}
internal interface IMsiClawModeWriter
{
    Task<bool> WriteAsync(MsiClawControlHidDevice device, MsiClawNativeMode mode, CancellationToken cancellationToken);
}

internal sealed class MsiClawControlHidResolver : IMsiClawControlHidResolver
{
    public MsiClawControlHidDevice? Resolve(IReadOnlyList<ControllerDeviceInfo> devices, MsiClawNativeMode mode, MsiClawPhysicalIdentity expectedIdentity)
    {
        if (!MsiClawModeTopology.TryGet(mode, out var topology)) return null;
        var pid = topology.ProductId;
        var usagePage = topology.UsagePage;
        var usage = topology.Usage;
        var candidates = devices.Where(d => d.Present && d.VendorId == MsiClawHardware.VendorId && d.ProductId == pid && MsiClawPhysicalIdentity.From(d).StronglyMatches(expectedIdentity))
            .Where(d => d.UsagePage == usagePage && d.Usage == usage).ToArray();
        return candidates.Length == 1 ? new(candidates[0], usagePage, usage, MsiClawPhysicalIdentity.From(candidates[0])) : null;
    }

    internal MsiClawControlHidDevice? ResolveCommand(IReadOnlyList<ControllerDeviceInfo> devices, MsiClawPhysicalIdentity expectedIdentity)
    {
        var candidates = devices
            .Where(d => d.Present
                && MsiClawHardware.IsKnownController(d.VendorId, d.ProductId)
                && MsiClawPhysicalIdentity.From(d).StronglyMatches(expectedIdentity)
                && ((d.UsagePage == 0xFFA0 && d.Usage == 0x0001)
                    || (d.UsagePage == MsiClawHardware.DirectInputControlUsagePage && d.Usage == MsiClawHardware.DirectInputControlUsage)))
            .ToArray();
        return candidates.Length == 1
            ? new(candidates[0], candidates[0].UsagePage!.Value, candidates[0].Usage!.Value, MsiClawPhysicalIdentity.From(candidates[0]))
            : null;
    }
}

internal readonly record struct MsiClawModeTopology(ushort ProductId, ushort UsagePage, ushort Usage)
{
    internal static bool TryGet(MsiClawNativeMode mode, out MsiClawModeTopology topology)
    {
        topology = mode switch
        {
            MsiClawNativeMode.XInput => new(MsiClawHardware.XInputProductId, 0xFFA0, 0x0001),
            MsiClawNativeMode.DirectInput => new(MsiClawHardware.DirectInputProductId, 0xFFF0, 0x0040),
            _ => default
        };
        return mode is MsiClawNativeMode.XInput or MsiClawNativeMode.DirectInput;
    }
}

internal sealed class MsiClawModeController(
    IControllerDeviceEnumerator deviceEnumerator,
    IMsiClawControlHidResolver resolver,
    IMsiClawModeWriter writer,
    TimeSpan? timeout = null,
    TimeSpan? pollInterval = null,
    Func<DateTimeOffset>? now = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null) : IMsiClawModeController
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(5);
    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(75);
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((duration, token) => Task.Delay(duration, token));

    public async Task<MsiClawModeTransitionResult> SwitchModeAsync(MsiClawNativeMode target, MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken)
    {
        var started = _now();
        var sourceMode = MsiClawNativeMode.Other;
        ushort? sourcePid = null;
        var writeSucceeded = false;
        var oldPidDisappeared = false;
        var targetPidPresent = false;
        var targetSeen = false;
        var targetTopologyVerified = false;
        DateTimeOffset? commandWrittenAt = null;
        long? elapsedBeforeWriteMs = null;

        MsiClawModeTransitionResult Complete(
            MsiClawModeTransitionStatus status,
            MsiClawNativeMode from,
            string reason,
            bool sourceIdentityVerified = false)
        {
            var result = new MsiClawModeTransitionResult(
                status,
                from,
                target,
                sourcePid,
                MsiClawModeTopology.TryGet(target, out var resultTopology) ? resultTopology.ProductId : null,
                writeSucceeded,
                oldPidDisappeared,
                targetSeen,
                sourceIdentityVerified,
                targetTopologyVerified,
                (long)(_now() - started).TotalMilliseconds,
                reason,
                targetPidPresent,
                commandWrittenAt is { } resultWriteAt ? (long)(_now() - resultWriteAt).TotalMilliseconds : null);
            AppLog.Info("NativeMode", "Native mode transition completed.",
                ("Event", "NativeModeTransitionCompleted"),
                ("SourceMode", from), ("TargetMode", target),
                ("WriteSucceeded", writeSucceeded), ("ElapsedBeforeWriteMs", elapsedBeforeWriteMs),
                ("SinceCommandWriteMs", commandWrittenAt is { } writeAt ? (long)(_now() - writeAt).TotalMilliseconds : null),
                ("OldPidDisappeared", oldPidDisappeared), ("TargetPidPresent", targetPidPresent),
                ("ExactTargetTopologyProven", targetTopologyVerified),
                ("Result", status), ("FailureReason", status == MsiClawModeTransitionStatus.Succeeded ? "None" : reason));
            return result;
        }

        if (!MsiClawModeTopology.TryGet(target, out var targetTopology))
            return Complete(MsiClawModeTransitionStatus.UnsupportedDevice, sourceMode, "Unsupported target native mode.");

        var devices = deviceEnumerator.EnumeratePresentDevices();
        var source = ResolveSource(devices, expectedIdentity);
        if (source.Status is not MsiClawModeTransitionStatus.Succeeded)
        {
            if (source.Reason == "A2vm230ControlEndpointUnverified")
                AppLog.Warn("NativeMode", "Native mode command was not issued because the A2VM 2.30 control endpoint is unverified.", null,
                    ("Reason", source.Reason), ("TargetMode", target), ("WriteIssued", false));
            else
                AppLog.Debug("NativeMode", "NativeModeSourceAmbiguous", ("Reason", source.Reason), ("TargetMode", target));
            sourceMode = source.Mode;
            sourcePid = source.ProductId;
            return Complete(source.Status, sourceMode, source.Reason);
        }
        sourceMode = source.Mode;
        sourcePid = source.ProductId;
        AppLog.Debug("NativeMode", "NativeModeSourceResolved", ("SourceMode", source.Mode), ("SourcePID", source.ProductId), ("SourceIdentityConfidence", expectedIdentity.Confidence));

        // Keep the original bounded source/HID-write phase. A successful write starts a separate
        // full verification budget so native HID latency cannot consume the PnP settle window.
        var writeDeadline = started + _timeout;
        MsiClawControlHidDevice? control = source.Control;
        var commandStartLogged = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!commandStartLogged)
            {
                commandStartLogged = true;
                AppLog.Debug("RoutingTrace", "Native mode command starting.",
                    ("Event", "NativeModeCommandStarted"), ("TargetMode", target));
            }
            if (await writer.WriteAsync(control!, target, cancellationToken).ConfigureAwait(false))
            {
                writeSucceeded = true;
                commandWrittenAt = _now();
                elapsedBeforeWriteMs = (long)(commandWrittenAt.Value - started).TotalMilliseconds;
                AppLog.Debug("RoutingTrace", "Native mode command written.",
                    ("Event", "NativeModeCommandWritten"), ("TargetMode", target));
                AppLog.Debug("NativeMode", "NativeModeCommandWriteSucceeded", ("TargetMode", target));
                break;
            }
            if (_now() >= writeDeadline)
                return Complete(MsiClawModeTransitionStatus.WriteFailed, source.Mode, "Control HID write failed.", true);
            await _delay(_pollInterval, cancellationToken).ConfigureAwait(false);
            devices = deviceEnumerator.EnumeratePresentDevices();
            source = ResolveSource(devices, expectedIdentity);
            if (source.Status is not MsiClawModeTransitionStatus.Succeeded)
            {
                sourceMode = source.Mode;
                sourcePid = source.ProductId;
                return Complete(source.Status, sourceMode, source.Reason);
            }
            control = source.Control;
        }

        var oldPid = source.ProductId;
        var verificationDeadline = commandWrittenAt!.Value + _timeout;
        var firstPid1902Logged = false;
        var poll = 0;
        var logPid1902Arrival = target == MsiClawNativeMode.DirectInput
            && targetTopology.ProductId == MsiClawHardware.DirectInputProductId;
        while (_now() < verificationDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            poll++;
            var probeStarted = Stopwatch.GetTimestamp();
            var verificationStarted = Stopwatch.GetTimestamp();
            var current = deviceEnumerator.EnumeratePresentDevices(MsiClawHardware.VendorId, targetTopology.ProductId);
            var exactVerificationMs = Stopwatch.GetElapsedTime(verificationStarted).TotalMilliseconds;
            var targetProbeMs = Stopwatch.GetElapsedTime(probeStarted).TotalMilliseconds;
            targetPidPresent = current.Any(d => d.Present && d.VendorId == MsiClawHardware.VendorId && d.ProductId == targetTopology.ProductId);
            oldPidDisappeared = oldPid is not { } oldProductId || !deviceEnumerator.IsPresent(MsiClawHardware.VendorId, oldProductId);
            // TargetPidPresent: any present node with the target PID, regardless of topology --
            // distinguishes "PID_1902 hasn't appeared yet" from "PID_1902 is present but the
            // strict control-HID candidate below hasn't shown up yet".
            var targets = current.Where(d => d.Present && d.VendorId == MsiClawHardware.VendorId && d.ProductId == targetTopology.ProductId && d.UsagePage == targetTopology.UsagePage && d.Usage == targetTopology.Usage).ToArray();
            var targetGroups = targets.GroupBy(MsiClawLogicalIdentity.GetLogicalKey, StringComparer.OrdinalIgnoreCase).ToArray();
            targetSeen = targetGroups.Length > 0;
            if (logPid1902Arrival && targetPidPresent && !firstPid1902Logged)
            {
                firstPid1902Logged = true;
                AppLog.Debug("RoutingTrace", "PID1902 first seen.", ("Event", "Pid1902FirstSeen"), ("TargetPID", targetTopology.ProductId));
            }
            AppLog.Debug("NativeMode", "NativeModeTransitionPoll",
                ("Poll", poll),
                ("ElapsedMs", (long)(_now() - started).TotalMilliseconds),
                ("SinceCommandWriteMs", (long)(_now() - commandWrittenAt.Value).TotalMilliseconds),
                ("TargetProbeMs", (long)targetProbeMs),
                ("ExactVerificationMs", (long)exactVerificationMs),
                ("EnumerationMs", (long)exactVerificationMs),
                ("OldPidPresent", !oldPidDisappeared),
                ("TargetPidPresent", targetPidPresent),
                ("TargetControlCandidateCount", targets.Length),
                ("LogicalCandidateCount", targetGroups.Length));
            if (targetGroups.Length > 1)
            {
                AppLog.Debug("NativeMode", "NativeModeTargetAmbiguous", ("TargetMode", target), ("CandidateCount", targets.Length), ("LogicalCandidateCount", targetGroups.Length));
                return Complete(MsiClawModeTransitionStatus.AmbiguousDevice, source.Mode, "Target control HID was ambiguous.", true);
            }
            targetTopologyVerified = targetGroups.Length == 1;
            // PR11 section 5: cross-mode continuity needs BOTH exactly one present target logical
            // control group AND the old PID gone. A single target group while the old PID is still
            // present is a normal mid-transition state -- keep settling inside the bounded window.
            if (targetGroups.Length == 1 && oldPidDisappeared)
            {
                var observed = targetGroups[0].First();
                AppLog.Debug("NativeMode", "NativeModeTargetObserved", ("TargetPID", targetTopology.ProductId), ("TargetIdentityConfidence", MsiClawPhysicalIdentity.From(observed).Confidence), ("CrossModeIdentityChanged", !expectedIdentity.StronglyMatches(MsiClawPhysicalIdentity.From(observed))));
                AppLog.Debug("NativeMode", "NativeModeTransitionSucceeded", ("SourceMode", source.Mode), ("TargetMode", target), ("OldPidDisappeared", true));
                return Complete(MsiClawModeTransitionStatus.Succeeded, source.Mode, "Native mode transition verified.", true);
            }
            await _delay(_pollInterval, cancellationToken).ConfigureAwait(false);
        }
        // Bounded window expired. Distinguish "target never appeared" from "target appeared but the
        // old native-mode device stayed present" so the caller can fail closed with a real reason.
        if (targetSeen && !oldPidDisappeared)
        {
            AppLog.Debug("NativeMode", "NativeModeOldDeviceDidNotDisappear", ("SourceMode", source.Mode), ("TargetMode", target));
            return Complete(MsiClawModeTransitionStatus.OldDeviceDidNotDisappear, source.Mode, "The previous native-mode device did not disappear.", true);
        }
        AppLog.Debug("NativeMode", "NativeModeTransitionTimedOut", ("SourceMode", source.Mode), ("TargetMode", target), ("OldPidDisappeared", oldPidDisappeared));
        return Complete(MsiClawModeTransitionStatus.TargetDeviceDidNotAppear, source.Mode, "Native mode re-enumeration did not complete.", true);
    }

    private SourceResolution ResolveSource(IReadOnlyList<ControllerDeviceInfo> devices, MsiClawPhysicalIdentity expectedIdentity)
    {
        var matching = devices.Where(d => d.Present && MsiClawPhysicalIdentity.From(d).StronglyMatches(expectedIdentity)).ToArray();
        if (matching.Length == 0) return new(MsiClawModeTransitionStatus.IdentityMismatch, MsiClawNativeMode.Other, null, null, "Current physical identity was not found.");
        var modes = matching.Select(d => d.ProductId switch { MsiClawHardware.XInputProductId => MsiClawNativeMode.XInput, MsiClawHardware.DirectInputProductId => MsiClawNativeMode.DirectInput, _ => MsiClawNativeMode.Other }).Distinct().ToArray();
        if (modes.Length != 1 || modes[0] == MsiClawNativeMode.Other) return new(MsiClawModeTransitionStatus.UnsupportedDevice, MsiClawNativeMode.Other, null, null, "Current native mode is unsupported or ambiguous.");
        var control = resolver.Resolve(devices, modes[0], expectedIdentity);
        if (control is null && modes[0] == MsiClawNativeMode.DirectInput)
        {
            var unverifiedA2vm230Candidates = matching.Count(MsiClawHardware.IsA2vm230ObservedControlEndpointCandidate);
            if (unverifiedA2vm230Candidates > 0)
                return new(MsiClawModeTransitionStatus.AmbiguousDevice, modes[0], matching[0].ProductId, null,
                    "A2vm230ControlEndpointUnverified");
        }
        return control is null
            ? new(MsiClawModeTransitionStatus.AmbiguousDevice, modes[0], matching[0].ProductId, null, "Source control HID was not uniquely resolved.")
            : new(MsiClawModeTransitionStatus.Succeeded, modes[0], control.Device.ProductId, control, "Source control HID resolved.");
    }

    private sealed record SourceResolution(MsiClawModeTransitionStatus Status, MsiClawNativeMode Mode, ushort? ProductId, MsiClawControlHidDevice? Control, string Reason);
}

internal sealed class UnavailableMsiClawModeWriter : IMsiClawModeWriter
{
    public Task<bool> WriteAsync(MsiClawControlHidDevice device, MsiClawNativeMode mode, CancellationToken cancellationToken) => Task.FromResult(false);
}
