using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Routing;

/// <summary>
/// Owns the routing/virtual-output object graph -- the handheld routing composition, its
/// borrowed safety session, the canonical Steam Deck output stage, and the routing pipeline
/// executor/session coordinator/runtime coordinator -- that was previously assembled and held
/// directly by <c>App.xaml.cs</c>. UI-independent: this type has no dependency on WinUI, the main
/// window, or the system tray. The application shell still owns startup/bootstrap, Steam/BPM
/// watcher lifecycle, power/suspend/resume orchestration, and top-level application shutdown
/// ordering -- this runtime is only the routing slice of that graph.
/// </summary>
/// <remarks>
/// <see cref="Create"/> returns null (never a fallback) when the supplied adapter has no
/// available routing composition, mirroring <see cref="HandheldRoutingCompositionFactory"/>'s own
/// unavailable/passive result. The returned instance owns the created
/// <see cref="IHandheldRoutingComposition"/>; the composition in turn owns its own backend
/// resources (see <see cref="IHandheldRoutingComposition"/>'s remarks). The borrowed
/// <see cref="IRoutingSafetySession"/> view is never disposed independently.
///
/// <para>
/// <see cref="ShutdownAsync"/> stops routing (with the existing fail-close fallback on failure)
/// and must be called, together with any other external orchestration referencing this runtime
/// (e.g. the power coordinator), before <see cref="DisposeAsync"/>. <see cref="DisposeAsync"/>
/// only releases the owned <see cref="IHandheldRoutingComposition"/>'s backend resources -- it
/// does not perform routing shutdown itself and must not be treated as a full-shutdown
/// orchestrator.
/// </para>
/// </remarks>
internal sealed class AddonRoutingRuntime : IAsyncDisposable, IPowerSuspendParticipant
{
    private readonly IHandheldRoutingComposition _composition;
    private readonly IRoutingSafetySession? _safetySession;
    private readonly RoutingPipelineRuntimeCoordinator _coordinator;

    private AddonRoutingRuntime(IHandheldRoutingComposition composition, IRoutingSafetySession? safetySession, RoutingPipelineRuntimeCoordinator coordinator)
    {
        _composition = composition;
        _safetySession = safetySession;
        _coordinator = coordinator;
    }

    internal static AddonRoutingRuntime? Create(
        IHandheldDeviceAdapter handheldDeviceAdapter,
        ISystemStatusProvider statusProvider,
        AddonOwnedVirtualDeviceTracker addonOwnedVirtualDeviceTracker,
        RecoveryManager recovery,
        PowerMutationGate powerGate,
        RecoverySafetyState recoverySafety)
    {
        var handheldRoutingComposition = new HandheldRoutingCompositionFactory().Create(handheldDeviceAdapter, recovery, powerGate, recoverySafety);
        if (handheldRoutingComposition is null) return null;

        var safetySession = handheldRoutingComposition.SafetySession;

        var canonicalViiperPath = Path.Combine(AppContext.BaseDirectory, "Dependencies", "Viiper", "libVIIPER.dll");
        SteamOutputComposition.LogTargetSelected();
        var deckStage = new CanonicalSteamDeckOutputStage(
            () => new CanonicalSteamDeckSession(CanonicalViiperNativeApi.Load(canonicalViiperPath)),
            new WindowsControllerDeviceEnumerator(),
            new SteamDeckVirtualDeviceIdentityResolver(new SteamDeckVirtualDeviceIdentityPolicy()),
            addonOwnedVirtualDeviceTracker,
            recovery,
            () => safetySession?.CurrentRecoverySessionId,
            new HidHideDriverClient(), handheldRoutingComposition.ControllerStateSource);
        IRoutingPipelineStage steamOutputStage = deckStage;
        var pipelineExecutor = new RoutingPipelineExecutor([.. handheldRoutingComposition.Stages, steamOutputStage]);
        var pipelineSessionCoordinator = new RoutingPipelineSessionCoordinator(pipelineExecutor);
        var coordinator = new RoutingPipelineRuntimeCoordinator(
            statusProvider,
            pipelineSessionCoordinator,
            handheldRoutingComposition.SessionBoundaryParticipants);
        deckStage.SetOutputFaultHandler(async () => { await coordinator.FailClosedAsync().ConfigureAwait(false); });

        return new AddonRoutingRuntime(handheldRoutingComposition, safetySession, coordinator);
    }

    internal bool HasResidualSessionState => _coordinator.HasResidualSessionState;
    internal bool IsSafetySessionActive => _safetySession?.IsActive == true;
    internal bool HasOwnedRecoveryBoundary => _safetySession?.HasOwnedRecoveryBoundary == true;

    internal RoutingRuntimeStatusSnapshot CaptureStatus() => new(
        Available: true,
        OperationalState: _coordinator.CurrentOperationalState,
        SteamOutputActive: _coordinator.ActiveSessionHasSteamOutputEnabled,
        NativeDirectInputActive: _safetySession?.IsActive == true);

    internal RoutingRuntimeTerminationSnapshot CaptureTerminationSnapshot() => _coordinator.CaptureTerminationSnapshot();

    internal Task<RoutingPipelineSessionReconcileResult> ReconcileAsync(CancellationToken cancellationToken = default) =>
        _coordinator.ReconcileAsync(cancellationToken).AsTask();

    internal Task<RoutingPipelineSessionReconcileResult> FailClosedAsync() => _coordinator.FailClosedAsync().AsTask();

    internal Task<bool> ReconcileFreshAfterResumeAsync(CancellationToken cancellationToken) =>
        _coordinator.ReconcileFreshAfterResumeAsync(cancellationToken).AsTask();

    internal Task<bool> RetryResidualCleanupForResumeAsync(CancellationToken cancellationToken) =>
        _coordinator.RetryResidualCleanupForResumeAsync(cancellationToken).AsTask();

    internal void CancelInFlightTransition() => _coordinator.CancelInFlightTransition();

    internal Task LatchRoutingFaultAsync(string reason, CancellationToken cancellationToken = default) =>
        _safetySession is null ? Task.CompletedTask : _safetySession.LatchRoutingFaultAsync(reason, cancellationToken);

    /// <summary>
    /// Stops routing via the existing coordinator shutdown, falling back to the safety session's
    /// fail-close on either an unsuccessful shutdown result or a thrown exception -- exactly the
    /// behavior <c>App.xaml.cs</c> previously performed inline. The caller must still dispose this
    /// runtime (and stop any other orchestration referencing it) afterward; this method does not
    /// release backend resources.
    /// </summary>
    internal async Task ShutdownAsync()
    {
        try
        {
            var shutdown = await _coordinator.ShutdownAsync().ConfigureAwait(false);
            if (!shutdown.Succeeded && _safetySession is not null)
                await _safetySession.FailClosedAsync("ApplicationShutdownRoutingRollbackFailed", CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Error("Routing.Runtime", "Routing pipeline shutdown failed; attempting NativeMode fail-close.", exception);
            if (_safetySession is not null)
            {
                try { await _safetySession.FailClosedAsync("ApplicationShutdownRoutingRollbackFailed", CancellationToken.None).ConfigureAwait(false); }
                catch (Exception failClosedException) { AppLog.Error("NativeMode", "NativeMode shutdown fail-close failed.", failClosedException); }
            }
        }
    }

    public string Name => _coordinator.Name;

    public Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken) =>
        _coordinator.QuiesceForSuspendAsync(deadline, cycle, epoch, cancellationToken);

    /// <summary>
    /// Releases the owned <see cref="IHandheldRoutingComposition"/>'s backend resources. The
    /// caller must have already stopped routing (<see cref="ShutdownAsync"/>) and any external
    /// orchestration (e.g. the power coordinator) that still references this runtime.
    /// </summary>
    public ValueTask DisposeAsync() => _composition.DisposeAsync();
}
