using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.GameBar;

internal sealed class WinGProtectionRoutingStage : IRoutingPipelineStage
{
    internal readonly record struct AuthoritySnapshot(bool Active, long Epoch);
    private readonly WinGSuppressionGuard? _guard;
    private readonly Func<bool> _arm;
    private readonly Action _disarm;
    private readonly object _authorityGate = new();
    private long _epoch;
    private bool _active;

    internal AuthoritySnapshot CaptureAuthority() { lock (_authorityGate) return new(_active, _epoch); }

    internal WinGProtectionRoutingStage(WinGSuppressionGuard guard)
    {
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _arm = _guard.EnsureArmed;
        _disarm = _guard.Disarm;
    }

    internal WinGProtectionRoutingStage(Func<bool> arm, Action disarm)
    { _arm = arm ?? throw new ArgumentNullException(nameof(arm)); _disarm = disarm ?? throw new ArgumentNullException(nameof(disarm)); }

    public RoutingStageKind Kind => RoutingStageKind.WinGProtection;
    public ValueTask<RoutingStageOperationResult> ObserveAsync(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(RoutingStageOperationResult.Success("WinGProtectionObserveNotApplicable")); }
    public ValueTask<RoutingStageOperationResult> PrepareMutationAsync(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(RoutingStageOperationResult.Success("Ready")); }

    public ValueTask<RoutingStageOperationResult> ExecuteMutationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppLog.Debug("Routing.Wing", "WinGProtectionArmStarted.");
        var armed = _arm();
        if (armed) lock (_authorityGate) { _epoch++; _active = true; }
        AppLog.Debug("Routing.Wing", armed ? "WinGProtectionArmed." : "WinGProtectionArmFailed.");
        return ValueTask.FromResult(armed
            ? RoutingStageOperationResult.Success("WinGProtectionArmed")
            : RoutingStageOperationResult.Failure("WinGProtectionArmFailed"));
    }

    public ValueTask<RoutingStageOperationResult> RollbackMutationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppLog.Debug("Routing.Wing", "WinGProtectionDisarmRequested.");
        try
        {
            lock (_authorityGate) { _active = false; _epoch++; }
            _disarm();
            AppLog.Debug("Routing.Wing", "WinGProtectionDisarmed.");
        }
        catch (Exception exception)
        {
            AppLog.Warn("Routing.Wing", "WinGProtectionDisarmFailedBestEffort.", exception);
        }
        return ValueTask.FromResult(RoutingStageOperationResult.Success("WinGProtectionDisarmedBestEffort"));
    }
}
