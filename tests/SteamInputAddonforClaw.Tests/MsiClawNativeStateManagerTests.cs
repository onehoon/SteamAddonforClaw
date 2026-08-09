using System.Text.Json;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawNativeStateManagerTests
{
    [Fact]
    public void DeviceNativeStateSnapshot_RejectsInvalidEnvelope()
    {
        Assert.Throws<ArgumentException>(() => new DeviceNativeStateSnapshot(default, 1, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { })));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeviceNativeStateSnapshot(new HandheldDeviceId("msi.claw"), 0, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { })));
        Assert.Throws<ArgumentException>(() => new DeviceNativeStateSnapshot(new HandheldDeviceId("msi.claw"), 1, DateTimeOffset.UtcNow, default));
    }

    [Theory]
    [InlineData(0x1901, (int)MsiClawNativeMode.XInput)]
    [InlineData(0x1902, (int)MsiClawNativeMode.DirectInput)]
    [InlineData(0x1903, (int)MsiClawNativeMode.Other)]
    public void CaptureSnapshot_KnownIdentity_ReturnsDeviceNeutralSnapshot(int productId, int expectedMode)
    {
        var result = new MsiClawNativeStateManager(new Enumerator([Device((ushort)productId)])).CaptureSnapshot();
        Assert.Equal(NativeStateCaptureStatus.Success, result.Status);
        Assert.Equal(new HandheldDeviceId("msi.claw"), result.Snapshot!.DeviceId);
        Assert.Equal(1, result.Snapshot.FormatVersion);
        Assert.Equal((MsiClawNativeMode)expectedMode, result.Snapshot.Payload.Deserialize<MsiClawNativeStatePayload>()!.Mode);
    }

    [Fact]
    public void CaptureSnapshot_PreservesFailClosedIdentityRules()
    {
        var noDevice = new MsiClawNativeStateManager(new Enumerator([Device(0x1234, vendorId: 0x054C)])).CaptureSnapshot();
        Assert.Equal(NativeStateCaptureStatus.DeviceNotFound, noDevice.Status);
        var conflicting = new MsiClawNativeStateManager(new Enumerator([Device(0x1901, "A"), Device(0x1902, "B")])).CaptureSnapshot();
        Assert.Equal(NativeStateCaptureStatus.Indeterminate, conflicting.Status);
        var duplicateContainer = Guid.NewGuid();
        var duplicate = new MsiClawNativeStateManager(new Enumerator([Device(0x1902, "A", duplicateContainer), Device(0x1902, "B", duplicateContainer)])).CaptureSnapshot();
        Assert.Equal(NativeStateCaptureStatus.Success, duplicate.Status);
        Assert.Equal(NativeStateCaptureStatus.Failed, new MsiClawNativeStateManager(new ThrowingEnumerator()).CaptureSnapshot().Status);
    }

    [Fact]
    public async Task Restore_OnlyConfirmsAlreadyOriginalState()
    {
        var source = new MsiClawNativeStateManager(new Enumerator([Device(0x1902)]));
        var snapshot = source.CaptureSnapshot().Snapshot!;
        Assert.Equal(NativeStateRestoreStatus.Success, (await source.RestoreSnapshotAsync(snapshot, CancellationToken.None)).Status);
        var different = new MsiClawNativeStateManager(new Enumerator([Device(0x1901)]));
        Assert.Equal(NativeStateRestoreStatus.Unsupported, (await different.RestoreSnapshotAsync(snapshot, CancellationToken.None)).Status);
        var wrong = new DeviceNativeStateSnapshot(new HandheldDeviceId("other.device"), 1, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { }));
        Assert.Equal(NativeStateRestoreStatus.Failed, (await source.RestoreSnapshotAsync(wrong, CancellationToken.None)).Status);
        var unsupported = new DeviceNativeStateSnapshot(new HandheldDeviceId("msi.claw"), 2, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { }));
        Assert.Equal(NativeStateRestoreStatus.Unsupported, (await source.RestoreSnapshotAsync(unsupported, CancellationToken.None)).Status);
        var malformed = new DeviceNativeStateSnapshot(new HandheldDeviceId("msi.claw"), 1, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { Invalid = true }));
        Assert.Equal(NativeStateRestoreStatus.Failed, (await source.RestoreSnapshotAsync(malformed, CancellationToken.None)).Status);
    }

    private static ControllerDeviceInfo Device(ushort productId, string instanceId = "MSI\\DEVICE", Guid? container = null, ushort vendorId = 0x0DB0) =>
        new(instanceId, container, "MSI\\PARENT", ["MSI\\PARENT"], "USB", [$"USB\\VID_{vendorId:X4}&PID_{productId:X4}"], [], "HIDClass", null, null, vendorId, productId, true);
    private sealed class Enumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator { public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => devices; }
    private sealed class ThrowingEnumerator : IControllerDeviceEnumerator { public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => throw new InvalidOperationException(); }
}
