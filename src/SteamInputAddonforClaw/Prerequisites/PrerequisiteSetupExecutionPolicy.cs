namespace SteamInputAddonforClaw.Prerequisites;

internal enum PrerequisiteComponentAction { Install, AlreadyReady, RestartRequired, Blocked }

internal static class PrerequisiteSetupExecutionPolicy
{
    public static PrerequisiteComponentAction SelectAction(bool packageInstalled, PrerequisiteStatus prerequisiteStatus, bool addonReceiptPendingReboot)
    {
        if (!packageInstalled) return PrerequisiteComponentAction.Install;
        if (prerequisiteStatus == PrerequisiteStatus.Ready) return PrerequisiteComponentAction.AlreadyReady;
        return addonReceiptPendingReboot ? PrerequisiteComponentAction.RestartRequired : PrerequisiteComponentAction.Blocked;
    }
}
