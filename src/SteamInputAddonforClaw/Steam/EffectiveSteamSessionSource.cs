using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Settings;

namespace SteamInputAddonforClaw.Steam;

public sealed record SteamSessionStateChangedEventArgs(SteamSessionState Previous, SteamSessionState Current);

public sealed class EffectiveSteamSessionSource : IDisposable
{
    private readonly SteamSessionWatcher _watcher;
    private readonly DeveloperTestModeState _testMode;
    private readonly SteamBigPictureWatcher _bigPictureWatcher;
    private readonly ISteamInputRoutingPreference _settings;
    private readonly Lock _sync = new();
    private readonly Lock _publicationGate = new();
    private readonly Queue<SteamSessionStateChangedEventArgs> _pendingTransitions = new();
    private bool _publishing;
    private bool _disposed;
    private SteamSessionState _state;

    internal EffectiveSteamSessionSource(SteamSessionWatcher watcher, SteamBigPictureWatcher bigPictureWatcher, DeveloperTestModeState testMode, ISteamInputRoutingPreference settings)
    {
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _testMode = testMode ?? throw new ArgumentNullException(nameof(testMode));
        _bigPictureWatcher = bigPictureWatcher ?? throw new ArgumentNullException(nameof(bigPictureWatcher));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _state = ComputeState();
        _watcher.StateChanged += OnInputChanged;
        _bigPictureWatcher.StateChanged += OnInputChanged;
        _settings.SteamInputRoutingEnabledChanged += OnInputChanged;
        _testMode.Changed += OnInputChanged;
    }

    public EffectiveSteamSessionSource(SteamSessionWatcher watcher, DeveloperTestModeState testMode)
        : this(watcher, new SteamBigPictureWatcher(new InactiveSteamBigPictureProbe()), testMode, new StaticSteamInputRoutingPreference())
    {
    }

    public SteamSessionState State { get { lock (_sync) return _state; } }
    public event EventHandler<SteamSessionStateChangedEventArgs>? StateChanged;

    public void Refresh() => OnInputChanged(this, EventArgs.Empty);

    private void OnInputChanged(object? sender, EventArgs args)
    {
        var shouldDrain = false;
        lock (_sync)
        {
            if (_disposed) return;
            var next = ComputeState();
            if (_state == next) return;
            var transition = new SteamSessionStateChangedEventArgs(_state, next);
            _state = next;
            _pendingTransitions.Enqueue(transition);
            if (!_publishing) { _publishing = true; shouldDrain = true; }
        }
        if (shouldDrain) Drain();
    }

    private SteamSessionState ComputeState()
    {
        // Steam Input routing is a single master eligibility gate: while it is off, no session source
        // (actual game, Big Picture, developer test) may produce an effective routing session. The
        // preference itself stays stored; it just cannot make routing eligible.
        if (!_settings.SteamInputRoutingEnabled) return SteamSessionState.FromRunningAppId(0);
        var actual = _watcher.State;
        if (actual.IsActive) return actual;
        if (_bigPictureWatcher.IsActive) return SteamSessionState.CreateBigPicture();
        return _testMode.IsEnabled ? SteamSessionState.CreateDeveloperTest() : actual;
    }

    private void Drain()
    {
        lock (_publicationGate)
        {
            while (true)
            {
                SteamSessionStateChangedEventArgs? transition;
                lock (_sync)
                {
                    if (_pendingTransitions.Count == 0) { _publishing = false; return; }
                    transition = _pendingTransitions.Dequeue();
                }

                Diagnostics.AppLog.Info("Steam.Effective", "Effective Steam session state changed.",
                    ("PreviousRunningAppID", transition.Previous.RunningAppId),
                    ("CurrentRunningAppID", transition.Current.RunningAppId),
                    ("PreviousActive", transition.Previous.IsActive),
                    ("CurrentActive", transition.Current.IsActive),
                    ("PreviousSource", transition.Previous.Source),
                    ("CurrentSource", transition.Current.Source));
                foreach (var handler in StateChanged?.GetInvocationList().OfType<EventHandler<SteamSessionStateChangedEventArgs>>() ?? [])
                {
                    lock (_sync)
                    {
                        if (_disposed) return;
                    }

                    try { handler(this, transition); }
                    catch (Exception exception) { Diagnostics.AppLog.Warn("Steam.Effective", "Effective state subscriber failed.", exception); }
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_publicationGate)
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _pendingTransitions.Clear();
                _publishing = false;
            }
        }
        _watcher.StateChanged -= OnInputChanged;
        _bigPictureWatcher.StateChanged -= OnInputChanged;
        _settings.SteamInputRoutingEnabledChanged -= OnInputChanged;
        _testMode.Changed -= OnInputChanged;
        GC.SuppressFinalize(this);
    }
}

// Used by the settings-less convenience constructor, which pairs an always-inactive Big Picture probe
// with the product default (routing enabled) so actual-game and developer-test sources behave normally.
internal sealed class StaticSteamInputRoutingPreference : ISteamInputRoutingPreference
{
    public bool SteamInputRoutingEnabled => true;
    public event EventHandler? SteamInputRoutingEnabledChanged { add { } remove { } }
}

internal sealed class InactiveSteamBigPictureProbe : ISteamBigPictureWindowProbe
{
    public BigPictureCandidateInspection InspectCandidate(IntPtr window) => new(false, true, 0);
    public BigPictureScanResult ScanForCandidate(IntPtr preferredHwnd) => new(false, IntPtr.Zero, 0, true);
    public BigPictureTrackedWindowInspection InspectTrackedWindow(IntPtr window, uint expectedProcessId) => new(false, true);
}
