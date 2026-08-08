using SteamInputAddonforClaw.Controllers.Detection;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ExternalControllerDetectorTests
{
    [Fact]
    public void Detect_WhenNoControllerDevicesExist_ReturnsClear()
    {
        var assessment = Detect([]);

        Assert.Equal(ExternalControllerAssessmentStatus.Clear, assessment.Status);
    }

    [Theory]
    [InlineData(0x1901)]
    [InlineData(0x1902)]
    [InlineData(0x1903)]
    public void Detect_WhenOnlyClawInternalInterfaceExists_ReturnsClear(int productId)
    {
        var assessment = Detect([GameController(0x0DB0, (ushort)productId)]);

        Assert.Equal(ExternalControllerAssessmentStatus.Clear, assessment.Status);
    }

    [Fact]
    public void Detect_WhenPhysicalXboxControllerExists_ReturnsExternalPresent()
    {
        var assessment = Detect([GameController(0x045E, 0x0B13)]);

        Assert.Equal(ExternalControllerAssessmentStatus.ExternalPresent, assessment.Status);
        Assert.Equal(1, assessment.DetectedExternalControllerCount);
    }

    [Fact]
    public void Detect_WhenClawHasMultipleInterfacesInOneContainer_ReturnsClear()
    {
        var containerId = Guid.NewGuid();
        var assessment = Detect(
        [
            GameController(0x0DB0, 0x1901, containerId),
            GameController(0x0DB0, 0x1902, containerId)
        ]);

        Assert.Equal(ExternalControllerAssessmentStatus.Clear, assessment.Status);
    }

    [Fact]
    public void Detect_WhenClawAndPhysicalXboxExist_ReturnsExternalPresent()
    {
        var assessment = Detect([GameController(0x0DB0, 0x1902), GameController(0x045E, 0x0B13)]);

        Assert.Equal(ExternalControllerAssessmentStatus.ExternalPresent, assessment.Status);
    }

    [Theory]
    [InlineData(0x054C, 0x0CE6)]
    [InlineData(0x2DC8, 0x3106)]
    public void Detect_WhenThirdPartyPhysicalControllerExists_ReturnsExternalPresent(int vendorId, int productId)
    {
        var assessment = Detect([GameController((ushort)vendorId, (ushort)productId)]);

        Assert.Equal(ExternalControllerAssessmentStatus.ExternalPresent, assessment.Status);
    }

    [Fact]
    public void Detect_WhenExternalControllerHasMultipleInterfacesInOneContainer_DeduplicatesIt()
    {
        var containerId = Guid.NewGuid();
        var assessment = Detect(
        [
            GameController(0x045E, 0x0B13, containerId),
            GameController(0x045E, 0x0B13, containerId)
        ]);

        Assert.Equal(1, assessment.DetectedExternalControllerCount);
    }

    [Fact]
    public void Detect_WhenOnlyAddonOwnedVirtualControllerExists_ReturnsClear()
    {
        var device = GameController(0x045E, 0x028E);
        var assessment = Detect([device], new ExclusionSource(device.InstanceId));

        Assert.Equal(ExternalControllerAssessmentStatus.Clear, assessment.Status);
    }

    [Theory]
    [InlineData("VIIPER")]
    [InlineData("ClawTweaks")]
    [InlineData("HandheldCompanion")]
    [InlineData("ViGEmBus")]
    public void Detect_WhenKnownVirtualIdentityExists_ReturnsClear(string enumeratorName)
    {
        var assessment = Detect([GameController(0x045E, 0x028E, enumeratorName: enumeratorName)]);

        Assert.Equal(ExternalControllerAssessmentStatus.Clear, assessment.Status);
    }

    [Fact]
    public void Detect_WhenVirtualControllerIdentityIsOnlyInRootAncestor_ReturnsClear()
    {
        var assessment = Detect(
        [
            GameController(
                0x045E,
                0x028E,
                ancestorInstanceIds:
                [
                    "USB\\VID_045E&PID_028E\\NORMAL_USB_PARENT",
                    "ROOT\\USBIP_WIN2\\UDE"
                ])
        ]);

        Assert.Equal(ExternalControllerAssessmentStatus.Clear, assessment.Status);
    }

    [Fact]
    public void Detect_WhenControllerHasUnverifiedVirtualIdentity_ReturnsIndeterminate()
    {
        var assessment = Detect([GameController(0x045E, 0x028E, instanceId: "ROOT\\UNKNOWN_CONTROLLER")]);

        Assert.Equal(ExternalControllerAssessmentStatus.Indeterminate, assessment.Status);
    }

    [Fact]
    public void Detect_WhenPhysicalControllerAndIndeterminateControllerExist_ReturnsExternalPresent()
    {
        var assessment = Detect(
        [
            GameController(0x045E, 0x0B13),
            GameController(0x1234, 0x5678, instanceId: "ROOT\\UNKNOWN_CONTROLLER")
        ]);

        Assert.Equal(ExternalControllerAssessmentStatus.ExternalPresent, assessment.Status);
        Assert.Equal(1, assessment.DetectedExternalControllerCount);
    }

    [Fact]
    public void Detect_WhenEnumeratorFails_ReturnsIndeterminate()
    {
        var detector = new ExternalControllerDetector(new ThrowingEnumerator(), new ControllerDeviceClassifier());

        Assert.Equal(ExternalControllerAssessmentStatus.Indeterminate, detector.Detect().Status);
    }

    [Fact]
    public void Detect_WhenOnlyUnrelatedHidExists_ReturnsClear()
    {
        var mouse = new ControllerDeviceInfo("HID\\MOUSE", Guid.NewGuid(), null, [], "HID", ["HID\\VID_1234&PID_0001"], [], "HIDClass", null, null, 0x1234, 0x0001, true);
        var assessment = Detect([mouse]);

        Assert.Equal(ExternalControllerAssessmentStatus.Clear, assessment.Status);
    }

    private static ExternalControllerAssessment Detect(IReadOnlyList<ControllerDeviceInfo> devices, IControllerIdentityExclusionSource? exclusionSource = null)
    {
        return new ExternalControllerDetector(new FakeEnumerator(devices), new ControllerDeviceClassifier(exclusionSource)).Detect();
    }

    private static ControllerDeviceInfo GameController(
        ushort vendorId,
        ushort productId,
        Guid? containerId = null,
        string? enumeratorName = "HID",
        string? instanceId = null,
        IReadOnlyList<string>? ancestorInstanceIds = null)
    {
        return new ControllerDeviceInfo(
            instanceId ?? $"HID\\VID_{vendorId:X4}&PID_{productId:X4}",
            containerId ?? Guid.NewGuid(),
            null,
            ancestorInstanceIds ?? [],
            enumeratorName,
            [$"HID\\VID_{vendorId:X4}&PID_{productId:X4}"],
            ["HID_DEVICE_UP:0001_U:0005"],
            "HIDClass",
            null,
            null,
            vendorId,
            productId,
            true);
    }

    private sealed class FakeEnumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => devices;
    }

    private sealed class ThrowingEnumerator : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => throw new InvalidOperationException();
    }

    private sealed class ExclusionSource(params string[] instanceIds) : IControllerIdentityExclusionSource
    {
        public bool IsExcluded(ControllerDeviceInfo device) => instanceIds.Contains(device.InstanceId, StringComparer.OrdinalIgnoreCase);
    }
}
