namespace SteamInputAddonforClaw.Controllers.Detection;

public enum ExternalControllerAssessmentStatus
{
    Clear,
    ExternalPresent,
    Indeterminate
}

public sealed record ExternalControllerAssessment(
    ExternalControllerAssessmentStatus Status,
    int DetectedExternalControllerCount,
    IReadOnlyList<ControllerDeviceInfo> ExternalControllers);
