using SteamInputAddonforClaw.Controllers;
using SteamInputAddonforClaw.Controllers.Detection;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiControllerModeManagerTests
{
    [Theory]
    [InlineData(0x1901, (int)MsiControllerNativeMode.XInput)]
    [InlineData(0x1902, (int)MsiControllerNativeMode.DirectInput)]
    [InlineData(0x1903, (int)MsiControllerNativeMode.Other)]
    public void CaptureSnapshot_KnownIdentity_ReturnsExactMode(int productId, int expectedMode)
    {
        var container = Guid.NewGuid();
        var result = new MsiControllerModeManager(new Enumerator([Device((ushort)productId, "MSI\\DEVICE", container)])).CaptureSnapshot();
        Assert.Equal(MsiControllerSnapshotStatus.Success, result.Status);
        Assert.True(result.AllowsMutation);
        Assert.Equal((MsiControllerNativeMode)expectedMode, result.Snapshot!.Mode);
        Assert.Equal("MSI\\DEVICE", result.Snapshot.InstanceId);
        Assert.Equal("MSI\\PARENT", result.Snapshot.ParentInstanceId);
        Assert.Equal(container, result.Snapshot.ContainerId);
        Assert.Equal((ushort)productId, result.Snapshot.ProductId);
    }

    [Fact]
    public void CaptureSnapshot_NoMsiDevice_IsFailSafe()
    {
        var result = new MsiControllerModeManager(new Enumerator([Device(0x1234, vendorId: 0x054C)])).CaptureSnapshot();
        Assert.Equal(MsiControllerSnapshotStatus.DeviceNotFound, result.Status);
        Assert.False(result.AllowsMutation);
    }

    [Fact]
    public void CaptureSnapshot_ConflictingModes_IsIndeterminate()
    {
        var result = new MsiControllerModeManager(new Enumerator([Device(0x1901, "A"), Device(0x1902, "B")])).CaptureSnapshot();
        Assert.Equal(MsiControllerSnapshotStatus.Indeterminate, result.Status);
        Assert.False(result.AllowsMutation);
    }

    [Fact]
    public void CaptureSnapshot_DuplicateInterfacesForLogicalDevice_DoesNotConflict()
    {
        var container = Guid.NewGuid();
        var result = new MsiControllerModeManager(new Enumerator([Device(0x1902, "A", container), Device(0x1902, "B", container)])).CaptureSnapshot();
        Assert.Equal(MsiControllerSnapshotStatus.Success, result.Status);
        Assert.Equal(MsiControllerNativeMode.DirectInput, result.Snapshot!.Mode);
    }

    [Fact]
    public void CaptureSnapshot_SameModeForMultipleLogicalDevices_IsIndeterminate()
    {
        var result = new MsiControllerModeManager(new Enumerator(
        [
            Device(0x1902, "MSI\\FIRST", Guid.NewGuid()),
            Device(0x1902, "MSI\\SECOND", Guid.NewGuid())
        ])).CaptureSnapshot();

        Assert.Equal(MsiControllerSnapshotStatus.Indeterminate, result.Status);
        Assert.False(result.AllowsMutation);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("00000000-0000-0000-ffff-ffffffffffff")]
    public void CaptureSnapshot_UnusableContainerId_UsesParentIdentity(string containerId)
    {
        var container = Guid.Parse(containerId);
        var result = new MsiControllerModeManager(new Enumerator(
        [
            Device(0x1902, "MSI\\FIRST", container, parentInstanceId: "MSI\\PARENT_A"),
            Device(0x1902, "MSI\\SECOND", container, parentInstanceId: "MSI\\PARENT_B")
        ])).CaptureSnapshot();

        Assert.Equal(MsiControllerSnapshotStatus.Indeterminate, result.Status);
        Assert.False(result.AllowsMutation);
    }

    [Fact]
    public void CaptureSnapshot_WithoutUsableContainerOrParent_UsesInstanceIdentity()
    {
        var result = new MsiControllerModeManager(new Enumerator(
        [
            Device(0x1902, "MSI\\FIRST", Guid.Empty, parentInstanceId: null),
            Device(0x1902, "MSI\\SECOND", Guid.Empty, parentInstanceId: null)
        ])).CaptureSnapshot();

        Assert.Equal(MsiControllerSnapshotStatus.Indeterminate, result.Status);
        Assert.False(result.AllowsMutation);
    }

    [Fact]
    public void CaptureSnapshot_UnrelatedDevicesAreIgnored()
    {
        var result = new MsiControllerModeManager(new Enumerator([Device(0x028E, vendorId: 0x045E), Device(0x1903)])).CaptureSnapshot();
        Assert.Equal(MsiControllerNativeMode.Other, result.Snapshot!.Mode);
    }

    [Fact]
    public void CaptureSnapshot_EnumerationException_IsFailSafe()
    {
        var result = new MsiControllerModeManager(new ThrowingEnumerator()).CaptureSnapshot();
        Assert.Equal(MsiControllerSnapshotStatus.EnumerationFailed, result.Status);
        Assert.False(result.AllowsMutation);
    }

    private static ControllerDeviceInfo Device(ushort productId, string instanceId = "MSI\\DEVICE", Guid? container = null, ushort vendorId = 0x0DB0, string? parentInstanceId = "MSI\\PARENT") =>
        new(instanceId, container, parentInstanceId, parentInstanceId is null ? [] : [parentInstanceId], "USB", [$"USB\\VID_{vendorId:X4}&PID_{productId:X4}"], [], "HIDClass", null, null, vendorId, productId, true);

    private sealed class Enumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator
    { public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => devices; }
    private sealed class ThrowingEnumerator : IControllerDeviceEnumerator
    { public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => throw new InvalidOperationException("PnP failed"); }
}
