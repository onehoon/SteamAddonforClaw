using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Steam;

namespace SteamInputAddonforClaw.Runtime;

/// <summary>
/// The single UI-independent owner of the running Addon runtime: the Steam/Big Picture
/// session-observation graph (<see cref="SteamSessionRuntime"/>), suspend/resume power orchestration
/// (<c>PowerTransitionCoordinator</c>/<c>PowerTransitionWatcher</c>), and user-termination safety
/// (<c>UserTerminationGuard</c>). No dependency on the presentation shell, the main window, the system
/// tray, or any device-specific (MSI/CenterM) type -- UI communication happens only through the
/// generic <see cref="SteamSessionStateChanged"/>/<see cref="StatusRefreshRequested"/> events, and
/// the device-specific stock-mode baseline capability arrives only as a generic
/// <c>Func&lt;CancellationToken, Task&lt;bool&gt;&gt;</c> callback.
/// </summary>
/// <remarks>
/// Full1902 controller authority (physical ownership, HidHide baseline, the VIIPER presentation
/// owner, the front-button runtime, Win+G suppression) is composed and owned by
/// <c>AddonProcessHost</c>, not here. This host keeps only Steam observation and generic power /
/// termination orchestration.
///
/// <para>
/// Lifecycle: construct, subscribe to the events, call <see cref="StartPowerObservation"/>
/// once dependents (e.g. <c>MainWindow</c>) are ready to receive the events it may synchronously
/// raise, then eventually call <see cref="PrepareForShutdown"/> (stops Steam observation; safe to
/// call multiple times) followed by <see cref="DisposeAsync"/> (idempotent: stops power observation
/// and disposes the power coordinator).
/// </para>
/// </remarks>
internal sealed class AddonRuntimeHost : IAsyncDisposable
{
    private readonly SteamSessionRuntime _steamRuntime;
    private readonly PowerMutationGate _powerGate;
    private readonly RecoverySafetyState _recoverySafetyState;
    private readonly PowerTransitionCoordinator _powerCoordinator;
    private readonly PowerTransitionWatcher _powerWatcher;
    private readonly UserTerminationGuard _userTerminationGuard;
    private readonly CancellationTokenSource _shutdownCancellation = new();

    // Guards against a resume notification that was already queued in PowerTransitionCoordinator
    // before shutdown began running ReconcileFreshAfterResumeAsync's Steam refresh concurrently
    // with (or after) PrepareForShutdown disposing the same SteamSessionRuntime --
    // SteamSessionWatcher.Refresh() throws ObjectDisposedException once disposed. Both
    // PrepareForShutdown and the Steam-touching section of ReconcileFreshAfterResumeAsync take
    // this lock, so whichever runs first either fully disposes before the other's refresh
    // attempt is even considered, or fully refreshes (a fast, synchronous, non-reentrant
    // registry read) before disposal can start.
    private readonly Lock _steamLifecycleLock = new();
    private bool _steamStopped;
    private int _disposed;

    internal AddonRuntimeHost(
        SteamSessionRuntime steamRuntime,
        PowerMutationGate powerGate,
        RecoverySafetyState recoverySafetyState,
        bool recoverySafe,
        Func<CancellationToken, Task<bool>> establishBaseline,
        IPowerSuspendResumeNotificationSource? notificationSource = null)
    {
        _steamRuntime = steamRuntime;
        _powerGate = powerGate;
        _recoverySafetyState = recoverySafetyState;
        _steamRuntime.StateChanged += OnSteamSessionStateChanged;
        _steamRuntime.ActualRunningAppIdChanged += OnActualRunningAppIdChanged;

        _powerCoordinator = new PowerTransitionCoordinator(powerGate, recoverySafetyState,
            Array.Empty<IPowerSuspendParticipant>(),
            token => ReconcileFreshAfterResumeAsync(token),
            recoveryEnabled: recoverySafe,
            establishBaseline: establishBaseline,
            resumeObserved: () => PowerResumeObserved?.Invoke());
        _powerWatcher = new PowerTransitionWatcher(
            notificationSource ?? new WindowsSuspendResumeNotificationSource(), powerGate, _powerCoordinator,
            static () => { });

        _userTerminationGuard = new UserTerminationGuard(
            () => _shutdownCancellation.IsCancellationRequested);
    }

    internal DeveloperTestModeState DeveloperTestModeState => _steamRuntime.DeveloperTestModeState;

    internal event EventHandler<SteamSessionStateChangedEventArgs>? SteamSessionStateChanged;
    internal event Action<uint>? ActualRunningAppIdChanged;
    internal event EventHandler? StatusRefreshRequested;
    internal event Action? PowerResumeObserved;

    internal uint ActualRunningAppId => _steamRuntime.ActualRunningAppId;

    /// <summary>PR6: one raw Steam/BPM read for the first virtual-presentation decision, so
    /// <c>AddonProcessHost</c> does not reach into Steam internals.</summary>
    internal Steam.SteamPresentationSnapshot CapturePresentationSnapshot() => _steamRuntime.CapturePresentationSnapshot();

    internal UserTerminationDecision EvaluateUserTermination() => _userTerminationGuard.Evaluate();

    /// <summary>
    /// Registers for suspend/resume notifications and opens the mutation gate if registration
    /// succeeded and recovery is safe -- exactly the startup sequence previously inlined in App.
    /// Registration failure is fail-closed: the gate stays closed. Call once, after subscribing to
    /// this host's events, before anything else may depend on power observation being active.
    /// </summary>
    internal void StartPowerObservation()
    {
        if (!_powerWatcher.Start())
            AppLog.Error("Power.Notify", "Suspend/resume notification registration failed.", new InvalidOperationException("PowerRegisterSuspendResumeNotification failed."));
        else if (_recoverySafetyState.Current == RecoverySafety.Safe)
            _powerGate.OpenAfterRecovery();
    }

    /// <summary>
    /// Resume reconciliation: an explicit Steam refresh so the post-resume actual-AppID / Big
    /// Picture facts are re-read and re-published (which is what drives the Full1902 presentation
    /// reconcile in <c>AddonProcessHost</c>). Safe to call after <see cref="PrepareForShutdown"/> --
    /// e.g. a resume notification queued in <c>PowerTransitionCoordinator</c> before shutdown began,
    /// running afterward -- the Steam refresh is skipped in that case rather than touching the
    /// disposed <see cref="SteamSessionRuntime"/>.
    /// </summary>
    private Task<bool> ReconcileFreshAfterResumeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_steamLifecycleLock)
        {
            if (!_steamStopped) _steamRuntime.Refresh();
        }
        RequestStatusRefresh();
        return Task.FromResult(true);
    }

    /// <summary>
    /// Stops Steam/Big Picture session observation -- the first phase of runtime shutdown.
    /// Idempotent. Does not stop power observation or dispose power objects; see
    /// <see cref="DisposeAsync"/> for the remaining sequence.
    /// </summary>
    internal void PrepareForShutdown()
    {
        _shutdownCancellation.Cancel();
        lock (_steamLifecycleLock)
        {
            if (_steamStopped) return;
            _steamStopped = true;
            _steamRuntime.StateChanged -= OnSteamSessionStateChanged;
            _steamRuntime.ActualRunningAppIdChanged -= OnActualRunningAppIdChanged;
            _steamRuntime.Dispose();
        }
    }

    private void OnSteamSessionStateChanged(object? sender, SteamSessionStateChangedEventArgs args) =>
        SteamSessionStateChanged?.Invoke(this, args);

    private void OnActualRunningAppIdChanged(uint appId) => ActualRunningAppIdChanged?.Invoke(appId);

    private void RequestStatusRefresh() => StatusRefreshRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Full runtime shutdown, idempotent: stops Steam observation (if not already stopped),
    /// disposes power observation, then disposes the power coordinator -- preserving the exact
    /// effective ordering App previously performed itself.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        PrepareForShutdown();
        _powerWatcher.Dispose();
        await _powerWatcher.DrainAsync().ConfigureAwait(false);
        await _powerCoordinator.DisposeAsync().ConfigureAwait(false);
        _shutdownCancellation.Dispose();
    }
}
