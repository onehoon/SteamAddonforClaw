namespace SteamInputAddonforClaw.Profiles;

/// <summary>One in-process gate for all Device/Profile document mutations.</summary>
internal sealed class ProfileMutationGate
{
    internal Lock Sync { get; } = new();
}
