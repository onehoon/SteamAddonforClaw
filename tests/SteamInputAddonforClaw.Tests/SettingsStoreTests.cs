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
        var value = StartupRegistration.BuildRunValue(@"C:\Custom Install\SteamInputAddonforClaw.exe");

        Assert.Equal("\"C:\\Custom Install\\SteamInputAddonforClaw.exe\"", value);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
