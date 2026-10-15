using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class FrontendLaunchArgumentsTests
{
    [Theory]
    [InlineData("Off", FrontendLogLevel.Off)]
    [InlineData("INFO", FrontendLogLevel.Info)]
    [InlineData("dEbUg", FrontendLogLevel.Debug)]
    public void Parses_supported_log_levels_case_insensitively(string value, FrontendLogLevel expected)
    {
        Assert.Equal(expected, FrontendLaunchArguments.ResolveLogLevel([FrontendLaunchArguments.LogLevelOption, value]));
    }

    [Theory]
    [InlineData("Verbose")]
    [InlineData("--log-directory")]
    public void Unsupported_or_malformed_log_level_fails_closed_to_off(string value)
    {
        Assert.Equal(FrontendLogLevel.Off, FrontendLaunchArguments.ResolveLogLevel([FrontendLaunchArguments.LogLevelOption, value]));
    }

    [Fact]
    public void Missing_log_level_fails_closed_to_off()
    {
        Assert.Equal(FrontendLogLevel.Off, FrontendLaunchArguments.ResolveLogLevel([]));
        Assert.Equal(FrontendLogLevel.Off, FrontendLaunchArguments.ResolveLogLevel([FrontendLaunchArguments.LogLevelOption]));
    }

    [Fact]
    public void Log_level_argument_does_not_change_log_directory_with_spaces()
    {
        const string directory = @"C:\Users\Test User\Addon Data\logs";
        var args = new[]
        {
            FrontendLaunchArguments.LogDirectoryOption, directory,
            FrontendLaunchArguments.LogLevelOption, "Debug",
        };

        Assert.Equal(directory, FrontendLaunchArguments.ResolveLogDirectory(args));
        Assert.Equal(FrontendLogLevel.Debug, FrontendLaunchArguments.ResolveLogLevel(args));
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamInputAddonforClaw-Data", "logs"),
            FrontendLaunchArguments.ResolveLogDirectory([FrontendLaunchArguments.LogLevelOption, "Debug"]));
    }
}
