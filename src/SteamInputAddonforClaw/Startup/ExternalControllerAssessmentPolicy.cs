using SteamInputAddonforClaw.Controllers.Detection;

namespace SteamInputAddonforClaw.Startup;

internal static class ExternalControllerAssessmentPolicy
{
    public static ExternalControllerAssessment ApplyEnvironmentSafety(
        ExternalControllerAssessment assessment,
        ControllerEnvironmentMode environmentMode,
        ControllerEnvironmentReadiness environmentReadiness)
    {
        if (assessment.Status == ExternalControllerAssessmentStatus.ExternalPresent)
        {
            return assessment;
        }

        return environmentMode == ControllerEnvironmentMode.HHCManaged
               || environmentReadiness != ControllerEnvironmentReadiness.Stable
            ? new ExternalControllerAssessment(ExternalControllerAssessmentStatus.Indeterminate, 0, [])
            : assessment;
    }
}
