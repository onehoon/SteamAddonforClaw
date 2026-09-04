using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
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

    // ---- Full1902 Cleanup A/D: non-owned Addon status (Center M Enabled / startup still settling)
    //      derives from the safety/setup facts, not a Steam-session routing decision and not a
    //      third-party controller-manager compatibility scan. ----

    [Fact]
    public async Task NonOwnedStatus_HealthySupportedEnvironment_IsPassiveCenterMOwned()
    {
        var snapshot = await NonOwnedProvider(Prerequisites(PrerequisiteStatus.Ready)).CaptureAsync();

        Assert.Equal(AddonOperationalStatus.Passive, snapshot.Addon.Status);
        Assert.Contains("MSI Center M", snapshot.Addon.Reason);
    }

    [Fact]
    public async Task NonOwnedStatus_MissingPrerequisites_IsSetupRequired()
    {
        var snapshot = await NonOwnedProvider(Prerequisites(PrerequisiteStatus.Missing)).CaptureAsync();

        Assert.Equal(AddonOperationalStatus.SetupRequired, snapshot.Addon.Status);
    }

    [Fact]
    public async Task NonOwnedStatus_UnsupportedHardware_IsUnsupported()
    {
        var provider = new SystemStatusProvider(
            new FakeDeviceProvider(), SupportedProbeFactory(),
            new FakeHardwareEvaluator(new(HardwareCompatibilityStatus.Unsupported, null, null, "test")),
            new FakePrerequisiteInspector(Prerequisites(PrerequisiteStatus.Ready)),
            () => new SteamPresentationSnapshot(0, false), () => true);

        var snapshot = await provider.CaptureAsync();

        Assert.Equal(AddonOperationalStatus.Unsupported, snapshot.Addon.Status);
        Assert.Contains("handheld model", snapshot.Addon.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SystemStatusProvider_ReusesPrerequisiteAssessmentAndBuildsOneSnapshot()
    {
        var prerequisites = Prerequisites(PrerequisiteStatus.Missing);
        var provider = new SystemStatusProvider(new FakeDeviceProvider(), SupportedProbeFactory(), SupportedHardware(), new FakePrerequisiteInspector(prerequisites), () => new SteamPresentationSnapshot(0, false), () => true);

        var snapshot = await provider.CaptureAsync();

        Assert.Same(prerequisites, snapshot.Prerequisites);
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
            new FakePrerequisiteInspector(Prerequisites(PrerequisiteStatus.Ready)),
            () => new SteamPresentationSnapshot(1, false),
            () => false);

        var snapshot = await provider.CaptureAsync();

        Assert.False(snapshot.RecoverySafe);
        Assert.Equal(AddonOperationalStatus.Indeterminate, snapshot.Addon.Status);
    }

    [Fact]
    public async Task SystemStatusProvider_SteamCard_UsesRawPresentationFacts()
    {
        var running = await NonOwnedProvider(Prerequisites(PrerequisiteStatus.Ready), new SteamPresentationSnapshot(480, false)).CaptureAsync();
        Assert.True(running.Steam.IsActive);
        Assert.Equal(480u, running.Steam.RunningAppId);
        Assert.Equal(SteamSessionSource.Actual, running.Steam.Source);

        var bigPicture = await NonOwnedProvider(Prerequisites(PrerequisiteStatus.Ready), new SteamPresentationSnapshot(0, true)).CaptureAsync();
        Assert.True(bigPicture.Steam.IsActive);
        Assert.Equal(SteamSessionSource.BigPicture, bigPicture.Steam.Source);

        var idle = await NonOwnedProvider(Prerequisites(PrerequisiteStatus.Ready), new SteamPresentationSnapshot(0, false)).CaptureAsync();
        Assert.False(idle.Steam.IsActive);
    }

    private static SystemStatusProvider NonOwnedProvider(RuntimePrerequisiteAssessment prerequisites, SteamPresentationSnapshot? presentation = null) =>
        new(new FakeDeviceProvider(), SupportedProbeFactory(), SupportedHardware(),
            new FakePrerequisiteInspector(prerequisites),
            () => presentation ?? new SteamPresentationSnapshot(0, false), () => true);

    // ---- Full1902 0903 cleanup section 9.1: optional Full1902 Addon-status override ----

    private static SystemStatusProvider ProviderWithFull1902Override(Func<AddonStatusSnapshot?>? capture) =>
        new(new FakeDeviceProvider(), SupportedProbeFactory(), SupportedHardware(),
            new FakePrerequisiteInspector(Prerequisites(PrerequisiteStatus.Ready)),
            () => new SteamPresentationSnapshot(0, false), () => true, capture);

    [Fact]
    public async Task Full1902_override_null_keeps_the_non_owned_addon_status()
    {
        var snapshot = await ProviderWithFull1902Override(() => null).CaptureAsync();

        Assert.Equal(AddonOperationalStatus.Passive, snapshot.Addon.Status);
    }

    [Fact]
    public async Task Full1902_override_ready_replaces_the_final_addon_status()
    {
        var snapshot = await ProviderWithFull1902Override(
            () => new AddonStatusSnapshot(AddonOperationalStatus.Ready, "Full1902 controller authority is active (SteamDeck)."))
            .CaptureAsync();

        Assert.Equal(AddonOperationalStatus.Ready, snapshot.Addon.Status);
        Assert.Contains("SteamDeck", snapshot.Addon.Reason);
    }

    [Fact]
    public async Task Full1902_override_that_throws_does_not_crash_capture_and_falls_back_to_non_owned()
    {
        var snapshot = await ProviderWithFull1902Override(
            () => throw new InvalidOperationException("status read failed")).CaptureAsync();

        Assert.Equal(AddonOperationalStatus.Passive, snapshot.Addon.Status);
    }

    private static RuntimePrerequisiteAssessment Prerequisites(PrerequisiteStatus status) => new(new(PrerequisiteKind.HidHide, status, "test"), new(PrerequisiteKind.UsbIpWin2, status, "test"), new(PrerequisiteKind.Viiper, status, "test"));
    private static IWindowsDeviceProbeContextFactory SupportedProbeFactory() => new FakeProbeFactory(new(DeviceProbeCaptureStatus.Success, new DeviceProbeContext(baseBoardProduct: "MS-1T91"), "test"));
    private static IHardwareCompatibilityEvaluator SupportedHardware() => new FakeHardwareEvaluator(new(HardwareCompatibilityStatus.Supported, new HandheldDeviceId("msi.claw"), new HandheldDeviceModelId("msi.claw.cg3em"), "test"));
    private sealed class FakeDeviceProvider : IDeviceInformationProvider { public DeviceStatusSnapshot Capture(DeviceProbeContext context) => new("MSI", "Claw", context.BaseBoardProduct ?? "Unknown", ["Intel Arc"]); }
    private sealed class FakeProbeFactory(DeviceProbeContextCapture capture) : IWindowsDeviceProbeContextFactory { public DeviceProbeContextCapture Capture() => capture; }
    private sealed class FakeHardwareEvaluator(HardwareCompatibilityAssessment assessment) : IHardwareCompatibilityEvaluator { public HardwareCompatibilityAssessment Evaluate(DeviceProbeContextCapture capture) => assessment; }
    private sealed class FakePrerequisiteInspector(RuntimePrerequisiteAssessment assessment) : IRuntimePrerequisiteInspector { public RuntimePrerequisiteAssessment Inspect() => assessment; }
}
