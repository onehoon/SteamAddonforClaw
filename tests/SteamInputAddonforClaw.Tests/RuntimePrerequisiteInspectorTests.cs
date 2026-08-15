using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Prerequisites;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RuntimePrerequisiteInspectorTests
{
    [Fact]
    public void ViiperExpectedHash_MatchesTheActualEmbeddedPayloadOnDisk()
    {
        // Guards against the constant and the shipped DLL drifting apart again (independent of
        // any fake/mock hash provider, which can only validate comparison logic, not real drift).
        var repoRoot = FindRepoRoot();
        var payloadPath = Path.Combine(repoRoot, "src", "SteamInputAddonforClaw", "Dependencies", "Viiper", "libVIIPER.dll");

        Assert.True(File.Exists(payloadPath), $"Expected embedded VIIPER payload at {payloadPath}");

        var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(payloadPath)));

        Assert.Equal(ViiperRuntimeInspector.ExpectedPayloadSha256, actualHash, ignoreCase: true);
    }

    private static string FindRepoRoot([CallerFilePath] string thisFilePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(thisFilePath)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root from test source path.");
    }

    [Fact]
    public void ViiperInspection_Rejects_a_payload_with_an_unexpected_hash()
    {
        var assessment = new ViiperRuntimeInspector(new FakePayloadFileSystem(true), "payload\\libVIIPER.dll", new FakePayloadHashProvider("00")).Inspect();
        Assert.Equal(PrerequisiteStatus.Unusable, assessment.Status);
        Assert.Equal("ViiperPayloadHashMismatch", assessment.Reason);
    }

    [Fact]
    public void ViiperInspection_AcceptsTheExactBundledPayloadHash()
    {
        var assessment = new ViiperRuntimeInspector(
            new FakePayloadFileSystem(true),
            "payload\\libVIIPER.dll",
            new FakePayloadHashProvider(ViiperRuntimeInspector.ExpectedPayloadSha256)).Inspect();

        Assert.Equal(PrerequisiteStatus.Ready, assessment.Status);
        Assert.Equal("ViiperRuntimeReady", assessment.Reason);
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
    [InlineData((int)HidHideInspectionStatus.Disabled, (int)PrerequisiteStatus.Ready)]
    [InlineData((int)HidHideInspectionStatus.InverseWhitelist, (int)PrerequisiteStatus.Unusable)]
    [InlineData((int)HidHideInspectionStatus.ConfigurationUnavailable, (int)PrerequisiteStatus.Indeterminate)]
    [InlineData((int)HidHideInspectionStatus.AccessDenied, (int)PrerequisiteStatus.Indeterminate)]
    public void HidHideInspection_MapsToFailClosedPrerequisiteStatus(int inspectionStatusValue, int expectedValue)
    {
        var inspectionStatus = (HidHideInspectionStatus)inspectionStatusValue;
        var expected = (PrerequisiteStatus)expectedValue;
        var assessment = new HidHidePrerequisiteInspector(new FakeHidHideClient(inspectionStatus)).Inspect();

        Assert.Equal(expected, assessment.Status);
        if (inspectionStatus != HidHideInspectionStatus.Available && inspectionStatus != HidHideInspectionStatus.Disabled)
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

    [Fact]
    public void UsbIpWin2Probe_ExactPackageAndHealthyRuntimeIsReady()
    {
        var assessment = InspectUsbIp(true, true, true, true);

        Assert.Equal(PrerequisiteStatus.Ready, assessment.Status);
        Assert.Equal("0.9.7.7", assessment.Version);
    }

    [Theory]
    [InlineData("0.9.7.8")]
    [InlineData("0.9.7.6")]
    [InlineData("1.0.0.0")]
    public void UsbIpWin2Probe_ValidUnsupportedPackageIsIncompatible(string version)
    {
        var assessment = InspectUsbIp(true, true, true, true, new(true, version, true, true));

        Assert.Equal(PrerequisiteStatus.Incompatible, assessment.Status);
        Assert.Equal(version, assessment.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UsbIpWin2Probe_PackageEntryWithoutVersionIsIndeterminate(string? version)
    {
        var assessment = InspectUsbIp(true, true, true, true, new(false, version, true, true));

        Assert.Equal(PrerequisiteStatus.Indeterminate, assessment.Status);
        Assert.Equal("UsbIpWin2VersionUnavailable", assessment.Reason);
    }

    [Fact]
    public void UsbIpWin2Probe_MalformedPackageVersionIsIndeterminate()
    {
        var assessment = InspectUsbIp(true, true, true, true, new(true, "unknown", true, true));

        Assert.Equal(PrerequisiteStatus.Indeterminate, assessment.Status);
        Assert.Equal("UsbIpWin2VersionMalformed", assessment.Reason);
        Assert.Equal("unknown", assessment.Version);
    }

    [Fact]
    public void UsbIpWin2Probe_PackageInspectionFailureIsIndeterminate()
    {
        var assessment = new UsbIpWin2PrerequisiteInspector(
            new FakeUsbIpProbe(true, true, true, true),
            new FakeUsbIpPackageProbe(new(false, null, false, false))).Inspect();

        Assert.Equal(PrerequisiteStatus.Indeterminate, assessment.Status);
        Assert.Equal("UsbIpWin2PackageInspectionFailed", assessment.Reason);
    }

    [Fact]
    public void UsbIpWin2Probe_PackageProbeExceptionIsIndeterminate()
    {
        var assessment = new UsbIpWin2PrerequisiteInspector(
            new FakeUsbIpProbe(true, true, true, true),
            new ThrowingUsbIpPackageProbe()).Inspect();

        Assert.Equal(PrerequisiteStatus.Indeterminate, assessment.Status);
        Assert.Equal("UsbIpWin2PackageInspectionFailed", assessment.Reason);
    }

    [Fact]
    public void UsbIpWin2Probe_NoPackageAndNoRuntimeEvidenceIsMissing()
    {
        var assessment = InspectUsbIp(false, false, false, false, new(false, null, true, false));

        Assert.Equal(PrerequisiteStatus.Missing, assessment.Status);
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, false, true)]
    public void UsbIpWin2Probe_RuntimeEvidenceWithoutPackageIsIndeterminate(bool serviceInstalled, bool devicePresent, bool driverUsable, bool filterInstalled)
    {
        var assessment = InspectUsbIp(serviceInstalled, devicePresent, driverUsable, filterInstalled, new(false, null, true, false));

        Assert.Equal(PrerequisiteStatus.Indeterminate, assessment.Status);
        Assert.NotEqual(PrerequisiteStatus.Ready, assessment.Status);
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, true, true)]
    public void UsbIpWin2Probe_ExactPackageWithUnhealthyRuntimeIsUnusable(bool serviceInstalled, bool devicePresent, bool driverUsable, bool filterInstalled)
    {
        var assessment = InspectUsbIp(serviceInstalled, devicePresent, driverUsable, filterInstalled);

        Assert.Equal(PrerequisiteStatus.Unusable, assessment.Status);
        Assert.Equal("0.9.7.7", assessment.Version);
    }

    [Fact]
    public void UsbIpWin2Probe_DeviceInspectionExceptionIsIndeterminate()
    {
        var assessment = new UsbIpWin2PrerequisiteInspector(new ThrowingUsbIpProbe(), ExactUsbIpPackageProbe()).Inspect();

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
    public void ViiperPayload_ExactBundledPayload_IsReady()
    {
        var assessment = new ViiperRuntimeInspector(new FakePayloadFileSystem(true), "payload\\libVIIPER.dll").Inspect();

        Assert.Equal(PrerequisiteStatus.Ready, assessment.Status);
        Assert.Equal("ViiperRuntimeReady", assessment.Reason);
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
    [InlineData((int)PrerequisiteStatus.Incompatible, (int)PrerequisiteStatus.Ready, (int)PrerequisiteStatus.Ready)]
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

    private static PrerequisiteAssessment InspectUsbIp(bool serviceInstalled, bool devicePresent, bool driverUsable, bool filterInstalled, UsbIpWin2PackageState? package = null)
        => new UsbIpWin2PrerequisiteInspector(
            new FakeUsbIpProbe(serviceInstalled, devicePresent, driverUsable, filterInstalled),
            new FakeUsbIpPackageProbe(package ?? new(true, "0.9.7.7", true, true))).Inspect();

    private static IUsbIpWin2PackageProbe ExactUsbIpPackageProbe() => new FakeUsbIpPackageProbe(new(true, "0.9.7.7", true, true));

    private sealed class FakeHidHideClient(HidHideInspectionStatus status) : IHidHideClient
    {
        public HidHideInspection Inspect() => new(status, new HashSet<string>());
        public bool AddApplication(string executablePath) => throw new NotSupportedException();
        public bool RemoveApplication(string executablePath) => throw new NotSupportedException();
        public bool AddHiddenDevice(string deviceEntry) => throw new NotSupportedException();
        public bool RemoveHiddenDevice(string deviceEntry) => throw new NotSupportedException();
    }

    private sealed class ThrowingHidHideClient : IHidHideClient
    {
        public HidHideInspection Inspect() => throw new InvalidOperationException();
        public bool AddApplication(string executablePath) => throw new NotSupportedException();
        public bool RemoveApplication(string executablePath) => throw new NotSupportedException();
        public bool AddHiddenDevice(string deviceEntry) => throw new NotSupportedException();
        public bool RemoveHiddenDevice(string deviceEntry) => throw new NotSupportedException();
    }

    private sealed class FakeUsbIpProbe(bool serviceInstalled, bool devicePresent, bool driverUsable, bool filterInstalled = false) : IUsbIpWin2DeviceProbe
    {
        public UsbIpWin2ProbeResult Probe() => new(serviceInstalled, devicePresent, driverUsable, filterInstalled);
    }

    private sealed class ThrowingUsbIpProbe : IUsbIpWin2DeviceProbe
    {
        public UsbIpWin2ProbeResult Probe() => throw new InvalidOperationException();
    }

    private sealed class FakeUsbIpPackageProbe(UsbIpWin2PackageState state) : IUsbIpWin2PackageProbe
    {
        public UsbIpWin2PackageState Inspect() => state;
    }

    private sealed class ThrowingUsbIpPackageProbe : IUsbIpWin2PackageProbe
    {
        public UsbIpWin2PackageState Inspect() => throw new InvalidOperationException();
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
