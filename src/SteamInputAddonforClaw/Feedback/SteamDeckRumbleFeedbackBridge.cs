using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Feedback;

internal sealed class SteamDeckRumbleFeedbackBridge
{
    private const uint MaximumCallbackLength = 64;
    private readonly FeedbackAuthority _authority;
    private readonly FeedbackAuthorityToken _token;
    private readonly IPhysicalRumbleSink _sink;
    private readonly object _gate = new();
    private CancellationTokenSource? _pendingStop;
    private long _sequence;
    private bool _disposed;
    private CancellationTokenSource? _developerTest;

    internal SteamDeckRumbleFeedbackBridge(FeedbackAuthority authority, FeedbackAuthorityToken token, IPhysicalRumbleSink sink)
    {
        _authority = authority;
        _token = token;
        _sink = sink;
        Callback = OnNativeOutput;
    }

    internal SteamDeckOutputCallback Callback { get; }
    internal Action? BeforeLease { get; set; }
    internal bool ProcessNormalizedReport(ReadOnlySpan<byte> report, string origin = "Steam") => ProcessNormalizedReport(report, origin, out _);

    internal bool ProcessNormalizedReport(ReadOnlySpan<byte> report, string origin, out long sequence)
    {
        sequence = 0;
        var decoded = SteamDeckRumbleDecoder.Decode(report);
        if (!decoded.IsSupported) return false;
        sequence = BeginFeedback();
        BeforeLease?.Invoke();
        if (!TryWrite(sequence, decoded.Rumble))
        {
            AppLog.Debug("Rumble", "SteamDeck feedback DROP", ("Reason", "AuthorityRejected"), ("Origin", origin));
            return false;
        }
        if (decoded.Command == SteamDeckFeedbackCommand.HapticPulse && decoded.PulseDurationMilliseconds is { } delay)
            ArmStop(sequence, delay);
        AppLog.Debug("Rumble", "Steam Deck feedback processed.", ("Origin", origin), ("Command", decoded.Command));
        return true;
    }

    internal void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _sequence++;
            _pendingStop?.Cancel();
            _pendingStop?.Dispose();
            _pendingStop = null;
            _developerTest?.Cancel();
            _developerTest?.Dispose();
            _developerTest = null;
        }
    }

    internal async Task<bool> ProcessDeveloperTestAsync(ReadOnlyMemory<byte> report, bool addDeveloperStop, CancellationToken cancellationToken)
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
            if (!ProcessNormalizedReport(report.Span, "DeveloperVibrationTest", out var sequence)) return false;
            if (!addDeveloperStop) return true;
            await Task.Delay(250, linked.Token).ConfigureAwait(false);
            // Write directly against the original sequence instead of routing back through
            // ProcessNormalizedReport (which would call BeginFeedback() again): if real Steam
            // feedback arrived during the 250ms delay it is now the newest sequence, and this
            // stale developer STOP must be a silent no-op rather than stopping that newer feedback.
            return TryWrite(sequence, TwoMotorRumble.Stopped);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { return false; }
        finally
        {
            lock (_gate) if (ReferenceEquals(_developerTest, linked)) _developerTest = null;
            linked.Dispose();
        }
    }

    /// <summary>Called when the Vibration Test detail page is left: cancels any pending
    /// developer-owned delayed STOP (so it can no longer fire and stop newer real feedback) and
    /// issues a best-effort production-path zero write to leave the motors physically stopped.</summary>
    internal void CancelDeveloperTestAndStop()
    {
        lock (_gate)
        {
            _developerTest?.Cancel();
            _developerTest?.Dispose();
            _developerTest = null;
        }
        var stop = new byte[64]; stop[0] = 0xEB; stop[1] = 9;
        ProcessNormalizedReport(stop, "DeveloperVibrationTestClose");
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
                AppLog.Debug("Rumble", "SteamDeck feedback Decode", ("Command", decoded.Command), ("Left", decoded.Rumble.LargeMotor), ("Right", decoded.Rumble.SmallMotor));
            else if (decoded.Command == SteamDeckFeedbackCommand.Haptic)
                AppLog.Debug("Rumble", "SteamDeck feedback Decode", ("Command", decoded.Command), ("Intensity", decoded.Intensity), ("Gain", decoded.Gain), ("Strength8", decoded.Strength8));
            else if (decoded.Command == SteamDeckFeedbackCommand.HapticPulse)
                AppLog.Debug("Rumble", "SteamDeck feedback Decode", ("Command", decoded.Command), ("Period", decoded.PulsePeriod), ("Count", decoded.PulseCount), ("Gain", decoded.Gain), ("Strength8", decoded.Strength8), ("DurationMs", decoded.PulseDurationMilliseconds));
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
            _pendingStop?.Cancel();
            _pendingStop?.Dispose();
            _pendingStop = null;
            var sequence = _sequence;
            return sequence;
        }
    }

    private void ArmStop(long sequence, int duration)
    {
        lock (_gate)
        {
            if (_disposed || sequence != _sequence) return;
            var cts = new CancellationTokenSource();
            _pendingStop = cts;
            _ = StopAfterAsync(sequence, duration, cts);
        }
    }

    private bool TryWrite(long sequence, TwoMotorRumble rumble)
    {
        lock (_gate)
        {
            if (_disposed || sequence != _sequence || !_authority.TryAcquireLease(_token, out var lease) || lease is null) return false;
            using (lease) _sink.SetRumble(rumble);
            return true;
        }
    }

    private async Task StopAfterAsync(long sequence, int duration, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(duration, cts.Token).ConfigureAwait(false);
            if (!TryWrite(sequence, TwoMotorRumble.Stopped)) return;
            lock (_gate) if (ReferenceEquals(_pendingStop, cts)) _pendingStop = null;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        finally { cts.Dispose(); }
    }
}
