namespace SteamInputAddonforClaw.Input;

internal interface IControllerStateSnapshotSource
{
    ControllerState LatestState { get; }
}
