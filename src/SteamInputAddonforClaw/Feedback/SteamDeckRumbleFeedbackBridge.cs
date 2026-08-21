using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Feedback;

/// <summary>Outcome of a single developer test command: <see cref="Succeeded"/> reflects the existing
/// authority/sequence-acceptance contract (unchanged from callers' perspective), while
/// <see cref="CommandResult"/> and <see cref="StopResult"/> carry the actual physical write status/
/// reason for diagnostic logging -- acceptance and physical success are different questions, and a
/// real MSI HID write failure must be visible in the dedicated log even when acceptance succeeded.</summary>
internal readonly record struct DeveloperVibrationTestOutcome(bool Succeeded, PhysicalRumbleWriteResult? CommandResult, PhysicalRumbleWriteResult? StopResult, SteamDeckFeedbackDecodeResult? Decode = null);

internal sealed class SteamDeckRumbleFeedbackBridge
{
    private const uint MaximumCallbackLength = 64;
    private const int EbDeadManMilliseconds = 5000;
    private const int UnknownHapticFallbackMilliseconds = 1000;
    private readonly FeedbackAuthority _authority;
    private readonly FeedbackAuthorityToken _token;
    private readonly IPhysicalRumbleSink _sink;
    private readonly object _gate = new();
    private readonly LatestWinsRumbleWriter _writer;
    private long _sequence;
    private bool _disposed;
    private CancellationTokenSource? _developerTest;
    private long _developerSequence;
    private Task<PhysicalRumbleWriteResult>? _developerCompletion;

    internal SteamDeckRumbleFeedbackBridge(FeedbackAuthority authority, FeedbackAuthorityToken token, IPhysicalRumbleSink sink)
    {
        _authority = authority;
        _token = token;
        _sink = sink;
        _writer = new LatestWinsRumbleWriter(sink);
        Callback = OnNativeOutput;
    }

    internal SteamDeckOutputCallback Callback { get; }
    internal Action? BeforeLease { get; set; }
    internal Func<TimeSpan, CancellationToken, Task>? DeveloperDelayOverride { get; set; }
    internal bool ProcessNormalizedReport(ReadOnlySpan<byte> report, string origin = "Steam") => ProcessNormalizedReportCore(report, origin, out _, out _);

    internal bool ProcessNormalizedReport(ReadOnlySpan<byte> report, string origin, out long sequence) => ProcessNormalizedReportCore(report, origin, out sequence, out _);

    internal bool ProcessNormalizedReport(ReadOnlySpan<byte> report, string origin, out long sequence, out PhysicalRumbleWriteResult? physicalResult) =>
        ProcessNormalizedReportCore(report, origin, out sequence, out physicalResult);

    private bool ProcessNormalizedReportCore(ReadOnlySpan<byte> report, string origin, out long sequence, out PhysicalRumbleWriteResult? physicalResult)
    {
        sequence = 0;
        physicalResult = null;
        var decoded = SteamDeckRumbleDecoder.Decode(report);
        if (!decoded.HasPhysicalTranslation) return false;
        sequence = BeginFeedback();
        if (origin == "DeveloperVibrationTest")
        {
            lock (_gate) _developerSequence = sequence;
        }
        BeforeLease?.Invoke();
        var rumble = decoded.Rumble;
        var duration = GetSafetyDuration(decoded);
        if (decoded.Command == SteamDeckFeedbackCommand.Haptic && decoded.Haptic?.CommandType == 0)
            rumble = TwoMotorRumble.Stopped;
        if (!TryWrite(sequence, rumble, duration, out physicalResult))
        {
            AppLog.Debug("Rumble", "SteamDeck feedback DROP", ("Reason", "AuthorityRejected"), ("Origin", origin));
            return false;
        }
        AppLog.Debug("Rumble", "Steam Deck feedback processed.", ("Origin", origin), ("Command", decoded.Command));
        return true;
    }

    internal void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _sequence++;
            _developerTest?.Cancel();
            _developerTest?.Dispose();
            _developerTest = null;
        }
        _writer.Retire();
    }

    internal void Retire() => _writer.Retire();

    internal async Task<DeveloperVibrationTestOutcome> ProcessDeveloperTestAsync(ReadOnlyMemory<byte> report, bool addDeveloperStop, CancellationToken cancellationToken)
    {
        CancellationTokenSource linked;
        lock (_gate)
        {
            _developerTest?.Cancel();
            _developerTest?.Dispose();
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _developerTest = linked;
        }
        try
        {
            var decoded = SteamDeckRumbleDecoder.Decode(report.Span);
            if (!ProcessNormalizedReport(report.Span, "DeveloperVibrationTest", out var sequence, out var commandResult))
                return new(false, commandResult, null, decoded);
            Task<PhysicalRumbleWriteResult>? commandCompletion;
            lock (_gate) commandCompletion = _developerCompletion;
            if (commandCompletion is not null)
            {
                var completed = await commandCompletion.WaitAsync(linked.Token).ConfigureAwait(false);
                commandResult = completed;
            }
            lock (_gate)
            {
                if (sequence == _sequence && _developerSequence == sequence && ReferenceEquals(_developerTest, linked))
                    _developerSequence = sequence;
            }
            if (!addDeveloperStop) return new(true, commandResult, null, decoded);
            var delay = DeveloperDelayOverride ?? Task.Delay;
            await delay(TimeSpan.FromMilliseconds(250), linked.Token).ConfigureAwait(false);
            // Write directly against the original sequence instead of routing back through
            // ProcessNormalizedReport (which would call BeginFeedback() again): if real Steam
            // feedback arrived during the 250ms delay it is now the newest sequence, and this
            // stale developer STOP must be a silent no-op rather than stopping that newer feedback.
            var stopAccepted = TryWrite(sequence, TwoMotorRumble.Stopped, TimeSpan.Zero, out var stopResult);
            if (stopAccepted && stopResult?.Reason == "Queued" && _developerCompletion is not null)
                stopResult = await _developerCompletion.WaitAsync(linked.Token).ConfigureAwait(false);
            return new(stopAccepted, commandResult, stopResult, decoded);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { return new(false, null, null); }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_developerTest, linked))
                {
                    _developerTest = null;
                    _developerSequence = 0;
                    _developerCompletion = null;
                }
            }
            linked.Dispose();
        }
    }

    /// <summary>Called when the Vibration Test detail page is left: cancels any pending
    /// developer-owned delayed STOP (so it can no longer fire and stop newer real feedback) and
    /// issues a best-effort production-path zero write to leave the motors physically stopped.</summary>
    internal PhysicalRumbleWriteResult? CancelDeveloperTestAndStop()
    {
        long developerSequence;
        lock (_gate)
        {
            developerSequence = _developerSequence;
            _developerSequence = 0;
            _developerTest?.Cancel();
            _developerTest?.Dispose();
            _developerTest = null;
        }
        if (developerSequence == 0) return null;
        return TryWrite(developerSequence, TwoMotorRumble.Stopped, TimeSpan.Zero, out var physicalResult)
            ? physicalResult
            : null;
    }

    private void OnNativeOutput(nuint handle, nint data, uint length)
    {
        try
        {
            // Diagnostics only: every classification path is logged before its return so a
            // hardware Ping can be matched to exactly what Steam sent, without changing behavior.
            if (length > MaximumCallbackLength || (data == 0 && length != 0))
            {
                AppLog.Debug("Rumble", "SteamDeck feedback RX invalid callback.", ("Length", length));
                return;
            }
            var report = new byte[(int)length];
            if (length != 0) Marshal.Copy(data, report, 0, report.Length);

            var opcode = report.Length > 0 ? report[0] : (byte?)null;
            AppLog.Debug("Rumble", "SteamDeck feedback RX", ("Opcode", opcode is null ? "None" : $"0x{opcode:X2}"), ("Length", length));

            var decoded = SteamDeckRumbleDecoder.Decode(report);
            if (decoded.Command == SteamDeckFeedbackCommand.Rumble)
                AppLog.Debug("Rumble", "SteamDeck feedback Decode", ("Command", decoded.Command), ("Type", decoded.RumbleType), ("Intensity", decoded.RumbleIntensity), ("Left", decoded.Rumble.LargeMotor), ("Right", decoded.Rumble.SmallMotor), ("LeftGain", decoded.RumbleLeftGain), ("RightGain", decoded.RumbleRightGain));
            else if (decoded.Command == SteamDeckFeedbackCommand.Haptic)
                AppLog.Debug("Rumble", "SteamDeck feedback Decode", ("Command", decoded.Command), ("PayloadLength", decoded.Haptic?.DeclaredPayloadLength), ("ModernSdl", decoded.Haptic?.IsModernSdlLayout), ("Side", decoded.Haptic?.Side), ("HapticType", decoded.Haptic?.CommandType), ("UiIntensity", decoded.Haptic?.UiIntensity), ("DbGain", decoded.Haptic?.DbGain), ("Frequency", decoded.Haptic?.Frequency), ("DurationMs", decoded.Haptic?.DurationMilliseconds), ("NoiseIntensity", decoded.Haptic?.NoiseIntensity), ("LfoFrequency", decoded.Haptic?.LfoFrequency), ("LfoDepth", decoded.Haptic?.LfoDepth), ("RandomToneGain", decoded.Haptic?.RandomToneGain), ("ScriptId", decoded.Haptic?.ScriptId), ("SweepStartFrequency", decoded.Haptic?.SweepStartFrequency), ("SweepEndFrequency", decoded.Haptic?.SweepEndFrequency), ("FallbackStrength8", decoded.Strength8));
            else if (decoded.Command == SteamDeckFeedbackCommand.HapticPulse)
                AppLog.Debug("Rumble", "SteamDeck feedback Decode", ("Command", decoded.Command), ("PayloadLength", decoded.HapticPulse?.DeclaredPayloadLength), ("LinuxLayout", decoded.HapticPulse?.IsLinuxLayout), ("Side", decoded.HapticPulse?.Side), ("OnDurationUs", decoded.HapticPulse?.OnDurationMicroseconds), ("OffIntervalUs", decoded.HapticPulse?.OffIntervalMicroseconds), ("Count", decoded.HapticPulse?.Count), ("GainRaw", decoded.HapticPulse?.GainRaw), ("PhysicalTranslation", "None"));
            else
                AppLog.Debug("Rumble", "SteamDeck feedback Decode", ("Command", decoded.Command));

            ProcessNormalizedReport(report, "Steam");
        }
        catch (Exception exception)
        {
            try { AppLog.Debug("Rumble", "Steam Deck feedback callback was contained.", ("Reason", exception.GetType().Name)); }
            catch { /* Never allow diagnostics to cross the unmanaged callback boundary. */ }
        }
    }

    private long BeginFeedback()
    {
        lock (_gate)
        {
            _sequence++;
            var sequence = _sequence;
            return sequence;
        }
    }

    private bool TryWrite(long sequence, TwoMotorRumble rumble) => TryWrite(sequence, rumble, out _);

    private bool TryWrite(long sequence, TwoMotorRumble rumble, out PhysicalRumbleWriteResult? physicalResult)
        => TryWrite(sequence, rumble, TimeSpan.Zero, out physicalResult);

    private bool TryWrite(long sequence, TwoMotorRumble rumble, TimeSpan safetyDuration, out PhysicalRumbleWriteResult? physicalResult)
    {
        lock (_gate)
        {
            physicalResult = null;
            if (_disposed || sequence != _sequence || !_authority.TryAcquireLease(_token, out var lease) || lease is null) return false;
            lease.Dispose();
            if (sequence == _developerSequence)
            {
                _developerCompletion = _writer.SubmitForResult(sequence, rumble, safetyDuration);
                physicalResult = new PhysicalRumbleWriteResult(PhysicalRumbleWriteStatus.Succeeded, "Queued");
            }
            else
            {
                _writer.Submit(sequence, rumble, safetyDuration);
                physicalResult = new PhysicalRumbleWriteResult(PhysicalRumbleWriteStatus.Succeeded, "Queued");
            }
            return true;
        }
    }

    private static TimeSpan GetSafetyDuration(SteamDeckFeedbackDecodeResult decoded)
    {
        if (decoded.Command == SteamDeckFeedbackCommand.Rumble && !decoded.Rumble.Equals(TwoMotorRumble.Stopped))
            return TimeSpan.FromMilliseconds(EbDeadManMilliseconds);
        if (decoded.Command != SteamDeckFeedbackCommand.Haptic || decoded.Rumble.Equals(TwoMotorRumble.Stopped))
            return TimeSpan.Zero;
        var duration = decoded.Haptic?.DurationMilliseconds;
        return duration is > 0 ? TimeSpan.FromMilliseconds(duration.Value) : TimeSpan.FromMilliseconds(UnknownHapticFallbackMilliseconds);
    }

    private sealed class LatestWinsRumbleWriter
    {
        private readonly IPhysicalRumbleSink _sink;
        private readonly object _gate = new();
        private readonly AutoResetEvent _wake = new(false);
        private readonly CancellationTokenSource _lifetime = new();
        private Request? _pending;
        private long _latestGeneration;
        private bool _retired;

        internal LatestWinsRumbleWriter(IPhysicalRumbleSink sink)
        {
            _sink = sink;
            _ = Task.Run(Loop);
        }

        internal void Submit(long generation, TwoMotorRumble rumble, TimeSpan safetyDuration) => SubmitCore(new Request(generation, rumble, safetyDuration));

        internal Task<PhysicalRumbleWriteResult> SubmitForResult(long generation, TwoMotorRumble rumble, TimeSpan safetyDuration)
        {
            var completion = new TaskCompletionSource<PhysicalRumbleWriteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            SubmitCore(new Request(generation, rumble, safetyDuration, completion));
            return completion.Task;
        }

        private void SubmitCore(Request request)
        {
            lock (_gate)
            {
                if (_retired)
                {
                    request.Completion?.TrySetResult(new(PhysicalRumbleWriteStatus.Unavailable, "Retired"));
                    return;
                }
                if (_pending is { Completion: { } superseded })
                    superseded.TrySetResult(new(PhysicalRumbleWriteStatus.Unavailable, "Superseded"));
                _pending = request;
                _latestGeneration = request.Generation;
            }
            _wake.Set();
        }

        internal void Retire()
        {
            lock (_gate)
            {
                if (_retired) return;
                _retired = true;
                if (_pending is { Completion: { } pending })
                    pending.TrySetResult(new(PhysicalRumbleWriteStatus.Unavailable, "Retired"));
                _pending = new Request(long.MaxValue, TwoMotorRumble.Stopped, TimeSpan.Zero);
                _latestGeneration = long.MaxValue;
                _lifetime.Cancel();
            }
            try { _sink.CancelPendingWrite(); }
            catch (Exception exception)
            {
                try { AppLog.Debug("Rumble", "Physical rumble cancellation failure was contained.", ("Reason", exception.GetType().Name)); }
                catch { }
            }
            _wake.Set();
        }

        private void Loop()
        {
            try
            {
                while (true)
                {
                    _wake.WaitOne();
                    Request? request;
                    bool retired;
                    lock (_gate) { request = _pending; _pending = null; retired = _retired; }
                    if (request is null) continue;
                    PhysicalRumbleWriteResult result;
                    try { result = _sink.SetRumble(request.Value.Rumble); }
                    catch (Exception exception) { result = new(PhysicalRumbleWriteStatus.Failed, exception.GetType().Name); }
                    request.Value.Completion?.TrySetResult(result);
                    if (request.Value.SafetyDuration > TimeSpan.Zero && !retired)
                        _ = StopAfterSafetyDeadlineAsync(request.Value);
                    if (retired) return;
                }
            }
            finally { _wake.Dispose(); _lifetime.Dispose(); }
        }

        private async Task StopAfterSafetyDeadlineAsync(Request request)
        {
            try
            {
                var token = _lifetime.Token;
                await Task.Delay(request.SafetyDuration, token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_retired) return;
                    if (_latestGeneration == request.Generation)
                        _pending = new Request(request.Generation, TwoMotorRumble.Stopped, TimeSpan.Zero);
                }
                _wake.Set();
            }
            catch (OperationCanceledException) { }
        }

        private readonly record struct Request(long Generation, TwoMotorRumble Rumble, TimeSpan SafetyDuration, TaskCompletionSource<PhysicalRumbleWriteResult>? Completion = null);
    }

}
