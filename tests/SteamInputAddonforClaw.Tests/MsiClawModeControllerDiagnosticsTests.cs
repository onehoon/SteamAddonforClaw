using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class MsiClawModeControllerDiagnosticsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MsiClawModeControllerDiagnosticsTests", Guid.NewGuid().ToString("N"));

    public MsiClawModeControllerDiagnosticsTests()
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
    }

    public void Dispose()
    {
        AppLog.DirectoryOverride = null;
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        // Best-effort cleanup: a concurrently-running test in a different, non-serialized
        // collection can transiently hold this directory's log file open. That does not
        // invalidate this test's already-passed assertions.
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } catch (IOException) { }
    }

    [Fact]
    public async Task PollDiagnosticDistinguishesTargetPidPresenceFromStrictTopologyMatch()
    {
        var container = Guid.NewGuid();
        var source = Topology(container, "USB\\VID_0DB0&PID_1901\\ROOT_A", 0x1901, 0xFFA0, 0x0001);
        // Poll 1: PID_1902 has appeared, but its usage page/usage do not match the strict
        // DirectInput control-HID topology yet (e.g. a transient/child enumeration order).
        var pidPresentWrongTopology = Topology(container, "USB\\VID_0DB0&PID_1902\\ROOT_A_STAGE1", 0x1902, 0x0001, 0x0001);
        // Poll 2: the expected control HID topology has now appeared.
        var pidPresentCorrectTopology = Topology(container, "USB\\VID_0DB0&PID_1902\\ROOT_A_STAGE2", 0x1902, 0xFFF0, 0x0040);

        var enumerator = new SequenceEnumerator(
            [source],
            [source, pidPresentWrongTopology],
            [source, pidPresentCorrectTopology]);
        var writer = new RecordingWriter();
        var controller = new MsiClawModeController(enumerator, new MsiClawControlHidResolver(), writer, TimeSpan.FromSeconds(5), TimeSpan.Zero);

        var result = await controller.SwitchModeAsync(MsiClawNativeMode.DirectInput, MsiClawPhysicalIdentity.From(source), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        var lines = File.ReadAllText(AppLog.CurrentLogFilePath).Split('\n');
        var poll1 = Assert.Single(lines, line => line.Contains("NativeModeTransitionPoll") && line.Contains("Poll=1"));
        Assert.Contains("TargetPidPresent=True", poll1);
        Assert.Contains("TargetControlCandidateCount=0", poll1);
        Assert.Contains("LogicalCandidateCount=0", poll1);
        var poll2 = Assert.Single(lines, line => line.Contains("NativeModeTransitionPoll") && line.Contains("Poll=2"));
        Assert.Contains("TargetPidPresent=True", poll2);
        Assert.Contains("TargetControlCandidateCount=1", poll2);
        Assert.Contains("LogicalCandidateCount=1", poll2);
        Assert.Contains("EnumerationMs=", poll2);
        Assert.Contains("ElapsedMs=", poll2);
        Assert.Contains("SinceCommandWriteMs=", poll2);
    }

    [Fact]
    public async Task PollDiagnosticReportsTargetPidAbsentWhenNoNewPidHasAppeared()
    {
        // Deterministic via an injected clock rather than a real-wall-clock timeout with
        // TimeSpan.Zero polling: that combination would spin EnumeratePresentDevices/AppLog
        // writes in a tight loop for the whole real timeout window on every run.
        var container = Guid.NewGuid();
        var source = Topology(container, "USB\\VID_0DB0&PID_1901\\ROOT_B", 0x1901, 0xFFA0, 0x0001);
        var enumerator = new SequenceEnumerator([source], [source]);
        var writer = new RecordingWriter();
        var baseTime = DateTimeOffset.UtcNow;
        // The first EnumeratePresentDevices call is ResolveSource's initial read (before the
        // poll loop starts); once the poll loop itself has enumerated once, jump the clock past
        // the deadline so exactly one poll runs before the loop exits.
        DateTimeOffset Clock() => enumerator.CallCount >= 2 ? baseTime.AddSeconds(1) : baseTime;
        var controller = new MsiClawModeController(enumerator, new MsiClawControlHidResolver(), writer, TimeSpan.FromMilliseconds(50), TimeSpan.Zero, Clock);

        var result = await controller.SwitchModeAsync(MsiClawNativeMode.DirectInput, MsiClawPhysicalIdentity.From(source), CancellationToken.None);

        Assert.Equal(MsiClawModeTransitionStatus.TargetDeviceDidNotAppear, result.Status);
        Assert.Equal(2, enumerator.CallCount);
        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("TargetPidPresent=False", log);
        Assert.Contains("TargetControlCandidateCount=0", log);
    }

    private static ControllerDeviceInfo Topology(Guid container, string root, ushort pid, ushort usagePage, ushort usage)
    {
        var child = $"{root}_CHILD";
        return new(child, container, root, [root], "HID", [], [], "HIDClass", null, null, 0x0DB0, pid, true, UsagePage: usagePage, Usage: usage);
    }

    private sealed class SequenceEnumerator(params IReadOnlyList<ControllerDeviceInfo>[] states) : IControllerDeviceEnumerator
    {
        private int _index;
        internal int CallCount => _index;
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => states[Math.Min(_index++, states.Length - 1)];
    }

    private sealed class RecordingWriter : IMsiClawModeWriter
    {
        public MsiClawNativeMode Mode { get; private set; }
        public Task<bool> WriteAsync(MsiClawControlHidDevice device, MsiClawNativeMode mode, CancellationToken cancellationToken)
        {
            Mode = mode;
            return Task.FromResult(true);
        }
    }
}
