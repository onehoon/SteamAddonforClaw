using SteamInputAddonforClaw.Contracts.Oem1;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// Resolves an <see cref="Oem1GesturePolicyRequest"/> to its configured <see cref="Oem1Action"/> and
/// dispatches it. Normal OEM1 mapping and the SteamDeck presentation are independent features: the
/// mapping DOMAIN is selected first by capturing whether the actual Full1902 SteamDeck presentation
/// is active right now (Full1902 A2 section 5) -- never routing eligibility or a persisted setting --
/// and only then is the slot's persisted binding resolved and executed. The domain fact and the
/// mapping are both captured fresh at every dispatch, never cached.
/// </summary>
/// <remarks>
/// This is the runtime half of the single capability definition in
/// <see cref="Oem1ActionCapabilities"/> -- the settings UI builds its per-slot action lists from the
/// same table. A persisted binding whose action is not valid for the slot it was found in (an older
/// build, a hand-edited settings file, a future action this build does not know) is refused here
/// rather than executed, so capability validation can never be bypassed by persistence.
/// </remarks>
internal sealed class Oem1ActionDispatcher
{
    private readonly Func<Oem1MappingSettings> _captureMapping;
    private readonly Func<bool> _captureRoutingDomainActive;
    private readonly Action _requestQuickAccessPulse;
    private readonly Action _launchBigPicture;
    private readonly Action<Oem1HotkeyBinding> _sendHotkey;
    private readonly Action<Oem1LaunchApplicationBinding> _launchApplication;

    /// <param name="captureRoutingDomainActive">Full1902 A2 section 5: <see langword="true"/> when the
    /// active Full1902 presentation is SteamDeck (Routing mapping domain), <see langword="false"/> for
    /// Xbox360 / no presentation (Normal mapping domain). Captured fresh per dispatch.</param>
    internal Oem1ActionDispatcher(
        Func<Oem1MappingSettings> captureMapping,
        Func<bool> captureRoutingDomainActive,
        Action requestQuickAccessPulse,
        Action launchBigPicture,
        Action<Oem1HotkeyBinding>? sendHotkey = null,
        Action<Oem1LaunchApplicationBinding>? launchApplication = null)
    {
        _captureMapping = captureMapping ?? throw new ArgumentNullException(nameof(captureMapping));
        _captureRoutingDomainActive = captureRoutingDomainActive ?? throw new ArgumentNullException(nameof(captureRoutingDomainActive));
        _requestQuickAccessPulse = requestQuickAccessPulse ?? throw new ArgumentNullException(nameof(requestQuickAccessPulse));
        _launchBigPicture = launchBigPicture ?? throw new ArgumentNullException(nameof(launchBigPicture));
        _sendHotkey = sendHotkey ?? Oem1KeyboardHotkeyExecutor.Send;
        _launchApplication = launchApplication ?? Oem1ApplicationLauncher.Launch;
    }

    /// <summary>
    /// Dispatches the resolved action. Returns <see langword="false"/> when a bound,
    /// non-<see cref="Oem1Action.None"/> action was actually invoked and its execution threw, OR when
    /// the domain/mapping capture itself threw before an action could even be resolved --
    /// the SteamDeck presentation being inactive is never a failure, it is the normal-mapping case, and
    /// neither is a binding that capability validation refuses (nothing was executed, so custom
    /// authority need not be revoked). The caller (production composition) treats a false return as an
    /// OEM1 replacement-backend failure: custom gesture authority must be revoked and native Center M
    /// restored, per the fail-open contract.
    /// </summary>
    internal bool Dispatch(Oem1GesturePolicyRequest request)
    {
        // Review fix (BLOCKER, carried forward): status capture and domain resolution must share the
        // same failure boundary as action execution. Capturing routing status/mapping is a
        // caller-supplied callback, not a pure/trusted operation -- if it throws, that must still be
        // treated as an OEM1 replacement-action failure (fail-open: revoke custom authority, restore
        // native Center M), never let the exception escape uncaught. An exception escaping Dispatch
        // would pass Oem1EventGestureBridge (which only logs a subscriber failure), so
        // OnOem1ActionFailed() would never be reached and suppression could remain armed with no
        // action ever selected/executed.
        var action = Oem1Action.None;
        var slot = default(Oem1BindingSlot);
        try
        {
            var mapping = _captureMapping();

            // The ONLY question that matters for domain selection: is the actual Full1902 SteamDeck
            // presentation active right now? Never routing eligibility, never a persisted setting.
            var routingActuallyActive = _captureRoutingDomainActive();

            slot = Oem1MappingSlots.Resolve(routingActuallyActive, request.Gesture);
            var binding = mapping.Resolve(slot);
            action = binding.Action;

            if (!Oem1ActionCapabilities.Supports(action, slot))
            {
                AppLog.Warn("CenterM.Oem1", "Persisted OEM1 binding is not valid for its mapping slot; refusing to execute it.", null,
                    ("Action", action), ("Slot", slot));
                return true;
            }

            AppLog.Debug("CenterM.Oem1", "OEM1 action dispatch", ("Domain", routingActuallyActive ? "Routing" : "Normal"),
                ("Slot", slot), ("Gesture", request.Gesture), ("Action", action));

            switch (action)
            {
                case Oem1Action.None:
                    return true;

                case Oem1Action.SteamQuickAccess:
                    _requestQuickAccessPulse();
                    return true;

                case Oem1Action.SteamBigPicture:
                    _launchBigPicture();
                    return true;

                case Oem1Action.KeyboardHotkey:
                    _sendHotkey(binding.Hotkey);
                    return true;

                case Oem1Action.LaunchApplication:
                    _launchApplication(binding.Launch);
                    return true;

                default:
                    return true;
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn("CenterM.Oem1", "OEM1 replacement action selection/execution failed.", exception, ("Action", action), ("Slot", slot));
            return false;
        }
    }
}
