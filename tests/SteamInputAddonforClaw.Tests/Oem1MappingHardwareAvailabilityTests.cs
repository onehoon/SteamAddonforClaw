using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// The frontend half of the OEM1 hardware-availability gate: bootstrap must report whether the
/// Center M Button feature exists on this machine at all, taken verbatim from the runtime's single
/// startup hardware-support result -- and that fact must be independent of Steam/BPM state and the
/// persisted remapping switch.
/// </summary>
/// <remarks>
/// CI fix: this touches the shared static
/// <see cref="SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride"/> via
/// <see cref="CreateControl"/>, so it must join the same "AppLog" collection.
/// </remarks>
[Collection("AppLog")]
public sealed class Oem1MappingHardwareAvailabilityTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task Supported_hardware_reports_OEM1_mapping_available()
    {
        var control = CreateControl(new AppSettings(), oem1MappingAvailable: true, out _);

        Assert.True((await control.GetBootstrapAsync()).Oem1MappingAvailable);
    }

    [Fact]
    public async Task Unsupported_hardware_reports_OEM1_mapping_unavailable_without_touching_the_persisted_mapping()
    {
        // Deliberately remapping-ON with a non-default binding: the hardware gate must suppress the
        // FEATURE, never rewrite or normalize what the user saved.
        var saved = Oem1MappingSettings.Default with
        {
            RemappingEnabled = true,
            NormalDouble = new Oem1SlotBinding { Action = Oem1Action.SteamBigPicture }
        };
        var control = CreateControl(new AppSettings { Oem1Mapping = saved }, oem1MappingAvailable: false, out _);

        var bootstrap = await control.GetBootstrapAsync();

        Assert.False(bootstrap.Oem1MappingAvailable);
        // The feature is reported unavailable while the saved mapping is reported exactly as stored:
        // the same settings carried to real Claw hardware still have the user's switch and bindings.
        Assert.Equal(saved, bootstrap.Settings.Oem1Mapping);
    }

    [Fact]
    public async Task A_settings_mutation_never_changes_OEM1_hardware_availability()
    {
        var control = CreateControl(new AppSettings(), oem1MappingAvailable: true, out _);

        await control.SetOem1MappingAsync(Oem1MappingSettings.Default with { RemappingEnabled = false });
        Assert.True((await control.GetBootstrapAsync()).Oem1MappingAvailable);

        await control.SetOem1MappingAsync(Oem1MappingSettings.Default with { RemappingEnabled = true });
        Assert.True((await control.GetBootstrapAsync()).Oem1MappingAvailable);
    }

    [Fact]
    public async Task A_settings_mutation_cannot_make_unsupported_hardware_report_available()
    {
        var control = CreateControl(new AppSettings(), oem1MappingAvailable: false, out _);

        await control.SetOem1MappingAsync(Oem1MappingSettings.Default with { RemappingEnabled = false });

        Assert.False((await control.GetBootstrapAsync()).Oem1MappingAvailable);
    }

    private InProcessAddonFrontendControl CreateControl(AppSettings settings, bool oem1MappingAvailable, out SettingsStore store)
    {
        // The bootstrap snapshot reports the log directory, which otherwise resolves through the
        // Velopack locator that only exists in an installed app.
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _testDirectory;
        store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        var coordinator = new StartupSettingsCoordinator(settings, store, new NoOpStartupManager());
        return new InProcessAddonFrontendControl(
            coordinator,
            new ThrowingSystemStatusProvider(),
            null,
            new DeveloperTestModeState(),
            "",
            oem1MappingAvailable: oem1MappingAvailable);
    }

    public void Dispose()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = null;
        if (Directory.Exists(_testDirectory)) Directory.Delete(_testDirectory, recursive: true);
    }

    private sealed class NoOpStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class ThrowingSystemStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Status capture is not part of these tests.");
    }
}
