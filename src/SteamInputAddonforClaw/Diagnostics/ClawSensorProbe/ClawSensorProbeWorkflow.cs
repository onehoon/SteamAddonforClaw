namespace SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;

internal sealed class ClawSensorProbeWorkflow
{
    private readonly List<(ClawSensorProbePhase Phase, int Pass)> _visits = [];
    public ClawSensorProbeState State { get; private set; } = ClawSensorProbeState.Idle;
    public int CurrentIndex { get; private set; } = -1;
    // The diagnostic purpose for the current/most recent session. Defaults to AxisCharacterization so
    // the many existing PR-A tests that call the mode-less Start() overload keep exercising exactly
    // the original seven-phase behavior.
    public ClawSensorProbeMode Mode { get; private set; } = ClawSensorProbeMode.AxisCharacterization;
    public IReadOnlyList<(ClawSensorProbePhase Phase, int Pass)> Visits => _visits;
    public static IReadOnlyList<ClawSensorProbePhase> Phases { get; } = Enum.GetValues<ClawSensorProbePhase>();
    public void Discovering() { if (State != ClawSensorProbeState.Idle) throw new InvalidOperationException(); State = ClawSensorProbeState.Discovering; }
    public void Ready() { if (State is not (ClawSensorProbeState.Idle or ClawSensorProbeState.Discovering)) throw new InvalidOperationException(); State = ClawSensorProbeState.Ready; }
    public void Start() => Start(ClawSensorProbeMode.AxisCharacterization);
    // Axis keeps the exact original behavior: phase 0 visited immediately, then Countdown. Live/Bias
    // (work order section 7) skip phase visits/countdown entirely and go straight to Starting so
    // BeginRecording() can fire directly after source discovery/startup succeeds.
    public void Start(ClawSensorProbeMode mode)
    {
        if (State != ClawSensorProbeState.Ready) throw new InvalidOperationException();
        Mode = mode;
        State = ClawSensorProbeState.Starting;
        if (mode == ClawSensorProbeMode.AxisCharacterization)
        {
            CurrentIndex = 0;
            Visit();
            State = ClawSensorProbeState.Countdown;
        }
    }
    public void BeginRecording()
    {
        var required = Mode == ClawSensorProbeMode.AxisCharacterization ? ClawSensorProbeState.Countdown : ClawSensorProbeState.Starting;
        if (State != required) throw new InvalidOperationException();
        State = ClawSensorProbeState.RecordingPhase;
    }
    public void Next()
    {
        if (Mode != ClawSensorProbeMode.AxisCharacterization) throw new InvalidOperationException("Phase navigation is only available in Axis Characterization mode.");
        if (State != ClawSensorProbeState.RecordingPhase) throw new InvalidOperationException();
        if (CurrentIndex >= Phases.Count - 1) { State = ClawSensorProbeState.Completed; return; }
        CurrentIndex++; State = ClawSensorProbeState.Countdown; Visit();
    }
    public void Back()
    {
        if (Mode != ClawSensorProbeMode.AxisCharacterization) return;
        if (State is not (ClawSensorProbeState.Countdown or ClawSensorProbeState.RecordingPhase) || CurrentIndex <= 0) return;
        CurrentIndex--; State = ClawSensorProbeState.Countdown; Visit();
    }
    public void Stop() { if (State is ClawSensorProbeState.Completed or ClawSensorProbeState.Failed) return; State = ClawSensorProbeState.Stopping; State = ClawSensorProbeState.Completed; }
    public void Fail() { if (State != ClawSensorProbeState.Completed) State = ClawSensorProbeState.Failed; }
    private void Visit() { var phase = Phases[CurrentIndex]; var pass = _visits.Count(x => x.Phase == phase) + 1; _visits.Add((phase, pass)); }
}
