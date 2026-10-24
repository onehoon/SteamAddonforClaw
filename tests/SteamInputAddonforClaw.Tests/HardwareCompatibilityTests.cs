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
        // Pin the exact fail-closed outcome (Unsupported, no family/model, the registry's
        // no-match reason) rather than just NotEqual(Supported), so a future regression that
        // turns this into Indeterminate would still be caught.
        var assessment = Evaluate(new(DeviceProbeCaptureStatus.Success,
            new DeviceProbeContext([], baseBoardProduct: baseBoardProduct), "Captured"));

        Assert.Equal(HardwareCompatibilityStatus.Unsupported, assessment.Status);
        Assert.Null(assessment.DeviceFamily);
        Assert.Null(assessment.DeviceModel);
        Assert.Equal("No handheld-device adapter matched.", assessment.Reason);
        Assert.False(assessment.AllowsMutation);
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
        Assert.Equal(new WindowsDeviceIdentity("MSI", "Claw", null, false), capture.Identity);
    }

    [Fact]
    public void PnpEnumerationFailure_IsIndeterminate()
    {
        var identity = new WindowsDeviceIdentity("MSI", "Claw", "MS-1T91", true);
        var factory = new WindowsDeviceProbeContextFactory(
            new FakeIdentitySource(identity),
            new ThrowingControllerEnumerator());

        var capture = factory.Capture();

        Assert.Equal(DeviceProbeCaptureStatus.Indeterminate, capture.Status);
        Assert.Equal(identity, capture.Identity);
    }

    [Fact]
    public void HardwareProbe_UsesScopedMsiSnapshotWithoutCallingGlobalInventory()
    {
        var controller = new ControllerDeviceInfo(
            @"HID\VID_0DB0&PID_1902&MI_00&COL01\CLAW_A", Guid.NewGuid(), @"USB\PARENT", [@"USB\VID_0DB0&PID_1902\CLAW_A"],
            "HID", [@"HID\VID_0DB0&PID_1902&MI_00&COL01\CLAW_A"], [], "HIDClass", null, null,
            0x0DB0, 0x1902, true);
        var enumerator = new ScopedOnlyEnumerator([controller]);
        var factory = new WindowsDeviceProbeContextFactory(
            new FakeIdentitySource(new("MSI", "Claw", "MS-1T91", true)), enumerator);

        var capture = factory.Capture();
        var result = Evaluate(capture);

        Assert.Equal(DeviceProbeCaptureStatus.Success, capture.Status);
        Assert.Equal(new WindowsDeviceIdentity("MSI", "Claw", "MS-1T91", true), capture.Identity);
        Assert.Equal(HardwareCompatibilityStatus.Supported, result.Status);
        Assert.Equal(0, enumerator.GlobalCalls);
        Assert.Equal(1, enumerator.ScopedCalls);
        Assert.Single(capture.Context.PresentPnpDevices);
    }

    [Fact]
    public void HardwareProbe_PreservesTypedPnpReadFailureForExistingStabilization()
    {
        var failure = new PnpDeviceReadException("SetupDiGetDeviceRegistryPropertyW", 5, 1,
            "SPDRP_HARDWAREID", 128, @"HID\VID_0DB0&PID_1902&MI_00&COL01\CLAW_A", 17);
        var factory = new WindowsDeviceProbeContextFactory(
            new FakeIdentitySource(new("MSI", "Claw", "MS-1T91", true)), new ScopedOnlyEnumerator([], failure));

        var capture = factory.Capture();
        var assessment = Evaluate(capture);

        Assert.Equal(DeviceProbeCaptureStatus.Indeterminate, capture.Status);
        Assert.Same(failure, capture.PnpReadFailure);
        Assert.Equal(HardwareCompatibilityStatus.Indeterminate, assessment.Status);
        Assert.Same(failure, assessment.PnpReadFailure);
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Resolver_MissingBoard_IsIndeterminate(string? baseBoardProduct)
    {
        var resolution = new MsiClawDeviceModelResolver().Resolve(new DeviceProbeContext(baseBoardProduct: baseBoardProduct));

        Assert.Equal(HandheldDeviceModelResolutionStatus.Indeterminate, resolution.Status);
        Assert.Equal("BaseBoardProductUnavailable", resolution.Reason);
    }

    private static HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture capture) =>
        new HardwareCompatibilityEvaluator(new HandheldDeviceRegistry([new MsiClawDeviceAdapter()])).Evaluate(capture);

    private sealed class FakeIdentitySource(WindowsDeviceIdentity identity) : IWindowsDeviceIdentitySource { public WindowsDeviceIdentity Capture() => identity; }
    private sealed class FakeControllerEnumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator { public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => devices; }
    private sealed class ThrowingControllerEnumerator : IControllerDeviceEnumerator { public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => throw new InvalidOperationException(); }
    private sealed class ScopedOnlyEnumerator(IReadOnlyList<ControllerDeviceInfo> scopedDevices, PnpDeviceReadException? failure = null) : IControllerDeviceEnumerator
    {
        internal int GlobalCalls { get; private set; }
        internal int ScopedCalls { get; private set; }
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
        {
            GlobalCalls++;
            throw new InvalidOperationException("Global detailed PnP enumeration must not be used by startup admission.");
        }
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices(ushort vendorId, IReadOnlyCollection<ushort> productIds)
        {
            ScopedCalls++;
            Assert.Equal((ushort)0x0DB0, vendorId);
            Assert.Equal(new ushort[] { 0x1901, 0x1902, 0x1903 }, productIds.Order().ToArray());
            if (failure is not null) throw failure;
            return scopedDevices;
        }
    }
    private sealed class FakeProbeFactory : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() => new(DeviceProbeCaptureStatus.Success, new DeviceProbeContext(baseBoardProduct: "MS-1T91"), "test"); }
    private sealed class FakeHardwareEvaluator(HardwareCompatibilityAssessment assessment) : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture capture) => assessment; }
}
