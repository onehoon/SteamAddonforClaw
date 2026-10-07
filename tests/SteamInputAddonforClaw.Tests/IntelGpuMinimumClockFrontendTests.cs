using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Performance;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class IntelGpuMinimumClockFrontendTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"MinimumGpuClockFrontend-{Guid.NewGuid():N}");

    [Fact]
    public async Task Developer_probe_ownership_refuses_production_mutations_before_persistence()
    {
        Directory.CreateDirectory(_directory);
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _directory;
        var profilesPath = Path.Combine(_directory, "profiles.json");
        var hardware = new FakeMinimumClockControl();
        using var runtime = new IntelGpuMinimumClockRuntime(
            new ProfileStore(profilesPath), new ProfileMutationGate(), hardware,
            () => AcDcPowerSource.AC, Path.Combine(_directory, "minimum-clock.json"));
        var control = CreateControl(runtime, developerModified: true);

        var capture = await control.CaptureGpuMinimumClockAsync();
        var enable = await control.SetDeviceGpuMinimumClockEnabledAsync(true);
        var rail = await control.SetDeviceGpuMinimumClockAcAsync(1625);

        Assert.True(capture.Available);
        Assert.Equal(FrontendGpuMinimumClockMutationOutcome.Unavailable, enable.Outcome);
        Assert.Contains("before enabling", enable.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FrontendGpuMinimumClockMutationOutcome.Unavailable, rail.Outcome);
        Assert.False(File.Exists(profilesPath));
        Assert.Equal(0, hardware.SetCalls);
    }

    [Fact]
    public void Production_ownership_blocks_only_developer_frequency_range_operations()
    {
        Assert.True(InProcessAddonFrontendControl.ShouldBlockDeveloperFrequencyMutation(
            FrontendIntelGpuFrequencyProbeOperation.SetMaxMax, productionOwnsFrequency: true));
        Assert.True(InProcessAddonFrontendControl.ShouldBlockDeveloperFrequencyMutation(
            FrontendIntelGpuFrequencyProbeOperation.RestoreOriginalFrequency, productionOwnsFrequency: true));
        Assert.False(InProcessAddonFrontendControl.ShouldBlockDeveloperFrequencyMutation(
            FrontendIntelGpuFrequencyProbeOperation.SetTestPl1, productionOwnsFrequency: true));
        Assert.False(InProcessAddonFrontendControl.ShouldBlockDeveloperFrequencyMutation(
            FrontendIntelGpuFrequencyProbeOperation.RestoreOriginalPower, productionOwnsFrequency: true));
        Assert.False(InProcessAddonFrontendControl.ShouldBlockDeveloperFrequencyMutation(
            FrontendIntelGpuFrequencyProbeOperation.SetMaxMax, productionOwnsFrequency: false));
    }

    private InProcessAddonFrontendControl CreateControl(IntelGpuMinimumClockRuntime runtime, bool developerModified)
    {
        var settings = new StartupSettingsCoordinator(
            new AppSettings(), new SettingsStore(Path.Combine(_directory, "settings.json")), new FakeStartupManager());
        return new InProcessAddonFrontendControl(
            settings,
            new ThrowingSystemStatusProvider(),
            null,
            intelGpuMinimumClockRuntime: runtime,
            developerGpuFrequencyProbeModified: () => developerModified);
    }

    public void Dispose()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class FakeStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class ThrowingSystemStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Status capture is not part of these tests.");
    }

    private sealed class FakeMinimumClockControl : IIntelGpuMinimumClockControl
    {
        public int SetCalls { get; private set; }
        public IntelGpuMinimumClockNativeCapability Initialize() => Capability;
        public IntelGpuMinimumClockNativeCapability Reinitialize() => Capability;
        public IntelGpuFrequencyRange GetRange() => new(-1, -1);
        public uint SetRange(IntelGpuFrequencyRange range) { SetCalls++; return 0; }
        public void Dispose() { }

        private static IntelGpuMinimumClockNativeCapability Capability { get; } = new(
            true, null, "Intel Integrated GPU", 0x8086, 0x1234, true, 300, 2300,
            [1400, 1525, 1625, 1725, 1825]);
    }
}
