using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Startup;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class StockCenterMStartupBaselineTests
{
    [Fact]
    public async Task AlreadyXInput_SucceedsWithoutModeWrite()
    {
        var devices = new MutableDevices(MsiClawNativeMode.XInput, Guid.NewGuid());
        var controller = new RecordingModeController(devices);
        var result = await new StockCenterMStartupBaseline(new MsiClawNativeStateManager(devices, controller)).EstablishAsync(CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.False(result.ModeWriteIssued);
        Assert.Equal(0, controller.CallCount);
    }

    [Fact]
    public async Task DirectInput_TransitionsOnceAndVerifiesXInput()
    {
        var devices = new MutableDevices(MsiClawNativeMode.DirectInput, Guid.NewGuid());
        var controller = new RecordingModeController(devices);
        var result = await new StockCenterMStartupBaseline(new MsiClawNativeStateManager(devices, controller)).EstablishAsync(CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.True(result.ModeWriteIssued);
        Assert.Equal(1, controller.CallCount);
        Assert.Equal(MsiClawNativeMode.XInput, devices.Mode);
    }

    [Fact]
    public async Task WeakIdentity_DoesNotWriteAndFailsClosed()
    {
        var devices = new MutableDevices(MsiClawNativeMode.DirectInput, Guid.Empty, weakIdentity: true);
        var controller = new RecordingModeController(devices);
        var result = await new StockCenterMStartupBaseline(new MsiClawNativeStateManager(devices, controller)).EstablishAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(0, controller.CallCount);
        Assert.Equal(MsiClawNativeMode.DirectInput, devices.Mode);
    }

    [Fact]
    public async Task UnsupportedMode_DoesNotWriteAndFailsClosed()
    {
        var devices = new MutableDevices(MsiClawNativeMode.Other, Guid.NewGuid());
        var controller = new RecordingModeController(devices);
        var result = await new StockCenterMStartupBaseline(new MsiClawNativeStateManager(devices, controller)).EstablishAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(0, controller.CallCount);
    }

    [Fact]
    public async Task SuccessfulTransitionWithoutLiveXInputVerification_FailsClosed()
    {
        var devices = new MutableDevices(MsiClawNativeMode.DirectInput, Guid.NewGuid());
        var controller = new RecordingModeController(devices, applyMode: false);
        var result = await new StockCenterMStartupBaseline(new MsiClawNativeStateManager(devices, controller)).EstablishAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.True(result.ModeWriteIssued);
        Assert.Equal(1, controller.CallCount);
        Assert.Equal(MsiClawNativeMode.DirectInput, devices.Mode);
    }

    private sealed class MutableDevices(MsiClawNativeMode mode, Guid container, bool weakIdentity = false) : IControllerDeviceEnumerator
    {
        public MsiClawNativeMode Mode { get; set; } = mode;
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
        {
            var productId = Mode switch { MsiClawNativeMode.XInput => MsiClawHardware.XInputProductId, MsiClawNativeMode.DirectInput => MsiClawHardware.DirectInputProductId, _ => MsiClawHardware.TestingProductId };
            return [new($"HID\\VID_0DB0&PID_{productId:X4}\\CLAW", container, weakIdentity ? null : "USB\\PARENT", weakIdentity ? [] : ["USB\\VID_0DB0&PID_1901\\CLAW"], "USB", [], [], "HIDClass", null, null, MsiClawHardware.VendorId, productId, true)];
        }
    }

    private sealed class RecordingModeController(MutableDevices devices, bool applyMode = true) : IMsiClawModeController
    {
        public int CallCount { get; private set; }
        public Task<MsiClawModeTransitionResult> SwitchModeAsync(MsiClawNativeMode target, MsiClawPhysicalIdentity expectedIdentity, CancellationToken cancellationToken)
        {
            CallCount++;
            if (applyMode) devices.Mode = target;
            return Task.FromResult(new MsiClawModeTransitionResult(MsiClawModeTransitionStatus.Succeeded, MsiClawNativeMode.DirectInput, target, null, MsiClawHardware.XInputProductId, true, true, true, true, true, 1, "test"));
        }
    }
}
