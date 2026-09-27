using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Feedback;

namespace SteamInputAddonforClaw.Diagnostics;

internal interface IXbox360RumbleLoopXInput
{
    uint GetState(uint slot);
    uint SetState(uint slot, ushort leftMotor, ushort rightMotor);
}

internal sealed class WindowsXbox360RumbleLoopXInput : IXbox360RumbleLoopXInput
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
        public short LeftThumbX;
        public short LeftThumbY;
        public short RightThumbX;
        public short RightThumbY;
    }
}

/// <summary>One presentation-owned XInput diagnostic run. It observes the existing production
/// callback and never registers another callback or writes the physical sink directly.</summary>
internal sealed class Xbox360RumbleLoopDiagnostic
{
    internal const uint ErrorSuccess = 0;
    internal const uint ErrorDeviceNotConnected = 1167;
    internal static readonly TimeSpan ProductionBurstStepCadence = TimeSpan.FromMilliseconds(65);
    internal static readonly TimeSpan ProductionCycleIdle = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan ProductionTerminalCallbackTimeout = TimeSpan.FromSeconds(1);

    private static readonly IReadOnlyList<RumbleReplayPattern> LiesOfPRumblePatterns = Array.AsReadOnly(new RumbleReplayPattern[]
    {
        new("LiesOfP-A", Array.AsReadOnly(new RumbleReplayStep[] { new(255, 255), new(241, 241), new(217, 217), new(252, 252), new(230, 230), new(208, 208), new(0, 0) })),
        new("LiesOfP-B", Array.AsReadOnly(new RumbleReplayStep[] { new(255, 255), new(242, 242), new(218, 218), new(204, 204), new(0, 0) })),
        new("LiesOfP-C", Array.AsReadOnly(new RumbleReplayStep[] { new(255, 255), new(242, 242), new(217, 217), new(0, 0) })),
        new("LiesOfP-D", Array.AsReadOnly(new RumbleReplayStep[] { new(255, 255), new(239, 239), new(219, 219), new(255, 255), new(247, 247), new(228, 228), new(209, 209), new(0, 0) }))
    });

    private readonly object _callbackGate = new();
    private readonly int _slot;
    private readonly IXbox360RumbleLoopXInput _xinput;
    private readonly Func<PhysicalRumbleWriteResult> _physicalStop;
    private readonly IXbox360UsbTraceCapture? _usbTraceCapture;
    private readonly TimeSpan _burstStepCadence;
    private readonly TimeSpan _cycleIdle;
    private readonly TimeSpan _terminalCallbackTimeout;
    private ExpectedTerminalStop? _expectedTerminalStop;
    private FrontendXbox360RumbleLoopSnapshot _snapshot;
    private int _physicalCleanupRequested;
    private bool _terminalResultFrozen;

    private sealed record ExpectedTerminalStop(
        string RunId,
        int Cycle,
        int Step,
        long ArmedAtTimestamp,
        TaskCompletionSource<bool> Observed);

    internal Xbox360RumbleLoopDiagnostic(
        int slot,
        IXbox360RumbleLoopXInput xinput,
        Func<PhysicalRumbleWriteResult> physicalStop,
        TimeSpan? burstStepCadence = null,
        TimeSpan? terminalCallbackTimeout = null,
        IXbox360UsbTraceCapture? usbTraceCapture = null,
        TimeSpan? cycleIdle = null)
    {
        _slot = slot;
        _xinput = xinput;
        _physicalStop = physicalStop;
        _usbTraceCapture = usbTraceCapture;
        _burstStepCadence = burstStepCadence ?? ProductionBurstStepCadence;
        _cycleIdle = cycleIdle ?? ProductionCycleIdle;
        _terminalCallbackTimeout = terminalCallbackTimeout ?? ProductionTerminalCallbackTimeout;
        var runId = Guid.NewGuid().ToString("N");
        _snapshot = new(true, FrontendXbox360RumbleLoopState.Running, "Running", runId, slot, 0, 0, 0,
            null, null, null, null, null);
    }

    internal FrontendXbox360RumbleLoopSnapshot Snapshot
    {
        get { lock (_callbackGate) return _snapshot; }
    }

    internal bool IsTerminalResultFrozen
    {
        get { lock (_callbackGate) return _terminalResultFrozen; }
    }

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        var currentCycle = 1;
        try
        {
            AppLog.Info("Rumble", "Xbox360 terminal STOP diagnostic started.",
                ("Event", "X360LoopProbeStart"), ("RunId", Snapshot.RunId ?? "Unknown"),
                ("Slot", _slot),
                ("BurstStepIntervalMs", _burstStepCadence.TotalMilliseconds),
                ("CycleIdleMs", _cycleIdle.TotalMilliseconds),
                ("PatternCount", LiesOfPRumblePatterns.Count),
                ("TerminalWaitMs", _terminalCallbackTimeout.TotalMilliseconds),
                ("ProductionDeadmanMs", Xbox360RumbleFeedbackBridge.DefaultSafetyStop.TotalMilliseconds));

            await StartTraceBestEffortAsync(cancellationToken).ConfigureAwait(false);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (currentCycle > 1 && !TryVerifyTopology(out var topologyFailure))
                {
                    var failureReason = topologyFailure.StartsWith("XInputGetState", StringComparison.Ordinal)
                        ? "XInputGetStateFailed"
                        : "XInputTopologyChanged";
                    await FailAndCleanAsync(failureReason, topologyFailure, currentCycle, cancellationToken).ConfigureAwait(false);
                    return;
                }

                var patternIndex = (currentCycle - 1) % LiesOfPRumblePatterns.Count;
                var pattern = LiesOfPRumblePatterns[patternIndex];
                var cycleStart = Stopwatch.GetTimestamp();
                AppLog.Debug("Rumble", "Xbox360 terminal STOP diagnostic cycle started.",
                    ("Event", "X360LoopProbeCycleStart"), ("RunId", Snapshot.RunId ?? "Unknown"),
                    ("Cycle", currentCycle), ("PatternIndex", patternIndex + 1), ("PatternName", pattern.Name),
                    ("Sequence", string.Join(',', pattern.Steps.Select(static step => $"{step.Left8}/{step.Right8}"))),
                    ("XInputSlot", _slot), ("StepCount", pattern.Steps.Count));

                for (var index = 0; index < pattern.Steps.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await DelayUntilAsync(AddDuration(cycleStart, _burstStepCadence, index), cancellationToken).ConfigureAwait(false);
                    var stepValue = pattern.Steps[index];
                    var step = index + 1;
                    UpdateSnapshot(snapshot => snapshot with
                    {
                        Cycle = currentCycle,
                        Step = step,
                        StepCount = pattern.Steps.Count,
                        CurrentValue8 = stepValue.Left8
                    });

                    if (stepValue.Left8 != 0 || stepValue.Right8 != 0)
                    {
                        if (!TrySetState(stepValue, currentCycle, step, pattern.Steps.Count, out var result, out var failure))
                        {
                            await FailAndCleanAsync(
                                failure,
                                $"XInputResult={result}",
                                currentCycle,
                                cancellationToken,
                                failure == "XInputSetStateFailed" ? result : null).ConfigureAwait(false);
                            return;
                        }
                        continue;
                    }

                    var expectation = ArmTerminalStop(currentCycle, step);
                    uint terminalResult;
                    try
                    {
                        terminalResult = _xinput.SetState((uint)_slot, 0, 0);
                    }
                    catch (Exception exception)
                    {
                        ClearExpectation(expectation);
                        await FailAndCleanAsync("XInputSetStateException", exception.GetType().Name, currentCycle, cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    AppLog.Debug("Rumble", "Xbox360 terminal STOP command returned.",
                        ("Event", "X360LoopProbeSend"), ("RunId", Snapshot.RunId ?? "Unknown"),
                        ("Cycle", currentCycle), ("Step", step), ("StepCount", pattern.Steps.Count),
                        ("ExpectedLeft8", 0), ("ExpectedRight8", 0), ("Left16", 0), ("Right16", 0), ("XInputSlot", _slot),
                        ("XInputResult", terminalResult));

                    if (terminalResult != ErrorSuccess)
                    {
                        ClearExpectation(expectation);
                        await FailAndCleanAsync("XInputSetStateFailed", $"XInputResult={terminalResult}", currentCycle, cancellationToken, terminalResult).ConfigureAwait(false);
                        return;
                    }

                    bool observed;
                    try
                    {
                        await expectation.Observed.Task.WaitAsync(_terminalCallbackTimeout, cancellationToken).ConfigureAwait(false);
                        observed = true;
                    }
                    catch (TimeoutException)
                    {
                        observed = false;
                    }
                    finally
                    {
                        ClearExpectation(expectation);
                    }

                    if (!observed)
                    {
                        await FailAndCleanAsync("TerminalStopMissing", "Successful XInput 0/0 had no exact production callback within the bounded wait.", currentCycle, cancellationToken, terminalResult).ConfigureAwait(false);
                        return;
                    }

                    UpdateSnapshot(snapshot => snapshot with { Cycle = currentCycle, Step = pattern.Steps.Count, CurrentValue8 = 0 });
                    AppLog.Debug("Rumble", "Xbox360 terminal STOP callback matched.",
                        ("Event", "X360LoopProbeTerminalStopMatched"), ("RunId", Snapshot.RunId ?? "Unknown"),
                        ("Cycle", currentCycle), ("Step", step), ("XInputSlot", _slot));
                    await DelayUntilAsync(AddDuration(Stopwatch.GetTimestamp(), _cycleIdle), cancellationToken).ConfigureAwait(false);
                    currentCycle++;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ClearExpectation();
        }
        catch (Exception exception)
        {
            ClearExpectation();
            await FailAndCleanAsync("DiagnosticFailed", exception.GetType().Name, currentCycle, CancellationToken.None).ConfigureAwait(false);
        }
    }

    internal void ObserveCallback(byte left8, byte right8, long? callbackSequence)
    {
        ExpectedTerminalStop? expected;
        lock (_callbackGate)
        {
            if (_snapshot.State != FrontendXbox360RumbleLoopState.Running || _terminalResultFrozen) return;
            _snapshot = _snapshot with
            {
                LastObservedLeft8 = left8,
                LastObservedRight8 = right8,
                LastObservedCallbackSequence = callbackSequence
            };
            expected = _expectedTerminalStop;
            if (expected is not null && left8 == 0 && right8 == 0)
                expected.Observed.TrySetResult(true);
        }

        if (expected is not null)
        {
            var elapsedMs = Stopwatch.GetElapsedTime(expected.ArmedAtTimestamp).TotalMilliseconds;
            AppLog.Debug("Rumble", "Xbox360 terminal STOP callback observed by the production bridge.",
                ("Event", left8 == 0 && right8 == 0 ? "X360LoopProbeCallbackMatched" : "X360LoopProbeCallbackObserved"),
                ("RunId", expected.RunId), ("Cycle", expected.Cycle), ("Step", expected.Step),
                ("ObservedLeft8", left8), ("ObservedRight8", right8),
                ("ObservedRumbleSeq", callbackSequence?.ToString(CultureInfo.InvariantCulture) ?? "NotAvailable"),
                ("ElapsedFromSetMs", elapsedMs));
        }
    }

    internal async Task CompleteManualStopAsync()
    {
        var snapshot = Snapshot;
        if (snapshot.State != FrontendXbox360RumbleLoopState.Running) return;

        var slotKnown = false;
        uint getResult = uint.MaxValue;
        uint? setResult = null;
        string? failure = null;
        try
        {
            getResult = _xinput.GetState((uint)_slot);
            slotKnown = getResult == ErrorSuccess;
            if (slotKnown)
                setResult = _xinput.SetState((uint)_slot, 0, 0);
        }
        catch (Exception exception)
        {
            failure = exception.GetType().Name;
        }

        AppLog.Debug("Rumble", "Xbox360 diagnostic cleanup STOP completed.",
            ("Event", "X360LoopProbeDiagnosticCleanupStop"), ("RunId", snapshot.RunId ?? "Unknown"),
            ("Cycle", snapshot.Cycle), ("XInputSlot", _slot), ("SlotKnown", slotKnown),
            ("GetStateResult", getResult), ("XInputResult", setResult?.ToString(CultureInfo.InvariantCulture) ?? "NotCalled"),
            ("Exception", failure ?? "None"));
        WritePhysicalCleanupStop(snapshot, "ManualStop");
        await StopTraceBestEffortAsync("ManualStop").ConfigureAwait(false);
        MarkStopped("Stopped");
    }

    internal Task FinalizeTraceAsync(string reason) => StopTraceBestEffortAsync(reason);

    internal void MarkStopped(string reason)
    {
        UpdateSnapshot(snapshot => snapshot with
        {
            State = FrontendXbox360RumbleLoopState.Stopped,
            Status = reason,
            CurrentValue8 = null
        });
    }

    internal static FrontendXbox360RumbleLoopSnapshot Ready(int slot) =>
        new(true, FrontendXbox360RumbleLoopState.Ready, "Ready", null, slot, 0, 0, 0, null, null, null, null, null);

    internal static int PatternCount => LiesOfPRumblePatterns.Count;

    internal static RumbleReplayPattern GetPatternForCycle(int cycle)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cycle);
        return LiesOfPRumblePatterns[(cycle - 1) % LiesOfPRumblePatterns.Count];
    }

    private ExpectedTerminalStop ArmTerminalStop(int cycle, int step)
    {
        lock (_callbackGate)
        {
            var snapshot = _snapshot;
            var expected = new ExpectedTerminalStop(snapshot.RunId ?? "Unknown", cycle, step, Stopwatch.GetTimestamp(),
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
            _expectedTerminalStop = expected;
            return expected;
        }
    }

    private void ClearExpectation(ExpectedTerminalStop? expected = null)
    {
        lock (_callbackGate)
        {
            if (expected is null || ReferenceEquals(_expectedTerminalStop, expected))
                _expectedTerminalStop = null;
        }
    }

    private bool TrySetState(RumbleReplayStep value, int cycle, int step, int stepCount, out uint result, out string failure)
    {
        try
        {
            var left16 = Xbox360RumbleFeedbackBridge.Expand(value.Left8);
            var right16 = Xbox360RumbleFeedbackBridge.Expand(value.Right8);
            result = _xinput.SetState((uint)_slot, left16, right16);
            AppLog.Debug("Rumble", "Xbox360 diagnostic XInput state sent.",
                ("Event", "X360LoopProbeSend"), ("RunId", Snapshot.RunId ?? "Unknown"),
                ("Cycle", cycle), ("Step", step), ("StepCount", stepCount),
                ("ExpectedLeft8", value.Left8), ("ExpectedRight8", value.Right8),
                ("Left16", left16), ("Right16", right16), ("XInputSlot", _slot), ("XInputResult", result));
            failure = result == ErrorSuccess ? string.Empty : "XInputSetStateFailed";
            return result == ErrorSuccess;
        }
        catch (Exception exception)
        {
            result = uint.MaxValue;
            failure = "XInputSetStateException";
            AppLog.Debug("Rumble", "Xbox360 diagnostic XInput state threw.",
                ("Event", "X360LoopProbeSend"), ("RunId", Snapshot.RunId ?? "Unknown"),
                ("Cycle", cycle), ("Step", step), ("StepCount", stepCount),
                ("ExpectedLeft8", value.Left8), ("ExpectedRight8", value.Right8), ("XInputSlot", _slot),
                ("XInputResult", "Exception:" + exception.GetType().Name));
            return false;
        }
    }

    private bool TryVerifyTopology(out string failure)
    {
        var connected = new List<int>();
        for (uint slot = 0; slot < 4; slot++)
        {
            uint result;
            try { result = _xinput.GetState(slot); }
            catch (Exception exception)
            {
                failure = $"XInputGetStateException:Slot={slot}:{exception.GetType().Name}";
                return false;
            }

            AppLog.Debug("Rumble", "Xbox360 diagnostic topology slot scanned.",
                ("Event", "X360LoopProbeTopologySlot"), ("RunId", Snapshot.RunId ?? "Unknown"),
                ("Cycle", Snapshot.Cycle + 1), ("XInputSlot", slot), ("XInputResult", result));
            if (result == ErrorSuccess) connected.Add((int)slot);
            else if (result != ErrorDeviceNotConnected)
            {
                failure = $"XInputGetStateFailed:Slot={slot}:Result={result}";
                return false;
            }
        }

        if (connected.Count == 1 && connected[0] == _slot)
        {
            failure = string.Empty;
            return true;
        }

        failure = "XInputTopologyChanged:" + string.Join(',', connected);
        return false;
    }

    private async Task FailAndCleanAsync(
        string reason,
        string detail,
        int cycle,
        CancellationToken cancellationToken,
        uint? xinputResult = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = UpdateSnapshot(current =>
        {
            _terminalResultFrozen = true;
            return current with { Cycle = cycle, CurrentValue8 = null };
        });
        WritePhysicalCleanupStop(snapshot, reason);
        await StopTraceBestEffortAsync(reason).ConfigureAwait(false);
        snapshot = UpdateSnapshot(current => current with
        {
            State = FrontendXbox360RumbleLoopState.Failed,
            Status = reason == "TerminalStopMissing" ? "Failed: terminal STOP callback missing" : "Failed: " + reason,
            FailureReason = reason,
            Cycle = cycle,
            CurrentValue8 = null
        });
        AppLog.Error("Rumble", "Xbox360 terminal STOP diagnostic failed.", null,
            ("Event", reason == "TerminalStopMissing" ? "X360LoopProbeTerminalStopMissing" : "X360LoopProbeFailed"),
            ("RunId", snapshot.RunId ?? "Unknown"), ("Cycle", cycle), ("Step", snapshot.Step),
            ("StepCount", snapshot.StepCount), ("XInputSlot", _slot), ("Reason", reason), ("Detail", detail),
            ("ExpectedLeft8", reason == "TerminalStopMissing" ? 0 : (int?)null),
            ("ExpectedRight8", reason == "TerminalStopMissing" ? 0 : (int?)null),
            ("XInputResult", xinputResult?.ToString(CultureInfo.InvariantCulture) ?? "NotAvailable"),
            ("LastObservedLeft8", snapshot.LastObservedLeft8),
            ("LastObservedRight8", snapshot.LastObservedRight8),
            ("LastObservedRumbleSeq", snapshot.LastObservedCallbackSequence?.ToString(CultureInfo.InvariantCulture) ?? "NotAvailable"),
            ("TimeoutMs", reason == "TerminalStopMissing" ? _terminalCallbackTimeout.TotalMilliseconds : (double?)null));
    }

    private async Task StartTraceBestEffortAsync(CancellationToken cancellationToken)
    {
        if (_usbTraceCapture is null) return;
        try
        {
            await _usbTraceCapture.StartAsync(Snapshot.RunId ?? "Unknown", cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AppLog.Debug("Rumble", "Xbox360 USB trace capture start was cancelled.",
                ("Event", "X360LoopProbeUsbTraceStart"), ("RunId", Snapshot.RunId ?? "Unknown"),
                ("Session", Xbox360UsbTraceCapture.SessionName), ("Status", "Unavailable"),
                ("OutputBase", "NotAvailable"), ("Reason", "Cancelled"));
        }
        catch (Exception exception)
        {
            AppLog.Debug("Rumble", "Xbox360 USB trace capture start was unavailable.",
                ("Event", "X360LoopProbeUsbTraceStart"), ("RunId", Snapshot.RunId ?? "Unknown"),
                ("Session", Xbox360UsbTraceCapture.SessionName), ("Status", "Failed"),
                ("OutputBase", "NotAvailable"), ("Reason", exception.GetType().Name));
        }
    }

    private async Task StopTraceBestEffortAsync(string reason)
    {
        if (_usbTraceCapture is null) return;
        try
        {
            await _usbTraceCapture.StopAsync(Snapshot.RunId ?? "Unknown", reason).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Debug("Rumble", "Xbox360 USB trace capture stop was unavailable.",
                ("Event", "X360LoopProbeUsbTraceStop"), ("RunId", Snapshot.RunId ?? "Unknown"),
                ("Session", Xbox360UsbTraceCapture.SessionName), ("Status", "Failed"),
                ("OutputPath", "NotAvailable"), ("Reason", exception.GetType().Name + ":" + reason));
        }
    }

    private void WritePhysicalCleanupStop(FrontendXbox360RumbleLoopSnapshot snapshot, string reason)
    {
        if (Interlocked.Exchange(ref _physicalCleanupRequested, 1) != 0) return;
        PhysicalRumbleWriteResult result;
        try { result = _physicalStop(); }
        catch (Exception exception) { result = new(PhysicalRumbleWriteStatus.Failed, exception.GetType().Name); }
        AppLog.Debug("Rumble", "Xbox360 diagnostic requested its bridge-serialized physical cleanup STOP.",
            ("Event", "X360LoopProbePhysicalCleanupStop"), ("RunId", snapshot.RunId ?? "Unknown"),
            ("Cycle", snapshot.Cycle), ("Status", result.Status), ("Reason", reason), ("WriteReason", result.Reason));
    }

    private FrontendXbox360RumbleLoopSnapshot UpdateSnapshot(Func<FrontendXbox360RumbleLoopSnapshot, FrontendXbox360RumbleLoopSnapshot> update)
    {
        lock (_callbackGate)
        {
            _snapshot = update(_snapshot);
            return _snapshot;
        }
    }

    private async Task DelayUntilAsync(long targetTimestamp, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remainingTicks = targetTimestamp - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0) return;
            var remaining = TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency);
            await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
        }
    }

    private static long AddDuration(long timestamp, TimeSpan duration, int periods = 1) =>
        timestamp + (long)(duration.TotalSeconds * Stopwatch.Frequency * periods);
}

internal readonly record struct RumbleReplayStep(byte Left8, byte Right8);

internal sealed record RumbleReplayPattern(string Name, IReadOnlyList<RumbleReplayStep> Steps);
