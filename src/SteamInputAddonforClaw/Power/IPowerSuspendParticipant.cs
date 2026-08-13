namespace SteamInputAddonforClaw.Power;

internal interface IPowerSuspendParticipant
{
    string Name { get; }
    Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken);
}
