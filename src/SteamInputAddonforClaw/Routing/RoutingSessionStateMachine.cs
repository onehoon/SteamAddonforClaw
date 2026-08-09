using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Steam;

namespace SteamInputAddonforClaw.Routing;

internal interface IRoutingSessionStateMachine
{
    void ObserveSteamSessionState(SteamSessionState state);
    RoutingDecision Evaluate(RoutingPolicyInput input);
}

internal sealed class RoutingSessionStateMachine : IRoutingSessionStateMachine
{
    private readonly Lock _sync = new();
    private bool _externalControllerVetoLatched;
    private SteamSessionState? _observedSteamState;
    private RoutingDecision? _previousDecision;

    public void ObserveSteamSessionState(SteamSessionState state)
    {
        lock (_sync)
        {
            _observedSteamState = state;
            if (!state.IsActive) ResetExternalControllerVetoLatch();
        }
    }

    public RoutingDecision Evaluate(RoutingPolicyInput input)
    {
        lock (_sync)
        {
            var isCurrentSteamSnapshot = _observedSteamState is null
                || input.Steam.RunningAppId == _observedSteamState.RunningAppId;

            if (isCurrentSteamSnapshot && !input.Steam.IsActive) ResetExternalControllerVetoLatch();
            if (isCurrentSteamSnapshot
                && input.Steam.IsActive
                && input.ExternalController.Status == ExternalControllerAssessmentStatus.ExternalPresent
                && !_externalControllerVetoLatched)
            {
                _externalControllerVetoLatched = true;
                AppLog.Info("Routing", "External controller session veto latched.", ("RunningAppID", input.Steam.RunningAppId));
            }

            var decision = RoutingEligibilityPolicy.Evaluate(input, _externalControllerVetoLatched);
            if (decision != _previousDecision)
            {
                AppLog.Info("Routing", "Routing decision changed.", ("Previous", _previousDecision?.Kind), ("Current", decision.Kind), ("Reason", decision.Reason));
                _previousDecision = decision;
            }
            return decision;
        }
    }

    private void ResetExternalControllerVetoLatch()
    {
        if (!_externalControllerVetoLatched) return;
        _externalControllerVetoLatched = false;
        AppLog.Info("Routing", "Steam session ended; external-controller veto latch reset.");
    }
}
