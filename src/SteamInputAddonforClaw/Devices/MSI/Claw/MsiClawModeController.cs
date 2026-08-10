using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed record MsiClawControlHidDevice(ControllerDeviceInfo Device, ushort UsagePage, ushort Usage);
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
        var pid = mode == MsiClawNativeMode.XInput ? MsiClawHardware.XInputProductId : mode == MsiClawNativeMode.DirectInput ? MsiClawHardware.DirectInputProductId : (ushort)0;
        var usagePage = mode == MsiClawNativeMode.XInput ? (ushort)0xFFA0 : (ushort)0xFFF0;
        var usage = mode == MsiClawNativeMode.XInput ? (ushort)0x0001 : (ushort)0x0040;
        var candidates = devices.Where(d => d.Present && d.VendorId == MsiClawHardware.VendorId && d.ProductId == pid && MsiClawPhysicalIdentity.From(d).StronglyMatches(expectedIdentity))
            .Where(d => d.UsagePage == usagePage && d.Usage == usage).ToArray();
        return candidates.Length == 1 ? new(candidates[0], usagePage, usage) : null;
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
    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(50);
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);

    public async Task<MsiClawModeTransitionResult> SwitchModeAsync(MsiClawNativeMode target, MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken)
    {
        var started = _now();
        var devices = deviceEnumerator.EnumeratePresentDevices();
        var from = devices.FirstOrDefault(d => d.Present && MsiClawPhysicalIdentity.From(d).StronglyMatches(expectedIdentity));
        if (from is null) return Result(MsiClawModeTransitionStatus.IdentityMismatch, target, expectedIdentity, started, "Current physical identity was not found.");
        var fromMode = from.ProductId == MsiClawHardware.XInputProductId ? MsiClawNativeMode.XInput : from.ProductId == MsiClawHardware.DirectInputProductId ? MsiClawNativeMode.DirectInput : MsiClawNativeMode.Other;
        var control = resolver.Resolve(devices, fromMode, expectedIdentity);
        if (control is null) return Result(MsiClawModeTransitionStatus.UnsupportedDevice, target, expectedIdentity, started, "Control HID was not uniquely resolved.", fromMode, from.ProductId);
        if (!await writer.WriteAsync(control, target, cancellationToken).ConfigureAwait(false)) return Result(MsiClawModeTransitionStatus.WriteFailed, target, expectedIdentity, started, "Control HID write failed.", fromMode, from.ProductId);
        var oldPid = from.ProductId; var targetPid = target == MsiClawNativeMode.XInput ? MsiClawHardware.XInputProductId : MsiClawHardware.DirectInputProductId;
        var deadline = started + _timeout; var oldGone = false; var targetSeen = false; MsiClawPhysicalIdentity? observed = null;
        while (_now() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = deviceEnumerator.EnumeratePresentDevices().Where(d => d.Present && MsiClawPhysicalIdentity.From(d).StronglyMatches(expectedIdentity)).ToArray();
            oldGone = !current.Any(d => d.ProductId == oldPid);
            var targets = current.Where(d => d.ProductId == targetPid).ToArray(); targetSeen = targets.Length == 1; observed = targetSeen ? MsiClawPhysicalIdentity.From(targets[0]) : null;
            if (oldGone && targetSeen && expectedIdentity.StronglyMatches(observed!)) return Result(MsiClawModeTransitionStatus.Succeeded, target, expectedIdentity, started, "Native mode transition verified.", fromMode, oldPid, true, true, true);
            await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
        }
        return Result(targetSeen ? MsiClawModeTransitionStatus.OldDeviceDidNotDisappear : MsiClawModeTransitionStatus.TargetDeviceDidNotAppear, target, expectedIdentity, started, "Native mode re-enumeration did not complete.", fromMode, oldPid, true, oldGone, targetSeen);
    }

    private MsiClawModeTransitionResult Result(MsiClawModeTransitionStatus status, MsiClawNativeMode target, MsiClawPhysicalIdentity identity, DateTimeOffset started, string reason, MsiClawNativeMode from = MsiClawNativeMode.Other, ushort? fromPid = null, bool write = false, bool oldGone = false, bool targetSeen = false) => new(status, from, target, fromPid, target == MsiClawNativeMode.XInput ? MsiClawHardware.XInputProductId : MsiClawHardware.DirectInputProductId, write, oldGone, targetSeen, status == MsiClawModeTransitionStatus.Succeeded, (long)(_now() - started).TotalMilliseconds, reason);
}

internal sealed class UnavailableMsiClawModeWriter : IMsiClawModeWriter
{
    public Task<bool> WriteAsync(MsiClawControlHidDevice device, MsiClawNativeMode mode, CancellationToken cancellationToken) => Task.FromResult(false);
}
