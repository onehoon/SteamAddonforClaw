using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Routing;

internal interface IRoutingSessionStateMachine { RoutingDecision Evaluate(RoutingPolicyInput input); }

internal sealed class RoutingSessionStateMachine : IRoutingSessionStateMachine
{
    private readonly Lock _sync = new();
    private bool _externalControllerVetoLatched;
    private RoutingDecision? _previousDecision;

    public RoutingDecision Evaluate(RoutingPolicyInput input)
    {
        lock (_sync)
        {
            if (!input.Steam.IsActive && _externalControllerVetoLatched)
            {
                _externalControllerVetoLatched = false;
                AppLog.Info("Routing", "Steam session ended; external-controller veto latch reset.");
            }
            else if (input.Steam.IsActive && input.ExternalController.Status == ExternalControllerAssessmentStatus.ExternalPresent && !_externalControllerVetoLatched)
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
}
