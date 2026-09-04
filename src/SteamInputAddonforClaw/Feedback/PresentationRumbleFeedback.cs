using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Feedback;

/// <summary>Full1902 production rumble: one presentation-scoped adapter converts the active virtual
/// presentation's already-decoded (Xbox360) or raw (SteamDeck) host output into a semantic
/// <see cref="TwoMotorRumble"/> and writes it to the one shared physical <see cref="IPhysicalRumbleSink"/>.
///
/// These are deliberately NOT authority managers: they own only a disposable native-callback lifetime,
/// a tiny write-drain lock, and (SteamDeck only) a bounded dead-man safety stop. Lifetime arm/disarm
/// ordering belongs to <c>MsiClawAddonPresentation</c>'s existing gate. The managed delegate stays
/// rooted by <see cref="CanonicalViiperNativeApi"/>'s own callback-root map for exactly as long as
/// native may call it, so a failed native clear here does not create an unrooted callback.</summary>
internal sealed class Xbox360RumbleFeedbackBridge : IDisposable
{
    private readonly IPhysicalRumbleSink _sink;
    private readonly Func<Xbox360RumbleCallback?, bool> _setNativeCallback;
    private readonly Xbox360RumbleCallback _callback;
    // Serializes the callback's physical write against Dispose: a callback that already passed the
    // _disposed check can still be inside a (up to 250 ms) physical write while the presentation
    // thread issues the lifecycle STOP -- without this drain the late non-zero write would land AFTER
    // the STOP and leave the motors latched across a switch / Overlay pause / teardown.
    private readonly object _callbackWriteGate = new();
    private int _disposed;

    private Xbox360RumbleFeedbackBridge(IPhysicalRumbleSink sink, Func<Xbox360RumbleCallback?, bool> setNativeCallback)
    {
        _sink = sink;
        _setNativeCallback = setNativeCallback;
        _callback = OnRumble;
    }

    /// <summary>8-bit to 16-bit full-range expansion. Preserves exact 8-bit magnitude through the
    /// physical sink's <c>&gt;&gt; 8</c> conversion: 0-&gt;0, 1-&gt;257 (&gt;&gt;8==1), 255-&gt;65535 (&gt;&gt;8==255).</summary>
    internal static ushort Expand(byte value) => (ushort)(value * 257);

    /// <summary>Registers the native Xbox360 rumble callback. Returns <see langword="null"/> (rumble
    /// unavailable for this presentation) if registration is not confirmed -- the caller keeps the
    /// controller presentation healthy regardless.</summary>
    internal static Xbox360RumbleFeedbackBridge? TryArm(IPhysicalRumbleSink sink, Func<Xbox360RumbleCallback?, bool> setNativeCallback)
    {
        var bridge = new Xbox360RumbleFeedbackBridge(sink, setNativeCallback);
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

    private void OnRumble(nuint handle, byte leftMotor, byte rightMotor)
    {
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            // XInput rumble is persistent host state (0,0 is the host's own stop), so no dead-man
            // timer is needed here -- only the write drain.
            lock (_callbackWriteGate)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                var result = _sink.SetRumble(new TwoMotorRumble(Expand(leftMotor), Expand(rightMotor)));
                if (result.Status == PhysicalRumbleWriteStatus.Failed)
                    AppLog.Debug("Rumble", "Production rumble write failed.",
                        ("Event", "ProductionRumbleWriteFailed"), ("Presentation", "Xbox360"), ("Reason", result.Reason));
            }
        }
        catch
        {
            // Never throw across the native callback boundary.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
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

    private readonly IPhysicalRumbleSink _sink;
    private readonly Func<bool> _clearNativeCallback;
    private readonly SteamDeckOutputCallback _callback;
    private readonly TimeSpan _untimedHapticSafetyStop;
    private readonly TimeSpan _rumbleSafetyStop;
    private readonly object _callbackWriteGate = new();
    private readonly object _safetyGate = new();
    private CancellationTokenSource? _safetyStop;
    private long _feedbackSequence;
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
            if (data == 0 || length == 0) return;
            var report = new byte[(int)Math.Min(length, 512u)];
            Marshal.Copy(data, report, 0, report.Length);
            var decoded = SteamDeckRumbleDecoder.Decode(report);
            if (!decoded.HasPhysicalTranslation) return;

            // A haptic command 0 is an explicit STOP, not persistent motor state.
            var rumble = decoded.Command == SteamDeckFeedbackCommand.Haptic && decoded.Haptic?.CommandType == 0
                ? TwoMotorRumble.Stopped
                : decoded.Rumble;

            lock (_callbackWriteGate)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                var result = _sink.SetRumble(rumble);
                if (result.Status == PhysicalRumbleWriteStatus.Failed)
                    AppLog.Debug("Rumble", "Production rumble write failed.",
                        ("Event", "ProductionRumbleWriteFailed"), ("Presentation", "SteamDeck"), ("Command", decoded.Command), ("Reason", result.Reason));
            }

            ScheduleSafetyStop(decoded, rumble);
        }
        catch
        {
            // Never throw across the native callback boundary.
        }
    }

    private void ScheduleSafetyStop(SteamDeckFeedbackDecodeResult decoded, TwoMotorRumble rumble)
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

        _ = StopAfterDelayAsync(sequence, delay, token);
    }

    private async Task StopAfterDelayAsync(long sequence, TimeSpan delay, CancellationToken token)
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
                _sink.SetRumble(TwoMotorRumble.Stopped);
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
}
