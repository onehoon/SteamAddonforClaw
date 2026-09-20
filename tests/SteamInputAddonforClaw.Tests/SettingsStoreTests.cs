using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void Load_LegacyKeys_AreIgnored()
    {
        var path = Path.Combine(_testDirectory, "settings.json");
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, "{\"LaunchAtWindowsStartup\":false,\"SteamInputRoutingEnabled\":true,\"LogLevel\":\"Debug\"}");

        var settings = new SettingsStore(path).Load();

        // App UI PR-B: the removed LaunchAtWindowsStartup preference (and the older routing key) must
        // not survive as any in-memory state, and unrelated settings still load.
        Assert.DoesNotContain("LaunchAtWindowsStartup", typeof(AppSettings).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain("SteamInputRoutingEnabled", typeof(AppSettings).GetProperties().Select(p => p.Name));
        Assert.Equal(AppLogPreference.Debug, settings.LogLevel);
    }

    [Fact]
    public void Save_DoesNotWriteTheRemovedLaunchAtWindowsStartupKey()
    {
        var path = Path.Combine(_testDirectory, "settings.json");
        var store = new SettingsStore(path);

        // An old pre-release JSON file carrying the obsolete property: it loads (ignored), and the
        // next save no longer serializes it.
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, "{\"LaunchAtWindowsStartup\":false,\"SuppressDeveloperMenuWarning\":false}");
        store.Save(store.Load() with { SuppressDeveloperMenuWarning = true });

        var json = File.ReadAllText(path);
        Assert.DoesNotContain("LaunchAtWindowsStartup", json);
        Assert.DoesNotContain("SteamInputRoutingEnabled", json);
        Assert.Contains("\"SuppressDeveloperMenuWarning\": true", json);
    }

    [Fact]
    public void SaveAndLoad_PreservesDeveloperMenuWarningSuppression()
    {
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));

        store.Save(new AppSettings(SuppressDeveloperMenuWarning: true));

        Assert.True(store.Load().SuppressDeveloperMenuWarning);
    }

    [Fact]
    public void Load_WhenDeveloperMenuEnabledIsMissing_DefaultsToFalse()
    {
        var path = Path.Combine(_testDirectory, "settings.json");
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, "{\"LaunchAtWindowsStartup\":false}");

        Assert.False(new SettingsStore(path).Load().DeveloperMenuEnabled);
    }

    [Fact]
    public void Load_ReadsExplicitDeveloperMenuEnabled()
    {
        var path = Path.Combine(_testDirectory, "settings.json");
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, "{\"DeveloperMenuEnabled\":true}");

        Assert.True(new SettingsStore(path).Load().DeveloperMenuEnabled);
    }

    [Fact]
    public void SaveAndLoad_PreservesDeveloperMenuEnabled()
    {
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));

        store.Save(new AppSettings { DeveloperMenuEnabled = true });

        Assert.True(store.Load().DeveloperMenuEnabled);
    }

    [Fact]
    public void QuickSettingsCurrentPowerSourceOnly_DefaultsToFalse_WhenMissing()
    {
        var path = Path.Combine(_testDirectory, "settings.json");
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, "{\"LogLevel\":\"Info\"}");

        Assert.False(new SettingsStore(path).Load().QuickSettingsCurrentPowerSourceOnly);
    }

    [Fact]
    public void QuickSettingsCurrentPowerSourceOnly_RoundTripsAndPersistsThroughCoordinator()
    {
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), store, new FakeStartupManager());

        coordinator.ChangeQuickSettingsCurrentPowerSourceOnly(true);

        Assert.True(coordinator.QuickSettingsCurrentPowerSourceOnly);
        Assert.True(store.Load().QuickSettingsCurrentPowerSourceOnly);
        Assert.Contains("\"QuickSettingsCurrentPowerSourceOnly\": true", File.ReadAllText(Path.Combine(_testDirectory, "settings.json")));
    }

    [Fact]
    public void QuickSettingsCurrentPowerSourceOnly_InvalidJsonKind_FailsClosedWithoutAffectingOtherSettings()
    {
        var path = Path.Combine(_testDirectory, "settings.json");
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, "{\"LogLevel\":\"Debug\",\"QuickSettingsCurrentPowerSourceOnly\":\"true\"}");

        var settings = new SettingsStore(path).Load();

        Assert.Equal(AppLogPreference.Debug, settings.LogLevel);
        Assert.False(settings.QuickSettingsCurrentPowerSourceOnly);
    }

    [Fact]
    public void ExistingSettingsSaveOperations_PreserveDeveloperMenuEnabled()
    {
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        var coordinator = new StartupSettingsCoordinator(new AppSettings { DeveloperMenuEnabled = true }, store, new FakeStartupManager());

        coordinator.ChangeLogLevel(AppLogPreference.Info);

        Assert.True(store.Load().DeveloperMenuEnabled);
    }

    [Fact]
    public void SuppressionIsNotPersistedUntilExplicitlyRequested()
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
    public void LegacySettings_DefaultsLogLevelToOff()
    {
        var path = Path.Combine(_testDirectory, "settings.json"); Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, "{\"LaunchAtWindowsStartup\":false}");
        var settings = new SettingsStore(path).Load();
        Assert.Equal(AppLogPreference.Off, settings.LogLevel);
    }

    [Fact]
    public void InvalidLogLevel_PreservesOtherSettingsAndDefaultsToOff()
    {
        var path = Path.Combine(_testDirectory, "settings.json"); Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, "{\"SuppressDeveloperMenuWarning\":true,\"LogLevel\":\"SomethingInvalid\"}");
        var settings = new SettingsStore(path).Load();
        Assert.True(settings.SuppressDeveloperMenuWarning); Assert.Equal(AppLogPreference.Off, settings.LogLevel);
    }

    [Fact]
    public void DebugLogLevel_RoundTripsAsText()
    {
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        store.Save(new AppSettings(AppLogPreference.Debug));
        Assert.Equal(AppLogPreference.Debug, store.Load().LogLevel);
        Assert.Contains("\"LogLevel\": \"Debug\"", File.ReadAllText(Path.Combine(_testDirectory, "settings.json")));
    }

    [Fact]
    public void InfoLogLevel_RoundTripsAsText()
    {
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        store.Save(new AppSettings(AppLogPreference.Info));
        Assert.Equal(AppLogPreference.Info, store.Load().LogLevel);
        Assert.Contains("\"LogLevel\": \"Info\"", File.ReadAllText(Path.Combine(_testDirectory, "settings.json")));
    }

    [Fact]
    public void OffLogLevel_RoundTripsAsText()
    {
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        store.Save(new AppSettings(AppLogPreference.Off));
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
    public void EnsureStartupRegistration_RequestsTaskRepair()
    {
        var manager = new FakeStartupManager();
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), new SettingsStore(Path.Combine(_testDirectory, "settings.json")), manager);

        coordinator.EnsureStartupRegistration();

        Assert.Equal([true], manager.Requests);
    }

    // ---- OQ5-UI-08: Overlay tab order --------------------------------------------------------------

    private static readonly AddonQuickSettingsTabId[] CustomTabOrder =
    [
        AddonQuickSettingsTabId.Controller,
        AddonQuickSettingsTabId.Device,
        AddonQuickSettingsTabId.Profile,
        AddonQuickSettingsTabId.Shortcut,
        AddonQuickSettingsTabId.Setting,
    ];

    [Fact]
    public void NewAppSettings_DefaultsAddonQuickSettingsTabOrderToTheFrozenOrder()
    {
        Assert.Equal(AddonQuickSettingsTabOrderContract.DefaultOrder, new AppSettings().AddonQuickSettingsTabOrder);
    }

    [Fact]
    public void Load_WhenAddonQuickSettingsTabOrderIsMissing_DefaultsWithoutLosingOtherSettings()
    {
        var path = Path.Combine(_testDirectory, "settings.json");
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, "{\"LaunchAtWindowsStartup\":false,\"LogLevel\":\"Debug\"}");

        var settings = new SettingsStore(path).Load();

        Assert.Equal(AddonQuickSettingsTabOrderContract.DefaultOrder, settings.AddonQuickSettingsTabOrder);
        Assert.Equal(AppLogPreference.Debug, settings.LogLevel);
    }

    [Fact]
    public void SaveAndLoad_PreservesACustomAddonQuickSettingsTabOrderAsEnumNames()
    {
        var path = Path.Combine(_testDirectory, "settings.json");
        var store = new SettingsStore(path);

        store.Save(new AppSettings { AddonQuickSettingsTabOrder = CustomTabOrder });

        Assert.Equal(CustomTabOrder, store.Load().AddonQuickSettingsTabOrder);
        var json = File.ReadAllText(path);
        Assert.Contains("\"OverlayTabOrder\"", json);
        Assert.DoesNotContain("\"AddonQuickSettingsTabOrder\"", json);
        foreach (var tab in AddonQuickSettingsTabOrderContract.DefaultOrder)
            Assert.Contains($"\"{tab}\"", json);                 // every tab persisted as its enum name
        Assert.DoesNotContain("DefaultOverlayTab", json);
        Assert.DoesNotContain("LastOverlayTab", json);
        Assert.DoesNotContain("SelectedOverlayTab", json);
        Assert.DoesNotContain("OverlayScrollOffset", json);
    }

    [Theory]
    [InlineData("\"OverlayTabOrder\":\"Device\"")]                                                    // wrong JSON kind
    [InlineData("\"OverlayTabOrder\":[\"Device\",\"Profile\",\"Controller\"]")]                       // missing tabs
    [InlineData("\"OverlayTabOrder\":[\"Device\",\"Device\",\"Profile\",\"Controller\",\"Shortcut\"]")] // duplicate
    [InlineData("\"OverlayTabOrder\":[\"Device\",\"Profile\",\"Controller\",\"Shortcut\",\"Nope\"]")] // unknown name
    [InlineData("\"OverlayTabOrder\":[0,1,2,3,4]")]                                                   // numeric enum
    [InlineData("\"OverlayTabOrder\":[\"Device\",\"Profile\",\"Controller\",\"Shortcut\",null]")]     // null element
    public void Load_InvalidAddonQuickSettingsTabOrder_FallsBackToDefaultAndKeepsOtherSettings(string tabOrderFragment)
    {
        var path = Path.Combine(_testDirectory, "settings.json");
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, "{\"LaunchAtWindowsStartup\":false,\"LogLevel\":\"Debug\"," + tabOrderFragment + "}");

        var settings = new SettingsStore(path).Load();

        Assert.Equal(AddonQuickSettingsTabOrderContract.DefaultOrder, settings.AddonQuickSettingsTabOrder);
        Assert.Equal(AppLogPreference.Debug, settings.LogLevel);
    }

    [Fact]
    public void TryChangeAddonQuickSettingsTabOrder_WithAValidOrder_PersistsAndPublishes()
    {
        var path = Path.Combine(_testDirectory, "settings.json");
        var store = new SettingsStore(path);
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), store, new FakeStartupManager());

        Assert.True(coordinator.TryChangeAddonQuickSettingsTabOrder(CustomTabOrder));

        Assert.Equal(CustomTabOrder, coordinator.AddonQuickSettingsTabOrder);
        Assert.Equal(CustomTabOrder, store.Load().AddonQuickSettingsTabOrder);
    }

    [Fact]
    public void TryChangeAddonQuickSettingsTabOrder_EqualToCurrent_IsAnAcceptedNoOp()
    {
        var path = Path.Combine(_testDirectory, "settings.json");
        var store = new SettingsStore(path);
        var coordinator = new StartupSettingsCoordinator(
            new AppSettings { AddonQuickSettingsTabOrder = CustomTabOrder }, store, new FakeStartupManager());

        Assert.True(coordinator.TryChangeAddonQuickSettingsTabOrder([.. CustomTabOrder]));
        Assert.Equal(CustomTabOrder, coordinator.AddonQuickSettingsTabOrder);
        Assert.False(File.Exists(path)); // no disk write for a no-op
    }

    [Fact]
    public void TryChangeAddonQuickSettingsTabOrder_WithAnInvalidOrder_IsRejectedWithoutStateChange()
    {
        var path = Path.Combine(_testDirectory, "settings.json");
        var store = new SettingsStore(path);
        store.Save(new AppSettings { AddonQuickSettingsTabOrder = CustomTabOrder });
        var coordinator = new StartupSettingsCoordinator(
            new AppSettings { AddonQuickSettingsTabOrder = CustomTabOrder }, store, new FakeStartupManager());

        Assert.False(coordinator.TryChangeAddonQuickSettingsTabOrder([AddonQuickSettingsTabId.Device, AddonQuickSettingsTabId.Device]));

        Assert.Equal(CustomTabOrder, coordinator.AddonQuickSettingsTabOrder);          // unchanged, NOT reset to default
        Assert.Equal(CustomTabOrder, store.Load().AddonQuickSettingsTabOrder);         // disk unchanged
    }

    [Fact]
    public void ExistingSettingsMutations_PreserveACustomAddonQuickSettingsTabOrder()
    {
        var path = Path.Combine(_testDirectory, "settings.json");
        var store = new SettingsStore(path);
        var coordinator = new StartupSettingsCoordinator(
            new AppSettings { AddonQuickSettingsTabOrder = CustomTabOrder }, store, new FakeStartupManager());

        coordinator.ChangeLogLevel(AppLogPreference.Info);

        Assert.Equal(CustomTabOrder, store.Load().AddonQuickSettingsTabOrder);
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
