using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Settings;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AppSettingsPolicyTests
{
    [Theory]
    [InlineData("Off", AppLogPreference.Off)]
    [InlineData("off", AppLogPreference.Off)]
    [InlineData("Info", AppLogPreference.Info)]
    [InlineData("info", AppLogPreference.Info)]
    [InlineData("Debug", AppLogPreference.Debug)]
    [InlineData("debug", AppLogPreference.Debug)]
    [InlineData("SomethingInvalid", AppLogPreference.Off)]
    [InlineData("", AppLogPreference.Off)]
    [InlineData(null, AppLogPreference.Off)]
    public void Normalize_RecognizesAllThreeLevelsCaseInsensitivelyAndDefaultsToOff(string? input, AppLogPreference expected)
    {
        Assert.Equal(expected, AppSettingsPolicy.Normalize(input));
    }

    [Fact]
    public void ToAppLogLevel_MapsOffToOff() => Assert.Equal(AppLogLevel.Off, AppSettingsPolicy.ToAppLogLevel(AppLogPreference.Off));

    [Fact]
    public void ToAppLogLevel_MapsInfoToInfo() => Assert.Equal(AppLogLevel.Info, AppSettingsPolicy.ToAppLogLevel(AppLogPreference.Info));

    [Fact]
    public void ToAppLogLevel_MapsDebugToDebug() => Assert.Equal(AppLogLevel.Debug, AppSettingsPolicy.ToAppLogLevel(AppLogPreference.Debug));
}
