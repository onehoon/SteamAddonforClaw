using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal enum AddonPresentationKind { Xbox360, SteamDeck }

/// <summary>The one in-memory PR6 first-attach result. Not persisted.</summary>
internal sealed record InitialPresentationResult(bool Succeeded, AddonPresentationKind? Presentation, string Reason);

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

    /// <summary>The official Center M Enable-and-Restart step: stop/join the publisher, neutral+detach
    /// the selected typed device, then tear the canonical VIIPER runtime down to its closed state.
    /// Must reach a proven-safe state before physical ownership is released to MSI.</summary>
    Task<bool> ReleaseForCenterMEnableAsync(CancellationToken cancellationToken);
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

    private async Task<bool> RetireAsync(string reason)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // 1. Stop + JOIN the publisher. A join failure is a hard barrier: never detach a device
            //    underneath a possibly-live publisher (section 12.3).
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
