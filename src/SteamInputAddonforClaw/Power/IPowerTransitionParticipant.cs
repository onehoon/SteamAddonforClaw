namespace SteamInputAddonforClaw.Power;

internal interface IPowerTransitionParticipant
{
    string Name { get; }
    Task<bool> QuiesceForSuspendAsync(DateTimeOffset deadline, long cycle, long epoch, CancellationToken cancellationToken);
    Task<bool> ReconcileAfterResumeAsync(long cycle, long epoch, CancellationToken cancellationToken);
}
