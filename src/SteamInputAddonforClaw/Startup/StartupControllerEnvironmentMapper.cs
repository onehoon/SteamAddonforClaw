using SteamInputAddonforClaw.Status;

namespace SteamInputAddonforClaw.Startup;

internal static class StartupControllerEnvironmentMapper
{
    internal static ControllerEnvironment Map(ControllerEnvironmentAssessmentSnapshot assessment) => new(ControllerEnvironmentMode.StockCenterM);
}
