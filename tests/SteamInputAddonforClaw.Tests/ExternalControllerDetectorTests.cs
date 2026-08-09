using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.MSI.Claw;
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
    public void Detect_WhenSteamController1304HasVerifiedPhysicalTopology_ReturnsExternalPresent()
    {
        var root = SteamController1304Root();
        var collection = SteamController1304HidCollection([root.InstanceId, "USB\\ROOT_HUB30\\1"]);

        var assessment = Detect([collection, root]);
        var result = new ControllerDeviceClassifier().ClassifyDetailed(collection, new ControllerTopologySnapshot([collection, root]));

        Assert.Equal(ExternalControllerAssessmentStatus.ExternalPresent, assessment.Status);
        Assert.Equal(1, assessment.DetectedExternalControllerCount);
        Assert.Equal(ControllerDeviceClassification.ExternalPhysical, result.Classification);
        Assert.Equal("VerifiedSteamController1304PhysicalTopology", result.Reason);
    }

    [Fact]
    public void Detect_WhenSteamController1304PhysicalRootCannotBeVerified_ReturnsIndeterminate()
    {
        var collection = SteamController1304HidCollection(["USB\\ROOT_HUB30\\1"]);

        var assessment = Detect([collection]);
        var result = new ControllerDeviceClassifier().ClassifyDetailed(collection, new ControllerTopologySnapshot([collection]));

        Assert.Equal(ExternalControllerAssessmentStatus.Indeterminate, assessment.Status);
        Assert.Equal(ControllerDeviceClassification.Indeterminate, result.Classification);
        Assert.Equal("SteamController1304PhysicalRootUnverified", result.Reason);
    }

    [Fact]
    public void Detect_WhenSteamController1304HasUsbIpAncestor_ReturnsClear()
    {
        var root = SteamController1304Root();
        var usbIpRoot = new ControllerDeviceInfo("ROOT\\USB\\0000", Guid.NewGuid(), null, [], "ROOT", ["ROOT\\USBIP_WIN2\\UDE"], [], "USB", null, "usbip2_ude", null, null, true);
        var collection = SteamController1304HidCollection([root.InstanceId, usbIpRoot.InstanceId]);

        var assessment = Detect([collection, root, usbIpRoot]);
        var result = new ControllerDeviceClassifier().ClassifyDetailed(collection, new ControllerTopologySnapshot([collection, root, usbIpRoot]));

        Assert.Equal(ExternalControllerAssessmentStatus.Clear, assessment.Status);
        Assert.Equal(ControllerDeviceClassification.KnownVirtual, result.Classification);
        Assert.Equal("KnownVirtualUsbIpAncestor", result.Reason);
    }

    [Fact]
    public void Detect_WhenSteamController1304IsAddonOwned_ReturnsClear()
    {
        var root = SteamController1304Root();
        var collection = SteamController1304HidCollection([root.InstanceId, "USB\\ROOT_HUB30\\1"]);
        var exclusionSource = new ExclusionSource(collection.InstanceId);

        var assessment = Detect([collection, root], exclusionSource);
        var result = new ControllerDeviceClassifier(exclusionSource).ClassifyDetailed(collection, new ControllerTopologySnapshot([collection, root]));

        Assert.Equal(ExternalControllerAssessmentStatus.Clear, assessment.Status);
        Assert.Equal(ControllerDeviceClassification.AddonOwnedVirtual, result.Classification);
        Assert.Equal("IdentityExclusionSource", result.Reason);
    }

    [Fact]
    public void Detect_WhenValveDeviceDoesNotHaveSteamController1304Identity_ReturnsClear()
    {
        var valveHid = new ControllerDeviceInfo(
            "HID\\VID_28DE&PID_1102&MI_02&COL01\\1",
            Guid.NewGuid(),
            null,
            [],
            "HID",
            ["HID\\VID_28DE&PID_1102"],
            [],
            "HIDClass",
            null,
            null,
            0x28DE,
            0x1102,
            true);

        Assert.Equal(ExternalControllerAssessmentStatus.Clear, Detect([valveHid]).Status);
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
                    "ROOT\\USBIP_WIN2\\UDE",
                    "HTREE\\ROOT\\0"
                ])
        ]);

        Assert.Equal(ExternalControllerAssessmentStatus.Clear, assessment.Status);
    }

    [Fact]
    public void Detect_WhenClawTweaksUsbIpXboxHasResolvedAncestorMetadata_ReturnsClear()
    {
        var root = new ControllerDeviceInfo("ROOT\\USB\\0000", Guid.Empty, null, ["HTREE\\ROOT\\0"], "ROOT", ["ROOT\\USBIP_WIN2\\UDE"], [], "USB", null, "usbip2_ude", null, null, true);
        var virtualXbox = GameController(0x045E, 0x028E, ancestorInstanceIds: ["USB\\VID_045E&PID_028E\\296013F", "ROOT\\USB\\0000", "HTREE\\ROOT\\0"]);

        var assessment = Detect([virtualXbox, root]);
        var result = new ControllerDeviceClassifier().ClassifyDetailed(virtualXbox, new ControllerTopologySnapshot([virtualXbox, root]));

        Assert.Equal(ExternalControllerAssessmentStatus.Clear, assessment.Status);
        Assert.Equal("KnownVirtualUsbIpAncestor", result.Reason);
        Assert.Equal("ROOT\\USB\\0000", result.EvidenceDevice?.InstanceId);
    }

    [Fact]
    public void Detect_WhenUsbIpRootIsNotInPhysicalXboxAncestry_ReturnsExternalPresent()
    {
        var unrelatedUsbIpRoot = new ControllerDeviceInfo("ROOT\\USB\\0000", Guid.Empty, null, [], "ROOT", ["ROOT\\USBIP_WIN2\\UDE"], [], "USB", null, "usbip2_ude", null, null, true);
        var physicalXbox = GameController(0x045E, 0x028E, ancestorInstanceIds: ["USB\\ROOT_HUB30\\1", "PCI\\VEN_1234"]);

        Assert.Equal(ExternalControllerAssessmentStatus.ExternalPresent, Detect([physicalXbox, unrelatedUsbIpRoot]).Status);
    }

    [Fact]
    public void Detect_WhenClawTweaksVirtualXboxAndPhysicalControllerExist_ReturnsExternalPresent()
    {
        var usbIpRoot = new ControllerDeviceInfo("ROOT\\USB\\0000", Guid.Empty, null, [], "ROOT", ["ROOT\\USBIP_WIN2\\UDE"], [], "USB", null, "usbip2_ude", null, null, true);
        var virtualXbox = GameController(0x045E, 0x028E, ancestorInstanceIds: ["ROOT\\USB\\0000"]);
        var physicalController = GameController(0x054C, 0x0CE6, ancestorInstanceIds: ["USB\\ROOT_HUB30\\1"]);

        var assessment = Detect([virtualXbox, usbIpRoot, physicalController]);

        Assert.Equal(ExternalControllerAssessmentStatus.ExternalPresent, assessment.Status);
        Assert.Equal(1, assessment.DetectedExternalControllerCount);
    }

    [Fact]
    public void Detect_WhenDevicesUseSentinelContainerId_DoesNotMergeUnrelatedControllers()
    {
        var sentinel = new Guid("00000000-0000-0000-ffff-ffffffffffff");
        var first = GameController(0x045E, 0x028E, sentinel, instanceId: "HID\\FIRST");
        var second = GameController(0x054C, 0x0CE6, sentinel, instanceId: "HID\\SECOND");

        Assert.Equal(2, Detect([first, second]).DetectedExternalControllerCount);
    }

    [Fact]
    public void Detect_WhenPhysicalXboxHasGenericRootAncestors_ReturnsExternalPresent()
    {
        var assessment = Detect(
        [
            GameController(
                0x045E,
                0x0B13,
                ancestorInstanceIds:
                [
                    "USB\\ROOT_HUB30\\NORMAL_USB_HUB",
                    "PCI\\VEN_1234&DEV_5678",
                    "HTREE\\ROOT\\0"
                ])
        ]);

        Assert.Equal(ExternalControllerAssessmentStatus.ExternalPresent, assessment.Status);
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

    [Fact]
    public void ClassifyDetailed_WhenInternalHandheldExists_ReportsMsiClawReason()
    {
        var result = new ControllerDeviceClassifier(new MsiClawInternalControllerMatcher()).ClassifyDetailed(GameController(0x0DB0, 0x1902));

        Assert.Equal(ControllerDeviceClassification.InternalHandheld, result.Classification);
        Assert.Equal("KnownMsiClawVidPid", result.Reason);
    }

    [Fact]
    public void ClassifyDetailed_WhenUsbIpIdentityExists_ReportsUsbIpReason()
    {
        var result = new ControllerDeviceClassifier().ClassifyDetailed(
            GameController(0x045E, 0x028E, ancestorInstanceIds: ["ROOT\\USBIP_WIN2\\UDE"]));

        Assert.Equal(ControllerDeviceClassification.KnownVirtual, result.Classification);
        Assert.Equal("KnownVirtualUsbIp", result.Reason);
    }

    [Fact]
    public void ClassifyDetailed_WhenRootVirtualIdentityExists_ReportsRootReason()
    {
        var result = new ControllerDeviceClassifier().ClassifyDetailed(
            GameController(0x045E, 0x028E, instanceId: "ROOT\\UNKNOWN_CONTROLLER"));

        Assert.Equal(ControllerDeviceClassification.Indeterminate, result.Classification);
        Assert.Equal("UnverifiedRootVirtualIdentity", result.Reason);
    }

    [Fact]
    public void ClassifyDetailed_WhenPhysicalGamepadExists_ReportsPhysicalReason()
    {
        var result = new ControllerDeviceClassifier().ClassifyDetailed(GameController(0x045E, 0x0B13));

        Assert.Equal(ControllerDeviceClassification.ExternalPhysical, result.Classification);
        Assert.Equal("PhysicalGameControllerWithoutExclusion", result.Reason);
    }

    private static ExternalControllerAssessment Detect(IReadOnlyList<ControllerDeviceInfo> devices, IControllerIdentityExclusionSource? exclusionSource = null)
    {
        return new ExternalControllerDetector(new FakeEnumerator(devices), new ControllerDeviceClassifier(new MsiClawInternalControllerMatcher(), exclusionSource)).Detect();
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

    private static ControllerDeviceInfo SteamController1304Root()
    {
        return new ControllerDeviceInfo(
            "USB\\VID_28DE&PID_1304\\STEAM_CONTROLLER",
            Guid.NewGuid(),
            "USB\\ROOT_HUB30\\1",
            ["USB\\ROOT_HUB30\\1"],
            "USB",
            ["USB\\VID_28DE&PID_1304"],
            [],
            "USB",
            null,
            "usbccgp",
            0x28DE,
            0x1304,
            true);
    }

    private static ControllerDeviceInfo SteamController1304HidCollection(IReadOnlyList<string> ancestorInstanceIds)
    {
        return new ControllerDeviceInfo(
            "HID\\VID_28DE&PID_1304&MI_02&COL03\\STEAM_CONTROLLER",
            Guid.NewGuid(),
            ancestorInstanceIds.FirstOrDefault(),
            ancestorInstanceIds,
            "HID",
            ["HID\\VID_28DE&PID_1304&MI_02&COL03"],
            [],
            "HIDClass",
            null,
            null,
            0x28DE,
            0x1304,
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
