using SteamInputAddonforClaw.Contracts.ControllerLed;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class ControllerLedFrontendTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.ControllerLedFrontend.{Guid.NewGuid():N}");

    [Fact]
    public async Task Persists_before_best_effort_apply_and_keeps_preference_when_apply_fails()
    {
        AppLog.DirectoryOverride = _directory;
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), store, new NoOpStartupManager());
        var requested = new ControllerLedSettings(true, 38, 10, 20, 30);
        var callbackCount = 0;
        var control = new InProcessAddonFrontendControl(
            coordinator,
            new ThrowingStatusProvider(),
            null,
            controllerLedAvailable: true,
            applyControllerLedSettings: (settings, _) =>
            {
                callbackCount++;
                Assert.Equal(settings, store.Load().ControllerLed);
                throw new IOException("temporary HID failure");
            });

        var snapshot = await control.SetControllerLedSettingsAsync(requested);

        Assert.Equal(1, callbackCount);
        Assert.Equal(requested, snapshot.ControllerLed);
        Assert.Equal(requested, coordinator.ControllerLed);
        Assert.True((await control.GetBootstrapAsync()).ControllerLedAvailable);
    }

    [Fact]
    public async Task Unsupported_authority_or_invalid_brightness_does_not_persist_or_apply()
    {
        AppLog.DirectoryOverride = _directory;
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
        var original = new ControllerLedSettings(false, 73, 1, 2, 3);
        store.Save(new AppSettings { ControllerLed = original });
        var coordinator = new StartupSettingsCoordinator(new AppSettings { ControllerLed = original }, store, new NoOpStartupManager());
        var callbackCount = 0;
        var unavailable = new InProcessAddonFrontendControl(
            coordinator, new ThrowingStatusProvider(), null,
            controllerLedAvailable: false,
            applyControllerLedSettings: (_, _) => { callbackCount++; return Task.CompletedTask; });

        var blocked = await unavailable.SetControllerLedSettingsAsync(new(true, 50, 255, 0, 0));
        Assert.Equal(original, blocked.ControllerLed);
        Assert.False((await unavailable.GetBootstrapAsync()).ControllerLedAvailable);

        var available = new InProcessAddonFrontendControl(
            coordinator, new ThrowingStatusProvider(), null,
            controllerLedAvailable: true,
            applyControllerLedSettings: (_, _) => { callbackCount++; return Task.CompletedTask; });
        var invalid = await available.SetControllerLedSettingsAsync(original with { Brightness = 101 });

        Assert.Equal(original, invalid.ControllerLed);
        Assert.Equal(original, store.Load().ControllerLed);
        Assert.Equal(0, callbackCount);
    }

    [Fact]
    public async Task A2vm_led_read_probe_reports_candidate_and_does_not_claim_write_safety_or_hardware_validation()
    {
        AppLog.DirectoryOverride = _directory;
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), new SettingsStore(Path.Combine(_directory, "settings.json")), new NoOpStartupManager());
        var control = new InProcessAddonFrontendControl(
            coordinator,
            new ThrowingStatusProvider(),
            null,
            controllerLedProfileReadProbe: _ => Task.FromResult(new MsiClawLedProfileReadProbeResult(
                MsiClawLedProfileReadProbeOutcome.CandidateReadbackParsed, 0x0230, 1, 3, 100, "CandidateReadbackParsed")));

        var result = await control.RunControllerLedProfileReadProbeAsync();

        Assert.Equal(FrontendControllerLedProfileReadProbeOutcome.CandidateReadbackParsed, result.Outcome);
        Assert.Equal((ushort)0x0230, result.FirmwareVersion);
        Assert.Equal((ushort)0x024A, result.CandidateAddress);
        Assert.True(result.ReadResponseValid);
        Assert.Contains("does not verify that the candidate address is safe to write", result.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("CG3EM", result.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A2vm_led_read_probe_without_runtime_hook_returns_unavailable()
    {
        AppLog.DirectoryOverride = _directory;
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), new SettingsStore(Path.Combine(_directory, "settings.json")), new NoOpStartupManager());
        var control = new InProcessAddonFrontendControl(coordinator, new ThrowingStatusProvider(), null);

        var result = await control.RunControllerLedProfileReadProbeAsync();

        Assert.Equal(FrontendControllerLedProfileReadProbeOutcome.Unavailable, result.Outcome);
        Assert.Equal((ushort)0x024A, result.CandidateAddress);
        Assert.False(result.ReadResponseValid);
    }

    public void Dispose()
    {
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class NoOpStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class ThrowingStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
