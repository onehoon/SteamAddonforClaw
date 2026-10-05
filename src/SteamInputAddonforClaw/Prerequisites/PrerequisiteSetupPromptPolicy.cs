namespace SteamInputAddonforClaw.Prerequisites;

using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Processes;

internal static class PrerequisiteSetupPromptPolicy
{
    internal static bool IsInstallable(FirstTimeSetupAssessment assessment) =>
        assessment.Status == FirstTimeSetupStatus.Required && assessment.CanInstallRequiredComponents;

    internal static bool RequiresForegroundActivation(FirstTimeSetupAssessment assessment) => IsInstallable(assessment);
}

internal static class PrerequisiteSetupRunnerPolicy
{
    internal static async Task<ChildProcessResult?> RunIfInstallableAsync(
        FirstTimeSetupAssessment assessment,
        IChildProcessRunner runner,
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runner);
        if (!PrerequisiteSetupPromptPolicy.IsInstallable(assessment)) return null;
        return await runner.RunAsync(fileName, arguments, cancellationToken).ConfigureAwait(false);
    }
}
