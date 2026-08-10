using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Prerequisites;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RuntimePrerequisiteInspectorTests
{
    [Fact]
    public void ViiperInspection_Rejects_a_payload_with_an_unexpected_hash()
    {
        var assessment = new ViiperRuntimeInspector(new FakePayloadFileSystem(true), "payload\\libVIIPER.dll", new FakePayloadHashProvider("00")).Inspect();
        Assert.Equal(PrerequisiteStatus.Unusable, assessment.Status);
        Assert.Equal("ViiperPayloadHashMismatch", assessment.Reason);
    }
    [Fact]
    public void UsbIpWin2Inspection_UsesTheUdePnPAndServiceIdentities()
    {
        Assert.Equal(@"ROOT\USBIP_WIN2\UDE", UsbIpWin2PrerequisiteInspector.RootHardwareId);
        Assert.Equal("usbip2_ude", UsbIpWin2PrerequisiteInspector.ServiceName);
    }

    [Theory]
    [InlineData((int)HidHideInspectionStatus.Available, (int)PrerequisiteStatus.Ready)]
    [InlineData((int)HidHideInspectionStatus.NotInstalled, (int)PrerequisiteStatus.Missing)]
    [InlineData((int)HidHideInspectionStatus.Disabled, (int)PrerequisiteStatus.Unusable)]
    [InlineData((int)HidHideInspectionStatus.InverseWhitelist, (int)PrerequisiteStatus.Unusable)]
    [InlineData((int)HidHideInspectionStatus.ConfigurationUnavailable, (int)PrerequisiteStatus.Indeterminate)]
    [InlineData((int)HidHideInspectionStatus.AccessDenied, (int)PrerequisiteStatus.Indeterminate)]
    public void HidHideInspection_MapsToFailClosedPrerequisiteStatus(int inspectionStatusValue, int expectedValue)
    {
        var inspectionStatus = (HidHideInspectionStatus)inspectionStatusValue;
        var expected = (PrerequisiteStatus)expectedValue;
        var assessment = new HidHidePrerequisiteInspector(new FakeHidHideClient(inspectionStatus)).Inspect();

        Assert.Equal(expected, assessment.Status);
        if (inspectionStatus != HidHideInspectionStatus.Available)
        {
            Assert.NotEqual(PrerequisiteStatus.Ready, assessment.Status);
        }
    }

    [Fact]
    public void HidHideInspection_ExceptionIsIndeterminate()
    {
        var assessment = new HidHidePrerequisiteInspector(new ThrowingHidHideClient()).Inspect();

        Assert.Equal(PrerequisiteStatus.Indeterminate, assessment.Status);
        Assert.Equal("HidHideInspectionFailed", assessment.Reason);
    }

    [Theory]
    [InlineData(false, false, false, (int)PrerequisiteStatus.Missing)]
    [InlineData(true, true, true, (int)PrerequisiteStatus.Ready)]
    [InlineData(true, false, false, (int)PrerequisiteStatus.Unusable)]
    [InlineData(true, true, false, (int)PrerequisiteStatus.Unusable)]
    [InlineData(false, true, true, (int)PrerequisiteStatus.Unusable)]
    public void UsbIpWin2Probe_MapsPresenceAndUsability(bool serviceInstalled, bool devicePresent, bool driverUsable, int expectedValue)
    {
        var expected = (PrerequisiteStatus)expectedValue;
        var assessment = new UsbIpWin2PrerequisiteInspector(new FakeUsbIpProbe(serviceInstalled, devicePresent, driverUsable)).Inspect();

        Assert.Equal(expected, assessment.Status);
    }

    [Fact]
    public void UsbIpWin2Probe_MissingFilterServiceIsUnusable()
    {
        var assessment = new UsbIpWin2PrerequisiteInspector(new FakeUsbIpProbe(true, true, true, filterInstalled: false)).Inspect();

        Assert.Equal(PrerequisiteStatus.Unusable, assessment.Status);
    }

    [Fact]
    public void UsbIpWin2Probe_ExceptionIsIndeterminate()
    {
        var assessment = new UsbIpWin2PrerequisiteInspector(new ThrowingUsbIpProbe()).Inspect();

        Assert.Equal(PrerequisiteStatus.Indeterminate, assessment.Status);
        Assert.Equal("UsbIpWin2InspectionFailed", assessment.Reason);
    }

    [Fact]
    public void ViiperPayload_MissingIsNotReady()
    {
        var assessment = new ViiperRuntimeInspector(new FakePayloadFileSystem(false), "payload\\libVIIPER.dll").Inspect();

        Assert.Equal(PrerequisiteStatus.Missing, assessment.Status);
        Assert.Equal("ViiperPayloadMissing", assessment.Reason);
    }

    [Fact]
    public void ViiperPayload_PresentRemainsUnverifiedAndNotReady()
    {
        var assessment = new ViiperRuntimeInspector(new FakePayloadFileSystem(true), "payload\\libVIIPER.dll").Inspect();

        Assert.Equal(PrerequisiteStatus.Present, assessment.Status);
        Assert.Equal("ViiperPayloadPresentUnverified", assessment.Reason);
    }

    [Fact]
    public void ViiperPayload_FileSystemExceptionIsIndeterminate()
    {
        var assessment = new ViiperRuntimeInspector(new ThrowingPayloadFileSystem(), "payload\\libVIIPER.dll").Inspect();

        Assert.Equal(PrerequisiteStatus.Indeterminate, assessment.Status);
    }

    [Fact]
    public void Aggregate_AllReadyIsRoutingReady()
    {
        var assessment = Aggregate(PrerequisiteStatus.Ready, PrerequisiteStatus.Ready, PrerequisiteStatus.Ready);

        Assert.True(assessment.IsRoutingReady);
    }

    [Theory]
    [InlineData((int)PrerequisiteStatus.Missing, (int)PrerequisiteStatus.Ready, (int)PrerequisiteStatus.Ready)]
    [InlineData((int)PrerequisiteStatus.Ready, (int)PrerequisiteStatus.Missing, (int)PrerequisiteStatus.Ready)]
    [InlineData((int)PrerequisiteStatus.Ready, (int)PrerequisiteStatus.Ready, (int)PrerequisiteStatus.Missing)]
    [InlineData((int)PrerequisiteStatus.Indeterminate, (int)PrerequisiteStatus.Ready, (int)PrerequisiteStatus.Ready)]
    public void Aggregate_AnyNonReadyPrerequisiteFailsClosed(int hidHideValue, int usbIpWin2Value, int viiperValue)
    {
        var hidHide = (PrerequisiteStatus)hidHideValue;
        var usbIpWin2 = (PrerequisiteStatus)usbIpWin2Value;
        var viiper = (PrerequisiteStatus)viiperValue;
        Assert.False(Aggregate(hidHide, usbIpWin2, viiper).IsRoutingReady);
    }

    private static RuntimePrerequisiteAssessment Aggregate(PrerequisiteStatus hidHide, PrerequisiteStatus usbIpWin2, PrerequisiteStatus viiper) => new(
        new PrerequisiteAssessment(PrerequisiteKind.HidHide, hidHide, "test"),
        new PrerequisiteAssessment(PrerequisiteKind.UsbIpWin2, usbIpWin2, "test"),
        new PrerequisiteAssessment(PrerequisiteKind.Viiper, viiper, "test"));

    private sealed class FakeHidHideClient(HidHideInspectionStatus status) : IHidHideClient
    {
        public HidHideInspection Inspect() => new(status, new HashSet<string>());
        public bool AddApplication(string executablePath) => throw new NotSupportedException();
        public bool RemoveApplication(string executablePath) => throw new NotSupportedException();
    }

    private sealed class ThrowingHidHideClient : IHidHideClient
    {
        public HidHideInspection Inspect() => throw new InvalidOperationException();
        public bool AddApplication(string executablePath) => throw new NotSupportedException();
        public bool RemoveApplication(string executablePath) => throw new NotSupportedException();
    }

    private sealed class FakeUsbIpProbe(bool serviceInstalled, bool devicePresent, bool driverUsable, bool filterInstalled = true) : IUsbIpWin2DeviceProbe
    {
        public UsbIpWin2ProbeResult Probe() => new(serviceInstalled, devicePresent, driverUsable, filterInstalled);
    }

    private sealed class ThrowingUsbIpProbe : IUsbIpWin2DeviceProbe
    {
        public UsbIpWin2ProbeResult Probe() => throw new InvalidOperationException();
    }

    private sealed class FakePayloadFileSystem(bool exists) : IRuntimePayloadFileSystem
    {
        public bool FileExists(string path) => exists;
    }

    private sealed class ThrowingPayloadFileSystem : IRuntimePayloadFileSystem
    {
        public bool FileExists(string path) => throw new IOException();
    }

    private sealed class FakePayloadHashProvider(string hash) : IRuntimePayloadHashProvider
    {
        public string GetSha256(string path) => hash;
    }
}
