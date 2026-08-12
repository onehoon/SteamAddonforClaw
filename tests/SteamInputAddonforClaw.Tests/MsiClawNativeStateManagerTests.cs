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
    public void CaptureSnapshot_MultipleLogicalControllers_IsIndeterminate()
    {
        var result = new MsiClawNativeStateManager(new Enumerator(
        [
            Device(0x1902, "MSI\\FIRST", Guid.NewGuid()),
            Device(0x1902, "MSI\\SECOND", Guid.NewGuid())
        ])).CaptureSnapshot();
        Assert.Equal(NativeStateCaptureStatus.Indeterminate, result.Status);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("00000000-0000-0000-ffff-ffffffffffff")]
    public void CaptureSnapshot_UnusableContainerId_UsesParentFallback(string containerId)
    {
        var result = new MsiClawNativeStateManager(new Enumerator(
        [
            Device(0x1902, "MSI\\FIRST", Guid.Parse(containerId), parentInstanceId: "MSI\\PARENT_A"),
            Device(0x1902, "MSI\\SECOND", Guid.Parse(containerId), parentInstanceId: "MSI\\PARENT_B")
        ])).CaptureSnapshot();
        Assert.Equal(NativeStateCaptureStatus.Indeterminate, result.Status);
    }

    [Fact]
    public void CaptureSnapshot_CompositeSentinelContainer_CollapsesByPhysicalRoot()
    {
        var devices = new[]
        {
            Device(0x1901, "USB\\VID_0DB0&PID_1901&MI_00\\A", Guid.Parse("00000000-0000-0000-ffff-ffffffffffff"), parentInstanceId: "PARENT_00", ancestors: ["USB\\VID_0DB0&PID_1901\\CLAW_A"]),
            Device(0x1901, "USB\\VID_0DB0&PID_1901&MI_01\\A", Guid.Parse("00000000-0000-0000-ffff-ffffffffffff"), parentInstanceId: "PARENT_01", ancestors: ["USB\\VID_0DB0&PID_1901\\CLAW_A"]),
            Device(0x1901, "HID\\VID_0DB0&PID_1901\\A", Guid.Parse("00000000-0000-0000-ffff-ffffffffffff"), parentInstanceId: "PARENT_HID", ancestors: ["USB\\VID_0DB0&PID_1901\\CLAW_A"])
        };
        var result = new MsiClawNativeStateManager(new Enumerator(devices)).CaptureSnapshot();
        Assert.Equal(NativeStateCaptureStatus.Success, result.Status);
        Assert.Equal(MsiClawNativeMode.XInput, result.Snapshot!.Payload.Deserialize<MsiClawNativeStatePayload>()!.Mode);
    }

    [Fact]
    public void CaptureSnapshot_RootAndCompositeChildren_CollapseByRootInstance()
    {
        var sentinel = Guid.Parse("00000000-0000-0000-ffff-ffffffffffff");
        var devices = new[]
        {
            Device(0x1901, "USB\\VID_0DB0&PID_1901\\CLAW_A", sentinel, parentInstanceId: "USB_PARENT"),
            Device(0x1901, "USB\\VID_0DB0&PID_1901&MI_00\\A", sentinel, parentInstanceId: "PARENT_00", ancestors: ["USB\\VID_0DB0&PID_1901\\CLAW_A"]),
            Device(0x1901, "HID\\VID_0DB0&PID_1901\\A", sentinel, parentInstanceId: "PARENT_HID", ancestors: ["USB\\VID_0DB0&PID_1901&MI_00\\A", "USB\\VID_0DB0&PID_1901\\CLAW_A"])
        };
        var result = new MsiClawNativeStateManager(new Enumerator(devices)).CaptureSnapshot();
        Assert.Equal(NativeStateCaptureStatus.Success, result.Status);
        Assert.Equal(MsiClawNativeMode.XInput, result.Snapshot!.Payload.Deserialize<MsiClawNativeStatePayload>()!.Mode);
    }

    [Fact]
    public void CaptureSnapshot_TwoPhysicalRoots_RemainsIndeterminate()
    {
        var result = new MsiClawNativeStateManager(new Enumerator([
            Device(0x1901, "USB\\VID_0DB0&PID_1901&MI_00\\A", Guid.Empty, ancestors: ["USB\\VID_0DB0&PID_1901\\CLAW_A"]),
            Device(0x1901, "USB\\VID_0DB0&PID_1901&MI_00\\B", Guid.Empty, ancestors: ["USB\\VID_0DB0&PID_1901\\CLAW_B"])
        ])).CaptureSnapshot();
        Assert.Equal(NativeStateCaptureStatus.Indeterminate, result.Status);
    }

    [Fact]
    public void CaptureSnapshot_WithoutContainerOrParent_UsesInstanceFallback()
    {
        var result = new MsiClawNativeStateManager(new Enumerator(
        [
            Device(0x1902, "MSI\\FIRST", Guid.Empty, parentInstanceId: null),
            Device(0x1902, "MSI\\SECOND", Guid.Empty, parentInstanceId: null)
        ])).CaptureSnapshot();
        Assert.Equal(NativeStateCaptureStatus.Indeterminate, result.Status);
    }

    [Fact]
    public void CaptureSnapshot_UnrelatedDevicesAreIgnored()
    {
        var result = new MsiClawNativeStateManager(new Enumerator([Device(0x028E, vendorId: 0x045E), Device(0x1903)])).CaptureSnapshot();
        Assert.Equal(NativeStateCaptureStatus.Success, result.Status);
        Assert.Equal(MsiClawNativeMode.Other, result.Snapshot!.Payload.Deserialize<MsiClawNativeStatePayload>()!.Mode);
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

    [Fact]
    public async Task Restore_AllowsLegacyUsableContainerSnapshotWithoutPhysicalKey()
    {
        var container = Guid.NewGuid();
        var devices = new MutableModeEnumerator(container);
        devices.Mode = MsiClawNativeMode.DirectInput;
        var manager = new MsiClawNativeStateManager(devices, new ApplyingModeController(devices));
        var legacySnapshot = Snapshot(new(MsiClawNativeMode.XInput, "HID\\LEGACY", "USB\\PARENT", container,
            MsiClawHardware.XInputProductId, MsiClawIdentityConfidence.Strong));

        var restored = await manager.RestoreSnapshotAsync(legacySnapshot, CancellationToken.None);

        Assert.Equal(NativeStateRestoreStatus.Success, restored.Status);
        Assert.Equal(MsiClawNativeMode.XInput, devices.Mode);
    }

    [Fact]
    public async Task Restore_RejectsLegacyUsableContainerSnapshotWithDifferentParent()
    {
        var container = Guid.NewGuid();
        var devices = new MutableModeEnumerator(container) { Parent = "USB\\OTHER_PARENT" };
        devices.Mode = MsiClawNativeMode.DirectInput;
        var manager = new MsiClawNativeStateManager(devices, new ApplyingModeController(devices));
        var legacySnapshot = Snapshot(new(MsiClawNativeMode.XInput, "HID\\LEGACY", "USB\\EXPECTED_PARENT", container,
            MsiClawHardware.XInputProductId, MsiClawIdentityConfidence.Strong));

        var restored = await manager.RestoreSnapshotAsync(legacySnapshot, CancellationToken.None);

        Assert.Equal(NativeStateRestoreStatus.Indeterminate, restored.Status);
        Assert.Equal("PhysicalIdentityMismatch", restored.Reason);
        Assert.Equal(MsiClawNativeMode.DirectInput, devices.Mode);
    }

    [Fact]
    public async Task Restore_RejectsLegacySentinelSnapshotWithoutPhysicalKey()
    {
        var sentinel = Guid.Parse("00000000-0000-0000-ffff-ffffffffffff");
        var devices = new MutableModeEnumerator(sentinel);
        devices.Mode = MsiClawNativeMode.DirectInput;
        var manager = new MsiClawNativeStateManager(devices, new ApplyingModeController(devices));
        var legacySnapshot = Snapshot(new(MsiClawNativeMode.XInput, "HID\\LEGACY", "USB\\PARENT", sentinel,
            MsiClawHardware.XInputProductId, MsiClawIdentityConfidence.Strong));

        var restored = await manager.RestoreSnapshotAsync(legacySnapshot, CancellationToken.None);

        Assert.Equal(NativeStateRestoreStatus.Indeterminate, restored.Status);
        Assert.Equal("PhysicalIdentityMismatch", restored.Reason);
        Assert.Equal(MsiClawNativeMode.DirectInput, devices.Mode);
    }

    private static ControllerDeviceInfo Device(ushort productId, string instanceId = "MSI\\DEVICE", Guid? container = null, ushort vendorId = 0x0DB0, string? parentInstanceId = "MSI\\PARENT", IReadOnlyList<string>? ancestors = null) =>
        new(instanceId, container, parentInstanceId, ancestors ?? (parentInstanceId is null ? [] : [parentInstanceId]), "USB", [$"USB\\VID_{vendorId:X4}&PID_{productId:X4}"], [], "HIDClass", null, null, vendorId, productId, true);
    private static DeviceNativeStateSnapshot Snapshot(MsiClawNativeStatePayload payload) =>
        new(new HandheldDeviceId("msi.claw"), 1, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(payload));
    private sealed class Enumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator { public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => devices; }
    private sealed class MutableModeEnumerator(Guid container) : IControllerDeviceEnumerator
    {
        public MsiClawNativeMode Mode { get; set; } = MsiClawNativeMode.XInput;
        public string Parent { get; set; } = "USB\\PARENT";
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
        {
            var productId = Mode == MsiClawNativeMode.XInput ? MsiClawHardware.XInputProductId : MsiClawHardware.DirectInputProductId;
            var root = $"USB\\VID_0DB0&PID_{productId:X4}\\CLAW_A";
            return [Device(productId, $"USB\\VID_0DB0&PID_{productId:X4}&MI_00\\A", container, parentInstanceId: Parent, ancestors: [root])];
        }
    }
    private sealed class ApplyingModeController(MutableModeEnumerator devices) : IMsiClawModeController
    {
        public Task<MsiClawModeTransitionResult> SwitchModeAsync(MsiClawNativeMode target, MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken)
        {
            devices.Mode = target;
            return Task.FromResult(new MsiClawModeTransitionResult(MsiClawModeTransitionStatus.Succeeded,
                target == MsiClawNativeMode.XInput ? MsiClawNativeMode.DirectInput : MsiClawNativeMode.XInput,
                target, null, target == MsiClawNativeMode.XInput ? MsiClawHardware.XInputProductId : MsiClawHardware.DirectInputProductId,
                true, true, true, true, 1, "test"));
        }
    }
    private sealed class ThrowingEnumerator : IControllerDeviceEnumerator { public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => throw new InvalidOperationException(); }
}
