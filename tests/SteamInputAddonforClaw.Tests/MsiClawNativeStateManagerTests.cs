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
    public async Task Restore_Allows_reenumerated_identity_with_different_parent()
    {
        var container = Guid.NewGuid();
        var devices = new MutableModeEnumerator(container) { Parent = "USB\\OTHER_PARENT" };
        devices.Mode = MsiClawNativeMode.DirectInput;
        var manager = new MsiClawNativeStateManager(devices, new ApplyingModeController(devices));
        var legacySnapshot = Snapshot(new(MsiClawNativeMode.XInput, "HID\\LEGACY", "USB\\EXPECTED_PARENT", container,
            MsiClawHardware.XInputProductId, MsiClawIdentityConfidence.Strong));

        var restored = await manager.RestoreSnapshotAsync(legacySnapshot, CancellationToken.None);

        Assert.Equal(NativeStateRestoreStatus.Success, restored.Status);
        Assert.Equal(MsiClawNativeMode.XInput, devices.Mode);
    }

    [Fact]
    public async Task Restore_Allows_reenumerated_sentinel_identity()
    {
        var sentinel = Guid.Parse("00000000-0000-0000-ffff-ffffffffffff");
        var devices = new MutableModeEnumerator(sentinel);
        devices.Mode = MsiClawNativeMode.DirectInput;
        var manager = new MsiClawNativeStateManager(devices, new ApplyingModeController(devices));
        var legacySnapshot = Snapshot(new(MsiClawNativeMode.XInput, "HID\\LEGACY", "USB\\PARENT", sentinel,
            MsiClawHardware.XInputProductId, MsiClawIdentityConfidence.Strong));

        var restored = await manager.RestoreSnapshotAsync(legacySnapshot, CancellationToken.None);

        Assert.Equal(NativeStateRestoreStatus.Success, restored.Status);
        Assert.Equal(MsiClawNativeMode.XInput, devices.Mode);
    }

    [Fact]
    public async Task Restore_XInput_root_A_to_DirectInput_root_B_to_XInput_root_C_succeeds()
    {
        var container = Guid.Parse("00000000-0000-0000-ffff-ffffffffffff");
        var devices = new MutableModeEnumerator(container) { Mode = MsiClawNativeMode.DirectInput, Root = "ROOT_B" };
        var controller = new ApplyingModeController(devices) { RestoredRoot = "ROOT_C" };
        var manager = new MsiClawNativeStateManager(devices, controller);
        var original = Snapshot(new(MsiClawNativeMode.XInput, "USB\\VID_0DB0&PID_1901&MI_00\\A", "PARENT_A", container,
            MsiClawHardware.XInputProductId, MsiClawIdentityConfidence.Strong, "USB\\VID_0DB0\\ROOT_A"));

        var restored = await manager.RestoreSnapshotAsync(original, CancellationToken.None);

        Assert.Equal(NativeStateRestoreStatus.Success, restored.Status);
        Assert.Equal(MsiClawNativeMode.XInput, devices.Mode);
        Assert.Equal("ROOT_C", devices.Root);
        Assert.NotNull(controller.SourceIdentity);
        Assert.Equal("USB\\VID_0DB0\\ROOT_B", controller.SourceIdentity!.PhysicalDeviceKey);
    }

    [Fact]
    public async Task Restore_original_DirectInput_from_current_XInput_succeeds()
    {
        var devices = new MutableModeEnumerator(Guid.NewGuid()) { Mode = MsiClawNativeMode.XInput, Root = "ROOT_A" };
        var manager = new MsiClawNativeStateManager(devices, new ApplyingModeController(devices));
        var original = Snapshot(new(MsiClawNativeMode.DirectInput, "HID\\ORIGINAL", "PARENT_B", Guid.NewGuid(),
            MsiClawHardware.DirectInputProductId, MsiClawIdentityConfidence.Strong));

        var restored = await manager.RestoreSnapshotAsync(original, CancellationToken.None);

        Assert.Equal(NativeStateRestoreStatus.Success, restored.Status);
        Assert.Equal(MsiClawNativeMode.DirectInput, devices.Mode);
    }

    [Fact]
    public async Task Restore_waits_for_mixed_topology_to_settle_before_switching()
    {
        var stale = Topology(0x1901, "ROOT_A");
        var current = Topology(0x1902, "ROOT_B");
        var controller = new RecordingSuccessModeController();
        var manager = new MsiClawNativeStateManager(new SequenceEnumerator([stale, current], [stale, current], [current], [Topology(0x1901, "ROOT_C")]), controller,
            TimeSpan.FromSeconds(1), TimeSpan.Zero);

        var restored = await manager.RestoreSnapshotAsync(Snapshot(new(MsiClawNativeMode.XInput, "HID\\ORIGINAL", "PARENT_A", Guid.NewGuid(), MsiClawHardware.XInputProductId, MsiClawIdentityConfidence.Strong)), CancellationToken.None);

        Assert.Equal(NativeStateRestoreStatus.Success, restored.Status);
        Assert.Equal(1, controller.CallCount);
        Assert.Equal(MsiClawHardware.DirectInputProductId, controller.SourceIdentity!.ProductId);
    }

    [Fact]
    public async Task Restore_verification_waits_for_mixed_topology_to_settle_after_switch()
    {
        var current = Topology(0x1902, "ROOT_B");
        var stale = Topology(0x1902, "ROOT_B");
        var restoredTarget = Topology(0x1901, "ROOT_C");
        var manager = new MsiClawNativeStateManager(new SequenceEnumerator([current], [stale, restoredTarget], [stale, restoredTarget], [restoredTarget]), new RecordingSuccessModeController(),
            TimeSpan.FromSeconds(1), TimeSpan.Zero);

        var restored = await manager.RestoreSnapshotAsync(Snapshot(new MsiClawNativeStatePayload(MsiClawNativeMode.XInput, "HID\\ORIGINAL", "PARENT_A", Guid.NewGuid(), MsiClawHardware.XInputProductId, MsiClawIdentityConfidence.Strong)), CancellationToken.None);

        Assert.Equal(NativeStateRestoreStatus.Success, restored.Status);
    }

    [Fact]
    public async Task Restore_fails_closed_when_mixed_topology_never_settles()
    {
        var mixed = new[] { Topology(0x1901, "ROOT_A"), Topology(0x1902, "ROOT_B") };
        var controller = new RecordingSuccessModeController();
        var manager = new MsiClawNativeStateManager(new SequenceEnumerator(mixed), controller, TimeSpan.Zero, TimeSpan.Zero);

        var restored = await manager.RestoreSnapshotAsync(Snapshot(new MsiClawNativeStatePayload(MsiClawNativeMode.XInput, "HID\\ORIGINAL", "PARENT_A", Guid.NewGuid(), MsiClawHardware.XInputProductId, MsiClawIdentityConfidence.Strong)), CancellationToken.None);

        Assert.Equal(NativeStateRestoreStatus.Indeterminate, restored.Status);
        Assert.Equal(0, controller.CallCount);
    }

    [Fact]
    public async Task Restore_waits_when_mixed_modes_share_one_fallback_logical_identity()
    {
        var container = Guid.NewGuid();
        var xinput = Device(0x1901, "HID\\XINPUT", container, parentInstanceId: "PARENT_SHARED");
        var directInput = Device(0x1902, "HID\\DIRECT", container, parentInstanceId: "PARENT_SHARED");
        var controller = new RecordingSuccessModeController();
        var manager = new MsiClawNativeStateManager(new SequenceEnumerator([xinput, directInput], [xinput, directInput], [directInput], [Topology(0x1901, "ROOT_C")]), controller,
            TimeSpan.FromSeconds(1), TimeSpan.Zero);

        var restored = await manager.RestoreSnapshotAsync(Snapshot(new MsiClawNativeStatePayload(MsiClawNativeMode.XInput, "HID\\ORIGINAL", "PARENT_A", Guid.NewGuid(), MsiClawHardware.XInputProductId, MsiClawIdentityConfidence.Strong)), CancellationToken.None);

        Assert.Equal(NativeStateRestoreStatus.Success, restored.Status);
        Assert.Equal(1, controller.CallCount);
    }

    [Fact]
    public async Task Restore_rejects_independent_same_mode_controllers_without_settling()
    {
        var controller = new RecordingSuccessModeController();
        var manager = new MsiClawNativeStateManager(new SequenceEnumerator([Topology(0x1902, "ROOT_A"), Topology(0x1902, "ROOT_B")]), controller,
            TimeSpan.FromSeconds(1), TimeSpan.Zero);

        var restored = await manager.RestoreSnapshotAsync(Snapshot(new MsiClawNativeStatePayload(MsiClawNativeMode.XInput, "HID\\ORIGINAL", "PARENT_A", Guid.NewGuid(), MsiClawHardware.XInputProductId, MsiClawIdentityConfidence.Strong)), CancellationToken.None);

        Assert.Equal(NativeStateRestoreStatus.Indeterminate, restored.Status);
        Assert.Equal(0, controller.CallCount);
    }

    private static ControllerDeviceInfo Device(ushort productId, string instanceId = "MSI\\DEVICE", Guid? container = null, ushort vendorId = 0x0DB0, string? parentInstanceId = "MSI\\PARENT", IReadOnlyList<string>? ancestors = null) =>
        new(instanceId, container, parentInstanceId, ancestors ?? (parentInstanceId is null ? [] : [parentInstanceId]), "USB", [$"USB\\VID_{vendorId:X4}&PID_{productId:X4}"], [], "HIDClass", null, null, vendorId, productId, true);
    private static DeviceNativeStateSnapshot Snapshot(MsiClawNativeStatePayload payload) =>
        new(new HandheldDeviceId("msi.claw"), 1, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(payload));
    private static ControllerDeviceInfo Topology(ushort productId, string root) =>
        Device(productId, $"USB\\VID_0DB0&PID_{productId:X4}&MI_00\\{root}", Guid.Parse("00000000-0000-0000-ffff-ffffffffffff"), parentInstanceId: $"PARENT_{root}", ancestors: [$"USB\\VID_0DB0&PID_{productId:X4}\\{root}"]);
    private sealed class Enumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator { public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => devices; }
    private sealed class SequenceEnumerator(params IReadOnlyList<ControllerDeviceInfo>[] states) : IControllerDeviceEnumerator
    { private int _index; public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => states[Math.Min(_index++, states.Length - 1)]; }
    private sealed class MutableModeEnumerator(Guid container) : IControllerDeviceEnumerator
    {
        public MsiClawNativeMode Mode { get; set; } = MsiClawNativeMode.XInput;
        public string Parent { get; set; } = "USB\\PARENT";
        public string Root { get; set; } = "CLAW_A";
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
        {
            var productId = Mode == MsiClawNativeMode.XInput ? MsiClawHardware.XInputProductId : MsiClawHardware.DirectInputProductId;
            var root = $"USB\\VID_0DB0&PID_{productId:X4}\\{Root}";
            return [Device(productId, $"USB\\VID_0DB0&PID_{productId:X4}&MI_00\\A", container, parentInstanceId: Parent, ancestors: [root])];
        }
    }
    private sealed class ApplyingModeController(MutableModeEnumerator devices) : IMsiClawModeController
    {
        public string? RestoredRoot { get; set; }
        public MsiClawPhysicalIdentity? SourceIdentity { get; private set; }
        public Task<MsiClawModeTransitionResult> SwitchModeAsync(MsiClawNativeMode target, MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken)
        {
            SourceIdentity = expectedIdentity;
            devices.Mode = target;
            if (RestoredRoot is not null) devices.Root = RestoredRoot;
            return Task.FromResult(new MsiClawModeTransitionResult(MsiClawModeTransitionStatus.Succeeded,
                target == MsiClawNativeMode.XInput ? MsiClawNativeMode.DirectInput : MsiClawNativeMode.XInput,
                target, null, target == MsiClawNativeMode.XInput ? MsiClawHardware.XInputProductId : MsiClawHardware.DirectInputProductId,
                true, true, true, true, true, 1, "test"));
        }
    }
    private sealed class ThrowingEnumerator : IControllerDeviceEnumerator { public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => throw new InvalidOperationException(); }
    private sealed class RecordingSuccessModeController : IMsiClawModeController
    {
        public int CallCount { get; private set; }
        public MsiClawPhysicalIdentity? SourceIdentity { get; private set; }
        public Task<MsiClawModeTransitionResult> SwitchModeAsync(MsiClawNativeMode target, MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken)
        {
            CallCount++; SourceIdentity = expectedIdentity;
            return Task.FromResult(new MsiClawModeTransitionResult(MsiClawModeTransitionStatus.Succeeded, MsiClawNativeMode.DirectInput, target, MsiClawHardware.DirectInputProductId,
                target == MsiClawNativeMode.XInput ? MsiClawHardware.XInputProductId : MsiClawHardware.DirectInputProductId, true, true, true, true, true, 1, "test"));
        }
    }
}
