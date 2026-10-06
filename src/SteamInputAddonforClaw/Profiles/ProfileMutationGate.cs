namespace SteamInputAddonforClaw.Profiles;

/// <summary>One in-process gate for all Device, Steam-game, and XBOX-game profile document mutations.</summary>
internal sealed class ProfileMutationGate
{
    internal Lock Sync { get; } = new();
}
