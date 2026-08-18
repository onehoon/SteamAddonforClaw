namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// Real MainUI lifecycle, classified purely from result state (process lifetime + recognized
/// window visibility) -- never from the cause of a hide (X button, OEM1 re-press, minimize, tray
/// action). Different MSI versions may implement "close" as hide-only or as an actual process
/// exit; both must be correctly represented by this state set without the observer trying to
/// distinguish which happened.
/// </summary>
internal enum MainUiLifecycleState
{
    /// <summary>No tracked real MainUI process, and none was ever seen visible for this identity.</summary>
    Absent,
    /// <summary>Process is alive but has never shown a recognized visible MainUI window yet
    /// (e.g. still starting) -- never a kill candidate.</summary>
    StartingOrHiddenNeverVisible,
    Visible,
    /// <summary>Previously visible (SeenVisible == true), now all recognized MainUI windows are
    /// hidden. The only state from which a debounced safe-termination may later be considered.</summary>
    HiddenAfterVisible,
    /// <summary>Previously visible tracked process has exited naturally -- no termination
    /// necessary or attempted.</summary>
    Exited,
    /// <summary>The window snapshot itself could not be determined (e.g. enumeration failure).
    /// Never treated as any specific lifecycle state.</summary>
    Uncertain
}

/// <summary>Stateful per-identity lifecycle tracker. Owns exactly one safety invariant: SeenVisible
/// only ever becomes true after a recognized MainUI window has actually been observed visible for
/// the exact tracked process id currently being observed -- it is never assumed, and it resets
/// whenever the tracked process id changes (no identity carries over to a different PID).</summary>
internal sealed class MainUiLifecycleObserver(IMainUiWindowSnapshotProvider? provider = null)
{
    private readonly IMainUiWindowSnapshotProvider _provider = provider ?? new Win32MainUiWindowSnapshotProvider();
    private int? _trackedProcessId;
    private bool _seenVisible;

    internal bool SeenVisible => _seenVisible;

    internal MainUiLifecycleState Observe(int processId)
    {
        if (_trackedProcessId != processId)
        {
            _trackedProcessId = processId;
            _seenVisible = false;
        }

        var snapshot = _provider.Capture(processId);
        if (snapshot is null) return MainUiLifecycleState.Uncertain;

        return Classify(snapshot.Value, ref _seenVisible);
    }

    internal void Reset()
    {
        _trackedProcessId = null;
        _seenVisible = false;
    }

    /// <summary>Pure classification core, unit-testable independent of the stateful wrapper above.</summary>
    internal static MainUiLifecycleState Classify(MainUiWindowSnapshot snapshot, ref bool seenVisible)
    {
        if (!snapshot.ProcessAlive)
            return seenVisible ? MainUiLifecycleState.Exited : MainUiLifecycleState.Absent;

        if (snapshot.VisibleMainWindowCount > 0)
        {
            seenVisible = true;
            return MainUiLifecycleState.Visible;
        }

        return seenVisible ? MainUiLifecycleState.HiddenAfterVisible : MainUiLifecycleState.StartingOrHiddenNeverVisible;
    }
}
