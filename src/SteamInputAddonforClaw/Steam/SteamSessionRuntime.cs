namespace SteamInputAddonforClaw.Steam;

/// <summary>One raw, read-only Steam/BPM fact for the Full-1902 first-presentation decision (work
/// order PR6 section 8). Deliberately actual-only -- Developer Test Mode must not influence it.</summary>
internal readonly record struct SteamPresentationSnapshot(uint RunningAppId, bool BigPictureActive)
{
    internal bool WantsSteamDeck => RunningAppId != 0 || BigPictureActive;
}

/// <summary>
/// Owns the concrete Steam/Big Picture observation the Full1902 runtime actually consumes: the
/// running-AppID registry source and the Big Picture watcher. Exposes only current facts --
/// <see cref="ActualRunningAppId"/> / <see cref="ActualRunningAppIdChanged"/> and
/// <see cref="IsBigPictureActive"/> / <see cref="BigPictureStateChanged"/> -- plus the one-shot
/// <see cref="CapturePresentationSnapshot"/> the presentation owner reads.
/// </summary>
/// <remarks>
/// Full1902 Cleanup I removed the synthetic effective-session graph (the Developer-Test-driven
/// effective source and its per-session diagnostic tracker). Those helpers still exist as parked
/// source for a later Developer-feature redesign, but no production controller / presentation /
/// status owner constructs or consumes them.
/// </remarks>
internal sealed class SteamSessionRuntime : IDisposable
{
    private readonly IRunningAppIdSource _runningAppIdSource;
    private readonly SteamBigPictureWatcher _bigPictureWatcher;
    private bool _actualObservationStarted;
    private bool _disposed;

    internal SteamSessionRuntime(IRunningAppIdSource? runningAppIdSource = null)
    {
        _runningAppIdSource = runningAppIdSource ?? new SteamRunningAppIdRegistrySource();
        _bigPictureWatcher = new SteamBigPictureWatcher();
        _bigPictureWatcher.StateChanged += OnBigPictureStateChanged;
        _bigPictureWatcher.Start();
    }

    internal uint ActualRunningAppId => _runningAppIdSource.GetRunningAppId();
    internal event Action<uint>? ActualRunningAppIdChanged;
    internal event Action<bool>? BigPictureStateChanged;
    internal bool IsBigPictureActive => _bigPictureWatcher.IsActive;

    private void OnBigPictureStateChanged(object? sender, EventArgs args) => BigPictureStateChanged?.Invoke(_bigPictureWatcher.IsActive);

    private void OnActualRunningAppIdChanged(object? sender, EventArgs args) => ActualRunningAppIdChanged?.Invoke(_runningAppIdSource.GetRunningAppId());

    /// <summary>Starts only the actual AppID fact observation used by Device/Profile.</summary>
    internal void StartActualObservation()
    {
        if (_actualObservationStarted) return;
        _actualObservationStarted = true;
        _runningAppIdSource.Changed += OnActualRunningAppIdChanged;
    }

    /// <summary>One-shot raw Steam/BPM read for the Full-1902 first-presentation decision (work order
    /// PR6 section 8). Nudges the Big Picture watcher's inactive-only one-shot scan; adds no polling.</summary>
    internal SteamPresentationSnapshot CapturePresentationSnapshot()
    {
        _bigPictureWatcher.Refresh();
        return new(_runningAppIdSource.GetRunningAppId(), _bigPictureWatcher.IsActive);
    }

    /// <summary>Explicit resume refresh: re-scan the current Steam/BPM facts and re-notify consumers
    /// so the Full1902 presentation reconcile converges on the post-suspend state.</summary>
    internal void Refresh()
    {
        _bigPictureWatcher.Refresh();
        OnActualRunningAppIdChanged(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_actualObservationStarted)
            _runningAppIdSource.Changed -= OnActualRunningAppIdChanged;
        _bigPictureWatcher.StateChanged -= OnBigPictureStateChanged;
        _bigPictureWatcher.Dispose();
        (_runningAppIdSource as IDisposable)?.Dispose();
    }
}
