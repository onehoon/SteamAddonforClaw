using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>App UI PR-C section 22.8: bootstrap reports one <c>FrontButtonMappingAvailable</c> fact
/// taken verbatim from the runtime's single startup hardware-support result -- independent of any
/// Steam/BPM/presentation/settings state, and never resurfacing the removed two-boolean split.</summary>
[Collection("AppLog")]
public sealed class FrontButtonMappingHardwareAvailabilityTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void The_old_two_availability_booleans_no_longer_exist()
    {
        Assert.Null(typeof(FrontendBootstrapSnapshot).GetProperty("Oem1MappingAvailable"));
        Assert.Null(typeof(FrontendBootstrapSnapshot).GetProperty("WingMappingAvailable"));
        Assert.NotNull(typeof(FrontendBootstrapSnapshot).GetProperty("FrontButtonMappingAvailable"));
    }

    [Fact]
    public async Task Supported_hardware_reports_the_feature_available()
    {
        var control = CreateControl(new AppSettings(), available: true);
        Assert.True((await control.GetBootstrapAsync()).FrontButtonMappingAvailable);
    }

    [Fact]
    public async Task Unsupported_hardware_reports_unavailable_without_touching_the_saved_mapping()
    {
        var saved = FrontButtonMappingSettings.Default.With(
            FrontButtonKind.Gamebar, FrontButtonDomain.Normal, FrontButtonBinding.Of(FrontButtonAction.SteamBigPicture));
        var control = CreateControl(new AppSettings { FrontButtonMapping = saved }, available: false);

        var bootstrap = await control.GetBootstrapAsync();

        Assert.False(bootstrap.FrontButtonMappingAvailable);
        Assert.Equal(saved, bootstrap.Settings.FrontButtonMapping);
    }

    [Fact]
    public async Task A_settings_mutation_never_changes_hardware_availability()
    {
        var control = CreateControl(new AppSettings(), available: true);

        await control.SetFrontButtonMappingAsync(FrontButtonMappingSettings.Default.With(
            FrontButtonKind.Gamebar, FrontButtonDomain.Steam, FrontButtonBinding.Of(FrontButtonAction.QuickSettingsOverlay)));

        Assert.True((await control.GetBootstrapAsync()).FrontButtonMappingAvailable);
    }

    private InProcessAddonFrontendControl CreateControl(AppSettings settings, bool available)
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _testDirectory;
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        var coordinator = new StartupSettingsCoordinator(settings, store, new NoOpStartupManager());
        return new InProcessAddonFrontendControl(
            coordinator, new ThrowingSystemStatusProvider(), null, new DeveloperTestModeState(),
            frontButtonMappingAvailable: available);
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
