using SteamInputAddonforClaw.Routing;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// Resolves an <see cref="Oem1GesturePolicyRequest"/> to its configured <see cref="Oem1Action"/> and
/// dispatches it. Routing state alone never redefines OEM1 -- the bound action is resolved first,
/// and only <see cref="Oem1Action.SteamQuickAccess"/> then checks whether the Steam output stage is
/// actually active. Routing status is captured fresh at dispatch time, never cached.
/// </summary>
internal sealed class Oem1ActionDispatcher
{
    private readonly Oem1ActionBindings _bindings;
    private readonly Func<RoutingRuntimeStatusSnapshot> _captureRoutingStatus;
    private readonly Action _requestQuickAccessPulse;

    internal Oem1ActionDispatcher(
        Oem1ActionBindings bindings,
        Func<RoutingRuntimeStatusSnapshot> captureRoutingStatus,
        Action requestQuickAccessPulse)
    {
        _bindings = bindings;
        _captureRoutingStatus = captureRoutingStatus ?? throw new ArgumentNullException(nameof(captureRoutingStatus));
        _requestQuickAccessPulse = requestQuickAccessPulse ?? throw new ArgumentNullException(nameof(requestQuickAccessPulse));
    }

    internal void Dispatch(Oem1GesturePolicyRequest request)
    {
        switch (_bindings.Resolve(request.Gesture))
        {
            case Oem1Action.None:
                return;

            case Oem1Action.SteamQuickAccess:
                if (!_captureRoutingStatus().SteamOutputActive)
                    return;

                _requestQuickAccessPulse();
                return;
        }
    }
}
