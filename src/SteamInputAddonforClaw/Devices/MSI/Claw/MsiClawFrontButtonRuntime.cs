using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Wing;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

/// <summary>
/// Full1902 A2: the one narrow feature-local owner of the MSI Claw front-button (OEM1 / Center M
/// button, and WING) action paths. It observes the OEM1/WING WMI events, recognizes the
/// single/double gesture, and dispatches the persisted mapping's action.
///
/// <para>
/// It is NOT a controller authority. It owns only feature-local items -- Event41/Event88 WMI
/// observation, the OEM1/WING gesture recognizers/bridges/dispatchers, and a small WING lifetime
/// epoch used to reject a delayed double-click after this owner is torn down. It never touches
/// PID1901/PID1902, DirectInput, HidHide, VIIPER, Steam/BPM observation, controller recovery, or
/// Center M startup authority, and it never instantiates the legacy dummy MSI Center M helper.
/// </para>
///
/// <para>
/// The OEM1 Normal/Routing mapping domain and the OEM1/WING "Steam"/"Quick Access" actions are all
/// resolved against the live Full1902 SteamDeck presentation via the supplied callbacks -- never the
/// legacy routing runtime status or its Steam Deck output stage.
/// </para>
/// </summary>
internal sealed class MsiClawFrontButtonRuntime : IAsyncDisposable
{
    private readonly IMsiEventSource? _oem1EventSource;
    private readonly Oem1GestureRecognizer? _oem1Recognizer;
    private readonly Oem1EventGestureBridge? _oem1Bridge;

    private readonly IMsiEventSource? _wingEventSource;
    private WingEventGestureBridge? _wingBridge;

    private readonly Func<bool> _nativeWinGSuppressionReady;
    private readonly object _wingAuthorityGate = new();
    private long _wingAuthorityEpoch;
    private bool _wingLifetimeRevoked;

    private readonly object _oem1AuthorityGate = new();
    private bool _oem1FailedOpen;

    private int _disposed;

    private MsiClawFrontButtonRuntime(Func<bool>? nativeWinGSuppressionReady = null)
        => _nativeWinGSuppressionReady = nativeWinGSuppressionReady ?? (static () => false);

    private MsiClawFrontButtonRuntime(
        IMsiEventSource oem1EventSource,
        Oem1GestureRecognizer oem1Recognizer,
        Oem1EventGestureBridge oem1Bridge,
        IMsiEventSource wingEventSource,
        Func<bool> nativeWinGSuppressionReady)
    {
        _oem1EventSource = oem1EventSource;
        _oem1Recognizer = oem1Recognizer;
        _oem1Bridge = oem1Bridge;
        _wingEventSource = wingEventSource;
        _nativeWinGSuppressionReady = nativeWinGSuppressionReady;
    }

    /// <summary>
    /// Composes and starts the front-button action paths. Returns an inert instance on unrecognized
    /// hardware (the OEM1 mapping feature only exists on a supported MSI Claw), matching the previous
    /// <c>MsiClawRoutingComposition.ConfigureOem1ActionPath</c> hardware gate.
    /// </summary>
    /// <param name="isSteamDeckPresentationActive">Full1902 A2 section 5: <see langword="true"/> when
    /// the active Full1902 presentation is SteamDeck. Selects the OEM1 Routing mapping domain.</param>
    /// <param name="tryRequestQuickAccessPulse">OEM1 <c>SteamQuickAccess</c> action -- the live
    /// SteamDeck presentation's Quick Access system-button pulse seam.</param>
    /// <param name="tryRequestSteamPulse">WING <c>SteamButton</c> action -- the live SteamDeck
    /// presentation's Steam system-button pulse seam.</param>
    /// <param name="nativeWinGSuppressionReady">Full1902 A2 section 9.2: WING custom action delivery
    /// stays gated by native Win+G suppression readiness until Policy B binds suppression to Addon
    /// authority. Production passes a callback that is currently always <see langword="false"/>.</param>
    internal static MsiClawFrontButtonRuntime Create(
        bool hardwareSupported,
        Settings.IOem1MappingPreference oem1MappingPreference,
        Settings.IWingMappingPreference wingMappingPreference,
        Func<bool> isSteamDeckPresentationActive,
        Func<bool> tryRequestQuickAccessPulse,
        Func<bool> tryRequestSteamPulse,
        Func<bool>? nativeWinGSuppressionReady = null,
        IMsiEventSource? oem1EventSourceOverride = null,
        IMsiEventSource? wingEventSourceOverride = null,
        IOem1GestureDelay? oem1GestureDelay = null,
        IOem1GestureClock? oem1GestureClock = null,
        Action? launchBigPictureOverride = null,
        IOem1GestureDelay? wingGestureDelay = null,
        TimeProvider? wingTimeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(oem1MappingPreference);
        ArgumentNullException.ThrowIfNull(wingMappingPreference);
        ArgumentNullException.ThrowIfNull(isSteamDeckPresentationActive);
        ArgumentNullException.ThrowIfNull(tryRequestQuickAccessPulse);
        ArgumentNullException.ThrowIfNull(tryRequestSteamPulse);
        var suppressionReady = nativeWinGSuppressionReady ?? (static () => false);

        if (!hardwareSupported)
        {
            AppLog.Info("CenterM.Oem1", "Front-button action paths are unavailable on this hardware; nothing is wired.",
                ("Reason", "HardwareNotSupported"), ("Action", "Passive"));
            return new MsiClawFrontButtonRuntime(suppressionReady);
        }

        // ---- OEM1 (Center M button) ----
        var oem1EventSource = oem1EventSourceOverride ?? new WmiMsiEventSource();
        var oem1Recognizer = new Oem1GestureRecognizer(
            doubleClickEnabled: () =>
            {
                var mapping = oem1MappingPreference.Oem1Mapping;
                var slot = Oem1MappingSlots.Resolve(isSteamDeckPresentationActive(), Oem1Gesture.Double);
                var binding = mapping.Resolve(slot);
                return Oem1ActionCapabilities.Supports(binding.Action, slot) && binding.Action != Oem1Action.None;
            },
            doubleClickWindow: TimeSpan.FromMilliseconds(200),
            delay: oem1GestureDelay, clock: oem1GestureClock);
        var oem1Bridge = new Oem1EventGestureBridge(oem1EventSource, oem1Recognizer);
        var oem1Dispatcher = new Oem1ActionDispatcher(
            captureMapping: () => oem1MappingPreference.Oem1Mapping,
            captureRoutingDomainActive: isSteamDeckPresentationActive,
            // The SteamQuickAccess action is only reachable from the Routing mapping domain, i.e. only
            // while the SteamDeck presentation is live, so this seam is effectively self-gating.
            requestQuickAccessPulse: () => tryRequestQuickAccessPulse(),
            launchBigPicture: launchBigPictureOverride ?? Oem1BigPictureLauncher.Launch);

        var runtime = new MsiClawFrontButtonRuntime(oem1EventSource, oem1Recognizer, oem1Bridge,
            wingEventSourceOverride ?? new WmiMsiEventSource(), suppressionReady);

        oem1Bridge.PolicyRequested += request =>
        {
            if (!oem1Dispatcher.Dispatch(request))
                runtime.RevokeOem1CustomAuthority();
        };
        oem1Bridge.RecognitionFailed += runtime.RevokeOem1CustomAuthority;

        // Full1902 A2 section 8.2: Center M Disabled already owns the controller and its startup roots
        // are disabled, so no fake Center M process / suppression lifecycle is needed to justify
        // custom button delivery. Just start observation and grant gesture authority.
        oem1EventSource.Start();
        oem1Bridge.SetCustomAuthority(true, allowActivation: () => !runtime.Oem1FailedOpen);

        // ---- WING ----
        var wingRecognizer = new WingGestureRecognizer(
            doubleEnabled: () => WingMapping.From(wingMappingPreference.WingMapping).Double.Action != WingAction.None,
            delay: wingGestureDelay, timeProvider: wingTimeProvider);
        var wingDispatcher = new WingActionDispatcher(
            () => WingMapping.From(wingMappingPreference.WingMapping),
            trySteam: tryRequestSteamPulse);
        var wingBridge = new WingEventGestureBridge(runtime._wingEventSource!, wingRecognizer, runtime.CaptureWingAuthority, wingDispatcher);
        runtime._wingBridge = wingBridge;
        if (!runtime._wingEventSource!.Start())
        {
            AppLog.Warn("Wing.Event", "WING Event88 observation unavailable; front-button OEM1 path remains functional.");
            wingBridge.Dispose();
            runtime._wingBridge = null;
        }

        AppLog.Info("CenterM.Oem1", "Front-button action paths configured (Full1902 A2; no legacy routing owner).",
            ("Oem1", true), ("Wing", runtime._wingBridge is not null));
        return runtime;
    }

    private bool Oem1FailedOpen { get { lock (_oem1AuthorityGate) return _oem1FailedOpen; } }

    private void RevokeOem1CustomAuthority()
    {
        lock (_oem1AuthorityGate) _oem1FailedOpen = true;
        _oem1Bridge?.SetCustomAuthority(false);
        AppLog.Warn("CenterM.Oem1", "OEM1 replacement action failed; custom gesture authority revoked for this runtime lifetime.",
            null, ("Action", "FailOpen"));
    }

    /// <summary>Full1902 A2 section 9: the WING route-authority fact, derived only from this owner's
    /// process lifetime plus native Win+G suppression readiness -- never a persisted setting. The
    /// epoch bumps once when the lifetime is revoked so a delayed double-click that started before
    /// teardown is discarded by <see cref="WingEventGestureBridge"/>.</summary>
    internal WingRouteAuthoritySnapshot CaptureWingAuthority()
    {
        lock (_wingAuthorityGate)
        {
            var active = !_wingLifetimeRevoked && Volatile.Read(ref _disposed) == 0 && _nativeWinGSuppressionReady();
            return new WingRouteAuthoritySnapshot(active, _wingAuthorityEpoch);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        lock (_wingAuthorityGate)
        {
            if (!_wingLifetimeRevoked)
            {
                _wingLifetimeRevoked = true;
                _wingAuthorityEpoch++;
            }
        }

        _wingBridge?.Dispose(); // also disposes its own event source + recognizer
        _oem1Bridge?.Dispose();
        _oem1Recognizer?.Dispose();
        _oem1EventSource?.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
