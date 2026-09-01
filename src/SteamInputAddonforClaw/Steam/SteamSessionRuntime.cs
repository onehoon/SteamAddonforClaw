using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Settings;

namespace SteamInputAddonforClaw.Steam;

/// <summary>One raw, read-only Steam/BPM fact for the Full-1902 first-presentation decision (work
/// order PR6 section 8). Deliberately NOT derived from <c>EffectiveSteamSessionSource</c> -- the
/// user's old routing preference and Developer Test Mode must not influence it.</summary>
internal readonly record struct SteamPresentationSnapshot(uint RunningAppId, bool BigPictureActive)
{
    internal bool WantsSteamDeck => RunningAppId != 0 || BigPictureActive;
}

/// <summary>
/// Owns the concrete Steam/Big Picture session-observation source graph that previously was
/// constructed and held directly by <c>App.xaml.cs</c>: the running-AppID registry source, the
/// session watcher, the Big Picture watcher, developer test mode, the effective-session source
/// that combines all three plus the user's routing preference, and per-session diagnostics
/// observation. The same concrete implementations are reused unchanged -- this type is a focused
/// owner, not a rewrite.
/// </summary>
/// <remarks>
/// Diagnostic session observation (<see cref="DiagnosticSessionTracker.Observe"/>) happens
/// internally on every effective-session transition, before that transition is republished via
/// <see cref="StateChanged"/> -- matching the exact order the previous inline App handler used.
/// <see cref="Dispose"/> completes the current diagnostic session before disposing the Steam
/// sources, and is idempotent.
/// </remarks>
internal sealed class SteamSessionRuntime : IDisposable
{
    private readonly IRunningAppIdSource _runningAppIdSource;
    private readonly SteamSessionWatcher _sessionWatcher;
    private readonly SteamBigPictureWatcher _bigPictureWatcher;
    private readonly EffectiveSteamSessionSource _effectiveSource;
    private readonly DiagnosticSessionTracker _diagnosticSessions = new();
    private bool _actualObservationStarted;
    private bool _disposed;

    internal SteamSessionRuntime(ISteamInputRoutingPreference routingPreference, IRunningAppIdSource? runningAppIdSource = null)
    {
        _runningAppIdSource = runningAppIdSource ?? new SteamRunningAppIdRegistrySource();
        _sessionWatcher = new SteamSessionWatcher(_runningAppIdSource);
        _bigPictureWatcher = new SteamBigPictureWatcher();
        DeveloperTestModeState = new DeveloperTestModeState();
        _bigPictureWatcher.StateChanged += OnBigPictureStateChanged;
        _bigPictureWatcher.Start();
        _effectiveSource = new EffectiveSteamSessionSource(_sessionWatcher, _bigPictureWatcher, DeveloperTestModeState, routingPreference);
        _effectiveSource.StateChanged += OnEffectiveStateChanged;
    }

    internal DeveloperTestModeState DeveloperTestModeState { get; }
    internal SteamSessionState State => _effectiveSource.State;
    internal uint ActualRunningAppId => _runningAppIdSource.GetRunningAppId();
    internal event EventHandler<SteamSessionStateChangedEventArgs>? StateChanged;
    internal event Action<uint>? ActualRunningAppIdChanged;
    internal event Action<bool>? BigPictureStateChanged;
    internal bool IsBigPictureActive => _bigPictureWatcher.IsActive;

    private void OnBigPictureStateChanged(object? sender, EventArgs args) => BigPictureStateChanged?.Invoke(_bigPictureWatcher.IsActive);

    private void OnActualRunningAppIdChanged(object? sender, EventArgs args) => ActualRunningAppIdChanged?.Invoke(_runningAppIdSource.GetRunningAppId());

    /// <summary>Starts only the actual AppID fact observation used by Device/Profile. This is
    /// intentionally independent from the recovery-gated Routing session watcher.</summary>
    internal void StartActualObservation()
    {
        if (_actualObservationStarted) return;
        _actualObservationStarted = true;
        _runningAppIdSource.Changed += OnActualRunningAppIdChanged;
    }

    /// <summary>
    /// Starts the primary Steam session watcher and refreshes the effective state. Callers must
    /// only invoke this when recovery is safe -- mirrors the previous inline
    /// <c>if (recoverySafe) { watcher.Start(); effectiveSource.Refresh(); }</c> gate.
    /// </summary>
    internal void StartRoutingObservation()
    {
        _sessionWatcher.Start();
        StartActualObservation();
        _effectiveSource.Refresh();
    }

    /// <summary>One-shot raw Steam/BPM read for the Full-1902 first-presentation decision (work order
    /// PR6 section 8). Nudges the Big Picture watcher's inactive-only one-shot scan; adds no polling.</summary>
    internal SteamPresentationSnapshot CapturePresentationSnapshot()
    {
        _bigPictureWatcher.Refresh();
        return new(_runningAppIdSource.GetRunningAppId(), _bigPictureWatcher.IsActive);
    }

    /// <summary>Explicit resume refresh: session watcher first, then the effective source.</summary>
    internal void Refresh()
    {
        _sessionWatcher.Refresh();
        _effectiveSource.Refresh();
    }

    private void OnEffectiveStateChanged(object? sender, SteamSessionStateChangedEventArgs args)
    {
        _diagnosticSessions.Observe(_runningAppIdSource.GetRunningAppId(), args.Current.RunningAppId, args.Current.Source.ToString());
        StateChanged?.Invoke(this, args);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _diagnosticSessions.Complete();
        _effectiveSource.StateChanged -= OnEffectiveStateChanged;
        if (_actualObservationStarted)
            _runningAppIdSource.Changed -= OnActualRunningAppIdChanged;
        _effectiveSource.Dispose();
        _sessionWatcher.Dispose();
        _bigPictureWatcher.Dispose();
        (_runningAppIdSource as IDisposable)?.Dispose();
    }
}
