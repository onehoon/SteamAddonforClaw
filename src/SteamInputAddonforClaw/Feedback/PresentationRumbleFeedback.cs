using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Feedback;

/// <summary>Full1902 production rumble: one presentation-scoped adapter converts the active virtual
/// presentation's already-decoded (Xbox360) or raw (SteamDeck) host output into a semantic
/// <see cref="TwoMotorRumble"/> and writes it to the one shared physical <see cref="IPhysicalRumbleSink"/>.
///
/// These are deliberately NOT authority managers: they own only a disposable native-callback lifetime,
/// a tiny write-drain lock, and a bounded dead-man safety stop. Lifetime arm/disarm
/// ordering belongs to <c>MsiClawAddonPresentation</c>'s existing gate. The managed delegate stays
/// rooted by <see cref="CanonicalViiperNativeApi"/>'s own callback-root map for exactly as long as
/// native may call it, so a failed native clear here does not create an unrooted callback.</summary>
internal sealed class Xbox360RumbleFeedbackBridge : IDisposable
{
    internal static readonly TimeSpan DefaultSafetyStop = TimeSpan.FromSeconds(5);

    private readonly IPhysicalRumbleSink _sink;
    private readonly Func<Xbox360RumbleCallback?, bool> _setNativeCallback;
    private readonly Xbox360RumbleCallback _callback;
    // Serializes the callback's physical write against Dispose: a callback that already passed the
    // _disposed check can still be inside a (up to 250 ms) physical write while the presentation
    // thread issues the lifecycle STOP -- without this drain the late non-zero write would land AFTER
    // the STOP and leave the motors latched across a switch / Overlay pause / teardown.
    private readonly object _callbackWriteGate = new();
    private readonly TimeSpan _safetyStopDelay;
    private readonly object _safetyGate = new();
    private CancellationTokenSource? _safetyStop;
    private long _feedbackSequence;
    private long _diagnosticRumbleSequence;
    private int _disposed;

    private Xbox360RumbleFeedbackBridge(
        IPhysicalRumbleSink sink,
        Func<Xbox360RumbleCallback?, bool> setNativeCallback,
        TimeSpan safetyStopDelay)
    {
        _sink = sink;
        _setNativeCallback = setNativeCallback;
        _callback = OnRumble;
        _safetyStopDelay = safetyStopDelay;
    }

    /// <summary>8-bit to 16-bit full-range expansion. Preserves exact 8-bit magnitude through the
    /// physical sink's <c>&gt;&gt; 8</c> conversion: 0-&gt;0, 1-&gt;257 (&gt;&gt;8==1), 255-&gt;65535 (&gt;&gt;8==255).</summary>
    internal static ushort Expand(byte value) => (ushort)(value * 257);

    /// <summary>Registers the native Xbox360 rumble callback. Returns <see langword="null"/> (rumble
    /// unavailable for this presentation) if registration is not confirmed -- the caller keeps the
    /// controller presentation healthy regardless.</summary>
    internal static Xbox360RumbleFeedbackBridge? TryArm(
        IPhysicalRumbleSink sink,
        Func<Xbox360RumbleCallback?, bool> setNativeCallback,
        TimeSpan? safetyStop = null)
    {
        var bridge = new Xbox360RumbleFeedbackBridge(sink, setNativeCallback, safetyStop ?? DefaultSafetyStop);
        if (!setNativeCallback(bridge._callback))
        {
            AppLog.Warn("Rumble", "Production rumble callback registration failed.", null,
                ("Event", "ProductionRumbleCallbackArmFailed"), ("Presentation", "Xbox360"));
            return null;
        }
        AppLog.Info("Rumble", "Production rumble callback armed.",
            ("Event", "ProductionRumbleCallbackArmed"), ("Presentation", "Xbox360"));
        return bridge;
    }

    internal bool IsArmed => Volatile.Read(ref _disposed) == 0;

    private void OnRumble(nuint handle, byte leftMotor, byte rightMotor)
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            var diagnosticEnabled = AppLog.IsEnabled(AppLogLevel.Debug);
            var rumbleSequence = diagnosticEnabled ? Interlocked.Increment(ref _diagnosticRumbleSequence) : 0;
            var rumble = new TwoMotorRumble(Expand(leftMotor), Expand(rightMotor));
            lock (_callbackWriteGate)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                if (diagnosticEnabled)
                    AppLog.Debug("Rumble", "Production Xbox360 rumble callback received.",
                        ("Event", "ProductionXbox360RumbleRx"), ("Presentation", "Xbox360"),
                        ("RumbleSeq", rumbleSequence), ("Left8", leftMotor), ("Right8", rightMotor),
                        ("IsStop", rumble.Equals(TwoMotorRumble.Stopped)));
                try
                {
                    var result = _sink.SetRumble(rumble);
                    if (diagnosticEnabled)
                        LogXbox360PhysicalWrite(rumbleSequence, rumble, result);
                    if (result.Status == PhysicalRumbleWriteStatus.Failed)
                        AppLog.Debug("Rumble", "Production rumble write failed.",
                            ("Event", "ProductionRumbleWriteFailed"), ("Presentation", "Xbox360"), ("Reason", result.Reason));
                }
                finally
                {
                    ScheduleSafetyStop(rumble, diagnosticEnabled ? rumbleSequence : null);
                }
            }
        }
        catch
        {
            // Never throw across the native callback boundary.
        }
    }

    private void ScheduleSafetyStop(TwoMotorRumble rumble, long? sourceRumbleSequence)
    {
        CancellationToken token;
        long sequence;

        lock (_safetyGate)
        {
            _safetyStop?.Cancel();
            _safetyStop?.Dispose();
            _safetyStop = null;
            sequence = ++_feedbackSequence;

            if (Volatile.Read(ref _disposed) != 0 || rumble.Equals(TwoMotorRumble.Stopped) || _safetyStopDelay <= TimeSpan.Zero)
                return;

            _safetyStop = new CancellationTokenSource();
            token = _safetyStop.Token;
        }

        _ = StopAfterDelayAsync(sequence, sourceRumbleSequence, token);
    }

    private async Task StopAfterDelayAsync(long sequence, long? sourceRumbleSequence, CancellationToken token)
    {
        try
        {
            await Task.Delay(_safetyStopDelay, token).ConfigureAwait(false);
            lock (_safetyGate)
            {
                if (token.IsCancellationRequested || sequence != _feedbackSequence || Volatile.Read(ref _disposed) != 0)
                    return;
            }
            lock (_callbackWriteGate)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                var diagnosticEnabled = AppLog.IsEnabled(AppLogLevel.Debug);
                if (diagnosticEnabled)
                    AppLog.Debug("Rumble", "Production Xbox360 rumble safety stop fired.",
                        ("Event", "ProductionXbox360RumbleSafetyStop"), ("Presentation", "Xbox360"),
                        ("SourceRumbleSeq", sourceRumbleSequence?.ToString(CultureInfo.InvariantCulture) ?? "NotAvailable"));
                var result = _sink.SetRumble(TwoMotorRumble.Stopped);
                if (diagnosticEnabled)
                    LogXbox360PhysicalWrite(sourceRumbleSequence, TwoMotorRumble.Stopped, result, isSafetyStop: true);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            AppLog.Debug("Rumble", "Production rumble safety stop was contained.",
                ("Event", "ProductionRumbleStopFailed"), ("Presentation", "Xbox360"), ("Reason", exception.GetType().Name));
        }
    }

    private static void LogXbox360PhysicalWrite(
        long? rumbleSequence,
        TwoMotorRumble rumble,
        PhysicalRumbleWriteResult result,
        bool isSafetyStop = false)
    {
        AppLog.Debug("Rumble", "Production Xbox360 physical rumble write completed.",
            ("Event", "ProductionXbox360RumblePhysicalWrite"), ("Presentation", "Xbox360"),
            ("RumbleSeq", rumbleSequence?.ToString(CultureInfo.InvariantCulture) ?? "NotAvailable"),
            ("WriteKind", isSafetyStop ? "SafetyStop" : "Callback"),
            ("Large8", MsiClawRumblePacketBuilder.ToPhysicalByte(rumble.LargeMotor)),
            ("Small8", MsiClawRumblePacketBuilder.ToPhysicalByte(rumble.SmallMotor)),
            ("Status", result.Status), ("Reason", result.Reason));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_safetyGate)
        {
            _safetyStop?.Cancel();
            _safetyStop?.Dispose();
            _safetyStop = null;
            _feedbackSequence++;
        }
        RumbleCallbackCleanup.ClearNativeCallback(_setNativeCallback, "Xbox360");
        // Drain a callback that already passed the first _disposed check and may still be inside its
        // physical write, so the presentation's subsequent lifecycle STOP is the final write.
        lock (_callbackWriteGate) { }
    }
}

/// <summary>SteamDeck production rumble: decodes the raw normalized Deck host-output report with the
/// stateless <see cref="SteamDeckRumbleDecoder"/> and writes recognized rumble/haptic packets to the
/// shared physical sink. Preserves the physical-output safety semantics that were local to the
/// deleted routing-era bridge -- a haptic <c>CommandType == 0</c> STOP and a bounded dead-man stop
/// for non-zero output -- with only a tiny local timer, no authority machinery.</summary>
internal sealed class SteamDeckRumbleFeedbackAdapter : IDisposable
{
    // Non-zero output that is never explicitly stopped by the host must not latch the physical
    // motors: a timed haptic uses its declared duration, an untimed haptic a 1 s fallback, and an
    // EB rumble a 5 s dead-man window (matching the deleted bridge's intent).
    internal static readonly TimeSpan DefaultUntimedHapticSafetyStop = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan DefaultRumbleSafetyStop = TimeSpan.FromSeconds(5);
    private const int DiagnosticRawPrefixLength = 64;
    private const int DiagnosticOtherOpcodeExampleLimit = 4;
    private static readonly long RumbleDiagnosticSampleIntervalTicks = Math.Max(1, Stopwatch.Frequency / 10);

    private readonly IPhysicalRumbleSink _sink;
    private readonly Func<bool> _clearNativeCallback;
    private readonly SteamDeckOutputCallback _callback;
    private readonly TimeSpan _untimedHapticSafetyStop;
    private readonly TimeSpan _rumbleSafetyStop;
    private readonly object _callbackWriteGate = new();
    private readonly object _safetyGate = new();
    private CancellationTokenSource? _safetyStop;
    private long _feedbackSequence;
    private long _diagnosticRxSequence;
    private readonly int[] _diagnosticOtherOpcodeCounts = new int[257];
    private bool _diagnosticRunActive;
    private bool _hasLastLoggedRumble;
    private TwoMotorRumble _lastLoggedRumble;
    private long _lastRumbleDiagnosticTimestamp;
    private int _disposed;

    private SteamDeckRumbleFeedbackAdapter(IPhysicalRumbleSink sink, Func<bool> clearNativeCallback, TimeSpan untimedHapticSafetyStop, TimeSpan rumbleSafetyStop)
    {
        _sink = sink;
        _clearNativeCallback = clearNativeCallback;
        _callback = OnOutput;
        _untimedHapticSafetyStop = untimedHapticSafetyStop;
        _rumbleSafetyStop = rumbleSafetyStop;
    }

    /// <summary>Registers the native Deck output callback via <paramref name="setNativeCallback"/>.
    /// Returns <see langword="null"/> if registration is not confirmed.</summary>
    internal static SteamDeckRumbleFeedbackAdapter? TryArm(
        IPhysicalRumbleSink sink,
        Func<SteamDeckOutputCallback, bool> setNativeCallback,
        Func<bool> clearNativeCallback,
        TimeSpan? untimedHapticSafetyStop = null,
        TimeSpan? rumbleSafetyStop = null)
    {
        var adapter = new SteamDeckRumbleFeedbackAdapter(
            sink, clearNativeCallback,
            untimedHapticSafetyStop ?? DefaultUntimedHapticSafetyStop,
            rumbleSafetyStop ?? DefaultRumbleSafetyStop);
        if (!setNativeCallback(adapter._callback))
        {
            AppLog.Warn("Rumble", "Production rumble callback registration failed.", null,
                ("Event", "ProductionRumbleCallbackArmFailed"), ("Presentation", "SteamDeck"));
            return null;
        }
        AppLog.Info("Rumble", "Production rumble callback armed.",
            ("Event", "ProductionRumbleCallbackArmed"), ("Presentation", "SteamDeck"));
        return adapter;
    }

    private void OnOutput(nuint handle, nint data, uint length)
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            var diagnosticsEnabled = AppLog.IsEnabled(AppLogLevel.Debug);
            PrepareDiagnosticRun(diagnosticsEnabled);
            if (data == 0 || length == 0)
            {
                if (diagnosticsEnabled)
                {
                    var rxSequence = Interlocked.Increment(ref _diagnosticRxSequence);
                    var decodedEmpty = SteamDeckRumbleDecoder.Decode(ReadOnlySpan<byte>.Empty);
                    if (ShouldLogOtherOpcodeExample(null))
                        LogSteamDeckFeedbackRx(rxSequence, [], length, decodedEmpty, "Ignored");
                }
                return;
            }
            var report = new byte[(int)Math.Min(length, 512u)];
            Marshal.Copy(data, report, 0, report.Length);
            var decoded = SteamDeckRumbleDecoder.Decode(report);
            var diagnosticRxSequence = diagnosticsEnabled ? Interlocked.Increment(ref _diagnosticRxSequence) : 0;
            var shouldLogReceive = diagnosticsEnabled && ShouldLogSteamDeckFeedback(report, decoded);
            if (!decoded.HasPhysicalTranslation)
            {
                if (shouldLogReceive)
                    LogSteamDeckFeedbackRx(diagnosticRxSequence, report, length, decoded, "Ignored");
                return;
            }

            // A haptic command 0 is an explicit STOP, not persistent motor state.
            var rumble = decoded.Command == SteamDeckFeedbackCommand.Haptic && decoded.Haptic?.CommandType == 0
                ? TwoMotorRumble.Stopped
                : decoded.Rumble;

            PhysicalRumbleWriteResult result;
            lock (_callbackWriteGate)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                if (shouldLogReceive)
                    LogSteamDeckFeedbackRx(
                        diagnosticRxSequence,
                        report,
                        length,
                        decoded,
                        rumble.Equals(TwoMotorRumble.Stopped) ? "Stop" : "SinkAttempted");
                result = _sink.SetRumble(rumble);
                if (result.Status == PhysicalRumbleWriteStatus.Failed)
                    AppLog.Debug("Rumble", "Production rumble write failed.",
                        ("Event", "ProductionRumbleWriteFailed"), ("Presentation", "SteamDeck"), ("Command", decoded.Command), ("Reason", result.Reason));
            }

            ScheduleSafetyStop(decoded, rumble, diagnosticsEnabled ? diagnosticRxSequence : null);
            if (diagnosticsEnabled && (shouldLogReceive || result.Status != PhysicalRumbleWriteStatus.Succeeded))
            {
                if (!shouldLogReceive)
                    LogSteamDeckFeedbackRx(
                        diagnosticRxSequence,
                        report,
                        length,
                        decoded,
                        rumble.Equals(TwoMotorRumble.Stopped) ? "Stop" : "SinkAttempted");
                LogSteamDeckFeedbackSinkResult(diagnosticRxSequence, report, decoded, rumble, result);
            }
        }
        catch
        {
            // Never throw across the native callback boundary.
        }
    }

    private void PrepareDiagnosticRun(bool diagnosticsEnabled)
    {
        if (!diagnosticsEnabled)
        {
            _diagnosticRunActive = false;
            return;
        }

        if (_diagnosticRunActive) return;
        Array.Clear(_diagnosticOtherOpcodeCounts, 0, _diagnosticOtherOpcodeCounts.Length);
        _hasLastLoggedRumble = false;
        _lastLoggedRumble = default;
        _lastRumbleDiagnosticTimestamp = 0;
        _diagnosticRunActive = true;
    }

    private bool ShouldLogSteamDeckFeedback(byte[] report, SteamDeckFeedbackDecodeResult decoded)
    {
        if (report.Length == 0) return ShouldLogOtherOpcodeExample(null);

        var opcode = report[0];
        if (opcode is 0xEA or 0x8F or 0xDC) return true;
        if (opcode == 0xEB && decoded.Command == SteamDeckFeedbackCommand.Rumble)
            return ShouldLogRumble(decoded.Rumble);

        return ShouldLogOtherOpcodeExample(opcode);
    }

    private bool ShouldLogRumble(TwoMotorRumble rumble)
    {
        var now = Stopwatch.GetTimestamp();
        if (!_hasLastLoggedRumble)
        {
            RememberRumbleDiagnostic(rumble, now);
            return true;
        }

        if (rumble.Equals(TwoMotorRumble.Stopped))
        {
            if (_lastLoggedRumble.Equals(TwoMotorRumble.Stopped)) return false;
            RememberRumbleDiagnostic(rumble, now);
            return true;
        }

        if (_lastLoggedRumble.Equals(TwoMotorRumble.Stopped) ||
            now - _lastRumbleDiagnosticTimestamp >= RumbleDiagnosticSampleIntervalTicks)
        {
            RememberRumbleDiagnostic(rumble, now);
            return true;
        }

        return false;
    }

    private void RememberRumbleDiagnostic(TwoMotorRumble rumble, long timestamp)
    {
        _lastLoggedRumble = rumble;
        _lastRumbleDiagnosticTimestamp = timestamp;
        _hasLastLoggedRumble = true;
    }

    private bool ShouldLogOtherOpcodeExample(byte? opcode)
    {
        var index = opcode ?? 256;
        return Interlocked.Increment(ref _diagnosticOtherOpcodeCounts[index]) <= DiagnosticOtherOpcodeExampleLimit;
    }

    private static void LogSteamDeckFeedbackRx(
        long rxSequence,
        byte[] report,
        uint originalLength,
        SteamDeckFeedbackDecodeResult decoded,
        string disposition)
    {
        var rawLength = Math.Min(report.Length, DiagnosticRawPrefixLength);
        var fields = new List<(string Key, object? Value)>
        {
            ("Event", "SteamDeckFeedbackRx"),
            ("Presentation", "SteamDeck"),
            ("RxSeq", rxSequence),
            ("Opcode", report.Length == 0 ? "None" : report[0].ToString("X2", CultureInfo.InvariantCulture)),
            ("OriginalLength", originalLength),
            ("CapturedLength", report.Length),
            ("RawHex", Convert.ToHexString(report.AsSpan(0, rawLength))),
            ("RawTruncated", originalLength > DiagnosticRawPrefixLength),
            ("DecodeResult", decoded.Command),
            ("HasPhysicalTranslation", decoded.HasPhysicalTranslation),
            ("Disposition", disposition),
        };

        if (report.Length > 0)
        {
            switch (report[0])
            {
                case 0xEB when decoded.Command == SteamDeckFeedbackCommand.Rumble:
                    fields.Add(("RumbleType", decoded.RumbleType));
                    fields.Add(("RumbleIntensity", decoded.RumbleIntensity));
                    fields.Add(("RumbleLeftGain", decoded.RumbleLeftGain));
                    fields.Add(("RumbleRightGain", decoded.RumbleRightGain));
                    fields.Add(("Large16", decoded.Rumble.LargeMotor));
                    fields.Add(("Small16", decoded.Rumble.SmallMotor));
                    break;
                case 0xEA when decoded.Haptic is { } haptic:
                    fields.Add(("Side", haptic.Side));
                    fields.Add(("CommandType", haptic.CommandType));
                    fields.Add(("UiIntensity", haptic.UiIntensity));
                    fields.Add(("DbGain", haptic.DbGain));
                    fields.Add(("IsModernSdlLayout", haptic.IsModernSdlLayout));
                    if (haptic.DurationMilliseconds is short durationMilliseconds)
                        fields.Add(("DurationMilliseconds", durationMilliseconds));
                    if (decoded.Strength8 is byte strength8)
                        fields.Add(("Strength8", strength8));
                    break;
                case 0x8F when decoded.HapticPulse is { } pulse:
                    fields.Add(("Side", pulse.Side));
                    fields.Add(("OnDurationMicroseconds", pulse.OnDurationMicroseconds));
                    fields.Add(("OffIntervalMicroseconds", pulse.OffIntervalMicroseconds));
                    fields.Add(("Count", pulse.Count));
                    fields.Add(("GainRaw", pulse.GainRaw));
                    fields.Add(("IsLinuxLayout", pulse.IsLinuxLayout));
                    break;
            }
        }

        AppLog.Debug("Rumble", "Steam Deck host feedback received.", fields.ToArray());
    }

    private static void LogSteamDeckFeedbackSinkResult(
        long rxSequence,
        byte[] report,
        SteamDeckFeedbackDecodeResult decoded,
        TwoMotorRumble rumble,
        PhysicalRumbleWriteResult result)
    {
        AppLog.Debug("Rumble", "Steam Deck host feedback physical sink attempt completed.",
            ("Event", "SteamDeckFeedbackSinkResult"),
            ("Presentation", "SteamDeck"),
            ("RxSeq", rxSequence),
            ("Opcode", report.Length == 0 ? "None" : report[0].ToString("X2", CultureInfo.InvariantCulture)),
            ("DecodeResult", decoded.Command),
            ("Large16", rumble.LargeMotor),
            ("Small16", rumble.SmallMotor),
            ("Status", result.Status),
            ("Reason", result.Reason),
            ("Stopped", rumble.Equals(TwoMotorRumble.Stopped)));
    }

    private void ScheduleSafetyStop(
        SteamDeckFeedbackDecodeResult decoded,
        TwoMotorRumble rumble,
        long? sourceDiagnosticRxSequence)
    {
        CancellationToken token;
        long sequence;
        TimeSpan delay = TimeSpan.Zero;

        lock (_safetyGate)
        {
            _safetyStop?.Cancel();
            _safetyStop?.Dispose();
            _safetyStop = null;
            sequence = ++_feedbackSequence;

            if (Volatile.Read(ref _disposed) != 0 || rumble.Equals(TwoMotorRumble.Stopped))
                return;

            delay = decoded.Command switch
            {
                SteamDeckFeedbackCommand.Haptic when decoded.Haptic is { DurationMilliseconds: > 0 } haptic =>
                    TimeSpan.FromMilliseconds(haptic.DurationMilliseconds!.Value),
                SteamDeckFeedbackCommand.Haptic => _untimedHapticSafetyStop,
                SteamDeckFeedbackCommand.Rumble => _rumbleSafetyStop,
                _ => TimeSpan.Zero,
            };
            if (delay <= TimeSpan.Zero) return;

            _safetyStop = new CancellationTokenSource();
            token = _safetyStop.Token;
        }

        _ = StopAfterDelayAsync(sequence, delay, token, sourceDiagnosticRxSequence);
    }

    private async Task StopAfterDelayAsync(
        long sequence,
        TimeSpan delay,
        CancellationToken token,
        long? sourceDiagnosticRxSequence)
    {
        try
        {
            await Task.Delay(delay, token).ConfigureAwait(false);
            lock (_safetyGate)
            {
                // A newer packet (or Dispose) has superseded this delayed stop.
                if (token.IsCancellationRequested || sequence != _feedbackSequence || Volatile.Read(ref _disposed) != 0)
                    return;
            }
            lock (_callbackWriteGate)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                var result = _sink.SetRumble(TwoMotorRumble.Stopped);
                if (AppLog.IsEnabled(AppLogLevel.Debug))
                    AppLog.Debug("Rumble", "Steam Deck feedback safety stop completed.",
                        ("Event", "SteamDeckFeedbackSafetyStop"),
                        ("Presentation", "SteamDeck"),
                        ("RxSeq", sourceDiagnosticRxSequence?.ToString(CultureInfo.InvariantCulture) ?? "NotAvailable"),
                        ("Large16", TwoMotorRumble.Stopped.LargeMotor),
                        ("Small16", TwoMotorRumble.Stopped.SmallMotor),
                        ("Status", result.Status),
                        ("Reason", result.Reason),
                        ("Stopped", true));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            AppLog.Debug("Rumble", "Production rumble safety stop was contained.",
                ("Event", "ProductionRumbleStopFailed"), ("Presentation", "SteamDeck"), ("Reason", exception.GetType().Name));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_safetyGate)
        {
            _safetyStop?.Cancel();
            _safetyStop?.Dispose();
            _safetyStop = null;
            _feedbackSequence++;
        }
        RumbleCallbackCleanup.ClearNativeCallback(() => _clearNativeCallback(), "SteamDeck");
        lock (_callbackWriteGate) { }
    }
}

/// <summary>Maps VIIPER's rumble-only DualSense Edge output callback to the shared physical sink.</summary>
internal sealed class DualSenseEdgeRumbleFeedbackAdapter : IDisposable
{
    internal static readonly TimeSpan DefaultSafetyStop = TimeSpan.FromSeconds(5);

    private readonly IPhysicalRumbleSink _sink;
    private readonly Func<bool> _clearNativeCallback;
    private readonly TimeSpan _safetyStopDelay;
    private readonly object _callbackWriteGate = new();
    private readonly object _safetyGate = new();
    private readonly DualSenseOutputCallback _callback;
    private CancellationTokenSource? _safetyStop;
    private long _feedbackSequence;
    private int _disposed;

    private DualSenseEdgeRumbleFeedbackAdapter(IPhysicalRumbleSink sink, Func<bool> clearNativeCallback, TimeSpan safetyStopDelay)
    {
        _sink = sink;
        _clearNativeCallback = clearNativeCallback;
        _safetyStopDelay = safetyStopDelay;
        _callback = OnOutput;
    }

    internal static ushort Expand(byte value) => (ushort)(value * 257);
    internal bool IsArmed => Volatile.Read(ref _disposed) == 0;

    internal static DualSenseEdgeRumbleFeedbackAdapter? TryArm(
        IPhysicalRumbleSink sink,
        Func<DualSenseOutputCallback, bool> setNativeCallback,
        Func<bool> clearNativeCallback,
        TimeSpan? safetyStop = null)
    {
        var adapter = new DualSenseEdgeRumbleFeedbackAdapter(sink, clearNativeCallback, safetyStop ?? DefaultSafetyStop);
        if (setNativeCallback(adapter._callback)) return adapter;
        AppLog.Warn("Rumble", "DualSense Edge rumble callback registration failed.", null,
            ("Event", "ProductionRumbleCallbackArmFailed"), ("Presentation", "DualSenseEdge"));
        return null;
    }

    private void OnOutput(nuint handle, byte rumbleSmall, byte rumbleLarge, byte ledRed, byte ledGreen, byte ledBlue, byte playerLeds)
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            // The experimental VIIPER callback is rumble-only. LED fields are intentionally ignored.
            var rumble = new TwoMotorRumble(Expand(rumbleLarge), Expand(rumbleSmall));
            lock (_callbackWriteGate)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                var isStop = rumble.Equals(TwoMotorRumble.Stopped);
                var shouldUpdateSafetyStop = !isStop;
                try
                {
                    var result = _sink.SetRumble(rumble);
                    if (isStop)
                        shouldUpdateSafetyStop = result.Status != PhysicalRumbleWriteStatus.Failed;
                }
                finally
                {
                    if (shouldUpdateSafetyStop)
                        ScheduleSafetyStop(rumble);
                }
            }
        }
        catch
        {
            // Never throw across the native C ABI callback boundary.
        }
    }

    private void ScheduleSafetyStop(TwoMotorRumble rumble)
    {
        CancellationToken token = default;
        long sequence;
        lock (_safetyGate)
        {
            _safetyStop?.Cancel();
            _safetyStop?.Dispose();
            _safetyStop = null;
            sequence = ++_feedbackSequence;
            if (Volatile.Read(ref _disposed) != 0 || rumble.Equals(TwoMotorRumble.Stopped) || _safetyStopDelay <= TimeSpan.Zero)
                return;
            _safetyStop = new CancellationTokenSource();
            token = _safetyStop.Token;
        }
        _ = StopAfterDelayAsync(sequence, token);
    }

    private async Task StopAfterDelayAsync(long sequence, CancellationToken token)
    {
        try
        {
            await Task.Delay(_safetyStopDelay, token).ConfigureAwait(false);
            lock (_callbackWriteGate)
            {
                // Recheck while holding the same gate used by native callback writes. A newer
                // callback may arrive after the timer's first check but before it gets this gate.
                lock (_safetyGate)
                    if (token.IsCancellationRequested || sequence != _feedbackSequence || Volatile.Read(ref _disposed) != 0) return;
                _sink.SetRumble(TwoMotorRumble.Stopped);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            AppLog.Debug("Rumble", "DualSense Edge rumble safety stop was contained.",
                ("Event", "ProductionRumbleStopFailed"), ("Presentation", "DualSenseEdge"), ("Reason", exception.GetType().Name));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_safetyGate)
        {
            _safetyStop?.Cancel();
            _safetyStop?.Dispose();
            _safetyStop = null;
            _feedbackSequence++;
        }
        RumbleCallbackCleanup.ClearNativeCallback(_clearNativeCallback, "DualSenseEdge");
        lock (_callbackWriteGate) { }
    }
}

internal static class RumbleCallbackCleanup
{
    /// <summary>Clears the native callback registration (a failed clear is still safe because future
    /// callbacks see <c>_disposed</c>) and logs the outcome, shared by both presentation adapters.</summary>
    internal static void ClearNativeCallback(Func<bool> clear, string presentation)
    {
        try
        {
            if (clear())
                AppLog.Info("Rumble", "Production rumble callback disarmed.",
                    ("Event", "ProductionRumbleCallbackDisarmed"), ("Presentation", presentation));
            else
                AppLog.Warn("Rumble", "Production rumble callback clear was not confirmed; native root retained until device teardown.", null,
                    ("Event", "ProductionRumbleCallbackClearFailed"), ("Presentation", presentation));
        }
        catch (Exception exception)
        {
            AppLog.Warn("Rumble", "Production rumble callback clear threw.", exception,
                ("Event", "ProductionRumbleCallbackClearFailed"), ("Presentation", presentation));
        }
    }

    internal static void ClearNativeCallback(Func<Xbox360RumbleCallback?, bool> clear, string presentation)
        => ClearNativeCallback(() => clear(null), presentation);

    internal static void ClearNativeCallback(Func<DualSenseOutputCallback?, bool> clear, string presentation)
        => ClearNativeCallback(() => clear(null), presentation);
}
