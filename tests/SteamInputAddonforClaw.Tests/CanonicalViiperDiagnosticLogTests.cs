using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Diagnostics.GordonDPad;
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
        GordonDPadDiagnosticHub.ResetForTests();
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
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        GordonDPadDiagnosticHub.ResetForTests();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void DebugDPadMessage_IsPublishedToTheHubWithThePrefixStripped()
    {
        var received = new List<string>();
        GordonDPadDiagnosticHub.LineObserved += received.Add;

        Invoke(ViiperLogLevel.Debug, "VIIPER.DPad Stage=ABIDecoded Up=1 Right=0 Left=0 Down=0 Mask=0x01");

        Assert.Equal("Stage=ABIDecoded Up=1 Right=0 Left=0 Down=0 Mask=0x01", Assert.Single(received));
    }

    [Fact]
    public void WarnDPadMessage_IsPublishedToTheHub()
    {
        var received = new List<string>();
        GordonDPadDiagnosticHub.LineObserved += received.Add;

        Invoke(ViiperLogLevel.Warn, "VIIPER.DPad Stage=GordonReportInvariant Expected=0x01 Actual=0x00");

        Assert.Equal("Stage=GordonReportInvariant Expected=0x01 Actual=0x00", Assert.Single(received));
    }

    [Fact]
    public void ArbitraryFutureStage_IsPublishedToTheHubWithoutAnyCodeChangeHere()
    {
        // The whole point of routing through GordonDPadDiagnosticHub rather than a fixed set of known
        // stages: a brand-new VIIPER-side stage (e.g. a future USB/IP or feature-command diagnostic)
        // must flow through automatically as long as it uses the "VIIPER.DPad" prefix and Debug/Warn.
        var received = new List<string>();
        GordonDPadDiagnosticHub.LineObserved += received.Add;

        Invoke(ViiperLogLevel.Debug, "VIIPER.DPad Stage=USBIPResponse Detail=whatever-viiper-adds-next");

        Assert.Equal("Stage=USBIPResponse Detail=whatever-viiper-adds-next", Assert.Single(received));
    }

    [Fact]
    public void MessageWithoutDPadPrefix_IsNotPublishedToTheHub()
    {
        var received = new List<string>();
        GordonDPadDiagnosticHub.LineObserved += received.Add;

        Invoke(ViiperLogLevel.Debug, "USB server started operation=NewUSBServer serverState=Active");

        Assert.Empty(received);
    }

    [Fact]
    public void InfoLevelDPadMessage_IsNotPublishedToTheHub()
    {
        var received = new List<string>();
        GordonDPadDiagnosticHub.LineObserved += received.Add;

        Invoke(ViiperLogLevel.Info, "VIIPER.DPad Stage=ABIDecoded Up=1 Right=0 Left=0 Down=0 Mask=0x01");

        Assert.Empty(received);
    }

    [Fact]
    public void HubSubscriberException_DoesNotPreventAppLogForwarding()
    {
        GordonDPadDiagnosticHub.LineObserved += _ => throw new InvalidOperationException("broken diagnostic sink");

        Invoke(ViiperLogLevel.Debug, "VIIPER.DPad Stage=ABIDecoded Up=1 Right=0 Left=0 Down=0 Mask=0x01");
        AppLog.DrainForTests();

        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("VIIPER.DPad Stage=ABIDecoded", log);
    }
}
