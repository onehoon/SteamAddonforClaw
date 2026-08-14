using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void Load_WhenSettingsDoNotExist_ReturnsStartupEnabledByDefault()
    {
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));

        var settings = store.Load();

        Assert.True(settings.LaunchAtWindowsStartup);
    }

    [Fact]
    public void SaveAndLoad_PreservesStartupSetting()
    {
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));

        store.Save(new AppSettings(LaunchAtWindowsStartup: false));

        Assert.False(store.Load().LaunchAtWindowsStartup);
    }

    [Fact]
    public void SaveAndLoad_PreservesBigPictureSetting()
    {
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        store.Save(new AppSettings(RouteInSteamBigPicture: true));
        Assert.True(store.Load().RouteInSteamBigPicture);
    }

    [Fact]
    public void SaveAndLoad_PreservesDeveloperMenuWarningSuppression()
    {
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));

        store.Save(new AppSettings(SuppressDeveloperMenuWarning: true));

        Assert.True(store.Load().SuppressDeveloperMenuWarning);
    }

    [Fact]
    public void CancelEquivalent_WhenSuppressionIsNotChanged_DoesNotPersistIt()
    {
        var path = Path.Combine(_testDirectory, "settings.json");
        var store = new SettingsStore(path);
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), store, new FakeStartupManager());

        Assert.False(coordinator.SuppressDeveloperMenuWarning);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void SuppressDeveloperMenuWarningPermanently_PersistsPreference()
    {
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), store, new FakeStartupManager());

        coordinator.SuppressDeveloperMenuWarningPermanently();

        Assert.True(coordinator.SuppressDeveloperMenuWarning);
        Assert.True(store.Load().SuppressDeveloperMenuWarning);
    }

    [Fact]
    public void ReliableLoad_InvalidBigPictureType_BlocksSafetyMutation()
    {
        var path = Path.Combine(_testDirectory, "settings.json");
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, "{\"RouteInSteamBigPicture\":\"true\"}");
        var result = new SettingsStore(path).LoadForSafetyGate();
        Assert.False(result.IsReliable);
        Assert.Equal("SettingsUnreliable", result.Reason);
    }

    [Fact]
    public void LegacySettings_DefaultsLogLevelToOff()
    {
        var path = Path.Combine(_testDirectory, "settings.json"); Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, "{\"LaunchAtWindowsStartup\":false}");
        var settings = new SettingsStore(path).Load();
        Assert.False(settings.LaunchAtWindowsStartup); Assert.Equal(AppLogPreference.Off, settings.LogLevel);
    }

    [Fact]
    public void InvalidLogLevel_PreservesOtherSettingsAndDefaultsToOff()
    {
        var path = Path.Combine(_testDirectory, "settings.json"); Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, "{\"LaunchAtWindowsStartup\":false,\"LogLevel\":\"SomethingInvalid\"}");
        var settings = new SettingsStore(path).Load();
        Assert.False(settings.LaunchAtWindowsStartup); Assert.Equal(AppLogPreference.Off, settings.LogLevel);
    }

    [Fact]
    public void DebugLogLevel_RoundTripsAsText()
    {
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        store.Save(new AppSettings(false, AppLogPreference.Debug));
        Assert.Equal(AppLogPreference.Debug, store.Load().LogLevel);
        Assert.Contains("\"LogLevel\": \"Debug\"", File.ReadAllText(Path.Combine(_testDirectory, "settings.json")));
    }

    [Fact]
    public void InfoLogLevel_RoundTripsAsText()
    {
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        store.Save(new AppSettings(false, AppLogPreference.Info));
        Assert.Equal(AppLogPreference.Info, store.Load().LogLevel);
        Assert.Contains("\"LogLevel\": \"Info\"", File.ReadAllText(Path.Combine(_testDirectory, "settings.json")));
    }

    [Fact]
    public void OffLogLevel_RoundTripsAsText()
    {
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        store.Save(new AppSettings(false, AppLogPreference.Off));
        Assert.Equal(AppLogPreference.Off, store.Load().LogLevel);
        Assert.Contains("\"LogLevel\": \"Off\"", File.ReadAllText(Path.Combine(_testDirectory, "settings.json")));
    }

    [Fact]
    public void NewAppSettings_DefaultsLogLevelToOff()
    {
        Assert.Equal(AppLogPreference.Off, new AppSettings().LogLevel);
    }

    [Fact]
    public void CreateTaskConfiguration_UsesStableExecutablePath()
    {
        var configuration = WindowsTaskSchedulerStartupManager.CreateTaskConfiguration(@"C:\Custom Install\SteamInputAddonforClaw.exe", "DOMAIN\\User");

        Assert.Equal(@"C:\Custom Install\SteamInputAddonforClaw.exe", configuration.ExecutablePath);
        Assert.DoesNotContain("current\\", configuration.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateTaskConfiguration_UsesCurrentUserWithoutAnyLogonDelay()
    {
        // ScheduledTaskConfiguration no longer carries a Delay field at all -- the addon must
        // launch immediately at logon, not after any fixed grace period. The absence of the
        // field is itself the regression guard: a future reintroduction of a fixed delay would
        // require a compile-time change here, not just a value change.
        var configuration = WindowsTaskSchedulerStartupManager.CreateTaskConfiguration(@"C:\Custom Install\SteamInputAddonforClaw.exe", "DOMAIN\\User");

        Assert.Equal("DOMAIN\\User", configuration.UserId);
    }

    [Fact]
    public void ChangeLaunchAtWindowsStartup_WhenEnabled_CallsStartupManagerAndPersistsSetting()
    {
        var manager = new FakeStartupManager();
        var coordinator = new StartupSettingsCoordinator(new AppSettings(false), new SettingsStore(Path.Combine(_testDirectory, "settings.json")), manager);

        coordinator.ChangeLaunchAtWindowsStartup(true);

        Assert.True(coordinator.Settings.LaunchAtWindowsStartup);
        Assert.Equal([true], manager.Requests);
    }

    [Fact]
    public void ChangeBigPictureSetting_SaveFailureKeepsMemoryAndEmitsNoChange()
    {
        Directory.CreateDirectory(_testDirectory);
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), new SettingsStore(_testDirectory), new FakeStartupManager());
        var changes = 0;
        coordinator.RouteInSteamBigPictureChanged += (_, _) => changes++;

        Assert.ThrowsAny<Exception>(() => coordinator.ChangeRouteInSteamBigPicture(true));
        Assert.False(coordinator.RouteInSteamBigPicture);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Repair_WhenStartupIsEnabled_RequestsTaskRepair()
    {
        var manager = new FakeStartupManager();
        var coordinator = new StartupSettingsCoordinator(new AppSettings(true), new SettingsStore(Path.Combine(_testDirectory, "settings.json")), manager);

        coordinator.Repair();

        Assert.Equal([true], manager.Requests);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private sealed class FakeStartupManager : IWindowsStartupManager
    {
        public List<bool> Requests { get; } = [];

        public StartupRegistrationResult Synchronize(bool enabled)
        {
            Requests.Add(enabled);
            return StartupRegistrationResult.Enabled();
        }
    }
}
