using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Feedback;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// Diagnostics-only coverage for <see cref="SteamDeckRumbleFeedbackBridge.OnNativeOutput"/>: proves
/// every classification path (RX, decode result, authority DROP) is logged before its silent return,
/// so a hardware Ping can be matched to exactly what Steam sent. Does not exercise or assert on any
/// rumble/authority behavior change -- see RumbleV1Tests for behavioral coverage.
/// </summary>
[Collection("AppLog")]
public sealed class SteamDeckFeedbackRxDiagnosticsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ClawSteamDeckFeedbackRxDiagTests", Guid.NewGuid().ToString("N"));

    public SteamDeckFeedbackRxDiagnosticsTests()
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
    }

    public void Dispose()
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Off;
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void RumbleCallback_LogsRxAndRumbleDecodeWithMotorValues()
    {
        using var bridge = Create(out _, out _);

        Invoke(bridge.Callback, RumblePacket(0x1234, 0x5678));

        var log = Drain();
        Assert.Contains("SteamDeck feedback RX", log);
        Assert.Contains("Opcode=0xEB", log);
        Assert.Contains("Length=11", log);
        Assert.Contains("SteamDeck feedback Decode", log);
        Assert.Contains("Command=Rumble", log);
        Assert.Contains($"Left={0x1234}", log);
        Assert.Contains($"Right={0x5678}", log);
    }

    [Fact]
    public void HapticCommandCallback_LogsHhcFields()
    {
        using var bridge = Create(out _, out _);

        Invoke(bridge.Callback, [0xEA, 0, 0, 0, 100, 2]);

        var log = Drain();
        Assert.Contains("Opcode=0xEA", log);
        Assert.Contains("Command=Haptic", log);
        Assert.Contains("Intensity=100", log);
        Assert.Contains("Gain=2", log);
        Assert.Contains("Strength8=116", log);
    }

    [Fact]
    public void HapticPulseCallback_LogsProtocolFieldsWithoutPhysicalTranslation()
    {
        using var bridge = Create(out _, out _);

        Invoke(bridge.Callback, [0x8F, 8, 2, 0x34, 0x12, 0x78, 0x56, 2, 0, 0xA0]);

        var log = Drain();
        Assert.Contains("Opcode=0x8F", log);
        Assert.Contains("Command=HapticPulse", log);
        Assert.Contains("PayloadLength=8", log);
        Assert.Contains("LinuxLayout=True", log);
        Assert.Contains("Side=2", log);
        Assert.Contains("OnDurationUs=4660", log);
        Assert.Contains("OffIntervalUs=22136", log);
        Assert.Contains("Count=2", log);
        Assert.Contains("GainRaw=160", log);
        Assert.Contains("PhysicalTranslation=None", log);
        Assert.DoesNotContain("Strength8=", log);
        Assert.DoesNotContain("DurationMs=", log);
    }

    [Fact]
    public void UnknownOpcodeCallback_LogsUnknown()
    {
        using var bridge = Create(out _, out _);

        Invoke(bridge.Callback, [0x99]);

        var log = Drain();
        Assert.Contains("Opcode=0x99", log);
        Assert.Contains("Command=Unknown", log);
    }

    [Fact]
    public void MalformedRumbleOpcodeCallback_LogsMalformed()
    {
        using var bridge = Create(out _, out _);

        // 0xEB present but truncated below the minimum 11-byte report -- decoder rejects it as
        // Malformed, distinct from a genuinely unrecognized opcode.
        Invoke(bridge.Callback, [0xEB, 9]);

        var log = Drain();
        Assert.Contains("Opcode=0xEB", log);
        Assert.Contains("Command=Malformed", log);
    }

    [Fact]
    public void AuthorityRejection_IsDistinguishableFromDecoderRejection()
    {
        var authority = new FeedbackAuthority();
        var token = authority.Acquire("SteamDeck");
        var sink = new RecordingSink();
        using var bridge = new SteamDeckRumbleFeedbackBridge(authority, token, sink);
        // Revoke before the callback runs so a well-formed, decodable Rumble command is rejected
        // purely by FeedbackAuthority, not by the decoder.
        authority.RevokeAndDrain();

        Invoke(bridge.Callback, RumblePacket(1, 2));

        var log = Drain();
        // The decoder still classified it as a valid Rumble command...
        Assert.Contains("Command=Rumble", log);
        // ...but it was dropped at the authority boundary, not written to the sink.
        Assert.Contains("SteamDeck feedback DROP", log);
        Assert.Contains("Reason=AuthorityRejected", log);
        Assert.Empty(sink.Values);
    }

    private static SteamDeckRumbleFeedbackBridge Create(out FeedbackAuthority authority, out RecordingSink sink)
    {
        authority = new FeedbackAuthority();
        var token = authority.Acquire("SteamDeck");
        sink = new RecordingSink();
        return new SteamDeckRumbleFeedbackBridge(authority, token, sink);
    }

    private string Drain()
    {
        AppLog.DrainForTests();
        return LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
    }

    private static void Invoke(SteamInputAddonforClaw.VirtualOutput.Viiper.SteamDeckOutputCallback callback, byte[] report)
    {
        var pointer = Marshal.AllocHGlobal(Math.Max(report.Length, 1));
        try { Marshal.Copy(report, 0, pointer, report.Length); callback(0, pointer, (uint)report.Length); }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static byte[] RumblePacket(ushort left, ushort right) =>
        [0xEB, 9, 0, 0, 0, (byte)left, (byte)(left >> 8), (byte)right, (byte)(right >> 8), 2, 0];

    private sealed class RecordingSink : IPhysicalRumbleSink
    {
        public List<TwoMotorRumble> Values { get; } = [];
        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
        { Values.Add(rumble); return new(PhysicalRumbleWriteStatus.Succeeded, "OK"); }
    }
}
