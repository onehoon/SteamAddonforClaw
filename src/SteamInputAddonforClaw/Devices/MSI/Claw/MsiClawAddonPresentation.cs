using SteamInputAddonforClaw.Diagnostics;
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
    private readonly Func<IControllerStateSnapshotSource, ICanonicalSteamDeckStateSink, Action<Exception>, IAddonPresentationPublisher> _deckPublisherFactory;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _faultLock = new();
    private AddonPresentationKind? _activeKind;
    private IAddonPresentationPublisher? _publisher;
    private ICanonicalSteamDeckSession? _deckSession;
    private Task? _faultCleanup;
    private bool _disposed;
    private bool _overlayPaused;

    internal MsiClawAddonPresentation(
        CanonicalViiperRuntime? viiper,
        Func<CanonicalViiperRuntime, ICanonicalSteamDeckSession>? deckSessionFactory = null,
        Func<IControllerStateSnapshotSource, Func<Xbox360DeviceState, bool>, Action<Exception>, IAddonPresentationPublisher>? xbox360PublisherFactory = null,
        Func<IControllerStateSnapshotSource, ICanonicalSteamDeckStateSink, Action<Exception>, IAddonPresentationPublisher>? deckPublisherFactory = null)
    {
        _viiper = viiper;
        _deckSessionFactory = deckSessionFactory ?? (runtime => new CanonicalSteamDeckSession(runtime));
        _xbox360PublisherFactory = xbox360PublisherFactory
            ?? ((source, setState, fault) => new PublisherAdapter(new CanonicalXbox360InputPublisher(source, setState, fault: fault)));
        _deckPublisherFactory = deckPublisherFactory
            ?? ((source, sink, fault) => new PublisherAdapter(new CanonicalSteamDeckInputPublisher(source, sink, fault: fault)));
    }

    /// <summary>The canonical VIIPER runtime state, or <see langword="null"/> if VIIPER could not be
    /// loaded/initialized at all. A new PR5 physical takeover is allowed only when this is
    /// <see cref="CanonicalViiperRuntimeState.Ready"/>.</summary>
    internal CanonicalViiperRuntimeState? ViiperState => _viiper?.State;

    internal AddonPresentationKind? ActivePresentation => _activeKind;

    internal bool IsOverlayPaused => _overlayPaused;

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

        var publisher = _deckPublisherFactory(source, session, OnPublisherFault);
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
        AppLog.Info("ControllerPresentation", "Initial presentation attached.", ("Event", "InitialPresentationAttached"),
            ("Presentation", AddonPresentationKind.SteamDeck), ("PublisherStarted", true));
        await Task.CompletedTask.ConfigureAwait(false);
        return new(true, AddonPresentationKind.SteamDeck, "Attached");
    }

    // ---- publisher runtime fault: async fail-close only, no self-join, no re-attach, no PID touch ----

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

            AppLog.Info("OverlayCapture", "Same presentation resumed.", ("Event", "OverlayResumeResumed"), ("Presentation", kind));
            return new(OverlayResumeOutcome.Resumed, "Resumed");
        }
        finally { _gate.Release(); }
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

        // 2. Detach the selected typed device (the runtime/session detach primitive writes neutral first).
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
        // durable and untouched here (section 18).
        try { await RetireAsync("ProcessTeardown").ConfigureAwait(false); }
        catch (Exception exception) { AppLog.Warn("ControllerPresentation", "Presentation teardown threw during dispose.", exception); }
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
