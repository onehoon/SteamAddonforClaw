namespace SteamInputAddonforClaw.Prerequisites;

internal static class PrerequisiteSetupPromptPolicy
{
    internal static bool IsInstallable(FirstTimeSetupAssessment assessment) =>
        assessment.Status == FirstTimeSetupStatus.Required && assessment.CanInstallRequiredComponents;

    internal static bool RequiresForegroundActivation(FirstTimeSetupAssessment assessment) => IsInstallable(assessment);
}
