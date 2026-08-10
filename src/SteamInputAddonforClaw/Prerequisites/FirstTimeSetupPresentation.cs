using SteamInputAddonforClaw.Status;

namespace SteamInputAddonforClaw.Prerequisites;

internal sealed record FirstTimeSetupAddonPresentation(string Status, string Reason);

internal static class FirstTimeSetupPresentation
{
    public static FirstTimeSetupAddonPresentation GetAddonPresentation(FirstTimeSetupAssessment setup, RuntimePrerequisiteAssessment prerequisites, AddonStatusSnapshot addon)
    {
        if (setup.Status == FirstTimeSetupStatus.Complete
            && prerequisites.HidHide.Status == PrerequisiteStatus.Ready
            && prerequisites.UsbIpWin2.Status == PrerequisiteStatus.Ready
            && prerequisites.Viiper.Status != PrerequisiteStatus.Ready)
            return new("Setup complete", "Routing runtime is not available in this build.");

        return new(addon.Status.ToString(), addon.Reason);
    }
}
