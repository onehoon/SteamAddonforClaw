namespace SteamInputAddonforClaw.Routing;

internal interface IRoutingRecoverySessionProvider
{
    Guid? CurrentRecoverySessionId { get; }
}

/// <summary>
/// The generic routing-safety/lifecycle capability a device-specific backend may optionally
/// provide: recovery-session identity, activity/recovery-boundary state, and the two fail-close
/// operations the application shell already calls today. Deliberately minimal -- it exists only
/// for the safety/lifecycle operations already in use, not as a general backend-session contract.
/// Backend-specific pipeline-stage behavior remains on backend-specific stage/session contracts
/// and is intentionally outside this capability.
/// </summary>
internal interface IRoutingSafetySession : IRoutingRecoverySessionProvider, IAsyncDisposable
{
    bool IsActive { get; }

    bool HasOwnedRecoveryBoundary { get; }

    Task LatchRoutingFaultAsync(string reason, CancellationToken cancellationToken = default);

    Task FailClosedAsync(string reason, CancellationToken cancellationToken = default);
}
