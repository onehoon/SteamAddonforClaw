namespace SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;

internal sealed class ClawSensorProbeWorkflow
{
    private readonly List<(ClawSensorProbePhase Phase, int Pass)> _visits = [];
    public ClawSensorProbeState State { get; private set; } = ClawSensorProbeState.Idle;
    public int CurrentIndex { get; private set; } = -1;
    public IReadOnlyList<(ClawSensorProbePhase Phase, int Pass)> Visits => _visits;
    public static IReadOnlyList<ClawSensorProbePhase> Phases { get; } = Enum.GetValues<ClawSensorProbePhase>();
    public void Discovering() { if (State != ClawSensorProbeState.Idle) throw new InvalidOperationException(); State = ClawSensorProbeState.Discovering; }
    public void Ready() { if (State is not (ClawSensorProbeState.Idle or ClawSensorProbeState.Discovering)) throw new InvalidOperationException(); State = ClawSensorProbeState.Ready; }
    public void Start() { if (State != ClawSensorProbeState.Ready) throw new InvalidOperationException(); State = ClawSensorProbeState.Starting; CurrentIndex = 0; Visit(); State = ClawSensorProbeState.Countdown; }
    public void BeginRecording() { if (State != ClawSensorProbeState.Countdown) throw new InvalidOperationException(); State = ClawSensorProbeState.RecordingPhase; }
    public void Next() { if (State != ClawSensorProbeState.RecordingPhase) throw new InvalidOperationException(); if (CurrentIndex >= Phases.Count - 1) { State = ClawSensorProbeState.Completed; return; } CurrentIndex++; State = ClawSensorProbeState.Countdown; Visit(); }
    public void Back() { if (State is not (ClawSensorProbeState.Countdown or ClawSensorProbeState.RecordingPhase) || CurrentIndex <= 0) return; CurrentIndex--; State = ClawSensorProbeState.Countdown; Visit(); }
    public void Stop() { if (State is ClawSensorProbeState.Completed or ClawSensorProbeState.Failed) return; State = ClawSensorProbeState.Stopping; State = ClawSensorProbeState.Completed; }
    public void Fail() { if (State != ClawSensorProbeState.Completed) State = ClawSensorProbeState.Failed; }
    private void Visit() { var phase = Phases[CurrentIndex]; var pass = _visits.Count(x => x.Phase == phase) + 1; _visits.Add((phase, pass)); }
}
