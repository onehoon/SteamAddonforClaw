using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SystemStatusTests
{
    [Theory]
    [InlineData("Intel(R) Arc(TM) 140V GPU", true)]
    [InlineData("AMD Radeon RX 7900 XT", true)]
    [InlineData("NVIDIA GeForce RTX 5090", true)]
    [InlineData("Microsoft Basic Display Adapter", false)]
    [InlineData("Remote Display Adapter", false)]
    [InlineData("Virtual Display Adapter", false)]
    public void DeviceInformation_OnlyRecognizedGpuManufacturersAreShown(string name, bool expected)
    {
        Assert.Equal(expected, WindowsDeviceInformationProvider.IsSupportedGpuName(name));
    }

    [Fact]
    public void SoftwareSorting_RanksRunningThenInstalledThenNotInstalledWithStableKindOrder()
    {
        var sorted = ControllerSoftwareStatusSorter.Sort(
        [
            Software(ControllerSoftwareKind.HandheldCompanion, SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.NotRunning),
            Software(ControllerSoftwareKind.ClawTweaks, SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.Running),
            Software(ControllerSoftwareKind.MsiCenterM, SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.Running)
        ]);

        Assert.Equal([ControllerSoftwareKind.MsiCenterM, ControllerSoftwareKind.ClawTweaks, ControllerSoftwareKind.HandheldCompanion], sorted.Select(item => item.Kind));
    }

    [Fact]
    public void AddonStatus_ExternalControllerVetoHasHighestPriority()
    {
        var status = AddonStatusEvaluator.Evaluate(SoftwareStates(), Prerequisites(PrerequisiteStatus.Missing), new(false, 0), new(ExternalControllerAssessmentStatus.ExternalPresent, 1, []), recoverySafe: false);

        Assert.Equal(AddonOperationalStatus.Passive, status.Status);
        Assert.Equal("External physical controller detected.", status.Reason);
    }

    [Theory]
    [InlineData((int)ControllerSoftwareKind.HandheldCompanion, "Handheld Companion is running.")]
    [InlineData((int)ControllerSoftwareKind.ClawTweaks, "ClawTweaks is running.")]
    public void AddonStatus_RunningControllerSoftwareIsPassive(int runningKindValue, string reason)
    {
        var runningKind = (ControllerSoftwareKind)runningKindValue;
        var software = SoftwareStates().Select(status => status.Kind == runningKind ? status with { Runtime = SoftwareRuntimeStatus.Running } : status).ToArray();
        var status = AddonStatusEvaluator.Evaluate(software, Prerequisites(PrerequisiteStatus.Ready), new(true, 1), new(ExternalControllerAssessmentStatus.Clear, 0, []), recoverySafe: true);

        Assert.Equal(AddonOperationalStatus.Passive, status.Status);
        Assert.Equal(reason, status.Reason);
    }

    [Fact]
    public void AddonStatus_MissingPrerequisiteRequiresSetup()
    {
        var status = AddonStatusEvaluator.Evaluate(SoftwareStates(), Prerequisites(PrerequisiteStatus.Missing), new(false, 0), new(ExternalControllerAssessmentStatus.Clear, 0, []), recoverySafe: true);

        Assert.Equal(AddonOperationalStatus.SetupRequired, status.Status);
    }

    [Fact]
    public void AddonStatus_IndeterminateHandheldCompanionFailsClosed()
    {
        var software = SoftwareStates().Select(status => status.Kind == ControllerSoftwareKind.HandheldCompanion ? status with { Installation = SoftwareInstallationStatus.Indeterminate, Runtime = SoftwareRuntimeStatus.Indeterminate } : status).ToArray();
        var status = AddonStatusEvaluator.Evaluate(software, Prerequisites(PrerequisiteStatus.Ready), new(true, 1), new(ExternalControllerAssessmentStatus.Clear, 0, []), recoverySafe: true);

        Assert.Equal(AddonOperationalStatus.Indeterminate, status.Status);
        Assert.Equal("Handheld Companion state is not stable.", status.Reason);
    }

    [Fact]
    public void AddonStatus_ReadyPrerequisitesAndInactiveSteamWaitsForSteam()
    {
        var status = AddonStatusEvaluator.Evaluate(SoftwareStates(), Prerequisites(PrerequisiteStatus.Ready), new(false, 0), new(ExternalControllerAssessmentStatus.Clear, 0, []), recoverySafe: true);

        Assert.Equal(AddonOperationalStatus.WaitingForSteam, status.Status);
    }

    [Fact]
    public async Task SystemStatusProvider_ReusesPrerequisiteAssessmentAndBuildsOneSnapshot()
    {
        var prerequisites = Prerequisites(PrerequisiteStatus.Missing);
        var provider = new SystemStatusProvider(new FakeDeviceProvider(), [new FakeSoftwareProvider(Software(ControllerSoftwareKind.MsiCenterM, SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.Running)), new FakeSoftwareProvider(Software(ControllerSoftwareKind.ClawTweaks, SoftwareInstallationStatus.NotInstalled, SoftwareRuntimeStatus.NotRunning)), new FakeSoftwareProvider(Software(ControllerSoftwareKind.HandheldCompanion, SoftwareInstallationStatus.NotInstalled, SoftwareRuntimeStatus.NotRunning))], new FakePrerequisiteInspector(prerequisites), () => SteamSessionState.FromRunningAppId(0), () => new(ExternalControllerAssessmentStatus.Clear, 0, []), () => true);

        var snapshot = await provider.CaptureAsync();

        Assert.Same(prerequisites, snapshot.Prerequisites);
        Assert.Equal([ControllerSoftwareKind.MsiCenterM, ControllerSoftwareKind.ClawTweaks, ControllerSoftwareKind.HandheldCompanion], snapshot.ControllerSoftware.Select(item => item.Kind));
        Assert.Equal(AddonOperationalStatus.SetupRequired, snapshot.Addon.Status);
    }

    [Fact]
    public void HandheldCompanion_InstalledButStoppedIsPreserved()
    {
        var status = new HandheldCompanionSoftwareStatusProvider(new FakeHhcRuntime(false), new FakeInstallationProbe(true)).Capture();

        Assert.Equal(SoftwareInstallationStatus.Installed, status.Installation);
        Assert.Equal(SoftwareRuntimeStatus.NotRunning, status.Runtime);
    }

    [Fact]
    public void HandheldCompanion_RunningPromotesInstalledWhenTheInstallationProbeIsAbsent()
    {
        var status = new HandheldCompanionSoftwareStatusProvider(new FakeHhcRuntime(true), new FakeInstallationProbe(false)).Capture();

        Assert.Equal(SoftwareInstallationStatus.Installed, status.Installation);
        Assert.Equal(SoftwareRuntimeStatus.Running, status.Runtime);
    }

    [Theory]
    [InlineData(true, false, (int)SoftwareInstallationStatus.Installed, (int)SoftwareRuntimeStatus.NotRunning)]
    [InlineData(false, true, (int)SoftwareInstallationStatus.Installed, (int)SoftwareRuntimeStatus.Running)]
    [InlineData(false, false, (int)SoftwareInstallationStatus.NotInstalled, (int)SoftwareRuntimeStatus.NotRunning)]
    public void MsiCenterM_InstallationAndRuntimeAreIndependent(bool installed, bool running, int expectedInstallationValue, int expectedRuntimeValue)
    {
        var expectedInstallation = (SoftwareInstallationStatus)expectedInstallationValue;
        var expectedRuntime = (SoftwareRuntimeStatus)expectedRuntimeValue;
        var status = new MsiCenterMSoftwareStatusProvider(new FakeInstallationProbe(installed), () => running).Capture();

        Assert.Equal(expectedInstallation, status.Installation);
        Assert.Equal(expectedRuntime, status.Runtime);
    }

    private static ControllerSoftwareStatus[] SoftwareStates() => [Software(ControllerSoftwareKind.MsiCenterM, SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.NotRunning), Software(ControllerSoftwareKind.ClawTweaks, SoftwareInstallationStatus.NotInstalled, SoftwareRuntimeStatus.NotRunning), Software(ControllerSoftwareKind.HandheldCompanion, SoftwareInstallationStatus.NotInstalled, SoftwareRuntimeStatus.NotRunning)];
    private static ControllerSoftwareStatus Software(ControllerSoftwareKind kind, SoftwareInstallationStatus installation, SoftwareRuntimeStatus runtime) => new(kind, kind.ToString(), installation, runtime, "test");
    private static RuntimePrerequisiteAssessment Prerequisites(PrerequisiteStatus status) => new(new(PrerequisiteKind.HidHide, status, "test"), new(PrerequisiteKind.UsbIpWin2, status, "test"), new(PrerequisiteKind.Viiper, status, "test"));
    private sealed class FakeDeviceProvider : IDeviceInformationProvider { public DeviceStatusSnapshot Capture() => new("MSI", "Claw", ["Intel Arc"]); }
    private sealed class FakeSoftwareProvider(ControllerSoftwareStatus status) : IControllerSoftwareStatusProvider { public ControllerSoftwareStatus Capture() => status; }
    private sealed class FakePrerequisiteInspector(RuntimePrerequisiteAssessment assessment) : IRuntimePrerequisiteInspector { public RuntimePrerequisiteAssessment Inspect() => assessment; }
    private sealed class FakeHhcRuntime(bool running) : SteamInputAddonforClaw.Startup.IHandheldCompanionRuntimeDetector { public bool IsRunning() => running; }
    private sealed class FakeInstallationProbe(bool installed) : IApplicationInstallationProbe { public ApplicationInstallationInfo Detect() => new(installed, "test"); }
}
