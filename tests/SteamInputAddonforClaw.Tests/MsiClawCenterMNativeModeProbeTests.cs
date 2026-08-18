using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawCenterMNativeModeProbeTests
{
    [Fact]
    public async Task XInput_with_strong_physical_identity_is_confirmed()
    {
        // Container + parent present -> IsUsableContainer(container) && parent present -> Strong.
        var device = Device(MsiClawHardware.XInputProductId, container: Guid.NewGuid(), parentInstanceId: "MSI\\PARENT");
        var manager = new MsiClawNativeStateManager(new Enumerator([device]));
        var probe = new MsiClawCenterMNativeModeProbe(manager);

        var result = await probe.CaptureAsync(CancellationToken.None);

        Assert.Equal(CenterMNativeModeProbeResult.XInput, result);
    }

    [Fact]
    public async Task XInput_with_indeterminate_physical_identity_is_uncertain()
    {
        // No usable container, no resolvable physical-device-key ancestor chain -> Indeterminate,
        // even though the capture itself succeeds with exactly one candidate. This must never be
        // reported as XInput-confirmed -- the real NativeMode preflight
        // (MsiClawNativeModeSessionCoordinator.InspectCoreLocked) explicitly requires Strong and
        // would reject this same evidence immediately afterward.
        var device = Device(MsiClawHardware.XInputProductId, container: null, parentInstanceId: null);
        var manager = new MsiClawNativeStateManager(new Enumerator([device]));
        var probe = new MsiClawCenterMNativeModeProbe(manager);

        var result = await probe.CaptureAsync(CancellationToken.None);

        Assert.Equal(CenterMNativeModeProbeResult.Uncertain, result);
    }

    [Fact]
    public async Task No_device_present_is_uncertain()
    {
        var manager = new MsiClawNativeStateManager(new Enumerator([]));
        var probe = new MsiClawCenterMNativeModeProbe(manager);

        var result = await probe.CaptureAsync(CancellationToken.None);

        Assert.Equal(CenterMNativeModeProbeResult.Uncertain, result);
    }

    private static ControllerDeviceInfo Device(ushort productId, string instanceId = "MSI\\DEVICE", Guid? container = null, ushort vendorId = MsiClawHardware.VendorId, string? parentInstanceId = "MSI\\PARENT", IReadOnlyList<string>? ancestors = null) =>
        new(instanceId, container, parentInstanceId, ancestors ?? (parentInstanceId is null ? [] : [parentInstanceId]), "USB", [$"USB\\VID_{vendorId:X4}&PID_{productId:X4}"], [], "HIDClass", null, null, vendorId, productId, true);

    private sealed class Enumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => devices;
    }
}
