using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

[assembly: InternalsVisibleTo("SteamInputAddonforClaw.Tests")]

namespace RumbleProbe;

internal enum ProbeCommand
{
    List,
    Pulse,
    Sequence,
    Deadman,
}

internal sealed record ProbeOptions(
    ProbeCommand Command,
    int? Slot,
    byte Left8,
    byte Right8,
    int Iterations,
    int OnMilliseconds,
    int OffMilliseconds,
    int SequenceStepMilliseconds,
    int IdleMilliseconds);

internal static class ProbeCommandLine
{
    internal static bool TryParse(
        IReadOnlyList<string> args,
        out ProbeOptions options,
        out string? error,
        out bool showHelp)
    {
        options = new(ProbeCommand.List, null, 3, 6, 200, 250, 250, 250, 7000);
        error = null;
        showHelp = false;

        if (args.Count == 0 || args[0] is "help" or "--help" or "-h")
        {
            showHelp = true;
            return true;
        }

        var command = args[0].ToLowerInvariant() switch
        {
            "list" => ProbeCommand.List,
            "pulse" => ProbeCommand.Pulse,
            "sequence" => ProbeCommand.Sequence,
            "deadman" => ProbeCommand.Deadman,
            _ => (ProbeCommand?)null,
        };
        if (command is null)
        {
            error = $"Unknown command '{args[0]}'.";
            return false;
        }
        var parsedCommand = command.Value;

        int? slot = null;
        var left8 = (byte)3;
        var right8 = (byte)6;
        var iterations = parsedCommand == ProbeCommand.Sequence ? 1 : 200;
        var onMilliseconds = 250;
        var offMilliseconds = 250;
        var idleMilliseconds = 7000;
        var supplied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 1; index < args.Count; index++)
        {
            var name = args[index];
            if (!IsAllowedOption(parsedCommand, name))
            {
                error = $"Option '{name}' is not supported for '{args[0]}'.";
                return false;
            }
            if (!supplied.Add(name))
            {
                error = $"Option '{name}' was supplied more than once.";
                return false;
            }
            if (index + 1 >= args.Count)
            {
                error = $"Option '{name}' requires a value.";
                return false;
            }

            var value = args[++index];
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                error = $"Value '{value}' for '{name}' must be a non-negative integer.";
                return false;
            }

            switch (name.ToLowerInvariant())
            {
                case "--slot" when number is >= 0 and <= 3:
                    slot = number;
                    break;
                case "--slot":
                    error = "--slot must be between 0 and 3.";
                    return false;
                case "--left8" when number is >= 0 and <= byte.MaxValue:
                    left8 = (byte)number;
                    break;
                case "--left8":
                    error = "--left8 must be between 0 and 255.";
                    return false;
                case "--right8" when number is >= 0 and <= byte.MaxValue:
                    right8 = (byte)number;
                    break;
                case "--right8":
                    error = "--right8 must be between 0 and 255.";
                    return false;
                case "--iterations" when number > 0:
                    iterations = number;
                    break;
                case "--iterations":
                    error = "--iterations must be greater than zero.";
                    return false;
                case "--on-ms" when number > 0:
                    onMilliseconds = number;
                    break;
                case "--on-ms":
                    error = "--on-ms must be greater than zero.";
                    return false;
                case "--off-ms" when number > 0:
                    offMilliseconds = number;
                    break;
                case "--off-ms":
                    error = "--off-ms must be greater than zero.";
                    return false;
                case "--idle-ms" when number > 0:
                    idleMilliseconds = number;
                    break;
                case "--idle-ms":
                    error = "--idle-ms must be greater than zero.";
                    return false;
                default:
                    error = $"Invalid value '{value}' for '{name}'.";
                    return false;
            }
        }

        if ((parsedCommand is ProbeCommand.Pulse or ProbeCommand.Deadman) && left8 == 0 && right8 == 0)
        {
            error = "At least one motor amplitude must be non-zero.";
            return false;
        }

        options = new(parsedCommand, slot, left8, right8, iterations, onMilliseconds, offMilliseconds, 250, idleMilliseconds);
        return true;
    }

    private static bool IsAllowedOption(ProbeCommand command, string option) => command switch
    {
        ProbeCommand.List => false,
        ProbeCommand.Pulse => option is "--slot" or "--left8" or "--right8" or "--iterations" or "--on-ms" or "--off-ms",
        ProbeCommand.Sequence => option is "--slot" or "--iterations",
        ProbeCommand.Deadman => option is "--slot" or "--left8" or "--right8" or "--idle-ms",
        _ => false,
    };
}

internal interface IXInputApi
{
    uint GetState(uint slot);
    uint SetState(uint slot, ushort leftMotor, ushort rightMotor);
}

internal sealed class WindowsXInputApi : IXInputApi
{
    private const string XInputDll = "xinput1_4.dll";

    public uint GetState(uint slot) => XInputGetStateNative(slot, out _);

    public uint SetState(uint slot, ushort leftMotor, ushort rightMotor)
    {
        var vibration = new NativeVibration { LeftMotor = leftMotor, RightMotor = rightMotor };
        return XInputSetStateNative(slot, in vibration);
    }

    [DllImport(XInputDll, EntryPoint = "XInputGetState", ExactSpelling = true)]
    private static extern uint XInputGetStateNative(uint userIndex, out NativeState state);

    [DllImport(XInputDll, EntryPoint = "XInputSetState", ExactSpelling = true)]
    private static extern uint XInputSetStateNative(uint userIndex, in NativeVibration vibration);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeVibration
    {
        public ushort LeftMotor;
        public ushort RightMotor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeState
    {
        public uint PacketNumber;
        public NativeGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLeftX;
        public short ThumbLeftY;
        public short ThumbRightX;
        public short ThumbRightY;
    }
}

internal enum ProbePhase
{
    SessionHeader,
    SlotProbe,
    NonZero,
    Stop,
    CleanupStop,
    IdleBegin,
    IdleEnd,
    CommandFailure,
}

internal sealed class ProbeRunLog : IDisposable
{
    private readonly TextWriter _writer;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private long _probeSequence;
    private ProbeCommand _command;

    internal ProbeRunLog(TextWriter writer, string filePath)
    {
        _writer = writer;
        FilePath = filePath;
    }

    internal string FilePath { get; }

    internal static ProbeRunLog OpenFile()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            throw new InvalidOperationException("The Local AppData directory is unavailable.");

        var directory = Path.Combine(localAppData, "SteamInputAddonforClaw-Data", "logs", "RumbleProbe");
        Directory.CreateDirectory(directory);
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss.fff", CultureInfo.InvariantCulture);
        var path = Path.Combine(directory, $"rumble-probe-{timestamp}-P{Environment.ProcessId}.log");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        try
        {
            return new(new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)), path);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    internal void WriteSessionHeader(ProbeOptions options)
    {
        _command = options.Command;
        var settings = options.Command switch
        {
            ProbeCommand.List => "Slot=all Left8=NA Right8=NA Left16=NA Right16=NA Iterations=NA OnMs=NA OffMs=NA IdleMs=NA",
            ProbeCommand.Pulse => $"Slot={FormatSlot(options.Slot)} Left8={options.Left8} Right8={options.Right8} Left16={Expand(options.Left8)} Right16={Expand(options.Right8)} Iterations={options.Iterations} OnMs={options.OnMilliseconds} OffMs={options.OffMilliseconds} IdleMs=NA",
            ProbeCommand.Sequence => $"Slot={FormatSlot(options.Slot)} Left8=sequence Right8=sequence Left16=sequence Right16=sequence Iterations={options.Iterations} SequenceStepMs={options.SequenceStepMilliseconds} OnMs=NA OffMs=NA IdleMs=NA",
            ProbeCommand.Deadman => $"Slot={FormatSlot(options.Slot)} Left8={options.Left8} Right8={options.Right8} Left16={Expand(options.Left8)} Right16={Expand(options.Right8)} Iterations=1 OnMs=NA OffMs=NA IdleMs={options.IdleMilliseconds}",
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        Write(ProbePhase.SessionHeader,
            $"Api=none Result=NotCalled Command={options.Command.ToString().ToLowerInvariant()} {settings}");
    }

    internal void WriteSlotProbe(int slot, uint result)
        => Write(ProbePhase.SlotProbe,
            $"Command={CommandName} Api=XInputGetState Slot={slot} Left8=NA Right8=NA Left16=NA Right16=NA GetStateResult={result} Result=NotCalled");

    internal void WriteSlotProbeException(int slot, Exception exception)
        => Write(ProbePhase.SlotProbe,
            $"Command={CommandName} Api=XInputGetState Slot={slot} Left8=NA Right8=NA Left16=NA Right16=NA GetStateResult=Exception:{exception.GetType().Name} Result=NotCalled");

    internal void WriteSetState(ProbePhase phase, int slot, byte left8, byte right8, uint result)
        => Write(phase,
            $"Command={CommandName} Api=XInputSetState Slot={slot} Left8={left8} Right8={right8} Left16={Expand(left8)} Right16={Expand(right8)} Result={result}");

    internal void WriteSetStateException(ProbePhase phase, int slot, byte left8, byte right8, Exception exception)
        => Write(phase,
            $"Command={CommandName} Api=XInputSetState Slot={slot} Left8={left8} Right8={right8} Left16={Expand(left8)} Right16={Expand(right8)} Result=Exception:{exception.GetType().Name}");

    internal void WriteIdleMarker(ProbePhase phase, int slot, byte left8, byte right8)
        => Write(phase,
            $"Command={CommandName} Api=none Slot={slot} Left8={left8} Right8={right8} Left16={Expand(left8)} Right16={Expand(right8)} Result=NotCalled");

    internal void WriteCommandFailure(ProbeCommand command, int? slot, string reason)
        => Write(ProbePhase.CommandFailure,
            $"Api=none Command={command.ToString().ToLowerInvariant()} Slot={FormatSlot(slot)} Left8=NA Right8=NA Left16=NA Right16=NA Result=NotCalled Reason={Sanitize(reason)}");

    private void Write(ProbePhase phase, string fields)
    {
        var now = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        var sequence = Interlocked.Increment(ref _probeSequence);
        _writer.WriteLine($"{now} ElapsedTicks={_stopwatch.ElapsedTicks} ElapsedMs={_stopwatch.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} ProbeSeq={sequence} Phase={phase} {fields}");
        _writer.Flush();
    }

    public void Dispose() => _writer.Dispose();

    internal static ushort Expand(byte value) => (ushort)(value * 257);

    private string CommandName => _command.ToString().ToLowerInvariant();

    private static string FormatSlot(int? slot) => slot?.ToString(CultureInfo.InvariantCulture) ?? "Auto";

    private static string Sanitize(string value)
        => value.Replace('\r', ' ').Replace('\n', ' ').Replace('=', ':');
}

internal static class RumbleProbeRunner
{
    internal const uint ErrorSuccess = 0;
    internal const uint ErrorDeviceNotConnected = 1167;
    private static readonly (byte Left, byte Right)[] FixedSequence =
    [
        (1, 1), (0, 0),
        (3, 6), (0, 0),
        (16, 0), (0, 0),
        (64, 32), (0, 0),
        (255, 255), (0, 0),
    ];

    internal static async Task<int> RunAsync(
        ProbeOptions options,
        IXInputApi xinput,
        Func<ProbeRunLog> logFactory,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        ProbeRunLog? log = null;
        try
        {
            log = logFactory();
            log.WriteSessionHeader(options);
        }
        catch (Exception exception)
        {
            try { log?.Dispose(); }
            catch { }
            stderr.WriteLine($"Unable to prepare the RumbleProbe log; no vibration command was sent. {exception.Message}");
            return 1;
        }

        using (log!)
        {
            var preparedLog = log!;
            stdout.WriteLine($"RumbleProbe log: {preparedLog.FilePath}");

            if (options.Command == ProbeCommand.List)
                return ListSlots(xinput, preparedLog, stdout, stderr, cancellationToken);

            SlotSelection selection;
            try
            {
                selection = ResolveSlot(options, xinput, preparedLog, stderr, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryWriteFailure(preparedLog, options, null, "CancelledBeforeMutation");
                return 130;
            }
            if (selection.Slot is not int selectedSlot)
                return 1;

            var exitCode = 0;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (options.Command)
                {
                    case ProbeCommand.Pulse:
                        await RunPulseAsync(options, selectedSlot, xinput, preparedLog, cancellationToken).ConfigureAwait(false);
                        break;
                    case ProbeCommand.Sequence:
                        await RunSequenceAsync(options, selectedSlot, xinput, preparedLog, cancellationToken).ConfigureAwait(false);
                        break;
                    case ProbeCommand.Deadman:
                        await RunDeadmanAsync(options, selectedSlot, xinput, preparedLog, cancellationToken).ConfigureAwait(false);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(options));
                }
                stdout.WriteLine("RumbleProbe completed successfully.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                exitCode = 130;
                stderr.WriteLine("RumbleProbe cancelled; final cleanup STOP was attempted.");
                TryWriteFailure(preparedLog, options, selectedSlot, "Cancelled");
            }
            catch (Exception exception)
            {
                exitCode = 1;
                stderr.WriteLine($"RumbleProbe failed: {exception.Message}");
                TryWriteFailure(preparedLog, options, selectedSlot, exception.Message);
            }
            finally
            {
                if (!TryCleanupStop(selectedSlot, xinput, preparedLog, stderr))
                    exitCode = 1;
            }

            return exitCode;
        }
    }

    private static int ListSlots(
        IXInputApi xinput,
        ProbeRunLog log,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        var failed = false;
        for (uint slot = 0; slot < 4; slot++)
        {
            if (cancellationToken.IsCancellationRequested)
                return 130;

            uint result;
            try
            {
                result = xinput.GetState(slot);
            }
            catch (Exception exception)
            {
                try { log.WriteSlotProbeException((int)slot, exception); }
                catch { }
                stderr.WriteLine($"XInputGetState failed for slot {slot}: {exception.Message}");
                failed = true;
                continue;
            }
            try { log.WriteSlotProbe((int)slot, result); }
            catch (Exception exception)
            {
                stderr.WriteLine($"Unable to write the slot {slot} probe result to the RumbleProbe log: {exception.Message}");
                return 1;
            }

            var connected = result == ErrorSuccess;
            stdout.WriteLine($"Slot {slot}: {(connected ? "connected" : result == ErrorDeviceNotConnected ? "not connected" : "error")} (XInputGetState={result})");
            if (!connected && result != ErrorDeviceNotConnected)
                failed = true;
        }

        return failed ? 1 : 0;
    }

    private static SlotSelection ResolveSlot(
        ProbeOptions options,
        IXInputApi xinput,
        ProbeRunLog log,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        if (options.Slot is int explicitSlot)
            return new(explicitSlot);

        var connected = new List<int>(4);
        for (uint slot = 0; slot < 4; slot++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint result;
            try
            {
                result = xinput.GetState(slot);
            }
            catch (Exception exception)
            {
                try { log.WriteSlotProbeException((int)slot, exception); }
                catch { }
                stderr.WriteLine($"XInputGetState failed for slot {slot}: {exception.Message}");
                return new(null);
            }
            try { log.WriteSlotProbe((int)slot, result); }
            catch (Exception exception)
            {
                stderr.WriteLine($"Unable to write the slot {slot} probe result to the RumbleProbe log: {exception.Message}");
                return new(null);
            }

            if (result == ErrorSuccess)
                connected.Add((int)slot);
            else if (result != ErrorDeviceNotConnected)
            {
                var message = $"XInputGetState returned {result} for slot {slot}.";
                stderr.WriteLine(message);
                return new(null);
            }
        }

        return connected.Count switch
        {
            1 => new(connected[0]),
            0 => Fail("No XInput slots are connected; connect the virtual Xbox360 controller or pass --slot explicitly.", stderr),
            _ => Fail($"Multiple XInput slots are connected ({string.Join(",", connected)}); specify --slot 0..3.", stderr),
        };
    }

    private static SlotSelection Fail(string message, TextWriter stderr)
    {
        stderr.WriteLine(message);
        return new(null);
    }

    private static async Task RunPulseAsync(
        ProbeOptions options,
        int slot,
        IXInputApi xinput,
        ProbeRunLog log,
        CancellationToken cancellationToken)
    {
        for (var iteration = 0; iteration < options.Iterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var started = SendState(ProbePhase.NonZero, slot, options.Left8, options.Right8, xinput, log);
            await WaitUntilAsync(started, options.OnMilliseconds, cancellationToken).ConfigureAwait(false);
            var stopped = SendState(ProbePhase.Stop, slot, 0, 0, xinput, log);
            await WaitUntilAsync(stopped, options.OffMilliseconds, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task RunSequenceAsync(
        ProbeOptions options,
        int slot,
        IXInputApi xinput,
        ProbeRunLog log,
        CancellationToken cancellationToken)
    {
        for (var iteration = 0; iteration < options.Iterations; iteration++)
        {
            foreach (var (left, right) in FixedSequence)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var phase = left == 0 && right == 0 ? ProbePhase.Stop : ProbePhase.NonZero;
                var started = SendState(phase, slot, left, right, xinput, log);
                await WaitUntilAsync(started, options.SequenceStepMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task RunDeadmanAsync(
        ProbeOptions options,
        int slot,
        IXInputApi xinput,
        ProbeRunLog log,
        CancellationToken cancellationToken)
    {
        var started = SendState(ProbePhase.NonZero, slot, options.Left8, options.Right8, xinput, log);
        log.WriteIdleMarker(ProbePhase.IdleBegin, slot, options.Left8, options.Right8);
        await WaitUntilAsync(started, options.IdleMilliseconds, cancellationToken).ConfigureAwait(false);
        log.WriteIdleMarker(ProbePhase.IdleEnd, slot, options.Left8, options.Right8);
    }

    private static long SendState(
        ProbePhase phase,
        int slot,
        byte left8,
        byte right8,
        IXInputApi xinput,
        ProbeRunLog log)
    {
        uint result;
        try
        {
            result = xinput.SetState((uint)slot, ProbeRunLog.Expand(left8), ProbeRunLog.Expand(right8));
        }
        catch (Exception exception)
        {
            try { log.WriteSetStateException(phase, slot, left8, right8, exception); }
            catch { }
            throw;
        }

        var started = Stopwatch.GetTimestamp();
        log.WriteSetState(phase, slot, left8, right8, result);
        if (result != ErrorSuccess)
            throw new XInputCallFailedException(phase, result);
        return started;
    }

    private static async Task WaitUntilAsync(long started, int durationMilliseconds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var duration = TimeSpan.FromMilliseconds(durationMilliseconds);
        var remaining = duration - Stopwatch.GetElapsedTime(started);
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
    }

    private static bool TryCleanupStop(int slot, IXInputApi xinput, ProbeRunLog log, TextWriter stderr)
    {
        uint result;
        try
        {
            result = xinput.SetState((uint)slot, 0, 0);
        }
        catch (Exception exception)
        {
            try { log.WriteSetStateException(ProbePhase.CleanupStop, slot, 0, 0, exception); }
            catch { }
            stderr.WriteLine($"Cleanup STOP call failed: {exception.Message}");
            return false;
        }

        try
        {
            log.WriteSetState(ProbePhase.CleanupStop, slot, 0, 0, result);
        }
        catch (Exception exception)
        {
            stderr.WriteLine($"Cleanup STOP result {result} could not be flushed to the probe log: {exception.Message}");
            return false;
        }

        if (result == ErrorSuccess)
            return true;

        stderr.WriteLine($"Cleanup STOP failed with XInputSetState result {result}; no retry will be attempted.");
        return false;
    }

    private static void TryWriteFailure(ProbeRunLog log, ProbeOptions options, int? slot, string reason)
    {
        try { log.WriteCommandFailure(options.Command, slot, reason); }
        catch { }
    }

    internal static IReadOnlyList<(byte Left, byte Right)> SequenceForTests => FixedSequence;

    private sealed record SlotSelection(int? Slot);

    private sealed class XInputCallFailedException(ProbePhase phase, uint result)
        : Exception($"XInputSetState failed in phase {phase} with result {result}.");
}

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (!ProbeCommandLine.TryParse(args, out var options, out var parseError, out var showHelp))
        {
            Console.Error.WriteLine(parseError);
            WriteUsage(Console.Error);
            return 2;
        }

        if (showHelp)
        {
            WriteUsage(Console.Out);
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            try { cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            return await RumbleProbeRunner.RunAsync(
                options,
                new WindowsXInputApi(),
                ProbeRunLog.OpenFile,
                Console.Out,
                Console.Error,
                cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"RumbleProbe could not complete: {exception.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static void WriteUsage(TextWriter output)
    {
        output.WriteLine("RumbleProbe list");
        output.WriteLine("RumbleProbe pulse [--slot 0..3] [--left8 0..255] [--right8 0..255] [--iterations n] [--on-ms n] [--off-ms n]");
        output.WriteLine("RumbleProbe sequence [--slot 0..3] [--iterations n]  (fixed 250 ms per state)");
        output.WriteLine("RumbleProbe deadman [--slot 0..3] [--left8 0..255] [--right8 0..255] [--idle-ms n]");
        output.WriteLine("Without --slot, a test command requires exactly one connected XInput slot.");
        output.WriteLine("Defaults: pulse left8=3 right8=6 iterations=200 on-ms=250 off-ms=250; deadman left8=3 right8=6 idle-ms=7000.");
    }
}
