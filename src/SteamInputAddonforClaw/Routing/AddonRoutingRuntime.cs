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
using SteamInputAddonforClaw.GameBar;

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
    // Serializes only Deck/X360 presentation mutations (Game Bar Enter/Exit and outer-route X360
    // retirement) so they cannot race each other. It is not a state authority: ownership remains
    // _xbox360Publisher/CanonicalSteamDeckOutputStage/CanonicalViiperRuntime, and outer routing
    // authority remains RoutingPipelineRuntimeCoordinator. Process-lifetime; disposed in
    // DisposeAsync, which only runs after ShutdownAsync has already retired routing.
    private readonly SemaphoreSlim _presentationGate = new(1, 1);
    private CanonicalXbox360InputPublisher? _xbox360Publisher;

    private AddonRoutingRuntime(IHandheldRoutingComposition composition, IRoutingSafetySession? safetySession, RoutingPipelineRuntimeCoordinator coordinator, CanonicalSteamDeckOutputStage deckStage, CanonicalViiperRuntime? viiperRuntime)
    {
        _composition = composition;
        _safetySession = safetySession;
        _coordinator = coordinator;
        _deckStage = deckStage;
        _viiperRuntime = viiperRuntime;
    }

    /// <summary>Owned initial OEM1 activation task. Frontend and tray startup do not await this task.
    /// Each routing reconcile awaits it before entering the routing pipeline/helper-acquisition
    /// boundary, ensuring persistent OEM1 ownership is settled before Routing may borrow the
    /// shared helper. <see cref="Task.CompletedTask"/> for a backend with no OEM1 feature.</summary>
    internal Task Oem1ActivationTask { get; private set; } = Task.CompletedTask;
    private bool? _testOnlySteamOutputReadyOverride;

    /// <summary>Test-only seam: lets a test hold OEM1 activation deliberately incomplete and prove
    /// <see cref="ReconcileSafelyAsync"/> cannot enter the routing coordinator until it resolves.
    /// Never touched by production code.</summary>
    internal void TestOnly_SetOem1ActivationTask(Task task) => Oem1ActivationTask = task;
    internal void TestOnly_SetSteamOutputReady(bool ready) => _testOnlySteamOutputReadyOverride = ready;

    internal static AddonRoutingRuntime? Create(
        IHandheldDeviceAdapter handheldDeviceAdapter,
        ISystemStatusProvider statusProvider,
        AddonOwnedVirtualDeviceTracker addonOwnedVirtualDeviceTracker,
        RecoveryManager recovery,
        PowerMutationGate powerGate,
        RecoverySafetyState recoverySafety,
        Settings.IOem1MappingPreference oem1MappingPreference,
        bool hardwareSupported,
        WinGSuppressionGuard winGSuppressionGuard,
        Settings.IWingMappingPreference? wingMappingPreference = null)
    {
        ArgumentNullException.ThrowIfNull(oem1MappingPreference);
        wingMappingPreference ??= new DefaultWingMappingPreference();
        ArgumentNullException.ThrowIfNull(winGSuppressionGuard);
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
        var winGProtectionStage = new WinGProtectionRoutingStage(winGSuppressionGuard);
        var stages = new List<IRoutingPipelineStage>(handheldRoutingComposition.Stages) { steamOutputStage, winGProtectionStage };
        var pipelineExecutor = new RoutingPipelineExecutor(stages);
        var pipelineSessionCoordinator = new RoutingPipelineSessionCoordinator(pipelineExecutor);
        AddonRoutingRuntime? runtime = null;
        var coordinator = new RoutingPipelineRuntimeCoordinator(
            statusProvider,
            pipelineSessionCoordinator,
            handheldRoutingComposition.SessionBoundaryParticipants,
            beforeActiveSessionExit: cancellationToken => runtime is null
                ? Task.FromResult(true)
                : runtime.RetireXbox360BeforeOuterRouteExitAsync(cancellationToken),
            pauseOwnedRouteForSuspend: cancellationToken => runtime is null
                ? Task.FromResult(RoutingStageOperationResult.Failure("RuntimeUnavailable"))
                : runtime.PauseOwnedRouteForSuspendAsync(cancellationToken),
            reconcileOwnedRouteState: cancellationToken => runtime is null
                ? Task.FromResult(RoutingStageOperationResult.Failure("RuntimeUnavailable"))
                : runtime.ReconcileOwnedRouteStateAsync(cancellationToken));
        deckStage.SetOutputFaultHandler(async () => { await coordinator.FailClosedAsync().ConfigureAwait(false); });
        handheldRoutingComposition.SetRuntimeFaultHandler((reason, yieldCurrentSteamSession) => HandleBackendRuntimeFaultAsync(reason, yieldCurrentSteamSession));

        async ValueTask HandleBackendRuntimeFaultAsync(string reason, bool yieldCurrentSteamSession)
        {
            if (yieldCurrentSteamSession)
            {
                var yieldRequest = coordinator.RequestCurrentSessionYield();
                if (yieldRequest is null) return;
                await Task.Yield();
                var takeoverRollback = await coordinator.FailClosedForSessionYieldAsync(yieldRequest.Value).ConfigureAwait(false);
                if (!takeoverRollback.Succeeded)
                    AppLog.Error("Routing.Runtime", "Backend runtime fault fail-close did not complete.", new InvalidOperationException(takeoverRollback.Reason), ("Reason", reason));
                else if (runtime is not null)
                    await runtime.TryConvergeSafetyAfterCleanupAsync("BackendRuntimeFault");
                return;
            }

            if (safetySession is not null)
                await safetySession.LatchRoutingFaultAsync(reason, CancellationToken.None).ConfigureAwait(false);

            var rollback = await coordinator.FailClosedAsync().ConfigureAwait(false);
            if (!rollback.Succeeded)
                AppLog.Error("Routing.Runtime", "Backend runtime fault fail-close did not complete.", new InvalidOperationException(rollback.Reason), ("Reason", reason));
            else if (runtime is not null)
                await runtime.TryConvergeSafetyAfterCleanupAsync("BackendRuntimeFault");
        }

        runtime = new AddonRoutingRuntime(handheldRoutingComposition, safetySession, coordinator, deckStage, viiperRuntime);

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
        _ = handheldRoutingComposition.ConfigureWingActionPath(
            captureAuthority: winGProtectionStage.CaptureAuthority,
            tryRequestSteamPulse: deckStage.TryRequestSteamPulse,
            mappingPreference: wingMappingPreference);

        return runtime;
    }

    private sealed class DefaultWingMappingPreference : Settings.IWingMappingPreference
    {
        public SteamInputAddonforClaw.Contracts.Wing.WingMappingSettings WingMapping => SteamInputAddonforClaw.Contracts.Wing.WingMappingSettings.Default;
        public event EventHandler? WingMappingChanged { add { } remove { } }
    }

    // Invoked as RoutingPipelineRuntimeCoordinator's beforeActiveSessionExit callback -- i.e.
    // already running while the coordinator holds its own routing transition authority. Per the
    // required lock order (routing transition -> presentation gate, never the reverse), this must
    // only acquire _presentationGate around the mutation itself and must never itself await
    // FailClosedAsync/FailClosedForXbox360PresentationAsync; the caller (coordinator or its own
    // outer rollback) owns that decision.
    private Task<bool> RetireXbox360BeforeOuterRouteExitAsync(CancellationToken cancellationToken) =>
        RunGatedPresentationMutationAsync(
            _presentationGate,
            async () =>
            {
                var publisher = _xbox360Publisher;
                if (publisher is null)
                    return (true, null);

                var result = await RetireXbox360PresentationCoreAsync(
                    publisher,
                    publisher.StopAsync,
                    _viiperRuntime is null
                        ? static () => USBDeviceDetachResult.Invalid
                        : _viiperRuntime.DetachXbox360,
                    cancellationToken).ConfigureAwait(false);

                if (!result.Succeeded)
                    return (false, null);

                _xbox360Publisher = null;
                return (true, null);
            },
            failClosed: null,
            cancellationToken);

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
    internal bool HasPreservedSession => _coordinator.HasPreservedSession;

    private async Task<RoutingStageOperationResult> PauseOwnedRouteForSuspendAsync(CancellationToken cancellationToken)
    {
        var result = await _composition.PauseOwnedRouteForSuspendAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) return result;
        var deckPaused = await _deckStage.PausePresentationAsync(
            cancellationToken, reportOutputFaultOnFailure: false).ConfigureAwait(false);
        return deckPaused
            ? RoutingStageOperationResult.Success("RoutePausedForSuspend")
            : RoutingStageOperationResult.Failure("SteamDeckPresentationPauseFailed");
    }
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
    /// active. It is reached by the production Game Bar delivery path while the outer Steam
    /// route remains authoritative. Deck pause remains the first and authoritative step; the X360
    /// attachment is not touched until the Deck publisher has stopped and neutral was accepted.
    /// </summary>
    internal Task<bool> EnterXbox360PresentationAsync(CancellationToken cancellationToken = default) =>
        RunGatedPresentationMutationAsync(
            _presentationGate,
            async () =>
            {
                // Ownership/readiness is evaluated fresh here, inside the gate, not before it was
                // acquired -- a queued Enter must never act on a pre-wait snapshot that a
                // concurrent Exit/retirement has since invalidated.
                if (!_coordinator.CanApplyInteractivePresentation ||
                    !CaptureStatus().SteamOutputActive ||
                    _viiperRuntime is not { State: CanonicalViiperRuntimeState.Ready } ||
                    _xbox360Publisher is not null)
                    return (false, null);

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
                    return (true, null);
                }
                return (false, entered.FailureReason);
            },
            failClosed: FailClosedForXbox360PresentationAsync,
            cancellationToken);

    /// <summary>
    /// Exits the Xbox360 presentation boundary while keeping the outer Steam
    /// route active. It is the narrow production policy primitive used by Game Bar delivery.
    /// The publisher must prove stopped before VIIPER detachment, and the
    /// Deck stage owns any failure encountered while resuming its existing publisher.
    /// </summary>
    internal Task<bool> ExitXbox360PresentationAsync(CancellationToken cancellationToken = default) =>
        RunGatedPresentationMutationAsync(
            _presentationGate,
            async () =>
            {
                // Same fresh-inside-the-gate rule as Enter: a queued Exit must observe whatever
                // ownership state the previous gated mutation actually committed.
                if (!_coordinator.CanApplyInteractivePresentation ||
                    !CaptureStatus().SteamOutputActive ||
                    _viiperRuntime is not { State: CanonicalViiperRuntimeState.Ready } ||
                    _xbox360Publisher is not { } publisher)
                    return (false, null);

                var exited = await ExitXbox360PresentationCoreAsync(
                    publisher: publisher,
                    stopPublisher: publisher.StopAsync,
                    detach: _viiperRuntime.DetachXbox360,
                    resumeDeck: () => _deckStage.ResumePresentationAsync(CancellationToken.None),
                    cancellationToken).ConfigureAwait(false);

                if (exited.PublisherReleased)
                    _xbox360Publisher = null;
                return (exited.Succeeded, exited.FailureReason);
            },
            failClosed: FailClosedForXbox360PresentationAsync,
            cancellationToken);

    internal static async Task<bool> ShutdownCoreAsync(
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
    /// policy seam only; foreground observation and serialized delivery are owned by
    /// <see cref="Hosting.AddonProcessHost"/>, not by this method.
    ///
    /// Deliberately does not pre-check <c>SteamOutputActive</c>/<c>_xbox360Publisher</c> here: a
    /// snapshot taken before <see cref="EnterXbox360PresentationAsync"/>/
    /// <see cref="ExitXbox360PresentationAsync"/> acquire <c>_presentationGate</c> can go stale
    /// while a prior call is still in flight (e.g. a queued foreground=false arriving before an
    /// in-progress Enter has committed <c>_xbox360Publisher</c>), which could otherwise cause this
    /// policy to skip the call it should make. Enter/Exit already re-evaluate readiness/ownership
    /// fresh, inside the gate, so they are the sole ownership authority.
    /// </summary>
    internal Task<bool> HandleGameBarForegroundChangedAsync(bool isForeground, CancellationToken cancellationToken = default) =>
        HandleGameBarForegroundChangedCoreAsync(isForeground, EnterXbox360PresentationAsync, ExitXbox360PresentationAsync, cancellationToken);

    // Test seam for the boolean policy only. It owns no presentation state and does not add a
    // second transition authority; the instance method supplies the real Enter/Exit primitives,
    // which make the authoritative ownership decision fresh inside _presentationGate.
    internal static Task<bool> HandleGameBarForegroundChangedCoreAsync(
        bool isForeground,
        Func<CancellationToken, Task<bool>> enter,
        Func<CancellationToken, Task<bool>> exit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return isForeground ? enter(cancellationToken) : exit(cancellationToken);
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
            else
                await TryConvergeSafetyAfterCleanupAsync("Xbox360PresentationFailClosed");
        }
        catch (Exception exception)
        {
            AppLog.Error("Routing.Runtime", "Xbox360 presentation fail-close threw an exception.", exception, ("Reason", reason));
        }
    }

    /// <summary>
    /// Runs <paramref name="mutate"/> under <paramref name="presentationGate"/>, then -- only after
    /// the gate has been released -- invokes <paramref name="failClosed"/> if the mutation reported
    /// a failure reason. Shared by <see cref="EnterXbox360PresentationAsync"/>,
    /// <see cref="ExitXbox360PresentationAsync"/>, and
    /// <see cref="RetireXbox360BeforeOuterRouteExitAsync"/> so the three Deck/X360 presentation
    /// mutations cannot race each other, while guaranteeing fail-close is never awaited while the
    /// gate is still held (see the routing-transition-then-presentation-gate lock order these three
    /// callers depend on). This is a small real-<see cref="SemaphoreSlim"/> primitive, not a new
    /// state authority or coordinator; ownership evaluation happens inside <paramref name="mutate"/>
    /// itself, only once the gate is actually held.
    /// </summary>
    internal static async Task<bool> RunGatedPresentationMutationAsync(
        SemaphoreSlim presentationGate,
        Func<Task<(bool Succeeded, string? FailureReason)>> mutate,
        Func<string, Task>? failClosed,
        CancellationToken cancellationToken)
    {
        bool succeeded;
        string? failureReason;
        await presentationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            (succeeded, failureReason) = await mutate().ConfigureAwait(false);
        }
        finally
        {
            presentationGate.Release();
        }

        if (failureReason is not null && failClosed is not null)
            await failClosed(failureReason).ConfigureAwait(false);
        return succeeded;
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

    internal async Task<bool> ReconcileFreshAfterResumeAsync(CancellationToken cancellationToken)
    {
        if (ShouldSkipNewForwardRouting)
            return true;

        // Frontend/tray are independent, but every forward Routing entry must wait until the
        // initial OEM1 persistent-helper ownership decision has settled. Resume can begin before
        // the deferred startup reconcile, so it needs the same one-shot barrier as normal routing.
        await Oem1ActivationTask.WaitAsync(cancellationToken).ConfigureAwait(false);

        // PowerTransitionCoordinator has already completed residual cleanup, committed Safe,
        // and opened the mutation gate before invoking this callback. Converge the stale routing
        // fault before fresh forward preflight can observe it.
        if (!_coordinator.HasResidualSessionState)
            await TryConvergeSafetyAfterCleanupAsync("FreshResumePreReconcile").ConfigureAwait(false);

        var succeeded = await _coordinator.ReconcileFreshAfterResumeAsync(cancellationToken).ConfigureAwait(false);
        if (succeeded)
            await TryConvergeSafetyAfterCleanupAsync("FreshResumeReconcile").ConfigureAwait(false);
        return succeeded;
    }

    internal async Task<bool> RetryResidualCleanupForResumeAsync(CancellationToken cancellationToken)
    {
        var succeeded = await _coordinator.RetryResidualCleanupForResumeAsync(cancellationToken).ConfigureAwait(false);
        if (succeeded)
            await TryConvergeSafetyAfterCleanupAsync("ResidualCleanupRetry").ConfigureAwait(false);
        return succeeded;
    }

    internal async Task<bool> ReconcilePreservedSessionAfterResumeAsync(
        Func<CancellationToken, Task> refreshBeforeDecision,
        CancellationToken cancellationToken)
    {
        var succeeded = await _coordinator.ReconcilePreservedSessionAsync(refreshBeforeDecision, cancellationToken).ConfigureAwait(false);
        if (succeeded) await TryConvergeSafetyAfterCleanupAsync("PreservedResumeReconcile").ConfigureAwait(false);
        return succeeded;
    }

    private async Task<RoutingStageOperationResult> ReconcileOwnedRouteStateAsync(CancellationToken cancellationToken)
    {
        var result = await _composition.ReconcileOwnedRouteStateAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) return result;
        return await _deckStage.ReconcileOwnedStateAsync(cancellationToken).ConfigureAwait(false);
    }

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
    internal async Task<bool> ReconcileSafelyAsync(Action requestStatusRefresh, CancellationToken cancellationToken = default)
    {
        var succeeded = false;
        await RoutingReconcileStatusRefresh.RunAsync(async () =>
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
                await Oem1ActivationTask.WaitAsync(cancellationToken).ConfigureAwait(false);

                if (ShouldSkipNewForwardRouting)
                {
                    succeeded = true;
                    return;
                }

                var result = await _coordinator.ReconcileAsync(cancellationToken).ConfigureAwait(false);
                succeeded = result.Succeeded;
                if (result.Succeeded && _coordinator.CurrentOperationalState == RoutingOperationalState.Passive && !_coordinator.HasResidualSessionState)
                    await TryConvergeSafetyAfterCleanupAsync("Reconcile");
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
                    else
                        await TryConvergeSafetyAfterCleanupAsync("ReconcileException");
                }
                catch (Exception rollbackException)
                {
                    AppLog.Error("Routing.Runtime", "Pipeline fail-close rollback threw an exception.", rollbackException);
                }
            }
        }, requestStatusRefresh).ConfigureAwait(false);
        return succeeded;
    }

    private async Task<bool> TryConvergeSafetyAfterCleanupAsync(string reason)
    {
        if (_safetySession is null || _coordinator.HasResidualSessionState)
            return false;
        return await _safetySession.ConvergeAfterRoutingCleanupAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private bool SteamOutputReady => _testOnlySteamOutputReadyOverride
        ?? (_viiperRuntime is { State: CanonicalViiperRuntimeState.Ready });
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
            var publisher = _xbox360Publisher;
            return await ShutdownCoreAsync(
                publisher,
                publisher is not null ? publisher.StopAsync : () => Task.CompletedTask,
                _viiperRuntime is { } viiper ? viiper.DetachXbox360 : () => USBDeviceDetachResult.Invalid,
                () => _xbox360Publisher = null,
                async () => (await _coordinator.ShutdownAsync().ConfigureAwait(false)).Succeeded,
                CancellationToken.None).ConfigureAwait(false);
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
        _presentationGate.Dispose();
    }
}
