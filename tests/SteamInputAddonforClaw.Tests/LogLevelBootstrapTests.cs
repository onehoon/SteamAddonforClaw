using SteamInputAddonforClaw.Settings;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class LogLevelBootstrapTests
{
    [Fact]
    public void MissingMalformedAndInvalidSettingsFailSafeToOff()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.Equal(AppLogPreference.Off, LogLevelBootstrap.Read(path));
        File.WriteAllText(path, "{");
        Assert.Equal(AppLogPreference.Off, LogLevelBootstrap.Read(path));
        File.WriteAllText(path, "{\"LogLevel\":\"Invalid\"}");
        Assert.Equal(AppLogPreference.Off, LogLevelBootstrap.Read(path));
        File.WriteAllText(path, "{\"LogLevel\":\"Info\"}");
        Assert.Equal(AppLogPreference.Info, LogLevelBootstrap.Read(path));
        File.WriteAllText(path, "{\"LogLevel\":\"Debug\"}");
        Assert.Equal(AppLogPreference.Debug, LogLevelBootstrap.Read(path));
        File.WriteAllText(path, "{\"LogLevel\":\"Off\"}");
        Assert.Equal(AppLogPreference.Off, LogLevelBootstrap.Read(path));
        File.Delete(path);
    }
}
