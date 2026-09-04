using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Feedback;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal enum AddonPresentationKind { Xbox360, SteamDeck }

/// <summary>The one in-memory PR6 first-attach result. Not persisted.</summary>
internal sealed record InitialPresentationResult(bool Succeeded, AddonPresentationKind? Presentation, string Reason);

internal enum PresentationReconcileOutcome
{
    /// <summary>The active presentation already matches the current raw Steam/BPM policy and its
    /// publisher is healthy -- no native call, no publisher restart.</summary>
    NoChange,
    /// <summary>The active typed device + publisher were changed to the other kind.</summary>
    Switched,
    /// <summary>There was no active presentation; the currently-desired one was attached.</summary>
    Attached,
    /// <summary>A native/publisher step could not be proven safe. No fallback to the other or previous
    /// presentation; PID / DirectInput / HidHide untouched.</summary>
    Failed,
    /// <summary>A precondition (owner disposed, VIIPER not Ready, PR5 source not running) was not met;
    /// no forward mutation was attempted.</summary>
    Blocked,
}

/// <summary>The in-memory result of one PR7 runtime presentation reconcile. Not persisted.</summary>
internal sealed record PresentationReconcileResult(PresentationReconcileOutcome Outcome, AddonPresentationKind? Presentation, string Reason)
{
    internal bool Succeeded => Outcome is PresentationReconcileOutcome.NoChange or PresentationReconcileOutcome.Switched or PresentationReconcileOutcome.Attached;
}

internal enum OverlayPauseOutcome
{
    /// <summary>Publisher proven stopped and the SAME attached device written neutral. The typed
    /// device stays attached; a later <see cref="IMsiClawAddonPresentation.ResumeAfterOverlayAsync"/>
    /// restarts the same publisher.</summary>
    Paused,
    /// <summary>The publisher could not be stopped/joined. Nothing was written neutral, nothing was
    /// detached; the current presentation stays owned and live.</summary>
    PublisherNotStopped,
    /// <summary>Fail-close boundary: the publisher was proven stopped but the neutral write was
    /// rejected, so the current active presentation was retired through the existing owner. No
    /// alternate presentation fallback.</summary>
    NeutralRejectedPresentationRetired,
    /// <summary>Fail-close boundary reached but the retirement of the current presentation could not
    /// itself be proven (e.g. native detach failure). The publisher is stopped, the last game-facing
    /// state was not proven neutral, and ownership evidence is retained -- callers must NOT treat
    /// this as a clean retirement.</summary>
    NeutralRejectedRetireFailed,
    /// <summary>A precondition (owner disposed, no active presentation / publisher, wrong kind) was
    /// not met; no mutation was attempted.</summary>
    Blocked,
}

/// <summary>The in-memory result of one OQ4 Overlay-capture pause. Not persisted.</summary>
internal sealed record OverlayPauseResult(OverlayPauseOutcome Outcome, string Reason)
{
    internal bool Succeeded => Outcome == OverlayPauseOutcome.Paused;
}

internal enum OverlayResumeOutcome
{
    /// <summary>The SAME publisher was restarted against a healthy source; the presentation is live.</summary>
    Resumed,
    /// <summary>The Overlay pause was cleared but the same presentation could not be safely resumed
    /// (source not running, presentation no longer structurally valid, or publisher start threw).
    /// Output stays neutral / publisher stopped; existing physical recovery / PR7 reconcile owns
    /// recovery.</summary>
    LeftNeutral,
    /// <summary>There was no Overlay pause to end.</summary>
    NotPaused,
}

/// <summary>The in-memory result of one OQ4 Overlay-capture resume. Not persisted.</summary>
internal sealed record OverlayResumeResult(OverlayResumeOutcome Outcome, string Reason)
{
    internal bool Succeeded => Outcome == OverlayResumeOutcome.Resumed;
}

internal enum SuspendPauseOutcome
{
    /// <summary>The game-facing publisher was proven stopped/joined and the SAME attached device (if
    /// any) was written neutral without detaching it. Suspend quiesce may report success.</summary>
    Paused,
    /// <summary>Full1902 authority exists but no presentation was attached. Nothing native happened;
    /// the suspend barrier is still recorded so a queued Steam/BPM event cannot attach a live
    /// presentation after it. Suspend quiesce may report success.</summary>
    PausedNoPresentation,
    /// <summary>The publisher could not be proven stopped/joined. Nothing was written neutral, nothing
    /// detached; the suspend pause fact is retained. Suspend quiesce must classify this unsafe.</summary>
    PublisherNotStopped,
    /// <summary>Fail-close: the publisher was proven stopped but the neutral write was rejected, so
    /// the current presentation was retired through the existing owner. Output is safe.</summary>
    NeutralRejectedPresentationRetired,
    /// <summary>Fail-close reached but the retirement could not itself be proven. Output was not
    /// proven neutral; suspend quiesce must classify this unsafe.</summary>
    NeutralRejectedRetireFailed,
    /// <summary>A precondition (owner disposed) was not met.</summary>
    Blocked,
}

/// <summary>The in-memory result of one Full1902 suspend-pause. Not persisted.</summary>
internal sealed record SuspendPauseResult(SuspendPauseOutcome Outcome, string Reason)
{
    /// <summary>The game-facing output is proven safe (stopped + neutral, or retired). Suspend
    /// quiesce may report success for this participant.</summary>
    internal bool Safe => Outcome is SuspendPauseOutcome.Paused or SuspendPauseOutcome.PausedNoPresentation
        or SuspendPauseOutcome.NeutralRejectedPresentationRetired;
}

internal enum SuspendResumeOutcome
{
    /// <summary>There was no suspend pause to release.</summary>
    NotPaused,
    /// <summary>The suspend pause was cleared and the SAME publisher was restarted against a healthy
    /// source for the still-desired presentation. No attach/detach/VIIPER recreation.</summary>
    SamePublisherResumed,
    /// <summary>The suspend pause was cleared but the presentation must be reconciled by the existing
    /// PR7 owner (desired kind changed, no presentation attached, or structural attachment unproven).
    /// The old publisher, if any, is left stopped + neutral.</summary>
    ReconcileRequired,
    /// <summary>The physical source is not available yet. The suspend pause and neutral/stopped
    /// output are retained; the existing PR8/PR10 recovery owns physical repair and will re-enter
    /// this release path through <c>PhysicalInputRecovered</c>.</summary>
    DeferredSourceUnavailable,
    /// <summary>Review #490 (3rd pass): the managed active-kind/publisher pair is empty, but residual
    /// typed-device ownership evidence (a retained Deck session, or a still-attached canonical X360)
    /// survives from a failed initial-attach cleanup detach -- the same structural proof
    /// <see cref="MsiClawAddonPresentation.PauseForSuspendAsync"/> requires. The suspend pause is
    /// retained; the caller must NOT fall through into PR7 mutation.</summary>
    DeferredUnsafePresentation,
    /// <summary>The suspend pause was cleared but game-facing publication is intentionally left
    /// stopped/neutral (Overlay capture still owns its own neutral pause, or owner disposed).</summary>
    LeftNeutral,
}

/// <summary>The in-memory result of one Full1902 suspend-release. Not persisted.</summary>
internal sealed record SuspendResumeResult(SuspendResumeOutcome Outcome, string Reason)
{
    /// <summary>The suspend pause is still active -- the caller must not continue into normal
    /// presentation reconciliation.</summary>
    internal bool StillBlocked => Outcome is SuspendResumeOutcome.DeferredSourceUnavailable
        or SuspendResumeOutcome.DeferredUnsafePresentation;
}

/// <summary>A narrow abstraction over the two canonical VIIPER input publishers so the presentation
/// owner can be tested without the production worker thread. Production adapters forward verbatim to
/// <see cref="CanonicalXbox360InputPublisher"/> / <see cref="CanonicalSteamDeckInputPublisher"/>.</summary>
internal interface IAddonPresentationPublisher
{
    bool IsRunning { get; }
    void Start();
    Task StopAsync();
}

internal interface IMsiClawAddonPresentation : IAsyncDisposable
{
    /// <summary>The currently attached typed device kind, or <see langword="null"/> when no
    /// presentation is attached. The feature-local front-button action path selects the OEM1
    /// Normal/Routing mapping domain from this actual Full1902 presentation rather than legacy
    /// routing status.</summary>
    AddonPresentationKind? ActivePresentation { get; }

    /// <summary>Full1902 A2: request a synthetic SteamDeck <c>Steam</c> system-button pulse on the
    /// existing publish path. Returns <see langword="false"/> (no-op) unless the current presentation
    /// is a healthy live SteamDeck publication. No attach/detach, no VIIPER recreation, no PID
    /// mutation, no retry loop.</summary>
    bool TryRequestSteamPulse();

    /// <summary>Full1902 A2: request a synthetic SteamDeck <c>QuickAccess</c> system-button pulse on
    /// the existing publish path. Same eligibility contract as <see cref="TryRequestSteamPulse"/>.</summary>
    bool TryRequestQuickAccessPulse();

    /// <summary>Selects one presentation from a fresh raw Steam/BPM snapshot, attaches exactly that
    /// typed device, sends neutral, and starts exactly one matching publisher on the supplied PR5
    /// input source. No fallback to the other presentation on failure.</summary>
    Task<InitialPresentationResult> AttachInitialAsync(IMsiClawPreparedInputSource source, SteamPresentationSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>PR7: reconcile the attached typed device + publisher to the current raw Steam/BPM
    /// policy. The freshest snapshot is captured AFTER the owner gate is acquired (work order PR7
    /// section 7/18), so queued/overlapping Steam events never apply stale desired state. Changes
    /// only virtual attachment/publisher state -- PID1902, the PR5 DirectInput source, the persistent
    /// HidHide baseline, and the canonical VIIPER server/bus are untouched. No fallback to the other
    /// presentation on failure; no polling; no retry loop.</summary>
    Task<PresentationReconcileResult> ReconcileDesiredPresentationAsync(
        IMsiClawPreparedInputSource source, Func<SteamPresentationSnapshot> captureSnapshot, CancellationToken cancellationToken);

    /// <summary>The official Center M Enable-and-Restart step: stop/join the publisher, neutral+detach
    /// the selected typed device, then tear the canonical VIIPER runtime down to its closed state.
    /// Must reach a proven-safe state before physical ownership is released to MSI.</summary>
    Task<bool> ReleaseForCenterMEnableAsync(CancellationToken cancellationToken);

    /// <summary>OQ4: stop the current publisher, prove it joined, and write the SAME attached typed
    /// device neutral -- WITHOUT detaching it or recreating VIIPER. On a proven-stopped publisher
    /// with a rejected neutral write the current presentation is retired through the existing owner
    /// (fail-close). While paused, <see cref="ReconcileDesiredPresentationAsync"/> is blocked from
    /// switching. Never blocks explicit authority release or process shutdown.</summary>
    Task<OverlayPauseResult> PauseForOverlayAsync(CancellationToken cancellationToken);

    /// <summary>OQ4: end an Overlay-capture pause. The pause fact is ALWAYS cleared -- even when
    /// <paramref name="source"/> is <see langword="null"/> / unavailable (real PID1902 loss) -- so
    /// normal physical recovery / PR7 reconcile is no longer blocked. The SAME publisher object is
    /// restarted only when the source is healthy and the presentation is still structurally valid
    /// (no attach/detach/VIIPER recreate); otherwise output is left neutral.</summary>
    Task<OverlayResumeResult> ResumeAfterOverlayAsync(IMsiClawPreparedInputSource? source, CancellationToken cancellationToken);

    /// <summary>Full1902 Suspend/Resume: process-memory-only fact -- live game-facing publication is
    /// intentionally blocked because the current power cycle entered Suspend and has not yet been
    /// safely released. Never persisted; not controller authority.</summary>
    bool IsSuspendPaused { get; }

    /// <summary>Full1902 Suspend/Resume section 7: mark suspend-pause active, stop + JOIN the current
    /// publisher, clear pending synthetic Steam/QuickAccess pulses, disarm + drain the rumble
    /// feedback callback, request a physical rumble STOP, then write the SAME currently-attached
    /// virtual device neutral without detaching a healthy typed device. A publisher that cannot be
    /// proven stopped leaves the pause active and reports failure (no unsafe neutral write). A
    /// rejected neutral write on a stopped publisher retires the current presentation (fail-close).</summary>
    Task<SuspendPauseResult> PauseForSuspendAsync(CancellationToken cancellationToken);

    /// <summary>Full1902 Suspend/Resume section 10: release the suspend pause at the Resume boundary.
    /// The caller must have already reset the physical snapshot to neutral. Restarts the SAME
    /// publisher only when the freshly-captured desired kind still matches the attached presentation,
    /// the source is running, VIIPER is Ready, and the typed device is still proven attached;
    /// otherwise the pause is cleared and the existing PR7 reconcile owns the transition. An
    /// unavailable source keeps the pause and defers to existing physical recovery.</summary>
    Task<SuspendResumeResult> ResumeAfterSuspendAsync(
        IMsiClawPreparedInputSource? source, Func<SteamPresentationSnapshot> captureSnapshot, CancellationToken cancellationToken);
}

/// <summary>
/// The one narrow Full-1902 virtual-presentation owner (work order PR6). It owns exactly one
/// <see cref="CanonicalViiperRuntime"/>, attaches exactly one typed device, owns its publisher, and
/// fails that virtual presentation closed. It does NOT own Center M authority, PID1902, DirectInput
/// acquisition, HidHide, Steam/BPM observation, or runtime presentation switching (PR7).
///
/// A single private gate serializes attach / publisher-fault cleanup / release / teardown so they
/// cannot detach the same native device concurrently. It is not a second authority source.
/// </summary>
internal sealed class MsiClawAddonPresentation : IMsiClawAddonPresentation
{
    private readonly CanonicalViiperRuntime? _viiper;
    private readonly Func<CanonicalViiperRuntime, ICanonicalSteamDeckSession> _deckSessionFactory;
    private readonly Func<IControllerStateSnapshotSource, Func<Xbox360DeviceState, bool>, Action<Exception>, IAddonPresentationPublisher> _xbox360PublisherFactory;
    private readonly Func<IControllerStateSnapshotSource, ICanonicalSteamDeckStateSink, SteamDeckSystemButtonOverlay, Action<Exception>, IAddonPresentationPublisher> _deckPublisherFactory;

    /// <summary>Full1902 production rumble: the one shared physical MSI writer, bound to the same
    /// process-owned PID1902 physical session that feeds this presentation. Null in unit tests and on
    /// a machine where the sink was not composed -- feedback is then simply never armed.</summary>
    private readonly IPhysicalRumbleSink? _rumbleSink;
    /// <summary>The presentation-scoped feedback callback adapter for the CURRENT active presentation,
    /// or null when none is armed. Its lifetime is part of the presentation lifecycle serialized by
    /// <see cref="_gate"/>; it is never a second authority.</summary>
    private IDisposable? _armedFeedback;

    /// <summary>Full1902 A2: the one output-only synthetic Steam/QuickAccess system-button primitive,
    /// shared with the live SteamDeck publisher via <see cref="_deckPublisherFactory"/> so a front
    /// button emits a pulse on the existing continuous publish path -- never a second publication
    /// path or a second VIIPER device. Cleared on every SteamDeck retirement.</summary>
    private readonly SteamDeckSystemButtonOverlay _systemButtonOverlay = new();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _faultLock = new();
    private AddonPresentationKind? _activeKind;
    private IAddonPresentationPublisher? _publisher;
    private ICanonicalSteamDeckSession? _deckSession;
    private Task? _faultCleanup;
    private bool _disposed;
    private bool _overlayPaused;
    // Full1902 Suspend/Resume section 6: one in-memory, never-persisted fact. Live game-facing
    // publication is blocked because the current power cycle entered Suspend and has not been safely
    // released. The existing _gate remains the serialization authority; this is not a second one.
    private bool _suspendPaused;

    internal MsiClawAddonPresentation(
        CanonicalViiperRuntime? viiper,
        IPhysicalRumbleSink? rumbleSink = null,
        Func<CanonicalViiperRuntime, ICanonicalSteamDeckSession>? deckSessionFactory = null,
        Func<IControllerStateSnapshotSource, Func<Xbox360DeviceState, bool>, Action<Exception>, IAddonPresentationPublisher>? xbox360PublisherFactory = null,
        Func<IControllerStateSnapshotSource, ICanonicalSteamDeckStateSink, SteamDeckSystemButtonOverlay, Action<Exception>, IAddonPresentationPublisher>? deckPublisherFactory = null)
    {
        _viiper = viiper;
        _rumbleSink = rumbleSink;
        _deckSessionFactory = deckSessionFactory ?? (runtime => new CanonicalSteamDeckSession(runtime));
        _xbox360PublisherFactory = xbox360PublisherFactory
            ?? ((source, setState, fault) => new PublisherAdapter(new CanonicalXbox360InputPublisher(source, setState, fault: fault)));
        _deckPublisherFactory = deckPublisherFactory
            ?? ((source, sink, overlay, fault) => new PublisherAdapter(new CanonicalSteamDeckInputPublisher(source, sink, fault: fault, systemButtonOverlay: overlay)));
    }

    /// <summary>The canonical VIIPER runtime state, or <see langword="null"/> if VIIPER could not be
    /// loaded/initialized at all. A new PR5 physical takeover is allowed only when this is
    /// <see cref="CanonicalViiperRuntimeState.Ready"/>.</summary>
    internal CanonicalViiperRuntimeState? ViiperState => _viiper?.State;

    /// <summary>Lock-free read (matches the existing internal accessor): a torn read during a switch
    /// at worst makes one queued gesture pick the other mapping domain, which the next press corrects.</summary>
    public AddonPresentationKind? ActivePresentation => _activeKind;

    internal bool IsOverlayPaused => _overlayPaused;

    public bool IsSuspendPaused => _suspendPaused;

    public bool TryRequestSteamPulse() => TryRequestSystemButtonPulse(_systemButtonOverlay.RequestSteamPulse);

    public bool TryRequestQuickAccessPulse() => TryRequestSystemButtonPulse(_systemButtonOverlay.RequestQuickAccessPulse);

    /// <summary>Full1902 A2 section 4.2: eligibility-gated, non-blocking synthetic system-button
    /// pulse. Acquire the owner gate without waiting (a press during an attach/switch/retire is
    /// simply dropped, no queue), then merge the pulse into the shared overlay only on a healthy
    /// live SteamDeck publication.</summary>
    private bool TryRequestSystemButtonPulse(Action requestPulse)
    {
        if (!_gate.Wait(0)) return false;
        try
        {
            if (_disposed) return false;
            if (_overlayPaused) return false;
            // Full1902 Suspend/Resume section 8.2: a pre-suspend Steam/QuickAccess pulse must never
            // survive Sleep and assert after Resume.
            if (_suspendPaused) return false;
            if (_activeKind != AddonPresentationKind.SteamDeck) return false;
            if (_publisher is not { IsRunning: true }) return false;
            if (_deckSession is not { State: CanonicalSteamDeckSessionState.Active }) return false;
            requestPulse();
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<InitialPresentationResult> AttachInitialAsync(IMsiClawPreparedInputSource source, SteamPresentationSnapshot snapshot, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed) return Fail(null, "Disposed", "OwnerDisposed");
            if (_activeKind is not null) return Fail(_activeKind, "Attach", "AlreadyAttached");
            if (_viiper is not { State: CanonicalViiperRuntimeState.Ready })
                return Fail(null, "ViiperReadiness", "ViiperNotReady:" + (ViiperState?.ToString() ?? "Unavailable"));
            if (source is null || !source.IsRunning)
                return Fail(null, "LiveInputSource", "LiveInputSourceNotRunning");

            var selected = snapshot.WantsSteamDeck ? AddonPresentationKind.SteamDeck : AddonPresentationKind.Xbox360;
            AppLog.Info("ControllerPresentation", "Initial presentation selected.", ("Event", "InitialPresentationSelected"),
                ("RunningAppId", snapshot.RunningAppId), ("BigPictureActive", snapshot.BigPictureActive), ("Selected", selected));

            return selected == AddonPresentationKind.Xbox360
                ? await AttachXbox360Async(source).ConfigureAwait(false)
                : await AttachSteamDeckAsync(source).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<PresentationReconcileResult> ReconcileDesiredPresentationAsync(
        IMsiClawPreparedInputSource source, Func<SteamPresentationSnapshot> captureSnapshot, CancellationToken cancellationToken)
    {
        // Honor caller/shutdown cancellation only BEFORE the first presentation mutation.
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed)
                return Blocked("OwnerDisposed");
            // OQ4 section 5.4: while Overlay capture is paused the current presentation stays
            // attached + neutral. No attach, no detach, no publisher restart. The Runtime requests
            // one normal reconcile with fresh Steam/BPM facts AFTER capture ends.
            if (_overlayPaused)
                return Blocked("OverlayCaptureActive");
            // Full1902 Suspend/Resume section 8.1: no attach/detach/publisher restart until the
            // suspend pause is explicitly released by ResumeAfterSuspendAsync.
            if (_suspendPaused)
                return Blocked("SuspendPaused");
            if (_viiper is not { State: CanonicalViiperRuntimeState.Ready })
                return Blocked("ViiperNotReady:" + (ViiperState?.ToString() ?? "Unavailable"));
            if (source is null || !source.IsRunning)
                return Blocked("LiveInputSourceNotRunning");

            // Fresh raw fact at the actual mutation boundary (section 18): a switch queued behind an
            // earlier one must converge to the state that is current now, not when its event fired.
            var snapshot = captureSnapshot();
            var desired = snapshot.WantsSteamDeck ? AddonPresentationKind.SteamDeck : AddonPresentationKind.Xbox360;

            if (_activeKind == desired && _publisher is { IsRunning: true })
            {
                AppLog.Debug("ControllerPresentation", "Runtime presentation reconcile: no change.", ("Event", "PresentationReconcileNoChange"),
                    ("RunningAppId", snapshot.RunningAppId), ("BigPictureActive", snapshot.BigPictureActive), ("CurrentPresentation", desired));
                return new(PresentationReconcileOutcome.NoChange, desired, "AlreadyDesired");
            }

            var previous = _activeKind;
            AppLog.Info("ControllerPresentation", "Runtime presentation switch started.", ("Event", "PresentationSwitchStarted"),
                ("RunningAppId", snapshot.RunningAppId), ("BigPictureActive", snapshot.BigPictureActive),
                ("PreviousPresentation", previous?.ToString() ?? "None"), ("DesiredPresentation", desired));

            if (previous is not null && !await RetireActivePresentationCoreAsync("SwitchTo:" + desired).ConfigureAwait(false))
            {
                // Hard cleanup barrier -- the current presentation could not be proven retired. Do
                // NOT attach the target; ownership evidence is retained.
                return new(PresentationReconcileOutcome.Failed, previous, "RetireCurrentPresentationFailed");
            }

            var attach = desired == AddonPresentationKind.Xbox360
                ? await AttachXbox360Async(source).ConfigureAwait(false)
                : await AttachSteamDeckAsync(source).ConfigureAwait(false);
            if (!attach.Succeeded)
            {
                // The previous presentation (if any) is already safely retired. No fallback / rollback
                // to it or to any alternate presentation (section 15.3); both typed devices stay
                // detached and a later real Steam/BPM event may reconcile again.
                AppLog.Warn("ControllerPresentation", "Runtime presentation switch failed at target attach.", null,
                    ("Event", "PresentationSwitchFailed"), ("DesiredPresentation", desired), ("Reason", attach.Reason));
                return new(PresentationReconcileOutcome.Failed, null, "TargetAttachFailed:" + attach.Reason);
            }

            AppLog.Info("ControllerPresentation", "Runtime presentation switch completed.", ("Event", "PresentationSwitchCompleted"),
                ("CurrentPresentation", desired), ("PreviousPresentation", previous?.ToString() ?? "None"));
            return new(previous is null ? PresentationReconcileOutcome.Attached : PresentationReconcileOutcome.Switched, desired, "Reconciled");
        }
        finally { _gate.Release(); }
    }

    private async Task<InitialPresentationResult> AttachXbox360Async(IMsiClawPreparedInputSource source)
    {
        var runtime = _viiper!;
        if (!runtime.TryGetXbox360AttachmentState(out var state) || state != USBDeviceAttachmentState.Detached)
            return Fail(AddonPresentationKind.Xbox360, "Xbox360Preattach", "Xbox360NotDetached:" + state);

        if (runtime.AttachXbox360() != USBDeviceAttachResult.Success)
            return Fail(AddonPresentationKind.Xbox360, "Xbox360Attach", "Xbox360AttachNotSuccess");

        if (!runtime.SetXbox360State(default))
        {
            var detach = runtime.DetachXbox360();
            return Fail(AddonPresentationKind.Xbox360, "Xbox360Neutral", "Xbox360NeutralRejected:Detach=" + detach);
        }

        var publisher = _xbox360PublisherFactory(source, runtime.SetXbox360State, OnPublisherFault);
        try
        {
            publisher.Start();
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerPresentation", "Xbox360 publisher start threw; detaching.", exception);
            var detach = runtime.DetachXbox360();
            return Fail(AddonPresentationKind.Xbox360, "Xbox360PublisherStart", "Xbox360PublisherStartThrew:Detach=" + detach);
        }

        _publisher = publisher;
        _activeKind = AddonPresentationKind.Xbox360;
        // Feedback is layered on top of a committed, healthy controller presentation: a callback
        // registration failure leaves rumble unavailable for this presentation, never tears down input.
        ArmFeedbackForActivePresentationLocked();
        AppLog.Info("ControllerPresentation", "Initial presentation attached.", ("Event", "InitialPresentationAttached"),
            ("Presentation", AddonPresentationKind.Xbox360), ("PublisherStarted", true));
        await Task.CompletedTask.ConfigureAwait(false);
        return new(true, AddonPresentationKind.Xbox360, "Attached");
    }

    private async Task<InitialPresentationResult> AttachSteamDeckAsync(IMsiClawPreparedInputSource source)
    {
        var session = _deckSessionFactory(_viiper!);
        if (!session.Start() || session.State != CanonicalSteamDeckSessionState.Active)
        {
            session.Dispose();
            return Fail(AddonPresentationKind.SteamDeck, "SteamDeckAttach", "SteamDeckSessionNotActive:" + session.State);
        }

        if (!session.SetNeutral())
        {
            var detached = session.DetachDevice();
            _deckSession = detached ? null : session;
            return Fail(AddonPresentationKind.SteamDeck, "SteamDeckNeutral", "SteamDeckNeutralRejected:Detached=" + detached);
        }

        var publisher = _deckPublisherFactory(source, session, _systemButtonOverlay, OnPublisherFault);
        try
        {
            publisher.Start();
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerPresentation", "Steam Deck publisher start threw; detaching.", exception);
            var detached = session.DetachDevice();
            _deckSession = detached ? null : session;
            return Fail(AddonPresentationKind.SteamDeck, "SteamDeckPublisherStart", "SteamDeckPublisherStartThrew:Detached=" + detached);
        }

        _deckSession = session;
        _publisher = publisher;
        _activeKind = AddonPresentationKind.SteamDeck;
        ArmFeedbackForActivePresentationLocked();
        AppLog.Info("ControllerPresentation", "Initial presentation attached.", ("Event", "InitialPresentationAttached"),
            ("Presentation", AddonPresentationKind.SteamDeck), ("PublisherStarted", true));
        await Task.CompletedTask.ConfigureAwait(false);
        return new(true, AddonPresentationKind.SteamDeck, "Attached");
    }

    // ---- publisher runtime fault: async fail-close only, no self-join, no re-attach, no PID touch ----

    // ---- production rumble feedback lifetime (part of the presentation lifecycle, _gate held) ----

    /// <summary>Arms the presentation-scoped feedback callback for the CURRENT active presentation.
    /// No-op when there is no shared sink or feedback is already armed. A registration failure logs
    /// and leaves <see cref="_armedFeedback"/> null -- the presentation stays healthy.</summary>
    private void ArmFeedbackForActivePresentationLocked()
    {
        if (_rumbleSink is null || _armedFeedback is not null) return;
        _armedFeedback = _activeKind switch
        {
            AddonPresentationKind.Xbox360 =>
                Xbox360RumbleFeedbackBridge.TryArm(_rumbleSink, cb => _viiper!.SetXbox360RumbleCallback(cb)),
            AddonPresentationKind.SteamDeck when _deckSession is { } session =>
                SteamDeckRumbleFeedbackAdapter.TryArm(_rumbleSink, session.SetOutputCallback, session.ClearOutputCallback),
            _ => null,
        };
    }

    /// <summary>Clears the native feedback callback and requests a best-effort physical STOP, so a
    /// retired/paused presentation can never leave a motor latched. Ordering: the caller has already
    /// stopped/joined the publisher; <c>armed.Dispose()</c> clears the native registration, cancels
    /// the SteamDeck dead-man stop, and DRAINS any callback still inside its physical write, so the
    /// STOP written below is guaranteed to be the final physical write.</summary>
    private void DisarmFeedbackAndStopLocked(string reason)
    {
        var armed = _armedFeedback;
        _armedFeedback = null;
        if (armed is not null)
        {
            try { armed.Dispose(); }
            catch (Exception exception)
            {
                AppLog.Warn("Rumble", "Production rumble feedback disarm threw.", exception, ("Reason", reason));
            }
        }

        if (_rumbleSink is null) return;
        try
        {
            var result = _rumbleSink.SetRumble(TwoMotorRumble.Stopped);
            if (result.Status is PhysicalRumbleWriteStatus.Failed or PhysicalRumbleWriteStatus.Disposed)
                AppLog.Debug("Rumble", "Production rumble STOP was not confirmed.",
                    ("Event", "ProductionRumbleStopFailed"), ("Reason", reason), ("Status", result.Status));
        }
        catch (Exception exception)
        {
            AppLog.Warn("Rumble", "Production rumble STOP threw.", exception,
                ("Event", "ProductionRumbleStopFailed"), ("Reason", reason));
        }
    }

    private void OnPublisherFault(Exception exception)
    {
        AppLog.Warn("ControllerPresentation", "Presentation publisher faulted; scheduling fail-close.", exception,
            ("Event", "PresentationFaulted"), ("Presentation", _activeKind?.ToString() ?? "None"));
        lock (_faultLock)
        {
            _faultCleanup ??= Task.Run(() => RetireAsync("PublisherFault"));
        }
    }

    public Task<bool> ReleaseForCenterMEnableAsync(CancellationToken cancellationToken) => RetireAsync("CenterMEnable");

    public async Task<OverlayPauseResult> PauseForOverlayAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed) return PauseBlocked("OwnerDisposed");
            if (_overlayPaused) return new(OverlayPauseOutcome.Paused, "AlreadyPaused");
            // Full1902 Suspend/Resume section 8.3: suspend already owns a neutral pause. The publisher
            // is already stopped and the device already neutral, so an Overlay capture request must
            // not run its own publication transition on top -- the visible Overlay still opens (the
            // host handles the surface), it just does not touch game-facing publication.
            if (_suspendPaused) return PauseBlocked("SuspendPaused");
            if (_activeKind is not { } kind || _publisher is not { } publisher)
                return PauseBlocked("NoActivePresentation");
            if (!publisher.IsRunning)
                return PauseBlocked("PublisherNotRunning");
            if (kind == AddonPresentationKind.SteamDeck && _deckSession is not { State: CanonicalSteamDeckSessionState.Active })
                return PauseBlocked("SteamDeckSessionNotActive:" + (_deckSession?.State.ToString() ?? "None"));

            // 1. Stop + prove the publisher joined. Never write neutral underneath a possibly-live
            //    publisher; never detach here.
            try
            {
                await publisher.StopAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                AppLog.Warn("OverlayCapture", "Presentation publisher could not be stopped for Overlay; presentation stays live.", exception,
                    ("Event", "OverlayPauseFailed"), ("Presentation", kind), ("Reason", "PublisherStopThrew"));
                return new(OverlayPauseOutcome.PublisherNotStopped, "PublisherStopThrew");
            }
            if (publisher.IsRunning)
            {
                AppLog.Warn("OverlayCapture", "Presentation publisher still running after StopAsync; presentation stays live.", null,
                    ("Event", "OverlayPauseFailed"), ("Presentation", kind), ("Reason", "PublisherStillRunning"));
                return new(OverlayPauseOutcome.PublisherNotStopped, "PublisherStillRunning");
            }

            // 1b. Clear the feedback callback and request a physical STOP so opening the Overlay can
            //     never leave a pre-existing vibration latched. Resume re-arms the SAME presentation.
            DisarmFeedbackAndStopLocked("OverlayPause");

            // 2. Write the SAME attached device neutral. A rejected neutral write on a proven-stopped
            //    publisher is a real output-safety failure: fail-close the current presentation
            //    through the existing owner. No alternate presentation fallback.
            var neutral = kind == AddonPresentationKind.Xbox360
                ? _viiper!.SetXbox360State(default)
                : _deckSession!.SetNeutral();
            if (!neutral)
            {
                AppLog.Error("OverlayCapture", "Neutral write rejected on a stopped publisher; retiring the current presentation.", null,
                    ("Event", "OverlayPauseNeutralRejected"), ("Presentation", kind));
                if (!await RetireActivePresentationCoreAsync("OverlayPauseNeutralRejected").ConfigureAwait(false))
                {
                    AppLog.Error("OverlayCapture", "Presentation could not be proven retired after Overlay neutral rejection; ownership retained.", null,
                        ("Event", "OverlayPauseFailCloseIncomplete"), ("Presentation", kind));
                    return new(OverlayPauseOutcome.NeutralRejectedRetireFailed, "NeutralRejectedRetireFailed");
                }
                return new(OverlayPauseOutcome.NeutralRejectedPresentationRetired, "NeutralRejected");
            }

            _overlayPaused = true;
            AppLog.Info("OverlayCapture", "Presentation paused neutral.", ("Event", "OverlayPausePaused"), ("Presentation", kind));
            return new(OverlayPauseOutcome.Paused, "Paused");
        }
        finally { _gate.Release(); }
    }

    public async Task<OverlayResumeResult> ResumeAfterOverlayAsync(IMsiClawPreparedInputSource? source, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_overlayPaused)
                return new(OverlayResumeOutcome.NotPaused, "NotPaused");

            // Ending capture always clears the pause fact so normal recovery / reconcile is no
            // longer blocked, even when the same presentation cannot be resumed.
            _overlayPaused = false;

            // Full1902 Suspend/Resume section 10.5: if a suspend pause is still active, it stays
            // authoritative for neutral output. Ending the Overlay pause must not restart game-facing
            // publication -- the later PowerResume release path (ResumeAfterSuspendAsync) owns that.
            if (_suspendPaused)
                return LeftNeutral("SuspendPaused");

            if (_disposed || _activeKind is not { } kind || _publisher is not { } publisher)
                return LeftNeutral("NoActivePresentation");
            if (source is null || !source.IsRunning)
                return LeftNeutral("SourceNotRunning");
            if (_viiper is not { State: CanonicalViiperRuntimeState.Ready })
                return LeftNeutral("ViiperNotReady:" + (ViiperState?.ToString() ?? "Unavailable"));

            // VIIPER Ready / session Active are not proof the typed USB device is still attached
            // (sleep/resume or PnP disruption can drop it while the Overlay is open). Prove it with
            // the same narrow attachment query the structural reconcile path uses before restarting
            // the publisher against a possibly-detached device.
            if (kind == AddonPresentationKind.Xbox360)
            {
                if (!_viiper!.TryGetXbox360AttachmentState(out var attachment) || attachment != USBDeviceAttachmentState.Attached)
                    return LeftNeutral("Xbox360AttachmentNotAttached:" + attachment);
            }
            else
            {
                if (_deckSession is not { State: CanonicalSteamDeckSessionState.Active } session)
                    return LeftNeutral("SteamDeckSessionNotActive:" + (_deckSession?.State.ToString() ?? "None"));
                if (!session.TryGetTrackedAttachmentState(out var attachment) || attachment != USBDeviceAttachmentState.Attached)
                    return LeftNeutral("SteamDeckAttachmentNotAttached:" + attachment);
            }

            try
            {
                publisher.Start();
            }
            catch (Exception exception)
            {
                AppLog.Error("OverlayCapture", "Publisher restart threw after Overlay; leaving output neutral.", exception,
                    ("Event", "OverlayResumeFailed"), ("Presentation", kind));
                return LeftNeutral("PublisherStartThrew");
            }

            // Re-arm feedback for the SAME still-active presentation now that the publisher is live.
            ArmFeedbackForActivePresentationLocked();
            AppLog.Info("OverlayCapture", "Same presentation resumed.", ("Event", "OverlayResumeResumed"), ("Presentation", kind));
            return new(OverlayResumeOutcome.Resumed, "Resumed");
        }
        finally { _gate.Release(); }
    }

    // ---- Full1902 Suspend/Resume: power-suspend neutral presentation (work order sections 7 / 10) ----

    public async Task<SuspendPauseResult> PauseForSuspendAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed) return SuspendPauseBlocked("OwnerDisposed");

            // Section 7.1 step 1: record the barrier first. Even when the rest is a no-op this blocks
            // a queued Steam/BPM event from attaching a live presentation after the suspend barrier.
            var wasAlreadyPaused = _suspendPaused;
            _suspendPaused = true;

            // Section 7.2: the ACTUAL empty state -> no native work. "_publisher is null" alone is not
            // proof there is nothing attached: RetireActivePresentationCoreAsync clears _publisher (a
            // proven-stopped publisher) BEFORE it attempts the canonical neutral+detach, and keeps
            // _activeKind set when that detach is not proven (e.g. DetachXbox360 RetryableFailure).
            // Review #490: only the true empty pair may certify PausedNoPresentation/Safe=true, and
            // even then only once residual typed-device ownership is structurally ruled out.
            // AttachXbox360Async / AttachSteamDeckAsync never commit _activeKind/_publisher until
            // AFTER neutral is proven, so a rejected initial neutral write followed by a failed
            // cleanup detach can leave a residual attached (non-neutral-proven) device while both
            // managed fields stay null.
            if (_activeKind is null && _publisher is null)
            {
                if (!TryProveNoResidualPresentationLocked(out var residualReason))
                {
                    AppLog.Error("ControllerPresentation", "Presentation suspend pause: residual typed-device ownership evidence.", null,
                        ("Event", "PresentationSuspendPauseFailed"), ("Reason", residualReason));
                    return new(SuspendPauseOutcome.Blocked, residualReason);
                }

                AppLog.Info("ControllerPresentation", "Presentation suspend pause: no active presentation.",
                    ("Event", "PresentationSuspendPausedNeutral"), ("Presentation", "None"), ("PublisherWasRunning", false), ("OverlayPaused", _overlayPaused));
                return new(SuspendPauseOutcome.PausedNoPresentation, wasAlreadyPaused ? "AlreadyPaused" : "NoActivePresentation");
            }

            // The inverse is an impossible state (a publisher with no active kind) -- never certify
            // Suspend safe over it.
            if (_activeKind is not { } kind)
            {
                AppLog.Error("ControllerPresentation", "Presentation suspend pause: publisher present without an active presentation kind.", null,
                    ("Event", "PresentationSuspendPauseFailed"), ("Reason", "InconsistentPresentationState"));
                return new(SuspendPauseOutcome.Blocked, "InconsistentPresentationState");
            }

            // A retained active kind with a null publisher means a prior retire already proved the
            // publisher stopped but could not prove the canonical neutral+detach -- Suspend must still
            // re-run the pulse-clear / rumble-stop / SAME-device neutral path below, because the
            // attached device may still be holding the last non-neutral report.
            var publisher = _publisher;
            var publisherWasRunning = publisher?.IsRunning == true;
            AppLog.Info("ControllerPresentation", "Presentation suspend pause started.",
                ("Event", "PresentationSuspendPauseStarted"), ("Presentation", kind), ("PublisherWasRunning", publisherWasRunning), ("OverlayPaused", _overlayPaused));

            // 1. Stop + prove the publisher joined. Never write neutral underneath a possibly-live
            //    publisher; never detach here (sections 7.1 / 7.3).
            if (publisherWasRunning)
            {
                try
                {
                    await publisher!.StopAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    AppLog.Warn("ControllerPresentation", "Presentation publisher could not be stopped for Suspend; pause stays unsafe.", exception,
                        ("Event", "PresentationSuspendPauseFailed"), ("Presentation", kind), ("Reason", "PublisherStopThrew"));
                    return new(SuspendPauseOutcome.PublisherNotStopped, "PublisherStopThrew");
                }
                if (publisher!.IsRunning)
                {
                    AppLog.Warn("ControllerPresentation", "Presentation publisher still running after StopAsync for Suspend; pause stays unsafe.", null,
                        ("Event", "PresentationSuspendPauseFailed"), ("Presentation", kind), ("Reason", "PublisherStillRunning"));
                    return new(SuspendPauseOutcome.PublisherNotStopped, "PublisherStillRunning");
                }
            }

            // 2. Clear any pending/active synthetic Steam/QuickAccess pulse (section 7.5).
            _systemButtonOverlay.Clear();

            // 3-6. Clear the feedback callback, DRAIN any in-progress physical rumble write, then
            //       request a final physical STOP (sections 7.1 / 12). Reuses the #488 helper.
            DisarmFeedbackAndStopLocked("Suspend");

            // 7. Write the SAME attached device neutral. A rejected write on a proven-stopped
            //    publisher is a real output-safety failure: fail-close the current presentation
            //    through the existing owner, no alternate-presentation fallback (section 7.4). A
            //    retained SteamDeck kind with no session left to write through is equally unsafe.
            bool neutral;
            if (kind == AddonPresentationKind.Xbox360)
            {
                neutral = _viiper!.SetXbox360State(default);
            }
            else if (_deckSession is { } deckSession)
            {
                neutral = deckSession.SetNeutral();
            }
            else
            {
                AppLog.Error("ControllerPresentation", "SteamDeck suspend pause has no session to write neutral through; pause stays unsafe.", null,
                    ("Event", "PresentationSuspendPauseFailed"), ("Presentation", kind), ("Reason", "SteamDeckSessionMissing"));
                return new(SuspendPauseOutcome.NeutralRejectedRetireFailed, "SteamDeckSessionMissing");
            }
            if (!neutral)
            {
                AppLog.Error("ControllerPresentation", "Neutral write rejected on a stopped publisher during Suspend; retiring the current presentation.", null,
                    ("Event", "PresentationSuspendPauseFailed"), ("Presentation", kind), ("Reason", "NeutralRejected"));
                if (!await RetireActivePresentationCoreAsync("SuspendNeutralRejected").ConfigureAwait(false))
                    return new(SuspendPauseOutcome.NeutralRejectedRetireFailed, "NeutralRejectedRetireFailed");
                return new(SuspendPauseOutcome.NeutralRejectedPresentationRetired, "NeutralRejected");
            }

            // 8. Keep the typed device attached (section 7.1 step 8) -- a healthy device is not
            //    detached/recreated merely because Windows is going to sleep.
            AppLog.Info("ControllerPresentation", "Presentation suspend paused neutral.",
                ("Event", "PresentationSuspendPausedNeutral"), ("Presentation", kind), ("PublisherWasRunning", publisherWasRunning));
            return new(SuspendPauseOutcome.Paused, "Paused");
        }
        finally { _gate.Release(); }
    }

    public async Task<SuspendResumeResult> ResumeAfterSuspendAsync(
        IMsiClawPreparedInputSource? source, Func<SteamPresentationSnapshot> captureSnapshot, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_suspendPaused)
                return new(SuspendResumeOutcome.NotPaused, "NotPaused");

            // Section 10.1: the physical source is not available yet -> keep the suspend pause and
            // neutral/stopped output. The existing PR8/PR10 recovery repairs the source and re-enters
            // this release path through the "PhysicalInputRecovered" reconcile.
            if (source is null || !source.IsRunning)
            {
                AppLog.Info("ControllerPresentation", "Presentation resume deferred; physical source unavailable.",
                    ("Event", "PresentationResumeDeferredSourceUnavailable"), ("Presentation", _activeKind?.ToString() ?? "None"));
                return new(SuspendResumeOutcome.DeferredSourceUnavailable, "SourceUnavailable");
            }

            AppLog.Info("ControllerPresentation", "Presentation resume requested.",
                ("Event", "PresentationResumeRequested"), ("Presentation", _activeKind?.ToString() ?? "None"), ("OverlayPaused", _overlayPaused));

            // Section 10.5: Overlay capture owns its own neutral pause. Suspend safety is established
            // here, so clear the suspend fact, but leave game-facing publication stopped -- the later
            // ResumeAfterOverlayAsync is responsible for ending the visible Overlay session.
            if (_overlayPaused)
            {
                _suspendPaused = false;
                AppLog.Info("ControllerPresentation", "Presentation suspend pause released; Overlay capture still owns neutral pause.",
                    ("Event", "PresentationResumeLeftNeutral"), ("Reason", "OverlayPaused"));
                return new(SuspendResumeOutcome.LeftNeutral, "OverlayPaused");
            }

            if (_disposed)
            {
                _suspendPaused = false;
                return new(SuspendResumeOutcome.LeftNeutral, "OwnerDisposed");
            }

            // Section 10.2: capture the fresh Steam/BPM desired kind at the actual resume boundary --
            // never the pre-sleep desired presentation.
            var desired = captureSnapshot().WantsSteamDeck ? AddonPresentationKind.SteamDeck : AddonPresentationKind.Xbox360;

            // Section 7.2 pause case: the ACTUAL empty state -> nothing to resume. Review #490 (3rd
            // pass): apply the SAME structural residual-attachment proof PauseForSuspendAsync requires
            // before releasing the pause -- a rejected initial neutral write + failed cleanup detach
            // can leave a residual attached device while both managed fields stay null, and that
            // residual ownership must stay fail-closed across Resume too, not just across Suspend.
            if (_activeKind is null && _publisher is null)
            {
                if (!TryProveNoResidualPresentationLocked(out var residualReason))
                {
                    AppLog.Warn("ControllerPresentation", "Presentation suspend pause retained on Resume; residual typed-device ownership evidence unresolved.", null,
                        ("Event", "PresentationResumeDeferredUnsafePresentation"), ("Reason", residualReason));
                    return new(SuspendResumeOutcome.DeferredUnsafePresentation, residualReason);
                }

                _suspendPaused = false;
                AppLog.Info("ControllerPresentation", "Presentation suspend pause released; no attached presentation, reconcile required.",
                    ("Event", "PresentationResumeReconcileRequired"), ("DesiredPresentation", desired));
                return new(SuspendResumeOutcome.ReconcileRequired, "NoActivePresentation");
            }

            // The inverse impossible state (a publisher with no active kind) is never resumed through.
            if (_activeKind is not { } kind)
            {
                AppLog.Warn("ControllerPresentation", "Presentation suspend pause retained on Resume; publisher present without an active presentation kind.", null,
                    ("Event", "PresentationResumeDeferredUnsafePresentation"), ("Reason", "InconsistentPresentationState"));
                return new(SuspendResumeOutcome.DeferredUnsafePresentation, "InconsistentPresentationState");
            }

            // A retained active kind with a null publisher (PauseForSuspendAsync already proved this
            // device neutral before Sleep, per review #490 1st pass) has no publisher object to
            // restart -- release the pause and let the existing PR7 reconcile retry detach/re-attach.
            if (_publisher is not { } publisher)
            {
                _suspendPaused = false;
                AppLog.Info("ControllerPresentation", "Presentation suspend pause released; no publisher to restart, reconcile required.",
                    ("Event", "PresentationResumeReconcileRequired"), ("ActivePresentation", kind), ("DesiredPresentation", desired));
                return new(SuspendResumeOutcome.ReconcileRequired, "PublisherNotAttached");
            }

            // Section 10.4: desired kind changed during Sleep -> do not briefly restart the old
            // publisher; leave it stopped + neutral and let the existing PR7 switch do the transition.
            if (desired != kind)
            {
                _suspendPaused = false;
                AppLog.Info("ControllerPresentation", "Presentation suspend pause released; desired presentation changed, PR7 switch required.",
                    ("Event", "PresentationResumeReconcileRequired"), ("ActivePresentation", kind), ("DesiredPresentation", desired));
                return new(SuspendResumeOutcome.ReconcileRequired, "DesiredKindChanged");
            }

            // Section 10.6: the typed device must still be proven structurally attached before the
            // publisher is restarted against it -- otherwise clear the pause and let the existing
            // reconcile / fail-close policy run. No sleep-only attachment repair here.
            if (_viiper is not { State: CanonicalViiperRuntimeState.Ready })
            {
                _suspendPaused = false;
                return SuspendResumeReconcile("ViiperNotReady:" + (ViiperState?.ToString() ?? "Unavailable"), kind);
            }
            if (kind == AddonPresentationKind.Xbox360)
            {
                if (!_viiper!.TryGetXbox360AttachmentState(out var attachment) || attachment != USBDeviceAttachmentState.Attached)
                {
                    _suspendPaused = false;
                    return SuspendResumeReconcile("Xbox360AttachmentNotAttached:" + attachment, kind);
                }
            }
            else
            {
                if (_deckSession is not { State: CanonicalSteamDeckSessionState.Active } session)
                {
                    _suspendPaused = false;
                    return SuspendResumeReconcile("SteamDeckSessionNotActive:" + (_deckSession?.State.ToString() ?? "None"), kind);
                }
                if (!session.TryGetTrackedAttachmentState(out var attachment) || attachment != USBDeviceAttachmentState.Attached)
                {
                    _suspendPaused = false;
                    return SuspendResumeReconcile("SteamDeckAttachmentNotAttached:" + attachment, kind);
                }
            }

            // Section 10.3: same kind + healthy source + Ready VIIPER + attached device + no Overlay
            // pause -> restart the SAME publisher object, re-arm feedback for the SAME presentation.
            // No detach, no attach, no VIIPER recreation.
            try
            {
                publisher.Start();
            }
            catch (Exception exception)
            {
                _suspendPaused = false;
                AppLog.Error("ControllerPresentation", "Publisher restart threw after Suspend; leaving output neutral for PR7 reconcile.", exception,
                    ("Event", "PresentationResumeReconcileRequired"), ("Presentation", kind));
                return new(SuspendResumeOutcome.ReconcileRequired, "PublisherStartThrew");
            }

            ArmFeedbackForActivePresentationLocked();
            _suspendPaused = false;
            AppLog.Info("ControllerPresentation", "Presentation resume restarted the same publisher.",
                ("Event", "PresentationResumeSamePublisher"), ("Presentation", kind));
            return new(SuspendResumeOutcome.SamePublisherResumed, "SamePublisher");
        }
        finally { _gate.Release(); }
    }

    /// <summary>Review #490 (3rd pass): the shared structural proof that no residual typed-device
    /// attachment/ownership evidence survives when both <see cref="_activeKind"/> and
    /// <see cref="_publisher"/> are null. A rejected INITIAL neutral write followed by a failed
    /// cleanup detach (in <c>AttachXbox360Async</c> / <c>AttachSteamDeckAsync</c>) never commits
    /// either managed field, so their emptiness alone is not proof of "nothing attached". Shared by
    /// <see cref="PauseForSuspendAsync"/> and the empty-state branch of
    /// <see cref="ResumeAfterSuspendAsync"/> so Suspend and Resume apply the identical fail-close
    /// rule. Assumes <see cref="_gate"/> is already held.</summary>
    private bool TryProveNoResidualPresentationLocked(out string reason)
    {
        // A retained Deck session is explicit residual ownership evidence from a failed detach.
        if (_deckSession is not null)
        {
            reason = "ResidualSteamDeckSession";
            return false;
        }

        // X360 has no separate managed session field, so prove the canonical device is detached.
        if (_viiper is { State: CanonicalViiperRuntimeState.Ready } runtime)
        {
            if (!runtime.TryGetXbox360AttachmentState(out var attachment) || attachment != USBDeviceAttachmentState.Detached)
            {
                reason = "ResidualXbox360Attachment:" + attachment;
                return false;
            }
        }
        else if (_viiper is { State: CanonicalViiperRuntimeState.Unsafe })
        {
            reason = "ViiperUnsafe";
            return false;
        }

        reason = "None";
        return true;
    }

    private static SuspendPauseResult SuspendPauseBlocked(string reason)
    {
        AppLog.Info("ControllerPresentation", "Presentation suspend pause not attempted.",
            ("Event", "PresentationSuspendPauseBlocked"), ("Reason", reason));
        return new(SuspendPauseOutcome.Blocked, reason);
    }

    private static SuspendResumeResult SuspendResumeReconcile(string reason, AddonPresentationKind kind)
    {
        AppLog.Warn("ControllerPresentation", "Presentation suspend pause released; structural attachment not proven, reconcile required.", null,
            ("Event", "PresentationResumeReconcileRequired"), ("Presentation", kind), ("Reason", reason));
        return new(SuspendResumeOutcome.ReconcileRequired, reason);
    }

    /// <summary>Retire ONLY the current active presentation (stop/join publisher -&gt; canonical
    /// detach primitive -&gt; clear managed fields). The canonical VIIPER runtime (server / bus / both
    /// typed device objects) stays alive and Ready -- this is the PR7 X360 &lt;-&gt; Deck switch step.
    /// Assumes <see cref="_gate"/> is already held; never reacquires it (work order PR7 section 14).</summary>
    private async Task<bool> RetireActivePresentationCoreAsync(string reason)
    {
        // 1. Stop + JOIN the publisher. A join failure is a hard barrier: never detach a device
        //    underneath a possibly-live publisher (section 13/15.1).
        if (_publisher is { } publisher)
        {
            try
            {
                await publisher.StopAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                AppLog.Error("ControllerPresentation", "Presentation publisher could not be joined; ownership retained.", exception, ("Reason", reason));
                return false;
            }
            if (publisher.IsRunning)
            {
                AppLog.Error("ControllerPresentation", "Presentation publisher still running after StopAsync; ownership retained.", null, ("Reason", reason));
                return false;
            }
            _publisher = null;
        }

        // Full1902 A2 section 4.3: clear any pending/active synthetic system-button pulse while the
        // publisher is retired so an old front-button pulse can never survive into a later new Deck
        // publisher (the overlay instance is shared across every SteamDeck publication).
        _systemButtonOverlay.Clear();

        // 2-3. Stop accepting old-presentation feedback (clear the native callback) and request a
        //      best-effort physical STOP before the typed device is detached, so a switch/release/
        //      shutdown/fail-close can never leave a motor latched.
        DisarmFeedbackAndStopLocked(reason);

        // 4. Detach the selected typed device (the runtime/session detach primitive writes neutral first).
        if (_activeKind == AddonPresentationKind.Xbox360)
        {
            var detach = _viiper!.DetachXbox360();
            if (detach != USBDeviceDetachResult.Success)
            {
                AppLog.Error("ControllerPresentation", "Xbox360 detach did not succeed; ownership retained.", null, ("Result", detach), ("Reason", reason));
                return false;
            }
        }
        else if (_activeKind == AddonPresentationKind.SteamDeck || _deckSession is not null)
        {
            if (_deckSession is { } session && session.State is CanonicalSteamDeckSessionState.Active or CanonicalSteamDeckSessionState.CleanupPending)
            {
                if (!session.DetachDevice())
                {
                    AppLog.Error("ControllerPresentation", "Steam Deck detach did not succeed; ownership retained.", null, ("State", session.State), ("Reason", reason));
                    return false;
                }
            }
            _deckSession?.Dispose();
            _deckSession = null;
        }
        _activeKind = null;
        // OQ4 section 5.6: an explicit authority release / teardown that retires the presentation
        // also clears any Overlay pause so the closed state is consistent.
        _overlayPaused = false;
        return true;
    }

    private async Task<bool> RetireAsync(string reason)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!await RetireActivePresentationCoreAsync(reason).ConfigureAwait(false))
                return false;

            // 3. Tear the canonical VIIPER runtime down to its proven-safe closed state.
            if (_viiper is { State: not (CanonicalViiperRuntimeState.Closed or CanonicalViiperRuntimeState.Unsafe) } runtime)
            {
                var teardown = await runtime.TeardownAsync().ConfigureAwait(false);
                if (!teardown || runtime.State != CanonicalViiperRuntimeState.Closed)
                {
                    AppLog.Error("ControllerPresentation", "Canonical VIIPER teardown could not be proven; ownership retained.", null,
                        ("State", runtime.State), ("Reason", reason));
                    return false;
                }
            }
            else if (_viiper is { State: CanonicalViiperRuntimeState.Unsafe })
            {
                AppLog.Error("ControllerPresentation", "Canonical VIIPER is Unsafe; cannot prove teardown.", null, ("Reason", reason));
                return false;
            }

            AppLog.Info("ControllerPresentation", "Presentation released.", ("Event", "PresentationReleased"),
                ("Presentation", "None"), ("ViiperTeardownSucceeded", true), ("Reason", reason));
            return true;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        Task? faultCleanup;
        lock (_faultLock) faultCleanup = _faultCleanup;
        if (faultCleanup is not null)
        {
            try { await faultCleanup.ConfigureAwait(false); }
            catch (Exception exception) { AppLog.Warn("ControllerPresentation", "Fault cleanup task failed during teardown.", exception); }
        }
        // Best-effort controlled teardown: retire the presentation and VIIPER. PID1902 / HidHide are
        // durable and untouched here (section 18). RetireAsync already disarmed feedback + requested
        // a physical STOP through RetireActivePresentationCoreAsync.
        try { await RetireAsync("ProcessTeardown").ConfigureAwait(false); }
        catch (Exception exception) { AppLog.Warn("ControllerPresentation", "Presentation teardown threw during dispose.", exception); }
        // Dispose the shared physical rumble sink (and its HID transport) before the physical owner
        // that backs its identity is disposed -- the host retires the presentation owner first.
        try { (_rumbleSink as IDisposable)?.Dispose(); }
        catch (Exception exception) { AppLog.Warn("ControllerPresentation", "Physical rumble sink dispose threw.", exception); }
        _gate.Dispose();
    }

    private static InitialPresentationResult Fail(AddonPresentationKind? kind, string stage, string reason)
    {
        AppLog.Warn("ControllerPresentation", "Initial presentation failed.", null,
            ("Event", "InitialPresentationFailed"), ("Stage", stage), ("Reason", reason), ("Presentation", kind?.ToString() ?? "None"));
        return new(false, kind, reason);
    }

    private static OverlayPauseResult PauseBlocked(string reason)
    {
        AppLog.Info("OverlayCapture", "Overlay pause not attempted.", ("Event", "OverlayPauseBlocked"), ("Reason", reason));
        return new(OverlayPauseOutcome.Blocked, reason);
    }

    private OverlayResumeResult LeftNeutral(string reason)
    {
        AppLog.Warn("OverlayCapture", "Same presentation could not be resumed after Overlay; output left neutral.", null,
            ("Event", "OverlayResumeLeftNeutral"), ("Presentation", _activeKind?.ToString() ?? "None"), ("Reason", reason));
        return new(OverlayResumeOutcome.LeftNeutral, reason);
    }

    private PresentationReconcileResult Blocked(string reason)
    {
        AppLog.Info("ControllerPresentation", "Runtime presentation reconcile blocked.", ("Event", "PresentationReconcileNoChange"),
            ("CurrentPresentation", _activeKind?.ToString() ?? "None"), ("Reason", reason));
        return new(PresentationReconcileOutcome.Blocked, _activeKind, reason);
    }

    private sealed class PublisherAdapter : IAddonPresentationPublisher
    {
        private readonly CanonicalXbox360InputPublisher? _xbox360;
        private readonly CanonicalSteamDeckInputPublisher? _deck;
        internal PublisherAdapter(CanonicalXbox360InputPublisher xbox360) => _xbox360 = xbox360;
        internal PublisherAdapter(CanonicalSteamDeckInputPublisher deck) => _deck = deck;
        public bool IsRunning => _xbox360?.IsRunning ?? _deck!.IsRunning;
        public void Start() { if (_xbox360 is not null) _xbox360.Start(); else _deck!.Start(); }
        public Task StopAsync() => _xbox360?.StopAsync() ?? _deck!.StopAsync();
    }
}
