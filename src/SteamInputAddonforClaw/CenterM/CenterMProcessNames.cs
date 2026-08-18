namespace SteamInputAddonforClaw.CenterM;

/// <summary>Process image names (no ".exe") relevant to Center M observation. Never used alone as
/// an ownership/kill authority -- see TrackedCenterMMainUi and CenterMHelperOwnership for the
/// retained-handle identity these names are only a starting filter for.</summary>
internal static class CenterMProcessNames
{
    internal const string MainUi = "MSI Center M";
    internal const string Launcher = "MSI_Center_M_Launcher";
    internal const string Server = "MSI_Center_M_Server";
    internal const string ServerControlMode = "MSI_Center_M_Server_ControlMode";
}
