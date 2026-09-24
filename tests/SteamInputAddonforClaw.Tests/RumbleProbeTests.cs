using RumbleProbe;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RumbleProbeTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 257)]
    [InlineData(127, 32639)]
    [InlineData(255, 65535)]
    public void Eight_bit_amplitude_expands_to_full_range(int input, int expected)
        => Assert.Equal((ushort)expected, ProbeRunLog.Expand((byte)input));

    [Theory]
    [InlineData("pulse", "--slot", "4")]
    [InlineData("pulse", "--left8", "256")]
    [InlineData("pulse", "--iterations", "0")]
    [InlineData("pulse", "--left8", "0", "--right8", "0")]
    public void Invalid_cli_values_are_rejected(params string[] args)
    {
        Assert.False(ProbeCommandLine.TryParse(args, out _, out _, out var showHelp));
        Assert.False(showHelp);
    }

    [Fact]
    public async Task Pulse_sends_nonzero_then_stop_per_iteration_and_one_final_cleanup_stop()
    {
        var options = Parse("pulse", "--slot", "2", "--left8", "3", "--right8", "6", "--iterations", "2", "--on-ms", "1", "--off-ms", "1");
        var xinput = new FakeXInput();

        var result = await RunAsync(options, xinput);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            [
                new XInputWrite(2, 771, 1542),
                new XInputWrite(2, 0, 0),
                new XInputWrite(2, 771, 1542),
                new XInputWrite(2, 0, 0),
                new XInputWrite(2, 0, 0),
            ],
            xinput.SetCalls);
        Assert.Equal(2, CountPhase(result.Log, "Stop"));
        Assert.Equal(1, CountPhase(result.Log, "CleanupStop"));
    }

    [Fact]
    public async Task Fixed_sequence_preserves_pairs_and_separates_final_cleanup_stop()
    {
        var options = Parse("sequence", "--slot", "1") with { SequenceStepMilliseconds = 1 };
        var xinput = new FakeXInput();

        var result = await RunAsync(options, xinput);

        Assert.Equal(0, result.ExitCode);
        var expected = RumbleProbeRunner.SequenceForTests
            .Select(pair => new XInputWrite(1, ProbeRunLog.Expand(pair.Left), ProbeRunLog.Expand(pair.Right)))
            .Append(new XInputWrite(1, 0, 0));
        Assert.Equal(expected, xinput.SetCalls);
        Assert.Equal(5, CountPhase(result.Log, "NonZero"));
        Assert.Equal(5, CountPhase(result.Log, "Stop"));
        Assert.Equal(1, CountPhase(result.Log, "CleanupStop"));
    }

    [Fact]
    public async Task Non_success_set_state_stops_sequence_and_cleanup_is_attempted_once()
    {
        var options = Parse("pulse", "--slot", "0", "--iterations", "5", "--on-ms", "1", "--off-ms", "1");
        var xinput = new FakeXInput { SetResults = new Queue<uint>([RumbleProbeRunner.ErrorDeviceNotConnected, 0]) };

        var result = await RunAsync(options, xinput);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal([new XInputWrite(0, 771, 1542), new XInputWrite(0, 0, 0)], xinput.SetCalls);
        Assert.Contains("Phase=NonZero", result.Log);
        Assert.Contains("Result=1167", result.Log);
        Assert.Equal(1, CountPhase(result.Log, "CleanupStop"));
    }

    [Fact]
    public async Task Set_state_exception_still_runs_single_final_cleanup_stop()
    {
        var options = Parse("pulse", "--slot", "0", "--on-ms", "1", "--off-ms", "1");
        var xinput = new FakeXInput { ThrowOnSetCall = 1 };

        var result = await RunAsync(options, xinput);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal([new XInputWrite(0, 771, 1542), new XInputWrite(0, 0, 0)], xinput.SetCalls);
        Assert.Contains("Result=Exception:InvalidOperationException", result.Log);
        Assert.Equal(1, CountPhase(result.Log, "CleanupStop"));
    }

    [Fact]
    public async Task Failed_cleanup_is_logged_and_never_retried()
    {
        var options = Parse("pulse", "--slot", "0", "--iterations", "1", "--on-ms", "1", "--off-ms", "1");
        var xinput = new FakeXInput { SetResults = new Queue<uint>([0, 0, 1167]) };

        var result = await RunAsync(options, xinput);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(3, xinput.SetCalls.Count);
        Assert.Equal("CleanupStop", PhaseLines(result.Log).Last().Phase);
        Assert.Contains("Result=1167", PhaseLines(result.Log).Last().Line);
    }

    [Fact]
    public async Task Ctrl_c_cancellation_uses_the_single_serial_cleanup_path()
    {
        using var cancellation = new CancellationTokenSource();
        var options = Parse("pulse", "--slot", "0", "--on-ms", "10000", "--off-ms", "1");
        var xinput = new FakeXInput { AfterSetCall = call => { if (call == 1) cancellation.Cancel(); } };

        var result = await RunAsync(options, xinput, cancellation.Token);

        Assert.Equal(130, result.ExitCode);
        Assert.Equal([new XInputWrite(0, 771, 1542), new XInputWrite(0, 0, 0)], xinput.SetCalls);
        Assert.Equal(1, CountPhase(result.Log, "CleanupStop"));
    }

    [Fact]
    public async Task Deadman_has_no_host_stop_during_idle_and_always_cleans_up()
    {
        var options = Parse("deadman", "--slot", "3", "--idle-ms", "30");
        var xinput = new FakeXInput();

        var result = await RunAsync(options, xinput);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal([new XInputWrite(3, 771, 1542), new XInputWrite(3, 0, 0)], xinput.SetCalls);
        Assert.Equal(0, CountPhase(result.Log, "Stop"));
        Assert.Equal(1, CountPhase(result.Log, "IdleBegin"));
        Assert.Equal(1, CountPhase(result.Log, "IdleEnd"));
        Assert.Equal(1, CountPhase(result.Log, "CleanupStop"));
        Assert.Contains("Phase=IdleBegin", result.Log);
        Assert.Contains("Result=NotCalled", result.Log);
    }

    [Fact]
    public async Task Automatic_slot_selection_fails_when_none_or_multiple_are_connected()
    {
        var options = Parse("pulse", "--iterations", "1", "--on-ms", "1", "--off-ms", "1");
        var noneConnected = new FakeXInput();
        var multipleConnected = new FakeXInput { ConnectedSlots = [0, 2] };

        var none = await RunAsync(options, noneConnected);
        var multiple = await RunAsync(options, multipleConnected);

        Assert.NotEqual(0, none.ExitCode);
        Assert.Contains("No XInput slots are connected", none.StandardError);
        Assert.Empty(noneConnected.SetCalls);
        Assert.NotEqual(0, multiple.ExitCode);
        Assert.Contains("specify --slot 0..3", multiple.StandardError);
        Assert.Empty(multipleConnected.SetCalls);
    }

    [Fact]
    public async Task Exactly_one_connected_slot_is_selected_automatically()
    {
        var options = Parse("pulse", "--iterations", "1", "--on-ms", "1", "--off-ms", "1");
        var xinput = new FakeXInput { ConnectedSlots = [2] };

        var result = await RunAsync(options, xinput);

        Assert.Equal(0, result.ExitCode);
        Assert.All(xinput.SetCalls, call => Assert.Equal(2, call.Slot));
        Assert.Equal(4, xinput.GetCalls.Count);
    }

    [Fact]
    public async Task Explicit_valid_slot_skips_automatic_slot_scanning()
    {
        var options = Parse("pulse", "--slot", "3", "--iterations", "1", "--on-ms", "1", "--off-ms", "1");
        var xinput = new FakeXInput();

        var result = await RunAsync(options, xinput);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(xinput.GetCalls);
        Assert.All(xinput.SetCalls, call => Assert.Equal(3, call.Slot));
    }

    [Fact]
    public async Task List_queries_all_four_slots_without_mutation()
    {
        var options = Parse("list");
        var xinput = new FakeXInput { ConnectedSlots = [1, 3] };

        var result = await RunAsync(options, xinput);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal([0u, 1u, 2u, 3u], xinput.GetCalls);
        Assert.Empty(xinput.SetCalls);
        Assert.Equal(4, CountPhase(result.Log, "SlotProbe"));
        Assert.Contains("GetStateResult=0 Result=NotCalled", result.Log);
    }

    [Fact]
    public async Task Log_header_flush_failure_prevents_all_xinput_calls()
    {
        var options = Parse("pulse", "--slot", "0");
        var xinput = new FakeXInput();
        var failedWriter = new FlushFailureWriter();

        var result = await RunAsync(options, xinput, logFactory: () => new ProbeRunLog(failedWriter, "memory"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("no vibration command was sent", result.StandardError);
        Assert.Empty(xinput.GetCalls);
        Assert.Empty(xinput.SetCalls);
    }

    [Fact]
    public async Task Log_open_failure_prevents_all_xinput_calls()
    {
        var options = Parse("pulse", "--slot", "0");
        var xinput = new FakeXInput();

        var result = await RunAsync(options, xinput, logFactory: () => throw new IOException("log unavailable"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(xinput.GetCalls);
        Assert.Empty(xinput.SetCalls);
    }

    private static ProbeOptions Parse(params string[] args)
    {
        Assert.True(ProbeCommandLine.TryParse(args, out var options, out var error, out var showHelp), error);
        Assert.False(showHelp);
        return options;
    }

    private static async Task<RunResult> RunAsync(
        ProbeOptions options,
        FakeXInput xinput,
        CancellationToken cancellationToken = default,
        Func<ProbeRunLog>? logFactory = null)
    {
        var writer = new StringWriter();
        var stderr = new StringWriter();
        var stdout = new StringWriter();
        var result = await RumbleProbeRunner.RunAsync(
            options,
            xinput,
            logFactory ?? (() => new ProbeRunLog(writer, "memory")),
            stdout,
            stderr,
            cancellationToken);
        return new(result, writer.ToString(), stdout.ToString(), stderr.ToString());
    }

    private static int CountPhase(string log, string phase)
        => PhaseLines(log).Count(line => line.Phase == phase);

    private static IReadOnlyList<(string Phase, string Line)> PhaseLines(string log)
        => log.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var marker = "Phase=";
                var start = line.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
                var end = line.IndexOf(' ', start);
                return (end < 0 ? line[start..] : line[start..end], line);
            })
            .ToArray();

    private sealed record RunResult(int ExitCode, string Log, string StandardOutput, string StandardError);
    private sealed record XInputWrite(int Slot, ushort LeftMotor, ushort RightMotor);

    private sealed class FakeXInput : IXInputApi
    {
        internal HashSet<int> ConnectedSlots { get; init; } = [];
        internal Queue<uint> SetResults { get; init; } = new();
        internal List<uint> GetCalls { get; } = [];
        internal List<XInputWrite> SetCalls { get; } = [];
        internal int? ThrowOnSetCall { get; init; }
        internal Action<int>? AfterSetCall { get; init; }

        public uint GetState(uint slot)
        {
            GetCalls.Add(slot);
            return ConnectedSlots.Contains((int)slot)
                ? RumbleProbeRunner.ErrorSuccess
                : RumbleProbeRunner.ErrorDeviceNotConnected;
        }

        public uint SetState(uint slot, ushort leftMotor, ushort rightMotor)
        {
            SetCalls.Add(new((int)slot, leftMotor, rightMotor));
            AfterSetCall?.Invoke(SetCalls.Count);
            if (ThrowOnSetCall == SetCalls.Count)
                throw new InvalidOperationException("simulated XInput failure");
            return SetResults.Count == 0 ? RumbleProbeRunner.ErrorSuccess : SetResults.Dequeue();
        }
    }

    private sealed class FlushFailureWriter : StringWriter
    {
        public override void Flush() => throw new IOException("simulated log flush failure");
        protected override void Dispose(bool disposing) { }
    }
}
