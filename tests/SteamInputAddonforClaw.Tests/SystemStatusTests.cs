using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Routing;
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

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070 SUPER", "PCI\\VEN_10DE&DEV_2783", true)]
    [InlineData("NVIDIA GeForce RTX 4070 SUPER", "", true)]
    [InlineData("SudoMaker Virtual Display Adapter", "ROOT\\DISPLAY\\0000", false)]
    [InlineData("NVIDIA Virtual Display Adapter", "ROOT\\DISPLAY\\0000", false)]
    [InlineData("Microsoft Basic Display Adapter", "PCI\\VEN_1414", false)]
    public void DeviceInformation_OnlyPhysicalVendorGpuWmiEntriesAreShown(string name, string pnpDeviceId, bool expected)
    {
        Assert.Equal(expected, WindowsDeviceInformationProvider.IsPhysicalGpu(name, pnpDeviceId));
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

    [Theory]
    [InlineData((int)RoutingDecisionKind.Eligible, (int)AddonOperationalStatus.Ready)]
    [InlineData((int)RoutingDecisionKind.VetoedForSession, (int)AddonOperationalStatus.Passive)]
    [InlineData((int)RoutingDecisionKind.WaitingForSteam, (int)AddonOperationalStatus.WaitingForSteam)]
    [InlineData((int)RoutingDecisionKind.SetupRequired, (int)AddonOperationalStatus.SetupRequired)]
    [InlineData((int)RoutingDecisionKind.Indeterminate, (int)AddonOperationalStatus.Indeterminate)]
    public void AddonStatus_MapsCanonicalRoutingDecision(int kindValue, int expectedValue)
    {
        var status = AddonStatusEvaluator.Map(new((RoutingDecisionKind)kindValue, RoutingDecisionReason.Eligible), new(ExternalControllerAssessmentStatus.Clear, 0, []));

        Assert.Equal((AddonOperationalStatus)expectedValue, status.Status);
    }

    [Fact]
    public void AddonStatus_ExternalControllerPresentationUsesFriendlyName()
    {
        var assessment = new ExternalControllerAssessment(ExternalControllerAssessmentStatus.ExternalPresent, 1, [Device("Xbox Wireless Controller", 0x045E, 0x0B13)]);

        var status = AddonStatusEvaluator.Map(new(RoutingDecisionKind.VetoedForSession, RoutingDecisionReason.ExternalControllerPresent), assessment);

        Assert.Equal("External physical controller detected: Xbox Wireless Controller.", status.Reason);
    }

    [Fact]
    public void ExternalControllerAssessment_HhcManagedEnvironmentFailsClosed()
    {
        var result = ExternalControllerAssessmentPolicy.ApplyEnvironmentSafety(
            new(ExternalControllerAssessmentStatus.Clear, 0, []),
            ControllerEnvironmentMode.HHCManaged,
            ControllerEnvironmentReadiness.Stable);

        Assert.Equal(ExternalControllerAssessmentStatus.Indeterminate, result.Status);
    }

    [Fact]
    public void ExternalControllerAssessment_ExternalControllerRemainsVetoInUnstableEnvironment()
    {
        var assessment = new ExternalControllerAssessment(
            ExternalControllerAssessmentStatus.ExternalPresent,
            1,
            [Device("Xbox Wireless Controller", 0x045E, 0x0B13)]);

        var result = ExternalControllerAssessmentPolicy.ApplyEnvironmentSafety(
            assessment,
            ControllerEnvironmentMode.HHCManaged,
            ControllerEnvironmentReadiness.Indeterminate);

        Assert.Same(assessment, result);
    }

    [Fact]
    public void ExternalControllerCards_ClearProducesOneClearCard()
    {
        var cards = ExternalControllerStatusCardFactory.Create(new(ExternalControllerAssessmentStatus.Clear, 0, []));

        var card = Assert.Single(cards);
        Assert.Equal("No External Controller", card.Name);
        Assert.Equal("Clear", card.Status);
    }

    [Fact]
    public void ExternalControllerCards_UseFriendlyNameAndListEveryController()
    {
        var cards = ExternalControllerStatusCardFactory.Create(new(ExternalControllerAssessmentStatus.ExternalPresent, 2, [Device("Wireless Controller", 0x054C, 0x09CC), Device("Xbox Wireless Controller", 0x045E, 0x0B13)]));

        Assert.Equal(2, cards.Count);
        Assert.Equal(["Wireless Controller", "Xbox Wireless Controller"], cards.Select(card => card.Name));
        Assert.Equal("VID 045E · PID 0B13", cards[1].Secondary);
    }

    [Fact]
    public void ExternalControllerCards_UseVidPidFallbackWhenFriendlyNameIsMissing()
    {
        var card = Assert.Single(ExternalControllerStatusCardFactory.Create(new(ExternalControllerAssessmentStatus.ExternalPresent, 1, [Device(null, 0x1234, 0x5678)])));

        Assert.Equal("Controller (VID 1234 / PID 5678)", card.Name);
        Assert.Equal("Connected", card.Status);
    }

    [Fact]
    public void ExternalControllerCards_IndeterminateDoesNotReportClear()
    {
        var card = Assert.Single(ExternalControllerStatusCardFactory.Create(new(ExternalControllerAssessmentStatus.Indeterminate, 0, [])));

        Assert.Equal("Indeterminate", card.Status);
    }

    [Fact]
    public async Task SystemStatusProvider_ReusesPrerequisiteAssessmentAndBuildsOneSnapshot()
    {
        var prerequisites = Prerequisites(PrerequisiteStatus.Missing);
        var provider = new SystemStatusProvider(new FakeDeviceProvider(), [new FakeSoftwareProvider(Software(ControllerSoftwareKind.MsiCenterM, SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.Running)), new FakeSoftwareProvider(Software(ControllerSoftwareKind.ClawTweaks, SoftwareInstallationStatus.NotInstalled, SoftwareRuntimeStatus.NotRunning)), new FakeSoftwareProvider(Software(ControllerSoftwareKind.HandheldCompanion, SoftwareInstallationStatus.NotInstalled, SoftwareRuntimeStatus.NotRunning))], new FakePrerequisiteInspector(prerequisites), () => SteamSessionState.FromRunningAppId(0), () => new(ExternalControllerAssessmentStatus.Clear, 0, []), () => true);

        var snapshot = await provider.CaptureAsync();

        Assert.Same(prerequisites, snapshot.Prerequisites);
        Assert.Equal([ControllerSoftwareKind.MsiCenterM, ControllerSoftwareKind.ClawTweaks, ControllerSoftwareKind.HandheldCompanion], snapshot.ControllerSoftware.Select(item => item.Kind));
        Assert.Equal(RoutingDecisionKind.SetupRequired, snapshot.RoutingDecision.Kind);
        Assert.Equal(AddonOperationalStatus.SetupRequired, snapshot.Addon.Status);
    }

    [Fact]
    public async Task SystemStatusProvider_CapturesExternalControllerAssessmentOncePerSnapshot()
    {
        var calls = 0;
        var provider = new SystemStatusProvider(
            new FakeDeviceProvider(),
            [
                new FakeSoftwareProvider(Software(ControllerSoftwareKind.MsiCenterM, SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.Running)),
                new FakeSoftwareProvider(Software(ControllerSoftwareKind.ClawTweaks, SoftwareInstallationStatus.NotInstalled, SoftwareRuntimeStatus.NotRunning)),
                new FakeSoftwareProvider(Software(ControllerSoftwareKind.HandheldCompanion, SoftwareInstallationStatus.NotInstalled, SoftwareRuntimeStatus.NotRunning))
            ],
            new FakePrerequisiteInspector(Prerequisites(PrerequisiteStatus.Ready)),
            () => SteamSessionState.FromRunningAppId(1),
            () =>
            {
                calls++;
                return new ExternalControllerAssessment(ExternalControllerAssessmentStatus.Clear, 0, []);
            },
            () => true);

        var snapshot = await provider.CaptureAsync();

        Assert.Equal(1, calls);
        Assert.Equal(ExternalControllerAssessmentStatus.Clear, snapshot.ExternalController.Status);
        Assert.Equal(RoutingDecisionKind.Eligible, snapshot.RoutingDecision.Kind);
        Assert.Equal(AddonOperationalStatus.Ready, snapshot.Addon.Status);
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

    [Theory]
    [InlineData("HKLM64", "MSI Center M", true)]
    [InlineData("HKLM32", "MSI Center M SDK", true)]
    [InlineData("HKCU", "MSI Center M", true)]
    [InlineData("HKLM32", "msi center m sdk", true)]
    [InlineData("HKLM32", "MSI Center", false)]
    [InlineData("HKLM32", "MSI Foundation Service", false)]
    [InlineData("HKLM32", "MSI Center SDK", false)]
    public void UninstallRegistrationProbe_UsesKnownNamesWithCaseInsensitiveExactMatch(string source, string displayName, bool expectedInstalled)
    {
        var probe = new TestUninstallProbe(new FakeUninstallRegistrationSource([new(source, displayName)]));
        Assert.Equal(expectedInstalled, probe.Detect().Installed);
    }

    [Fact]
    public void MsiCenterM_InstalledButNotRunning_ReportsInstalledReason()
    {
        var status = new MsiCenterMSoftwareStatusProvider(new FakeInstallationProbe(true), () => false).Capture();
        Assert.Equal(SoftwareInstallationStatus.Installed, status.Installation);
        Assert.Equal(SoftwareRuntimeStatus.NotRunning, status.Runtime);
        Assert.Equal("MsiCenterMInstalled", status.Reason);
    }

    [Fact]
    public void MsiCenterM_InstalledAndRunning_ReportsRunningReason()
    {
        var status = new MsiCenterMSoftwareStatusProvider(new FakeInstallationProbe(true), () => true).Capture();
        Assert.Equal(SoftwareInstallationStatus.Installed, status.Installation);
        Assert.Equal(SoftwareRuntimeStatus.Running, status.Runtime);
        Assert.Equal("MsiCenterMRunning", status.Reason);
    }

    private static ControllerSoftwareStatus[] SoftwareStates() => [Software(ControllerSoftwareKind.MsiCenterM, SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.NotRunning), Software(ControllerSoftwareKind.ClawTweaks, SoftwareInstallationStatus.NotInstalled, SoftwareRuntimeStatus.NotRunning), Software(ControllerSoftwareKind.HandheldCompanion, SoftwareInstallationStatus.NotInstalled, SoftwareRuntimeStatus.NotRunning)];
    private static ControllerSoftwareStatus Software(ControllerSoftwareKind kind, SoftwareInstallationStatus installation, SoftwareRuntimeStatus runtime) => new(kind, kind.ToString(), installation, runtime, "test");
    private static RuntimePrerequisiteAssessment Prerequisites(PrerequisiteStatus status) => new(new(PrerequisiteKind.HidHide, status, "test"), new(PrerequisiteKind.UsbIpWin2, status, "test"), new(PrerequisiteKind.Viiper, status, "test"));
    private static ControllerDeviceInfo Device(string? friendlyName, ushort? vendorId, ushort? productId) => new("USB\\test", null, null, [], "USB", [], [], "HIDClass", null, null, vendorId, productId, true, friendlyName);
    private sealed class FakeDeviceProvider : IDeviceInformationProvider { public DeviceStatusSnapshot Capture() => new("MSI", "Claw", ["Intel Arc"]); }
    private sealed class FakeSoftwareProvider(ControllerSoftwareStatus status) : IControllerSoftwareStatusProvider { public ControllerSoftwareStatus Capture() => status; }
    private sealed class FakePrerequisiteInspector(RuntimePrerequisiteAssessment assessment) : IRuntimePrerequisiteInspector { public RuntimePrerequisiteAssessment Inspect() => assessment; }
    private sealed class FakeHhcRuntime(bool running) : SteamInputAddonforClaw.Startup.IHandheldCompanionRuntimeDetector { public bool IsRunning() => running; }
    private sealed class FakeInstallationProbe(bool installed) : IApplicationInstallationProbe { public ApplicationInstallationInfo Detect() => new(installed, "test"); }
    private sealed class TestUninstallProbe(IUninstallRegistrationSource source) : UninstallRegistrationInstallationProbe(MsiCenterMIdentity.InstallationDisplayNames, [], source) { }
    private sealed class FakeUninstallRegistrationSource(IReadOnlyList<InstalledApplicationRegistration> registrations) : IUninstallRegistrationSource { public IReadOnlyList<InstalledApplicationRegistration> Enumerate() => registrations; }
}
