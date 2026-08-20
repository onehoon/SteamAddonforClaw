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
using SteamInputAddonforClaw.Input;

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
    internal static bool CanInitializeViiper(bool hardwareSupported, RecoverySafety recoverySafety) =>
        hardwareSupported && recoverySafety == RecoverySafety.Safe;

    internal static ICanonicalViiperNativeApi? TryLoadViiper(string path)
    {
        try { return CanonicalViiperNativeApi.Load(path); }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            AppLog.Error("SteamOutput", "Canonical VIIPER module could not be loaded; Steam output is unavailable for this process lifetime.", exception);
            return null;
        }
    }
    private readonly IHandheldRoutingComposition _composition;
    private readonly IRoutingSafetySession? _safetySession;
    private readonly RoutingPipelineRuntimeCoordinator _coordinator;
    private readonly CanonicalSteamDeckOutputStage _deckStage;
    private readonly CanonicalViiperRuntime? _viiperRuntime;
    private CanonicalXbox360InputPublisher? _xbox360Publisher;

    private AddonRoutingRuntime(IHandheldRoutingComposition composition, IRoutingSafetySession? safetySession, RoutingPipelineRuntimeCoordinator coordinator, CanonicalSteamDeckOutputStage deckStage, CanonicalViiperRuntime? viiperRuntime)
    {
        _composition = composition;
        _safetySession = safetySession;
        _coordinator = coordinator;
        _deckStage = deckStage;
        _viiperRuntime = viiperRuntime;
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
        CanonicalViiperRuntime? viiperRuntime = null;
        if (CanInitializeViiper(hardwareSupported, recoverySafety.Current) &&
            TryLoadViiper(canonicalViiperPath) is { } native)
            viiperRuntime = CanonicalViiperRuntime.TryInitialize(native, "127.0.0.1:3242");
        var deckStage = new CanonicalSteamDeckOutputStage(
            viiperRuntime is { State: CanonicalViiperRuntimeState.Ready } ? () => new CanonicalSteamDeckSession(viiperRuntime) : () => new UnavailableCanonicalSteamDeckSession(),
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

        var runtime = new AddonRoutingRuntime(handheldRoutingComposition, safetySession, coordinator, deckStage, viiperRuntime);

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
    internal Task<DeveloperVibrationTestOutcome> RunDeveloperVibrationTestAsync(Contracts.Frontend.FrontendVibrationTestCommand command, CancellationToken cancellationToken) => CaptureStatus().SteamOutputActive ? _deckStage.RunDeveloperVibrationTestAsync(command, cancellationToken) : Task.FromResult(new DeveloperVibrationTestOutcome(false, null, null));
    internal PhysicalRumbleWriteResult? CancelDeveloperVibrationTest() => _deckStage.CancelDeveloperVibrationTest();

    /// <summary>
    /// Enters the one-way Xbox360 presentation boundary while keeping the outer Steam route
    /// active. This is an internal foundation seam only: no Game Bar event or normal Runtime
    /// caller invokes it yet. Deck pause remains the first and authoritative step; the X360
    /// attachment is not touched until the Deck publisher has stopped and neutral was accepted.
    /// </summary>
    internal async Task<bool> EnterXbox360PresentationAsync(CancellationToken cancellationToken = default)
    {
        if (!CaptureStatus().SteamOutputActive || _viiperRuntime is not { State: CanonicalViiperRuntimeState.Ready } || _xbox360Publisher is not null)
            return false;

        var entered = await EnterXbox360PresentationCoreAsync(
            source: _composition.ControllerStateSource,
            pauseDeck: () => _deckStage.PausePresentationAsync(cancellationToken),
            queryAttachment: _viiperRuntime.TryGetXbox360AttachmentState,
            attach: _viiperRuntime.AttachXbox360,
            detach: _viiperRuntime.DetachXbox360,
            setState: _viiperRuntime.SetXbox360State,
            ticks: null,
            publisherFault: exception => _ = HandleXbox360PublisherFaultAsync(exception),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (entered.Publisher is not null)
        {
            _xbox360Publisher = entered.Publisher;
            return true;
        }

        if (entered.FailureReason is { } reason)
            await FailClosedForXbox360PresentationAsync(reason).ConfigureAwait(false);
        return false;
    }

    /// <summary>
    /// Exits the manually-entered Xbox360 presentation boundary while keeping the outer Steam
    /// route active. This is an internal foundation seam only; no Game Bar or normal Runtime
    /// caller invokes it yet. The publisher must prove stopped before VIIPER detachment, and the
    /// Deck stage owns any failure encountered while resuming its existing publisher.
    /// </summary>
    internal async Task<bool> ExitXbox360PresentationAsync(CancellationToken cancellationToken = default)
    {
        if (!CaptureStatus().SteamOutputActive || _viiperRuntime is not { State: CanonicalViiperRuntimeState.Ready } || _xbox360Publisher is null)
            return false;

        var retired = await RetireXbox360PresentationCoreAsync(
            publisher: _xbox360Publisher,
            stopPublisher: _xbox360Publisher.StopAsync,
            detach: _viiperRuntime.DetachXbox360,
            cancellationToken).ConfigureAwait(false);

        if (retired.PublisherReleased)
            _xbox360Publisher = null;
        if (retired.FailureReason is { } reason)
            await FailClosedForXbox360PresentationAsync(reason).ConfigureAwait(false);
        if (!retired.Succeeded)
            return false;

        var resumed = await _deckStage.ResumePresentationAsync(CancellationToken.None).ConfigureAwait(false);
        return resumed;
    }

    internal static async Task<bool> ShutdownAfterXbox360RetirementAsync(
        CanonicalXbox360InputPublisher? publisher,
        Func<Task> stopPublisher,
        Func<USBDeviceDetachResult> detach,
        Action clearPublisher,
        Func<Task<bool>> coordinatorShutdown,
        CancellationToken cancellationToken)
    {
        if (publisher is not null)
        {
            var retired = await RetireXbox360PresentationCoreAsync(publisher, stopPublisher, detach, cancellationToken).ConfigureAwait(false);
            if (!retired.Succeeded)
                return false;
            clearPublisher();
        }
        return await coordinatorShutdown().ConfigureAwait(false);
    }

    internal static async Task<bool> ShutdownCoreAsync(
        Func<Task<bool>> retireXbox360,
        Func<Task<bool>> shutdownOuter)
    {
        if (!await retireXbox360().ConfigureAwait(false))
            return false;
        return await shutdownOuter().ConfigureAwait(false);
    }

    /// <summary>Stops and detaches the X360 presentation without resuming Deck.</summary>
    internal static async Task<Xbox360PresentationExitResult> RetireXbox360PresentationCoreAsync(
        CanonicalXbox360InputPublisher? publisher,
        Func<Task> stopPublisher,
        Func<USBDeviceDetachResult> detach,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (publisher is null)
            return new(false, false);
        try
        {
            await stopPublisher().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return new(false, false, $"Xbox360PublisherStopFailed:{exception.GetType().Name}");
        }

        USBDeviceDetachResult detachResult;
        try
        {
            detachResult = detach();
        }
        catch (Exception exception)
        {
            return new(false, false, $"Xbox360DetachThrew={exception.GetType().Name}");
        }
        if (detachResult != USBDeviceDetachResult.Success)
            return new(false, false, $"Xbox360Detach{detachResult}");
        return new(true, true);
    }

    /// <summary>
    /// Selects the existing presentation primitive for a Game Bar foreground change. This is a
    /// policy seam only; the Game Bar watcher is deliberately not subscribed here, so normal
    /// Runtime behavior remains unchanged until a later production-wiring step.
    /// </summary>
    internal Task HandleGameBarForegroundChangedAsync(bool isForeground, CancellationToken cancellationToken = default) =>
        HandleGameBarForegroundChangedCoreAsync(
            isForeground,
            CaptureStatus().SteamOutputActive,
            _xbox360Publisher is not null,
            EnterXbox360PresentationAsync,
            ExitXbox360PresentationAsync,
            cancellationToken);

    // Test seam for the boolean policy only. It owns no presentation state and does not add a
    // second transition authority; the instance method supplies the existing Enter/Exit methods.
    internal static async Task HandleGameBarForegroundChangedCoreAsync(
        bool isForeground,
        bool steamOutputActive,
        bool xbox360PresentationOwned,
        Func<CancellationToken, Task<bool>> enter,
        Func<CancellationToken, Task<bool>> exit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (isForeground)
        {
            if (steamOutputActive && !xbox360PresentationOwned)
                await enter(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (xbox360PresentationOwned)
            await exit(cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleXbox360PublisherFaultAsync(Exception exception)
    {
        AppLog.Error("SteamOutput", "Canonical Xbox360 presentation publishing failed.", exception);
        await FailClosedForXbox360PresentationAsync("Xbox360PresentationPublisherFault").ConfigureAwait(false);
    }

    private async Task FailClosedForXbox360PresentationAsync(string reason)
    {
        try
        {
            if (_safetySession is not null)
                await _safetySession.LatchRoutingFaultAsync(reason, CancellationToken.None).ConfigureAwait(false);
            var rollback = await _coordinator.FailClosedAsync().ConfigureAwait(false);
            if (!rollback.Succeeded)
                AppLog.Error("Routing.Runtime", "Xbox360 presentation fail-close did not complete.", new InvalidOperationException(rollback.Reason), ("Reason", reason));
        }
        catch (Exception exception)
        {
            AppLog.Error("Routing.Runtime", "Xbox360 presentation fail-close threw an exception.", exception, ("Reason", reason));
        }
    }

    internal delegate bool Xbox360AttachmentQuery(out USBDeviceAttachmentState state);
    internal sealed record Xbox360PresentationEntryResult(CanonicalXbox360InputPublisher? Publisher, string? FailureReason = null);
    internal sealed record Xbox360PresentationExitResult(bool Succeeded, bool PublisherReleased, string? FailureReason = null);

    // Test seam for deterministic orchestration tests. Production supplies the real Deck pause,
    // VIIPER attachment/state operations, and publisher fault path above; this method adds no
    // production abstraction or alternate ownership model.
    internal static async Task<Xbox360PresentationEntryResult> EnterXbox360PresentationCoreAsync(
        IControllerStateSnapshotSource source,
        Func<Task<bool>> pauseDeck,
        Xbox360AttachmentQuery queryAttachment,
        Func<USBDeviceAttachResult> attach,
        Func<USBDeviceDetachResult> detach,
        Func<Xbox360DeviceState, bool> setState,
        IInputReportTickSource? ticks,
        Action<Exception> publisherFault,
        CancellationToken cancellationToken,
        Func<CanonicalXbox360InputPublisher>? createPublisher = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await pauseDeck().ConfigureAwait(false)) return new(null);

        if (!queryAttachment(out var attachment) || attachment != USBDeviceAttachmentState.Detached)
            return new(null, "Xbox360AttachmentStateNotDetached");
        var attachResult = attach();
        if (attachResult != USBDeviceAttachResult.Success)
            return new(null, $"Xbox360Attach{attachResult}");

        CanonicalXbox360InputPublisher? publisher = null;
        try
        {
            publisher = createPublisher is null
                ? new CanonicalXbox360InputPublisher(source, setState, ticks, publisherFault)
                : createPublisher();
            publisher.Start();
            return new(publisher);
        }
        catch (Exception exception)
        {
            string cleanup;
            try
            {
                cleanup = $"Detach={detach()}";
            }
            catch (Exception detachException)
            {
                cleanup = $"DetachThrew={detachException.GetType().Name}";
            }
            return new(null, $"Xbox360PublisherStartFailed:{exception.GetType().Name};{cleanup}");
        }
    }

    // Test seam for deterministic reverse-orchestration tests. The production method supplies
    // the existing publisher/runtime/stage operations directly; this does not add a production
    // ownership abstraction or a second transition authority.
    internal static async Task<Xbox360PresentationExitResult> ExitXbox360PresentationCoreAsync(
        CanonicalXbox360InputPublisher? publisher,
        Func<Task> stopPublisher,
        Func<USBDeviceDetachResult> detach,
        Func<Task<bool>> resumeDeck,
        CancellationToken cancellationToken)
    {
        var retired = await RetireXbox360PresentationCoreAsync(publisher, stopPublisher, detach, cancellationToken).ConfigureAwait(false);
        if (!retired.Succeeded)
            return retired;

        // X360 is now proven stopped and detached. Deck resume is the final completion step;
        // cancellation is deliberately not rechecked here so cleanup cannot be stranded.
        var resumed = await resumeDeck().ConfigureAwait(false);
        return resumed ? retired : new(false, true);
    }

    internal RoutingRuntimeTerminationSnapshot CaptureTerminationSnapshot() => _coordinator.CaptureTerminationSnapshot();

    internal Task<bool> ReconcileFreshAfterResumeAsync(CancellationToken cancellationToken) =>
        ShouldSkipNewForwardRouting
            ? Task.FromResult(true)
            : _coordinator.ReconcileFreshAfterResumeAsync(cancellationToken).AsTask();

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

                if (ShouldSkipNewForwardRouting)
                    return;

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

    private bool SteamOutputReady => _viiperRuntime is { State: CanonicalViiperRuntimeState.Ready };
    private bool ShouldSkipNewForwardRouting => !SteamOutputReady && !_coordinator.HasResidualSessionState;

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
            return await ShutdownCoreAsync(
                () => ShutdownAfterXbox360RetirementAsync(
                    _xbox360Publisher,
                    _xbox360Publisher is { } publisher ? publisher.StopAsync : () => Task.CompletedTask,
                    _viiperRuntime is { } viiper ? viiper.DetachXbox360 : () => USBDeviceDetachResult.Invalid,
                    () => _xbox360Publisher = null,
                    () => Task.FromResult(true),
                    CancellationToken.None),
                async () => (await _coordinator.ShutdownAsync().ConfigureAwait(false)).Succeeded).ConfigureAwait(false);
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
    public async ValueTask DisposeAsync()
    {
        await _composition.DisposeAsync().ConfigureAwait(false);
        if (_viiperRuntime is not null && !await _viiperRuntime.TeardownAsync().ConfigureAwait(false))
            AppLog.Error("Routing.Runtime", "Final canonical VIIPER teardown failed; owner retained.", new InvalidOperationException("CanonicalViiperRuntime.TeardownAsync returned false."));
    }
}
