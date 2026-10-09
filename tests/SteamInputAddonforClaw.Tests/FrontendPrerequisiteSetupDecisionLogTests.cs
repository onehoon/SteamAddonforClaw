using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class FrontendPrerequisiteSetupDecisionLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SetupDecisionLogTests.{Guid.NewGuid():N}");
    private readonly AppLogLevel _previousLogLevel = AppLog.MinimumLevelOverride;
    private readonly string? _previousDirectory = AppLog.DirectoryOverride;

    public FrontendPrerequisiteSetupDecisionLogTests()
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        AppLog.DirectoryOverride = _directory;
    }

    public void Dispose()
    {
        AppLog.DrainForTests();
        AppLog.MinimumLevelOverride = _previousLogLevel;
        AppLog.DirectoryOverride = _previousDirectory;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void SetupDecisionLog_EmitsInitialAndChangedDecisionOnlyAtInfo()
    {
        var executor = new FrontendPrerequisiteSetupExecutor();
        (string Key, object? Value)[] initial =
        [
            ("HardwareStatus", "Supported"),
            ("CenterMStartupState", "Enabled"),
            ("StockTopologyUnreadyBeforeBaseline", true),
            ("RecoverySafe", false),
            ("ViiperPrerequisiteStatus", "Ready"),
            ("HidHideInstallationStatus", "Missing"),
            ("UsbIpInstallationStatus", "Missing"),
            ("FirstTimeSetupStatus", "Required"),
            ("FirstTimeSetupReason", "MissingComponents"),
            ("CanInstallRequiredComponents", true),
        ];
        var changed = initial.Select(field => field.Key == "HidHideInstallationStatus"
            ? (field.Key, (object?)"Installed")
            : field).ToArray();

        Assert.True(executor.LogSetupDecisionIfChanged(initial));
        Assert.False(executor.LogSetupDecisionIfChanged(initial));
        Assert.True(executor.LogSetupDecisionIfChanged(changed));

        AppLog.DrainForTests();
        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Equal(2, CountOccurrences(log, "Prerequisite setup decision evaluated."));
        Assert.Contains("[INFO]", log);
        Assert.Contains("HardwareStatus=Supported", log);
        Assert.Contains("StockTopologyUnreadyBeforeBaseline=True", log);
        Assert.Contains("CanInstallRequiredComponents=True", log);
    }

    [Fact]
    public void SetupDecisionLog_RespectsExplicitOff()
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Off;
        var executor = new FrontendPrerequisiteSetupExecutor();

        Assert.False(executor.LogSetupDecisionIfChanged(("FirstTimeSetupStatus", "Required")));

        AppLog.DrainForTests();
        Assert.False(Directory.Exists(_directory) && Directory.EnumerateFiles(_directory).Any());
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }
        return count;
    }
}
