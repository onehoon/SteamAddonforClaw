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
    Func<DateTimeOffset>? now = null) : IMsiClawModeController
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(5);
    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);

    public async Task<MsiClawModeTransitionResult> SwitchModeAsync(MsiClawNativeMode target, MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken)
    {
        var started = _now();
        if (!MsiClawModeTopology.TryGet(target, out var targetTopology))
            return Result(MsiClawModeTransitionStatus.UnsupportedDevice, MsiClawNativeMode.Other, target, started, "Unsupported target native mode.");

        var devices = deviceEnumerator.EnumeratePresentDevices();
        var source = ResolveSource(devices, expectedIdentity);
        if (source.Status is not MsiClawModeTransitionStatus.Succeeded)
        {
            AppLog.Debug("NativeMode", "NativeModeSourceAmbiguous", ("Reason", source.Reason), ("TargetMode", target));
            return Result(source.Status, source.Mode, target, started, source.Reason, source.ProductId);
        }
        AppLog.Debug("NativeMode", "NativeModeSourceResolved", ("SourceMode", source.Mode), ("SourcePID", source.ProductId), ("SourceIdentityConfidence", expectedIdentity.Confidence));

        var deadline = started + _timeout;
        MsiClawControlHidDevice? control = source.Control;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await writer.WriteAsync(control!, target, cancellationToken).ConfigureAwait(false))
            {
                AppLog.Debug("NativeMode", "NativeModeCommandWriteSucceeded", ("TargetMode", target));
                break;
            }
            if (_now() >= deadline)
                return Result(MsiClawModeTransitionStatus.WriteFailed, source.Mode, target, started, "Control HID write failed.", source.ProductId, false, false, false, true);
            await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
            devices = deviceEnumerator.EnumeratePresentDevices();
            source = ResolveSource(devices, expectedIdentity);
            if (source.Status is not MsiClawModeTransitionStatus.Succeeded)
                return Result(source.Status, source.Mode, target, started, source.Reason, source.ProductId);
            control = source.Control;
        }

        var oldPid = source.ProductId; var oldGone = false; var targetSeen = false;
        while (_now() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = deviceEnumerator.EnumeratePresentDevices();
            oldGone = !current.Any(d => d.Present && d.VendorId == MsiClawHardware.VendorId && d.ProductId == oldPid);
            var targets = current.Where(d => d.Present && d.VendorId == MsiClawHardware.VendorId && d.ProductId == targetTopology.ProductId && d.UsagePage == targetTopology.UsagePage && d.Usage == targetTopology.Usage).ToArray();
            var targetGroups = targets.GroupBy(MsiClawLogicalIdentity.GetLogicalKey, StringComparer.OrdinalIgnoreCase).ToArray();
            targetSeen = targetGroups.Length > 0;
            if (targetGroups.Length == 1)
            {
                var observed = targetGroups[0].First();
                AppLog.Debug("NativeMode", "NativeModeTargetObserved", ("TargetPID", targetTopology.ProductId), ("TargetIdentityConfidence", MsiClawPhysicalIdentity.From(observed).Confidence), ("CrossModeIdentityChanged", !expectedIdentity.StronglyMatches(MsiClawPhysicalIdentity.From(observed))));
                AppLog.Debug("NativeMode", "NativeModeTransitionSucceeded", ("SourceMode", source.Mode), ("TargetMode", target), ("OldPidDisappeared", oldGone));
                return Result(MsiClawModeTransitionStatus.Succeeded, source.Mode, target, started, "Native mode transition verified.", oldPid, true, oldGone, true, true, true);
            }
            if (targetGroups.Length > 1)
            {
                AppLog.Debug("NativeMode", "NativeModeTargetAmbiguous", ("TargetMode", target), ("CandidateCount", targets.Length), ("LogicalCandidateCount", targetGroups.Length));
                return Result(MsiClawModeTransitionStatus.AmbiguousDevice, source.Mode, target, started, "Target control HID was ambiguous.", oldPid, true, oldGone, true, true);
            }
            await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
        }
        AppLog.Debug("NativeMode", "NativeModeTransitionTimedOut", ("SourceMode", source.Mode), ("TargetMode", target), ("OldPidDisappeared", oldGone));
        return Result(MsiClawModeTransitionStatus.TargetDeviceDidNotAppear, source.Mode, target, started, "Native mode re-enumeration did not complete.", oldPid, true, oldGone, targetSeen, true);
    }

    private SourceResolution ResolveSource(IReadOnlyList<ControllerDeviceInfo> devices, MsiClawPhysicalIdentity expectedIdentity)
    {
        var matching = devices.Where(d => d.Present && MsiClawPhysicalIdentity.From(d).StronglyMatches(expectedIdentity)).ToArray();
        if (matching.Length == 0) return new(MsiClawModeTransitionStatus.IdentityMismatch, MsiClawNativeMode.Other, null, null, "Current physical identity was not found.");
        var modes = matching.Select(d => d.ProductId switch { MsiClawHardware.XInputProductId => MsiClawNativeMode.XInput, MsiClawHardware.DirectInputProductId => MsiClawNativeMode.DirectInput, _ => MsiClawNativeMode.Other }).Distinct().ToArray();
        if (modes.Length != 1 || modes[0] == MsiClawNativeMode.Other) return new(MsiClawModeTransitionStatus.UnsupportedDevice, MsiClawNativeMode.Other, null, null, "Current native mode is unsupported or ambiguous.");
        var control = resolver.Resolve(devices, modes[0], expectedIdentity);
        return control is null
            ? new(MsiClawModeTransitionStatus.AmbiguousDevice, modes[0], matching[0].ProductId, null, "Source control HID was not uniquely resolved.")
            : new(MsiClawModeTransitionStatus.Succeeded, modes[0], control.Device.ProductId, control, "Source control HID resolved.");
    }

    private MsiClawModeTransitionResult Result(MsiClawModeTransitionStatus status, MsiClawNativeMode from, MsiClawNativeMode target, DateTimeOffset started, string reason, ushort? fromPid = null, bool write = false, bool oldGone = false, bool targetSeen = false, bool sourceVerified = false, bool targetVerified = false) => new(status, from, target, fromPid, MsiClawModeTopology.TryGet(target, out var topology) ? topology.ProductId : null, write, oldGone, targetSeen, sourceVerified, targetVerified, (long)(_now() - started).TotalMilliseconds, reason);

    private sealed record SourceResolution(MsiClawModeTransitionStatus Status, MsiClawNativeMode Mode, ushort? ProductId, MsiClawControlHidDevice? Control, string Reason);
}

internal sealed class UnavailableMsiClawModeWriter : IMsiClawModeWriter
{
    public Task<bool> WriteAsync(MsiClawControlHidDevice device, MsiClawNativeMode mode, CancellationToken cancellationToken) => Task.FromResult(false);
}
