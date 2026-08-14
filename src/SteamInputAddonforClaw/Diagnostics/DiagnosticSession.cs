using System.Diagnostics;
using SteamInputAddonforClaw.Input;

namespace SteamInputAddonforClaw.Diagnostics;

internal static class DiagnosticSession
{
    private static long _nextId;
    internal static Context Start(uint raw, uint effective, string source)
    {
        var context = new Context(Interlocked.Increment(ref _nextId), raw, effective, source, Stopwatch.GetTimestamp());
        AppLog.Info("SteamDiagnostic", "Session started", ("Session", context.Id), ("RawRunningAppID", raw), ("EffectiveRunningAppID", effective), ("EffectiveSource", source));
        return context;
    }
    internal static void Complete(Context context, params (string Key, object? Value)[] fields) => AppLog.Info("SteamDiagnostic", "Session completed", fields.Append(("Session", context.Id)).Append(("RawRunningAppID", context.RawRunningAppId)).Append(("EffectiveRunningAppID", context.EffectiveRunningAppId)).Append(("EffectiveSource", context.Source)).Append(("DurationMs", (long)Stopwatch.GetElapsedTime(context.StartTimestamp).TotalMilliseconds)).ToArray());
    internal sealed record Context(long Id, uint RawRunningAppId, uint EffectiveRunningAppId, string Source, long StartTimestamp);
}

internal sealed class DiagnosticSessionTracker
{
    private DiagnosticSession.Context? _current;
    internal void Observe(uint rawRunningAppId, uint effectiveRunningAppId, string source)
    {
        var identityChanged = _current is not null && (_current.RawRunningAppId != rawRunningAppId || _current.Source != source || _current.EffectiveRunningAppId != effectiveRunningAppId);
        if (identityChanged) { DiagnosticSession.Complete(_current!); _current = null; }
        if (_current is null && (rawRunningAppId != 0 || source is "DeveloperTest" or "BigPicture")) _current = DiagnosticSession.Start(rawRunningAppId, effectiveRunningAppId, source);
    }
    internal void Complete() { if (_current is not null) DiagnosticSession.Complete(_current); _current = null; }
}

internal static class ControllerStateDiagnostics
{
    private static readonly TimeSpan AnalogMinimumInterval = TimeSpan.FromMilliseconds(50);
    private static readonly Lock AnalogSync = new();
    private static long _lastAnalogTimestamp;
    private static ControllerState _lastAnalogState;
    private static bool _hasAnalogState;
    private static readonly Lock PovSync = new();
    private static int? _lastPovSession;
    private static int? _lastPov;
    private static readonly Lock DPadSync = new();
    private static int? _lastDPadSession;
    private static (bool Up, bool Right, bool Down, bool Left)? _lastDPad;

    internal static void LogChanges(ControllerState oldState, ControllerState state, int session)
    {
        var fields = new List<(string Key, object? Value)>();
        Add(fields, "A", oldState.Buttons.A, state.Buttons.A); Add(fields, "B", oldState.Buttons.B, state.Buttons.B); Add(fields, "X", oldState.Buttons.X, state.Buttons.X); Add(fields, "Y", oldState.Buttons.Y, state.Buttons.Y);
        Add(fields, "LB", oldState.Buttons.LeftBumper, state.Buttons.LeftBumper); Add(fields, "RB", oldState.Buttons.RightBumper, state.Buttons.RightBumper); Add(fields, "Back", oldState.Buttons.Back, state.Buttons.Back); Add(fields, "Start", oldState.Buttons.Start, state.Buttons.Start);
        Add(fields, "L3", oldState.Buttons.LeftStickClick, state.Buttons.LeftStickClick); Add(fields, "R3", oldState.Buttons.RightStickClick, state.Buttons.RightStickClick); Add(fields, "LTFull", oldState.Buttons.LeftTriggerFull, state.Buttons.LeftTriggerFull); Add(fields, "RTFull", oldState.Buttons.RightTriggerFull, state.Buttons.RightTriggerFull);
        Add(fields, "DPadUp", oldState.Buttons.DPadUp, state.Buttons.DPadUp); Add(fields, "DPadRight", oldState.Buttons.DPadRight, state.Buttons.DPadRight); Add(fields, "DPadDown", oldState.Buttons.DPadDown, state.Buttons.DPadDown); Add(fields, "DPadLeft", oldState.Buttons.DPadLeft, state.Buttons.DPadLeft);
        Add(fields, "M1", oldState.Auxiliary[1], state.Auxiliary[1]); Add(fields, "M2", oldState.Auxiliary[0], state.Auxiliary[0]);
        var analogChanged = oldState.LeftStick != state.LeftStick || oldState.RightStick != state.RightStick || oldState.Triggers != state.Triggers;
        if (analogChanged && ShouldLogAnalog(state)) fields.Add(("Analog", $"LX={state.LeftStick.X},LY={state.LeftStick.Y},RX={state.RightStick.X},RY={state.RightStick.Y},LT={state.Triggers.Left},RT={state.Triggers.Right}"));
        if (fields.Count > 0) AppLog.Debug("Input", "ControllerState changed", fields.Append(("TestSession", session)).ToArray());
    }

    /// <summary>
    /// M5 diagnostic: logs the physical/normalized D-pad state at INFO whenever it changes (including
    /// the first observed state), independent of <see cref="MinimumLevelOverride"/> being Debug. This is
    /// deliberately separate from <see cref="LogChanges"/> (which is Debug-only and covers every field)
    /// so a real-hardware D-pad investigation does not require enabling full Debug logging.
    /// </summary>
    internal static void LogDPadTransitionIfChanged(GamepadButtons buttons, int session)
    {
        var current = (buttons.DPadUp, buttons.DPadRight, buttons.DPadDown, buttons.DPadLeft);
        lock (DPadSync)
        {
            // Track session alongside the value, same as LogPovIfChanged: a new input session must
            // always produce an initial D-pad log, even if its first value happens to match the
            // previous session's last observed value (e.g. both start neutral).
            if (_lastDPadSession == session && _lastDPad == current) return;
            _lastDPadSession = session;
            _lastDPad = current;
        }
        AppLog.Info("Input", "Physical D-pad state changed", ("TestSession", session),
            ("Up", buttons.DPadUp), ("Right", buttons.DPadRight), ("Down", buttons.DPadDown), ("Left", buttons.DPadLeft));
    }

    /// <summary>
    /// Logs the raw DirectInput POV value whenever it changes, independent of whether the
    /// mapped ControllerState changed. This is diagnostic-only and exists to distinguish:
    /// raw POV never changing (A), POV changing but the mapped D-pad not following (B), or
    /// both being correct while Steam/Gordon output still does not respond (C).
    /// </summary>
    internal static void LogPovIfChanged(int session, int pov)
    {
        lock (PovSync)
        {
            // Track session alongside the value: a new input session must always produce an
            // initial POV log, even if that value happens to match the previous session's last
            // observed POV (e.g. both sessions start neutral at -1).
            if (_lastPovSession == session && _lastPov == pov) return;
            _lastPovSession = session;
            _lastPov = pov;
        }
        AppLog.Debug("Input", "DirectInput POV changed", ("TestSession", session), ("POV", pov));
    }
    private static bool ShouldLogAnalog(ControllerState state)
    {
        lock (AnalogSync)
        {
            var now = Stopwatch.GetTimestamp();
            var intervalElapsed = _lastAnalogTimestamp == 0 || Stopwatch.GetElapsedTime(_lastAnalogTimestamp) >= AnalogMinimumInterval;
            var meaningful = !_hasAnalogState || Math.Abs(state.LeftStick.X - _lastAnalogState.LeftStick.X) >= 512 || Math.Abs(state.LeftStick.Y - _lastAnalogState.LeftStick.Y) >= 512 || Math.Abs(state.RightStick.X - _lastAnalogState.RightStick.X) >= 512 || Math.Abs(state.RightStick.Y - _lastAnalogState.RightStick.Y) >= 512 || Math.Abs(state.Triggers.Left - _lastAnalogState.Triggers.Left) >= 8 || Math.Abs(state.Triggers.Right - _lastAnalogState.Triggers.Right) >= 8;
            if (!intervalElapsed || !meaningful) return false;
            _lastAnalogTimestamp = now; _lastAnalogState = state; _hasAnalogState = true; return true;
        }
    }
    private static void Add(List<(string Key, object? Value)> fields, string name, bool oldValue, bool newValue) { if (oldValue != newValue) fields.Add((name, $"{oldValue}->{newValue}")); }
}
