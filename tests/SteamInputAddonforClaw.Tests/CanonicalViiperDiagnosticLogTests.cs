using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class CanonicalViiperDiagnosticLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public CanonicalViiperDiagnosticLogTests()
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
        AppLog.DirectoryOverride = _directory;
    }

    private static void Invoke(ViiperLogLevel level, string text)
    {
        var message = Marshal.StringToCoTaskMemUTF8(text);
        try
        {
            CanonicalViiperDiagnosticLog.Callback(level, message);
        }
        finally
        {
            Marshal.FreeCoTaskMem(message);
        }
    }

    [Fact]
    public void DebugDPadMessage_IsForwardedAsDebugUnderViiperCategory()
    {
        Invoke(ViiperLogLevel.Debug, "VIIPER.DPad Stage=ABIDecoded Up=1 Right=0 Left=0 Down=0 Mask=0x01");
        AppLog.DrainForTests();

        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("VIIPER.DPad Stage=ABIDecoded", log);
        Assert.Contains("[VIIPER]", log);
        Assert.Contains("[DEBUG]", log);
    }

    [Fact]
    public void WarnDPadMessage_IsForwardedAsWarn()
    {
        Invoke(ViiperLogLevel.Warn, "VIIPER.DPad Stage=GordonReportInvariant Expected=0x01 Actual=0x00");
        AppLog.DrainForTests();

        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("VIIPER.DPad Stage=GordonReportInvariant", log);
        Assert.Contains("[WARN]", log);
    }

    [Fact]
    public void MessageWithoutDPadPrefix_IsNotForwarded()
    {
        Invoke(ViiperLogLevel.Debug, "USB server started operation=NewUSBServer serverState=Active");
        AppLog.DrainForTests();

        Assert.False(File.Exists(AppLog.CurrentLogFilePath) && LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Contains("USB server started"));
    }

    [Fact]
    public void InfoLevelDPadMessage_IsNotPromotedOrForwarded()
    {
        Invoke(ViiperLogLevel.Info, "VIIPER.DPad Stage=ABIDecoded Up=1 Right=0 Left=0 Down=0 Mask=0x01");
        AppLog.DrainForTests();

        Assert.False(File.Exists(AppLog.CurrentLogFilePath) && LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Contains("ABIDecoded"));
    }

    [Fact]
    public void NullOrEmptyMessage_DoesNotThrowAndLogsNothing()
    {
        CanonicalViiperDiagnosticLog.Callback(ViiperLogLevel.Debug, nint.Zero);
        Invoke(ViiperLogLevel.Debug, string.Empty);
        AppLog.DrainForTests();

        Assert.False(Directory.Exists(_directory) && Directory.EnumerateFiles(_directory).Any());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
