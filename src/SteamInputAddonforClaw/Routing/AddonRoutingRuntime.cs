using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using SteamInputAddonforClaw.Feedback;

namespace SteamInputAddonforClaw.Routing;

/// <summary>
/// Owns the routing/virtual-output object graph -- the handheld routing composition, its
/// borrowed safety session, the canonical Steam Deck output stage, and the routing pipeline
/// executor/session coordinator/runtime coordinator -- that was previously assembled and held
/// directly by the process shell. UI-independent: this type has no dependency on the presentation shell, the main
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
/// <see cref="ShutdownAsync"/> stops routing while preserving failed canonical rollback barriers;
/// it does not bypass residual SteamOutput cleanup with a device-specific fail-close.
/// and must be called, together with any other external orchestration referencing this runtime
/// (e.g. the power coordinator), before <see cref="DisposeAsync"/>. <see cref="DisposeAsync"/>
/// only releases the owned <see cref="IHandheldRoutingComposition"/>'s backend resources. It must
/// be called only after successful routing shutdown; failed canonical cleanup retains ownership.
/// </para>
/// </remarks>
internal sealed class AddonRoutingRuntime : IAsyncDisposable, IPowerSuspendParticipant
{
    private readonly IHandheldRoutingComposition _composition;
    private readonly IRoutingSafetySession? _safetySession;
    private readonly RoutingPipelineRuntimeCoordinator _coordinator;
    private readonly CanonicalSteamDeckOutputStage _deckStage;

    private AddonRoutingRuntime(IHandheldRoutingComposition composition, IRoutingSafetySession? safetySession, RoutingPipelineRuntimeCoordinator coordinator, CanonicalSteamDeckOutputStage deckStage)
    {
        _composition = composition;
        _safetySession = safetySession;
        _coordinator = coordinator;
        _deckStage = deckStage;
    }

    /// <summary>Review fix (BLOCKER): the OEM1 action path's startup activation
    /// (<see cref="IHandheldRoutingComposition.ConfigureOem1ActionPath"/>) and the routing guard
    /// share the SAME underlying helper ownership, but only their exact-handle Start() call itself
    /// serializes between them -- so the production startup boundary
    /// (<see cref="Hosting.AddonProcessHost.InitializeRuntimeAsync"/>) must await this task before
    /// routing/power observation is allowed to begin, ensuring the long-lived OEM1 owner's activation
    /// decision is settled first. <see cref="Task.CompletedTask"/> for a backend with no OEM1 feature
    /// (the interface default).</summary>
    internal Task Oem1ActivationTask { get; private set; } = Task.CompletedTask;

    /// <summary>Test-only seam: lets a test hold OEM1 activation deliberately incomplete and prove
    /// <see cref="ReconcileSafelyAsync"/> cannot enter the routing coordinator until it resolves.
    /// Never touched by production code.</summary>
    internal void TestOnly_SetOem1ActivationTask(Task task) => Oem1ActivationTask = task;

    internal static AddonRoutingRuntime? Create(
        IHandheldDeviceAdapter handheldDeviceAdapter,
        ISystemStatusProvider statusProvider,
        AddonOwnedVirtualDeviceTracker addonOwnedVirtualDeviceTracker,
        RecoveryManager recovery,
        PowerMutationGate powerGate,
        RecoverySafetyState recoverySafety,
        Settings.IOem1MappingPreference oem1MappingPreference,
        bool hardwareSupported)
    {
        ArgumentNullException.ThrowIfNull(oem1MappingPreference);
        // Forwarded, never recomputed: the startup hardware-support result is the single authority
        // both routing and the device composition's OEM1 availability gate read.
        var handheldRoutingComposition = new HandheldRoutingCompositionFactory().Create(handheldDeviceAdapter, recovery, powerGate, recoverySafety, hardwareSupported);
        if (handheldRoutingComposition is null) return null;

        var safetySession = handheldRoutingComposition.SafetySession;
        var feedbackAuthority = new FeedbackAuthority();

        var canonicalViiperPath = Path.Combine(AppContext.BaseDirectory, "Dependencies", "Viiper", "libVIIPER.dll");
        SteamOutputComposition.LogTargetSelected();
        var deckStage = new CanonicalSteamDeckOutputStage(
            () => new CanonicalSteamDeckSession(CanonicalViiperNativeApi.Load(canonicalViiperPath)),
            new WindowsControllerDeviceEnumerator(),
            new SteamDeckVirtualDeviceIdentityResolver(new SteamDeckVirtualDeviceIdentityPolicy()),
            addonOwnedVirtualDeviceTracker,
            recovery,
            () => safetySession?.CurrentRecoverySessionId,
            new HidHideDriverClient(), handheldRoutingComposition.ControllerStateSource,
            feedbackAuthority: feedbackAuthority, physicalRumbleSink: handheldRoutingComposition.PhysicalRumbleSink);
        IRoutingPipelineStage steamOutputStage = deckStage;
        var pipelineExecutor = new RoutingPipelineExecutor([.. handheldRoutingComposition.Stages, steamOutputStage]);
        var pipelineSessionCoordinator = new RoutingPipelineSessionCoordinator(pipelineExecutor);
        var coordinator = new RoutingPipelineRuntimeCoordinator(
            statusProvider,
            pipelineSessionCoordinator,
            handheldRoutingComposition.SessionBoundaryParticipants);
        deckStage.SetOutputFaultHandler(async () => { await coordinator.FailClosedAsync().ConfigureAwait(false); });
        handheldRoutingComposition.SetRuntimeFaultHandler(async reason =>
        {
            // Latch before fail-close: otherwise a still-eligible Steam session could immediately
            // re-enter routing right after this rollback completes, and if the physical device was
            // externally changed underneath us that would tug-of-war with whatever put it there.
            if (safetySession is not null)
                await safetySession.LatchRoutingFaultAsync(reason, CancellationToken.None).ConfigureAwait(false);

            var rollback = await coordinator.FailClosedAsync().ConfigureAwait(false);
            if (!rollback.Succeeded)
                AppLog.Error("Routing.Runtime", "Backend runtime fault fail-close did not complete.", new InvalidOperationException(rollback.Reason), ("Reason", reason));
        });

        var runtime = new AddonRoutingRuntime(handheldRoutingComposition, safetySession, coordinator, deckStage);

        // PR3: development-only OEM1 production E2E POC. The only two facts a device-specific OEM1
        // feature needs from this generic routing/output layer -- fresh routing status and the
        // canonical Steam Deck output stage's QAM pulse primitive -- passed down through the generic
        // IHandheldRoutingComposition seam (default no-op for a backend without an OEM1 feature). This
        // never gates on any routing "enabled" setting: SteamOutputActive reflects only whether
        // canonical routing is ACTUALLY active right now, exactly the fact OEM1 dispatch requires.
        // Reuses the same CaptureStatus() the rest of the runtime already uses, rather than
        // duplicating its field construction here, so the two can never drift apart.
        runtime.Oem1ActivationTask = handheldRoutingComposition.ConfigureOem1ActionPath(
            captureRoutingStatus: runtime.CaptureStatus,
            requestQuickAccessPulse: deckStage.RequestQuickAccessPulse,
            // The persisted OEM1 mapping travels alongside those two facts rather than through the
            // routing layer's own state: this runtime never reads it, and the mapping never becomes a
            // routing input.
            mappingPreference: oem1MappingPreference);

        return runtime;
    }

    /// <summary>PR2: optional additional power/resume participant the owned composition supplies
    /// (e.g. the MSI Center M OEM1 lifecycle driver). Null for a composition with nothing extra to
    /// quiesce/reconcile. Kept generic here -- this runtime never learns any device-specific
    /// detail, only that a capability may or may not be present.</summary>
    internal IPowerSuspendParticipant? AuxiliaryPowerParticipant => _composition.AuxiliaryPowerParticipant;
    internal IRuntimeResumeParticipant? AuxiliaryResumeParticipant => _composition.AuxiliaryResumeParticipant;

    /// <summary>Test-only observability seam onto the owned composition (e.g. so a test can inspect
    /// an MSI-specific composition's own state, such as the OEM1 coordinator's snapshot, to prove a
    /// capability actually ran). Never touched by production code.</summary>
    internal IHandheldRoutingComposition TestOnly_Composition => _composition;

    internal bool HasResidualSessionState => _coordinator.HasResidualSessionState;
    internal bool IsSafetySessionActive => _safetySession?.IsActive == true;
    internal bool HasOwnedRecoveryBoundary => _safetySession?.HasOwnedRecoveryBoundary == true;

    internal RoutingRuntimeStatusSnapshot CaptureStatus() => new(
        Available: true,
        OperationalState: _coordinator.CurrentOperationalState,
        SteamOutputActive: _coordinator.ActiveSessionHasSteamOutputEnabled,
        NativeDirectInputActive: _safetySession?.IsActive == true);
    internal Task<bool> RunDeveloperVibrationTestAsync(Contracts.Frontend.FrontendVibrationTestCommand command, CancellationToken cancellationToken) => CaptureStatus().SteamOutputActive ? _deckStage.RunDeveloperVibrationTestAsync(command, cancellationToken) : Task.FromResult(false);

    internal RoutingRuntimeTerminationSnapshot CaptureTerminationSnapshot() => _coordinator.CaptureTerminationSnapshot();

    internal Task<bool> ReconcileFreshAfterResumeAsync(CancellationToken cancellationToken) =>
        _coordinator.ReconcileFreshAfterResumeAsync(cancellationToken).AsTask();

    internal Task<bool> RetryResidualCleanupForResumeAsync(CancellationToken cancellationToken) =>
        _coordinator.RetryResidualCleanupForResumeAsync(cancellationToken).AsTask();

    internal void CancelInFlightTransition() => _coordinator.CancelInFlightTransition();

    /// <summary>
    /// Normal (non-resume) routing reconciliation, with the exact failure policy
    /// <c>App.xaml.cs</c> previously performed inline: an unsuccessful result is logged; an
    /// unexpected exception (other than cancellation the caller itself requested) is logged, the
    /// safety session's routing fault is latched, and the coordinator is failed closed, with the
    /// rollback outcome itself logged if it fails. <paramref name="requestStatusRefresh"/> is
    /// guaranteed to run exactly once, on every path, via
    /// <see cref="RoutingReconcileStatusRefresh.RunAsync"/>. Resume reconciliation
    /// (<see cref="ReconcileFreshAfterResumeAsync"/>) is a separate path with separate policy,
    /// owned by the caller.
    /// </summary>
    internal Task ReconcileSafelyAsync(Action requestStatusRefresh, CancellationToken cancellationToken = default) =>
        RoutingReconcileStatusRefresh.RunAsync(async () =>
        {
            try
            {
                // Review fix (BLOCKER): InitializeRuntimeAsync's own await of Oem1ActivationTask only
                // orders the CALLER-driven StartPowerObservation()/initial ReconcileAsync() -- it does
                // nothing to stop AddonRuntimeHost's SteamSessionRuntime.StateChanged subscription
                // (wired earlier in AddonRuntimeCompositionFactory.Create, before that await) from
                // firing a real event-driven reconcile while OEM1 activation is still in flight. Since
                // the OEM1 coordinator and the routing guard (CenterMGuard, the first enabled stage on
                // entry) share the SAME CenterMHelperOwnership, every normal reconcile entry point --
                // not just the startup one -- must wait behind the same one-shot activation task before
                // the routing coordinator/pipeline can run, or the two owners can still race the shared
                // helper's creation.
                await Oem1ActivationTask.ConfigureAwait(false);

                var result = await _coordinator.ReconcileAsync(cancellationToken).ConfigureAwait(false);
                if (!result.Succeeded)
                    AppLog.Warn("Routing.Runtime", "Canonical routing reconciliation did not complete successfully.", null,
                        ("Action", result.Action), ("State", result.State), ("Reason", result.Reason));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                AppLog.Warn("Routing.Runtime", "Canonical routing reconciliation failed; routing is being failed closed.", exception);
                try
                {
                    if (_safetySession is not null)
                        await _safetySession.LatchRoutingFaultAsync("CanonicalRoutingReconciliationFailed", CancellationToken.None).ConfigureAwait(false);
                    var rollback = await _coordinator.FailClosedAsync().ConfigureAwait(false);
                    if (!rollback.Succeeded)
                        AppLog.Error("Routing.Runtime", "Pipeline fail-close rollback did not complete.", new InvalidOperationException(rollback.Reason));
                }
                catch (Exception rollbackException)
                {
                    AppLog.Error("Routing.Runtime", "Pipeline fail-close rollback threw an exception.", rollbackException);
                }
            }
        }, requestStatusRefresh);

    /// <summary>
    /// Stops routing through the canonical coordinator. An unsuccessful result or exception is
    /// returned as <c>false</c> without invoking the safety-session fail-close, preserving any
    /// residual SteamOutput rollback barrier for retry. The caller must not dispose this runtime's
    /// backend resources until this method succeeds.
    /// </summary>
    internal async Task<bool> ShutdownAsync()
    {
        try
        {
            var shutdown = await _coordinator.ShutdownAsync().ConfigureAwait(false);
            if (!shutdown.Succeeded) return false;
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error("Routing.Runtime", "Routing pipeline shutdown failed; preserving the canonical rollback barrier.", exception);
            return false;
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
