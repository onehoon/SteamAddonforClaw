using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Prerequisites;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class HardwareCompatibilityTests
{
    [Fact]
    public void ExactMsiClawBoardAndKnownController_AreSupported()
    {
        var assessment = Evaluate(new(DeviceProbeCaptureStatus.Success,
            new DeviceProbeContext([new DeviceProbePnpDevice("HID\\MSI", vendorId: 0x0DB0, productId: 0x1902)], baseBoardProduct: "MS-1T91"), "Captured"));

        Assert.Equal(HardwareCompatibilityStatus.Supported, assessment.Status);
        Assert.Equal("msi.claw.cg3em", assessment.DeviceModel?.Value);
    }

    [Fact]
    public void DifferentMsiClawBoard_IsUnsupported()
    {
        var assessment = Evaluate(new(DeviceProbeCaptureStatus.Success,
            new DeviceProbeContext([new DeviceProbePnpDevice("HID\\MSI", vendorId: 0x0DB0, productId: 0x1902)], baseBoardProduct: "MS-1T91-OTHER"), "Captured"));

        Assert.Equal(HardwareCompatibilityStatus.Unsupported, assessment.Status);
        Assert.False(assessment.AllowsMutation);
    }

    [Fact]
    public void IndeterminateProbe_NeverTreatsAnEmptyPnpSnapshotAsNoDevice()
    {
        var assessment = Evaluate(new(DeviceProbeCaptureStatus.Indeterminate,
            new DeviceProbeContext([], baseBoardProduct: "MS-1T91"), "ControllerPnpEnumerationFailed"));

        Assert.Equal(HardwareCompatibilityStatus.Indeterminate, assessment.Status);
    }

    [Fact]
    public void BaseBoardCaptureFailure_IsIndeterminate()
    {
        var factory = new WindowsDeviceProbeContextFactory(
            new FakeIdentitySource(new("MSI", "Claw", null, false)),
            new FakeControllerEnumerator([]));

        var capture = factory.Capture();

        Assert.Equal(DeviceProbeCaptureStatus.Indeterminate, capture.Status);
        Assert.Equal("BaseBoardProductUnavailable", capture.Reason);
    }

    [Fact]
    public void PnpEnumerationFailure_IsIndeterminate()
    {
        var factory = new WindowsDeviceProbeContextFactory(
            new FakeIdentitySource(new("MSI", "Claw", "MS-1T91", true)),
            new ThrowingControllerEnumerator());

        Assert.Equal(DeviceProbeCaptureStatus.Indeterminate, factory.Capture().Status);
    }

    [Theory]
    [InlineData((int)HardwareCompatibilityStatus.Unsupported)]
    [InlineData((int)HardwareCompatibilityStatus.Indeterminate)]
    public void ElevatedPreflight_BlocksBeforeStorageForNonSupportedHardware(int status)
    {
        var storageCalls = 0;
        var result = ElevatedHardwareProvisioningPreflight.Evaluate(
            new FakeProbeFactory(),
            new FakeHardwareEvaluator(new((HardwareCompatibilityStatus)status, null, null, "test")),
            () => { storageCalls++; return new(ProvisioningStorageStatus.Trusted, "test"); });

        Assert.False(result.AllowsProvisioning);
        Assert.Equal(0, storageCalls);
        Assert.Null(result.Storage);
    }

    private static HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture capture) =>
        new HardwareCompatibilityEvaluator(new HandheldDeviceRegistry([new MsiClawDeviceAdapter()])).Evaluate(capture);

    private sealed class FakeIdentitySource(WindowsDeviceIdentity identity) : IWindowsDeviceIdentitySource { public WindowsDeviceIdentity Capture() => identity; }
    private sealed class FakeControllerEnumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator { public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => devices; }
    private sealed class ThrowingControllerEnumerator : IControllerDeviceEnumerator { public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => throw new InvalidOperationException(); }
    private sealed class FakeProbeFactory : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() => new(DeviceProbeCaptureStatus.Success, new DeviceProbeContext(baseBoardProduct: "MS-1T91"), "test"); }
    private sealed class FakeHardwareEvaluator(HardwareCompatibilityAssessment assessment) : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture capture) => assessment; }
}
