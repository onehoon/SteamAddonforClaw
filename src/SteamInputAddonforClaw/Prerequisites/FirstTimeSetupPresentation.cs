using SteamInputAddonforClaw.Status;

namespace SteamInputAddonforClaw.Prerequisites;

internal sealed record FirstTimeSetupAddonPresentation(string Status, string Reason);

internal static class FirstTimeSetupPresentation
{
    public static FirstTimeSetupAddonPresentation GetAddonPresentation(FirstTimeSetupAssessment setup, RuntimePrerequisiteAssessment prerequisites, AddonStatusSnapshot addon)
    {
        return new(addon.Status.ToString(), addon.Reason);
    }
}
