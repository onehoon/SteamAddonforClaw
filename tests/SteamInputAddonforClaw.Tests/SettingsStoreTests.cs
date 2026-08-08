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
    public void BuildRunValue_QuotesStableExecutablePath()
    {
        var value = WindowsTaskSchedulerStartupManager.BuildRunValue(@"C:\Custom Install\SteamInputAddonforClaw.exe");

        Assert.Equal("\"C:\\Custom Install\\SteamInputAddonforClaw.exe\"", value);
    }

    [Fact]
    public void BuildCreateArguments_UsesLogonTriggerAndThreeMinuteDelay()
    {
        var arguments = WindowsTaskSchedulerStartupManager.BuildCreateArguments(@"C:\Custom Install\SteamInputAddonforClaw.exe");

        Assert.Contains("/SC ONLOGON", arguments);
        Assert.Contains("/DELAY 0003:00", arguments);
        Assert.DoesNotContain("current\\", arguments, StringComparison.OrdinalIgnoreCase);
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
