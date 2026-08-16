namespace SteamInputAddonforClaw.Routing;

internal interface IRoutingRecoverySessionProvider
{
    Guid? CurrentRecoverySessionId { get; }
}

/// <summary>
/// The generic routing-safety/lifecycle capability a device-specific backend may optionally
/// provide: recovery-session identity, activity/recovery-boundary state, and the two fail-close
/// operations the application shell already calls today. Deliberately minimal -- it exists only
/// for the safety/lifecycle operations already in use, not as a general MSI-native-mode contract.
/// Backend-specific pipeline-stage behavior (e.g. MSI native-mode enter/exit/inspect) stays on
/// <c>IMsiClawNativeModeStageSession</c>, not here.
/// </summary>
internal interface IRoutingSafetySession : IRoutingRecoverySessionProvider, IAsyncDisposable
{
    bool IsActive { get; }

    bool HasOwnedRecoveryBoundary { get; }

    Task LatchRoutingFaultAsync(string reason, CancellationToken cancellationToken = default);

    Task FailClosedAsync(string reason, CancellationToken cancellationToken = default);
}
