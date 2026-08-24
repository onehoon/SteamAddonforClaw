using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
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
            Software(ControllerSoftwareKind.MsiCenterM, SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.Running)
        ]);

        Assert.Equal([ControllerSoftwareKind.MsiCenterM], sorted.Select(item => item.Kind));
    }

    [Theory]
    [InlineData((int)RoutingDecisionKind.Eligible, (int)AddonOperationalStatus.Ready)]
    [InlineData((int)RoutingDecisionKind.WaitingForSteam, (int)AddonOperationalStatus.WaitingForSteam)]
    [InlineData((int)RoutingDecisionKind.SetupRequired, (int)AddonOperationalStatus.SetupRequired)]
    [InlineData((int)RoutingDecisionKind.Indeterminate, (int)AddonOperationalStatus.Indeterminate)]
    public void AddonStatus_MapsCanonicalRoutingDecision(int kindValue, int expectedValue)
    {
        var status = AddonStatusEvaluator.Map(new((RoutingDecisionKind)kindValue, RoutingDecisionReason.Eligible), Compatibility(ControllerEnvironmentCompatibilityStatus.Supported));

        Assert.Equal((AddonOperationalStatus)expectedValue, status.Status);
    }

    [Fact]
    public void AddonStatus_UnsupportedHardwareMapsToUnsupportedPresentation()
    {
        var status = AddonStatusEvaluator.Map(
            new(RoutingDecisionKind.Passive, RoutingDecisionReason.UnsupportedDevice),
            Compatibility(ControllerEnvironmentCompatibilityStatus.Supported));

        Assert.Equal(AddonOperationalStatus.Unsupported, status.Status);
        Assert.Contains("handheld model", status.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SystemStatusProvider_ReusesPrerequisiteAssessmentAndBuildsOneSnapshot()
    {
        var prerequisites = Prerequisites(PrerequisiteStatus.Missing);
        var provider = new SystemStatusProvider(new FakeDeviceProvider(), SupportedProbeFactory(), SupportedHardware(), [new FakeSoftwareProvider(Software(ControllerSoftwareKind.MsiCenterM, SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.Running))], new FakePrerequisiteInspector(prerequisites), () => SteamSessionState.FromRunningAppId(0), () => true);

        var snapshot = await provider.CaptureAsync();

        Assert.Same(prerequisites, snapshot.Prerequisites);
        Assert.Equal([ControllerSoftwareKind.MsiCenterM], snapshot.ControllerSoftware.Select(item => item.Kind));
        Assert.Equal(RoutingDecisionKind.SetupRequired, snapshot.RoutingDecision.Kind);
        Assert.Equal(AddonOperationalStatus.SetupRequired, snapshot.Addon.Status);
        Assert.True(snapshot.RecoverySafe);
    }

    [Fact]
    public async Task SystemStatusProvider_PreservesActualRecoverySafetyInsteadOfInferringPresentationStatus()
    {
        var provider = new SystemStatusProvider(
            new FakeDeviceProvider(),
            SupportedProbeFactory(),
            SupportedHardware(),
            [
                new FakeSoftwareProvider(Software(ControllerSoftwareKind.MsiCenterM, SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.Running))
            ],
            new FakePrerequisiteInspector(Prerequisites(PrerequisiteStatus.Ready)),
            () => SteamSessionState.FromRunningAppId(1),
            () => false);

        var snapshot = await provider.CaptureAsync();

        Assert.False(snapshot.RecoverySafe);
        Assert.Equal(RoutingDecisionKind.Indeterminate, snapshot.RoutingDecision.Kind);
        Assert.Equal(AddonOperationalStatus.Indeterminate, snapshot.Addon.Status);
    }

    [Fact]
    public void MsiCenterM_BootBaselineWithoutDesktopUi_IsOperational()
    {
        var assessment = new MsiCenterMRuntimeDetector(new FakeMsiRuntimeSignals(new(true, true, true, true, true, false))).Detect();

        Assert.Equal(SoftwareRuntimeStatus.Running, assessment.Status);
        Assert.Equal("MsiCenterMOperational", assessment.Reason);
    }

    [Fact]
    public void MsiCenterM_BackendOperationalWithoutQuickSettingsWidget_IsOperational()
    {
        var assessment = new MsiCenterMRuntimeDetector(new FakeMsiRuntimeSignals(new(true, true, true, true, false, false))).Detect();

        Assert.Equal(SoftwareRuntimeStatus.Running, assessment.Status);
        Assert.Equal("MsiCenterMOperational", assessment.Reason);
        Assert.False(assessment.QuickSettingsWidgetRunning);
    }

    [Fact]
    public void MsiCenterM_DesktopUiDoesNotAffectOperationalAssessment()
    {
        var assessment = new MsiCenterMRuntimeDetector(new FakeMsiRuntimeSignals(new(true, true, true, true, true, true))).Detect();

        Assert.Equal(SoftwareRuntimeStatus.Running, assessment.Status);
        Assert.Equal("MsiCenterMOperational", assessment.Reason);
    }

    [Fact]
    public void MsiCenterM_DesktopUiOnly_IsNotRunning()
    {
        var assessment = new MsiCenterMRuntimeDetector(new FakeMsiRuntimeSignals(new(false, false, false, true, false, true))).Detect();

        Assert.Equal(SoftwareRuntimeStatus.NotRunning, assessment.Status);
        Assert.Equal("MsiCenterMNotRunning", assessment.Reason);
    }

    [Fact]
    public void MsiCenterM_UsesExactStockBackendIdentities()
    {
        Assert.Equal("MSI_Center_M_Server", MsiCenterMIdentity.ServerProcessName);
        Assert.Equal("MSI_Center_M_Server_ControlMode", MsiCenterMIdentity.ControlModeProcessName);
        Assert.NotEqual("Center_M_Server", MsiCenterMIdentity.ServerProcessName);
    }

    [Theory]
    [InlineData(false, true, true, true, true, "MsiCenterMFoundationServiceNotReady")]
    [InlineData(true, false, true, true, true, "MsiCenterMBackendNotReady")]
    [InlineData(true, true, false, true, true, "MsiCenterMControlModeNotReady")]
    [InlineData(true, true, true, false, true, "MsiCenterMQuickSettingsNotReady")]
    public void MsiCenterM_PartialStack_IsStarting(bool foundation, bool server, bool controlMode, bool package, bool widget, string reason)
    {
        var assessment = new MsiCenterMRuntimeDetector(new FakeMsiRuntimeSignals(new(foundation, server, controlMode, package, widget, false))).Detect();

        Assert.Equal(SoftwareRuntimeStatus.Starting, assessment.Status);
        Assert.Equal(reason, assessment.Reason);
    }

    [Fact]
    public void MsiCenterM_RuntimeInspectionFailure_IsIndeterminate()
    {
        var assessment = new MsiCenterMRuntimeDetector(new ThrowingMsiRuntimeSignals()).Detect();

        Assert.Equal(SoftwareRuntimeStatus.Indeterminate, assessment.Status);
        Assert.Equal("MsiCenterMInspectionFailed", assessment.Reason);
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
    public void MsiCenterM_InstalledButNotRunning_PreservesInstallation()
    {
        var status = new MsiCenterMSoftwareStatusProvider(new FakeInstallationProbe(true), new MsiCenterMRuntimeDetector(new FakeMsiRuntimeSignals(new(false, false, false, true, false, false)))).Capture();
        Assert.Equal(SoftwareInstallationStatus.Installed, status.Installation);
        Assert.Equal(SoftwareRuntimeStatus.NotRunning, status.Runtime);
        Assert.Equal("MsiCenterMNotRunning", status.Reason);
    }

    [Fact]
    public void MsiCenterM_RunningPromotesInstallation()
    {
        var status = new MsiCenterMSoftwareStatusProvider(new FakeInstallationProbe(false), new MsiCenterMRuntimeDetector(new FakeMsiRuntimeSignals(new(true, true, true, true, true, false)))).Capture();
        Assert.Equal(SoftwareInstallationStatus.Installed, status.Installation);
        Assert.Equal(SoftwareRuntimeStatus.Running, status.Runtime);
        Assert.Equal("MsiCenterMOperational", status.Reason);
    }

    [Theory]
    [InlineData((int)SoftwareInstallationStatus.Installed, (int)SoftwareRuntimeStatus.Starting, "Starting")]
    [InlineData((int)SoftwareInstallationStatus.Installed, (int)SoftwareRuntimeStatus.NotRunning, "Installed / Not running")]
    public void SoftwareStatusFormatting_PreservesStarting(int installationValue, int runtimeValue, string expected) =>
        Assert.Equal(expected, ControllerSoftwareStatusFormatter.Format(Software(ControllerSoftwareKind.MsiCenterM, (SoftwareInstallationStatus)installationValue, (SoftwareRuntimeStatus)runtimeValue)));

    [Theory]
    [InlineData(@"C:\Packages\MSIQuickSettings_1.0", @"C:\Packages\MSIQuickSettings_1.0\Gamebar_Widget.exe", true)]
    [InlineData("C:\\Packages\\MSIQuickSettings_1.0\\", @"C:\Packages\MSIQuickSettings_1.0\Gamebar_Widget.exe", true)]
    [InlineData(@"C:\Packages\MSIQuickSettings", @"C:\Packages\MSIQuickSettings_evil\Gamebar_Widget.exe", false)]
    [InlineData(@"C:\Packages\MSIQuickSettings_1.0", @"C:\Packages\AnotherPackage\Gamebar_Widget.exe", false)]
    [InlineData(@"C:\Packages\MSIQuickSettings_1.0", @"C:\Packages\MSIQuickSettings_1.0\Gamebar_Widget_Backup.exe", false)]
    public void QuickSettingsWidgetOwnership_RequiresPackageChildBoundaryAndExactFilename(string root, string executable, bool expected) =>
        Assert.Equal(expected, WindowsMsiCenterMRuntimeSignalSource.IsPackageOwnedWidget(root, executable));

    private static ControllerSoftwareStatus[] SoftwareStates() => [Software(ControllerSoftwareKind.MsiCenterM, SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.NotRunning)];
    private static ControllerSoftwareStatus Software(ControllerSoftwareKind kind, SoftwareInstallationStatus installation, SoftwareRuntimeStatus runtime) => new(kind, kind.ToString(), installation, runtime, "test");
    private static RuntimePrerequisiteAssessment Prerequisites(PrerequisiteStatus status) => new(new(PrerequisiteKind.HidHide, status, "test"), new(PrerequisiteKind.UsbIpWin2, status, "test"), new(PrerequisiteKind.Viiper, status, "test"));
    private static ControllerEnvironmentCompatibilityAssessment Compatibility(ControllerEnvironmentCompatibilityStatus status) => new(status, status == ControllerEnvironmentCompatibilityStatus.Supported ? ControllerEnvironmentCompatibilityReason.StockCenterMOnlySupported : ControllerEnvironmentCompatibilityReason.ControllerSoftwareStateIndeterminate);
    private static ControllerDeviceInfo Device(string? friendlyName, ushort? vendorId, ushort? productId) => new("USB\\test", null, null, [], "USB", [], [], "HIDClass", null, null, vendorId, productId, true, friendlyName);
    private static IWindowsDeviceProbeContextFactory SupportedProbeFactory() => new FakeProbeFactory(new(DeviceProbeCaptureStatus.Success, new DeviceProbeContext(baseBoardProduct: "MS-1T91"), "test"));
    private static IHardwareCompatibilityEvaluator SupportedHardware() => new FakeHardwareEvaluator(new(HardwareCompatibilityStatus.Supported, new HandheldDeviceId("msi.claw"), new HandheldDeviceModelId("msi.claw.cg3em"), "test"));
    private sealed class FakeDeviceProvider : IDeviceInformationProvider { public DeviceStatusSnapshot Capture(DeviceProbeContext context) => new("MSI", "Claw", context.BaseBoardProduct ?? "Unknown", ["Intel Arc"]); }
    private sealed class FakeProbeFactory(DeviceProbeContextCapture capture) : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() => capture; }
    private sealed class FakeHardwareEvaluator(HardwareCompatibilityAssessment assessment) : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture capture) => assessment; }
    private sealed class FakeSoftwareProvider(ControllerSoftwareStatus status) : IControllerSoftwareStatusProvider { public ControllerSoftwareStatus Capture() => status; }
    private sealed class FakeInstallationProbe(bool installed) : IApplicationInstallationProbe { public ApplicationInstallationInfo Detect() => new(installed, "test"); }
    private sealed class FakePrerequisiteInspector(RuntimePrerequisiteAssessment assessment) : IRuntimePrerequisiteInspector { public RuntimePrerequisiteAssessment Inspect() => assessment; }
    private sealed class FakeMsiRuntimeSignals(MsiCenterMRuntimeSignals signals) : IMsiCenterMRuntimeSignalSource { public MsiCenterMRuntimeSignals Capture() => signals; }
    private sealed class ThrowingMsiRuntimeSignals : IMsiCenterMRuntimeSignalSource { public MsiCenterMRuntimeSignals Capture() => throw new InvalidOperationException(); }
    private sealed class TestUninstallProbe(IUninstallRegistrationSource source) : UninstallRegistrationInstallationProbe(MsiCenterMIdentity.InstallationDisplayNames, [], source) { }
    private sealed class FakeUninstallRegistrationSource(IReadOnlyList<InstalledApplicationRegistration> registrations) : IUninstallRegistrationSource { public IReadOnlyList<InstalledApplicationRegistration> Enumerate() => registrations; }
}
