using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Routing;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// Resolves an <see cref="Oem1GesturePolicyRequest"/> to its configured <see cref="Oem1Action"/> and
/// dispatches it. Normal OEM1 mapping and Steam routing are independent features (see the OEM1
/// production E2E POC work order): the mapping DOMAIN is selected first by capturing whether
/// canonical Steam Deck routing is actually active right now -- never whether routing is merely
/// enabled/available/eligible -- and only then is the gesture resolved within that domain. Routing
/// status is captured fresh at every dispatch, never cached, and <see cref="RoutingRuntimeStatusSnapshot.Available"/>
/// is never consulted here: an unavailable/disabled/idle routing runtime is simply "not currently
/// routing", which selects the normal mapping exactly like every other non-active case.
/// </summary>
internal sealed class Oem1ActionDispatcher
{
    private readonly Oem1ActionBindings _normalBindings;
    private readonly Oem1ActionBindings _routingActiveBindings;
    private readonly Func<RoutingRuntimeStatusSnapshot> _captureRoutingStatus;
    private readonly Action _requestQuickAccessPulse;
    private readonly Action _launchBigPicture;

    internal Oem1ActionDispatcher(
        Oem1ActionBindings normalBindings,
        Oem1ActionBindings routingActiveBindings,
        Func<RoutingRuntimeStatusSnapshot> captureRoutingStatus,
        Action requestQuickAccessPulse,
        Action launchBigPicture)
    {
        _normalBindings = normalBindings;
        _routingActiveBindings = routingActiveBindings;
        _captureRoutingStatus = captureRoutingStatus ?? throw new ArgumentNullException(nameof(captureRoutingStatus));
        _requestQuickAccessPulse = requestQuickAccessPulse ?? throw new ArgumentNullException(nameof(requestQuickAccessPulse));
        _launchBigPicture = launchBigPicture ?? throw new ArgumentNullException(nameof(launchBigPicture));
    }

    /// <summary>
    /// Dispatches the resolved action. Returns <see langword="false"/> only when a bound,
    /// non-<see cref="Oem1Action.None"/> action was actually invoked and its execution threw --
    /// routing being unavailable/inactive is never a failure, it is the normal-mapping case. The
    /// caller (production composition) treats a false return as an OEM1 replacement-backend failure:
    /// custom gesture authority must be revoked and native Center M restored, per the work order's
    /// fail-open contract.
    /// </summary>
    internal bool Dispatch(Oem1GesturePolicyRequest request)
    {
        // Review fix (BLOCKER): status capture and domain resolution must share the same failure
        // boundary as action execution. Capturing routing status is a caller-supplied callback, not a
        // pure/trusted operation -- if it throws, that must still be treated as an OEM1
        // replacement-action failure (fail-open: revoke custom authority, restore native Center M),
        // never let the exception escape uncaught. Previously an exception here would propagate out of
        // Dispatch, past Oem1EventGestureBridge (which only logs a subscriber failure), so
        // OnOem1ActionFailed() was never reached and suppression could remain armed with no action
        // ever selected/executed.
        var action = Oem1Action.None;
        try
        {
            // The ONLY question that matters for domain selection: is canonical Steam Deck routing
            // actually active right now? Never Available, never routing-enabled, never eligibility.
            var routingActuallyActive = _captureRoutingStatus().SteamOutputActive;

            action = routingActuallyActive
                ? _routingActiveBindings.Resolve(request.Gesture)
                : _normalBindings.Resolve(request.Gesture);

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

                default:
                    return true;
            }
        }
        catch (Exception exception)
        {
            AppLog.Warn("CenterM.Oem1", "OEM1 replacement action selection/execution failed.", exception, ("Action", action));
            return false;
        }
    }
}
