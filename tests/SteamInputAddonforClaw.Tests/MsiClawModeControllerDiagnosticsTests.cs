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
        AppLog.MinimumLevelOverride = AppLogLevel.Off;
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
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
            [source, pidPresentCorrectTopology],
            [pidPresentCorrectTopology]); // PR11 section 5: the transition completes only once the old PID is gone
        var writer = new RecordingWriter();
        var controller = new MsiClawModeController(enumerator, new MsiClawControlHidResolver(), writer, TimeSpan.FromSeconds(5), TimeSpan.Zero);

        var result = await controller.SwitchModeAsync(MsiClawNativeMode.DirectInput, MsiClawPhysicalIdentity.From(source), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        AppLog.DrainForTests();
        var lines = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath).Split('\n');
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
        // The first broad read resolves the source. Once the target probe has run once, jump
        // the clock past the deadline so exactly one poll runs before the loop exits.
        DateTimeOffset Clock() => enumerator.TargetProbeCount >= 1 ? baseTime.AddSeconds(1) : baseTime;
        var controller = new MsiClawModeController(enumerator, new MsiClawControlHidResolver(), writer, TimeSpan.FromMilliseconds(50), TimeSpan.Zero, Clock);

        var result = await controller.SwitchModeAsync(MsiClawNativeMode.DirectInput, MsiClawPhysicalIdentity.From(source), CancellationToken.None);

        Assert.Equal(MsiClawModeTransitionStatus.TargetDeviceDidNotAppear, result.Status);
        Assert.Equal(2, enumerator.CallCount);
        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("TargetPidPresent=False", log);
        Assert.Contains("TargetControlCandidateCount=0", log);
        var terminal = Assert.Single(log.Split('\n'), line => line.Contains("Event=NativeModeTransitionCompleted", StringComparison.Ordinal));
        Assert.Contains("WriteSucceeded=True", terminal);
        Assert.Contains("ElapsedBeforeWriteMs=", terminal);
        Assert.Contains("SinceCommandWriteMs=", terminal);
        Assert.Contains("OldPidDisappeared=False", terminal);
        Assert.Contains("ExactTargetTopologyProven=False", terminal);
        Assert.Contains("Result=TargetDeviceDidNotAppear", terminal);
    }

    [Fact]
    public async Task Pid1902FirstSeen_is_logged_only_for_the_forward_DirectInput_transition()
    {
        var container = Guid.NewGuid();
        var xinput = Topology(container, "USB\\VID_0DB0&PID_1901\\ROOT_C", 0x1901, 0xFFA0, 0x0001);
        var directInput = Topology(container, "USB\\VID_0DB0&PID_1902\\ROOT_C", 0x1902, 0xFFF0, 0x0040);

        var enter = new MsiClawModeController(
            new SequenceEnumerator([xinput], [xinput, directInput], [directInput]),
            new MsiClawControlHidResolver(), new RecordingWriter(), TimeSpan.FromSeconds(1), TimeSpan.Zero);
        Assert.True((await enter.SwitchModeAsync(MsiClawNativeMode.DirectInput, MsiClawPhysicalIdentity.From(xinput), CancellationToken.None)).Succeeded);

        var restore = new MsiClawModeController(
            new SequenceEnumerator([directInput], [directInput, xinput], [xinput]),
            new MsiClawControlHidResolver(), new RecordingWriter(), TimeSpan.FromSeconds(1), TimeSpan.Zero);
        Assert.True((await restore.SwitchModeAsync(MsiClawNativeMode.XInput, MsiClawPhysicalIdentity.From(directInput), CancellationToken.None)).Succeeded);

        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Equal(1, log.Split('\n').Count(line => line.Contains("Event=Pid1902FirstSeen", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(0x1902, 0xFFF0, 0x0040, 0x1901, 0xFFA0, 0x0001)]
    [InlineData(0x1901, 0xFFA0, 0x0001, 0x1902, 0xFFF0, 0x0040)]
    public async Task Slow_mode_write_gets_a_fresh_full_five_second_target_window(
        ushort sourcePid, ushort sourcePage, ushort sourceUsage,
        ushort targetPid, ushort targetPage, ushort targetUsage)
    {
        var clock = new FakeClock();
        var source = Topology(Guid.NewGuid(), $"USB\\VID_0DB0&PID_{sourcePid:X4}\\SLOW_SOURCE", sourcePid, sourcePage, sourceUsage);
        var target = Topology(Guid.NewGuid(), $"USB\\VID_0DB0&PID_{targetPid:X4}\\SLOW_TARGET", targetPid, targetPage, targetUsage);
        var writer = new SlowWriter(clock, TimeSpan.FromMilliseconds(1700));
        var enumerator = new TimedTargetEnumerator(source, target, clock, writer, TimeSpan.FromSeconds(4));
        var controller = new MsiClawModeController(
            enumerator,
            new MsiClawControlHidResolver(),
            writer,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(1),
            () => clock.Now,
            (duration, _) => { clock.Advance(duration); return Task.CompletedTask; });

        var result = await controller.SwitchModeAsync(
            targetPid == MsiClawHardware.DirectInputProductId ? MsiClawNativeMode.DirectInput : MsiClawNativeMode.XInput,
            MsiClawPhysicalIdentity.From(source),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.True(result.WriteSucceeded);
        Assert.True(result.OldPidDisappeared);
        Assert.True(result.TargetPidPresent);
        Assert.True(result.TargetPidAppeared);
        Assert.True(result.SourceIdentityVerified);
        Assert.True(result.TargetTopologyVerified);
        Assert.InRange(clock.Now - writer.CompletedAt!.Value, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(0x1902, 0xFFF0, 0x0040, 0x1901, 0xFFA0, 0x0001)]
    [InlineData(0x1901, 0xFFA0, 0x0001, 0x1902, 0xFFF0, 0x0040)]
    public async Task Missing_target_fails_only_after_full_post_write_budget(
        ushort sourcePid, ushort sourcePage, ushort sourceUsage,
        ushort targetPid, ushort targetPage, ushort targetUsage)
    {
        var clock = new FakeClock();
        var source = Topology(Guid.NewGuid(), $"USB\\VID_0DB0&PID_{sourcePid:X4}\\NO_TARGET_SOURCE", sourcePid, sourcePage, sourceUsage);
        var absent = Topology(Guid.NewGuid(), $"USB\\VID_0DB0&PID_{targetPid:X4}\\NO_TARGET", targetPid, targetPage, targetUsage);
        var writer = new SlowWriter(clock, TimeSpan.FromMilliseconds(1700));
        var enumerator = new TimedTargetEnumerator(source, absent, clock, writer, null);
        var controller = new MsiClawModeController(
            enumerator,
            new MsiClawControlHidResolver(),
            writer,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(1),
            () => clock.Now,
            (duration, _) => { clock.Advance(duration); return Task.CompletedTask; });

        var result = await controller.SwitchModeAsync(
            targetPid == MsiClawHardware.DirectInputProductId ? MsiClawNativeMode.DirectInput : MsiClawNativeMode.XInput,
            MsiClawPhysicalIdentity.From(source),
            CancellationToken.None);

        Assert.Equal(MsiClawModeTransitionStatus.TargetDeviceDidNotAppear, result.Status);
        Assert.True(result.WriteSucceeded);
        Assert.True(result.OldPidDisappeared);
        Assert.False(result.TargetPidPresent);
        Assert.False(result.TargetPidAppeared);
        Assert.False(result.TargetTopologyVerified);
        Assert.True(clock.Now - writer.CompletedAt!.Value >= TimeSpan.FromSeconds(5));
    }

    private static ControllerDeviceInfo Topology(Guid container, string root, ushort pid, ushort usagePage, ushort usage)
    {
        var child = $"{root}_CHILD";
        return new(child, container, root, [root], "HID", [], [], "HIDClass", null, null, 0x0DB0, pid, true, UsagePage: usagePage, Usage: usage);
    }

    private sealed class SequenceEnumerator(params IReadOnlyList<ControllerDeviceInfo>[] states) : IControllerDeviceEnumerator
    {
        private int _index;
        internal int TargetProbeCount { get; private set; }
        internal int CallCount => _index;
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => states[Math.Min(_index++, states.Length - 1)];
        public bool IsPresent(ushort vendorId, ushort productId)
        {
            // NativeMode's target probe is the polling observation. Exact verification reads the
            // same observed state; the source-PID absence check must not advance the sequence.
            if (productId == 0x1902) TargetProbeCount++;
            return states[Math.Min(_index, states.Length - 1)].Any(d => d.VendorId == vendorId && d.ProductId == productId);
        }
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices(ushort vendorId, ushort productId)
        {
            if (productId == 0x1902) TargetProbeCount++;
            return states[Math.Min(_index++, states.Length - 1)].Where(d => d.VendorId == vendorId && d.ProductId == productId).ToArray();
        }
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

    private sealed class FakeClock
    {
        internal DateTimeOffset Now { get; private set; } = DateTimeOffset.UnixEpoch;
        internal void Advance(TimeSpan duration) => Now += duration;
    }

    private sealed class SlowWriter(FakeClock clock, TimeSpan duration) : IMsiClawModeWriter
    {
        internal DateTimeOffset? CompletedAt { get; private set; }

        public Task<bool> WriteAsync(MsiClawControlHidDevice device, MsiClawNativeMode mode, CancellationToken cancellationToken)
        {
            clock.Advance(duration);
            CompletedAt = clock.Now;
            return Task.FromResult(true);
        }
    }

    private sealed class TimedTargetEnumerator(
        ControllerDeviceInfo source,
        ControllerDeviceInfo target,
        FakeClock clock,
        SlowWriter writer,
        TimeSpan? targetArrivalAfterWrite) : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => [source];

        public bool IsPresent(ushort vendorId, ushort productId) => false;

        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices(ushort vendorId, ushort productId) =>
            targetArrivalAfterWrite is { } arrival
                && writer.CompletedAt is { } completedAt
                && clock.Now - completedAt >= arrival
                ? [target]
                : [];
    }
}
