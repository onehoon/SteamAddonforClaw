namespace SteamInputAddonforClaw.Feedback;

internal enum PhysicalRumbleWriteStatus
{
    Succeeded,
    Unavailable,
    Failed,
    Disposed
}

internal readonly record struct PhysicalRumbleWriteResult(PhysicalRumbleWriteStatus Status, string Reason)
{
    internal bool Succeeded => Status == PhysicalRumbleWriteStatus.Succeeded;
}
