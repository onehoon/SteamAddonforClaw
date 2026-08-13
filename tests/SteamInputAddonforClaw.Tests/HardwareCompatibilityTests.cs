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

    [Theory]
    [InlineData("MS-1T42", "msi.claw.a2vm.7")]
    [InlineData("MS-1T52", "msi.claw.a2vm.8")]
    public void ExactMsiClawA2vmBoardAndKnownController_AreSupported(string baseBoardProduct, string expectedModelId)
    {
        var assessment = Evaluate(new(DeviceProbeCaptureStatus.Success,
            new DeviceProbeContext([new DeviceProbePnpDevice("HID\\MSI", vendorId: 0x0DB0, productId: 0x1902)], baseBoardProduct: baseBoardProduct), "Captured"));

        Assert.Equal(HardwareCompatibilityStatus.Supported, assessment.Status);
        Assert.Equal(expectedModelId, assessment.DeviceModel?.Value);
        Assert.Equal("msi.claw", assessment.DeviceFamily?.Value);
    }

    [Theory]
    [InlineData("MS-1T42")]
    [InlineData("MS-1T52")]
    public void A2vmBoardWithoutRecognizedController_IsNotSupported(string baseBoardProduct)
    {
        // BaseBoard identity alone must never be sufficient -- the MSI Claw family probe
        // requires actual PnP evidence of the 0x0DB0 controller before model resolution runs.
        var assessment = Evaluate(new(DeviceProbeCaptureStatus.Success,
            new DeviceProbeContext([], baseBoardProduct: baseBoardProduct), "Captured"));

        Assert.NotEqual(HardwareCompatibilityStatus.Supported, assessment.Status);
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

    [Theory]
    [InlineData("MS-1T42", "msi.claw.a2vm.7")]
    [InlineData("MS-1T52", "msi.claw.a2vm.8")]
    [InlineData("MS-1T91", "msi.claw.cg3em")]
    public void Resolver_MatchesKnownBoardToExpectedModelAndFamily(string baseBoardProduct, string expectedModelId)
    {
        var resolution = new MsiClawDeviceModelResolver().Resolve(new DeviceProbeContext(baseBoardProduct: baseBoardProduct));

        Assert.Equal(HandheldDeviceModelResolutionStatus.Matched, resolution.Status);
        Assert.Equal(expectedModelId, resolution.Model?.Id.Value);
        Assert.Equal("msi.claw", resolution.Model?.FamilyId.Value);
    }

    [Theory]
    [InlineData("ms-1t42")]
    [InlineData("  MS-1T42  ")]
    public void Resolver_IsCaseInsensitiveAndTrimsWhitespace(string baseBoardProduct)
    {
        var resolution = new MsiClawDeviceModelResolver().Resolve(new DeviceProbeContext(baseBoardProduct: baseBoardProduct));

        Assert.Equal(HandheldDeviceModelResolutionStatus.Matched, resolution.Status);
        Assert.Equal("msi.claw.a2vm.7", resolution.Model?.Id.Value);
    }

    [Fact]
    public void Resolver_UnknownBoard_IsUnsupported()
    {
        var resolution = new MsiClawDeviceModelResolver().Resolve(new DeviceProbeContext(baseBoardProduct: "MS-UNKNOWN"));

        Assert.Equal(HandheldDeviceModelResolutionStatus.Unsupported, resolution.Status);
        Assert.Null(resolution.Model);
    }

    [Fact]
    public void Resolver_MissingBoard_IsIndeterminate()
    {
        var resolution = new MsiClawDeviceModelResolver().Resolve(new DeviceProbeContext(baseBoardProduct: null));

        Assert.Equal(HandheldDeviceModelResolutionStatus.Indeterminate, resolution.Status);
        Assert.Equal("BaseBoardProductUnavailable", resolution.Reason);
    }

    private static HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture capture) =>
        new HardwareCompatibilityEvaluator(new HandheldDeviceRegistry([new MsiClawDeviceAdapter()])).Evaluate(capture);

    private sealed class FakeIdentitySource(WindowsDeviceIdentity identity) : IWindowsDeviceIdentitySource { public WindowsDeviceIdentity Capture() => identity; }
    private sealed class FakeControllerEnumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator { public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => devices; }
    private sealed class ThrowingControllerEnumerator : IControllerDeviceEnumerator { public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => throw new InvalidOperationException(); }
    private sealed class FakeProbeFactory : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() => new(DeviceProbeCaptureStatus.Success, new DeviceProbeContext(baseBoardProduct: "MS-1T91"), "test"); }
    private sealed class FakeHardwareEvaluator(HardwareCompatibilityAssessment assessment) : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture capture) => assessment; }
}
