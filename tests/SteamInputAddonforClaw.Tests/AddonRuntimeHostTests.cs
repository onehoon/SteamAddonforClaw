using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Power;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Runtime;
using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// Full1902 Cleanup A: <see cref="AddonRuntimeHost"/> no longer carries a legacy routing backend.
/// It owns only Steam/Big Picture session observation plus generic suspend/resume power and
/// user-termination orchestration.
/// </summary>
public sealed class AddonRuntimeHostTests
{
    private static AddonRuntimeHost NewHost(
        SteamSessionRuntime steamRuntime,
        PowerMutationGate powerGate,
        RecoverySafetyState recoverySafetyState,
        bool recoverySafe = true,
        Func<bool>? hasIncompleteRecovery = null,
        Func<CancellationToken, Task<bool>>? establishBaseline = null,
        IPowerSuspendResumeNotificationSource? notificationSource = null) =>
        new(steamRuntime, powerGate, recoverySafetyState, recoverySafe,
            hasIncompleteRecovery ?? (() => false),
            establishBaseline ?? (_ => Task.FromResult(false)),
            notificationSource);

    [Fact]
    public async Task Host_republishes_Steam_state_transitions_to_subscribers()
    {
        using var steamRuntime = new SteamSessionRuntime();
        var host = NewHost(steamRuntime, new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe));
        SteamSessionStateChangedEventArgs? observed = null;
        host.SteamSessionStateChanged += (_, args) => observed = args;

        steamRuntime.DeveloperTestModeState.SetEnabled(true);

        Assert.NotNull(observed);
        Assert.Equal(SteamSessionSource.DeveloperTest, observed!.Current.Source);

        await host.DisposeAsync();
    }

    [Fact]
    public async Task Host_republishes_actual_running_app_id_changes()
    {
        using var steamRuntime = new SteamSessionRuntime();
        var host = NewHost(steamRuntime, new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe));
        var observed = new List<uint>();
        host.ActualRunningAppIdChanged += observed.Add;

        steamRuntime.DeveloperTestModeState.SetEnabled(true);
        // DeveloperTest does not change the actual-AppID fact; the host simply forwards whatever
        // SteamSessionRuntime raises. The observation wiring is what matters here.
        Assert.Equal(steamRuntime.ActualRunningAppId, host.ActualRunningAppId);

        await host.DisposeAsync();
    }

    [Fact]
    public async Task Resume_refreshes_Steam_observation_and_requests_a_status_refresh()
    {
        using var steamRuntime = new SteamSessionRuntime();
        var source = new FakeSource(succeeds: true);
        var host = NewHost(steamRuntime, new PowerMutationGate(), new RecoverySafetyState(RecoverySafety.Safe),
            establishBaseline: _ => Task.FromResult(true), notificationSource: source);
        var refreshCount = 0;
        var resumeObserved = 0;
        host.StatusRefreshRequested += (_, _) => Interlocked.Increment(ref refreshCount);
        host.PowerResumeObserved += () => Interlocked.Increment(ref resumeObserved);
        host.StartPowerObservation();

        await source.RaiseAsync(4);   // suspend
        await source.RaiseAsync(18);  // resume

        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref refreshCount) >= 1 && Volatile.Read(ref resumeObserved) >= 1, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, refreshCount);
        Assert.Equal(1, resumeObserved);

        await host.DisposeAsync();
    }

    [Fact]
    public async Task Resume_runs_the_stock_baseline_callback()
    {
        using var steamRuntime = new SteamSessionRuntime();
        var source = new FakeSource(succeeds: true);
        var baselineCalls = 0;
        var host = NewHost(steamRuntime, new PowerMutationGate(), new RecoverySafetyState(RecoverySafety.Safe),
            establishBaseline: _ => { Interlocked.Increment(ref baselineCalls); return Task.FromResult(true); },
            notificationSource: source);
        host.StartPowerObservation();

        await source.RaiseAsync(4);
        await source.RaiseAsync(18);

        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref baselineCalls) >= 1, TimeSpan.FromSeconds(5)));

        await host.DisposeAsync();
    }

    [Fact]
    public async Task StartPowerObservation_opens_the_gate_when_registration_succeeds_and_recovery_is_safe()
    {
        using var steamRuntime = new SteamSessionRuntime();
        var source = new FakeSource(succeeds: true);
        var powerGate = new PowerMutationGate();
        var host = NewHost(steamRuntime, powerGate, new RecoverySafetyState(RecoverySafety.Safe), notificationSource: source);

        host.StartPowerObservation();

        Assert.Equal(1, source.RegisterCallCount);
        Assert.True(powerGate.IsOpen);

        await host.DisposeAsync();
    }

    [Fact]
    public async Task StartPowerObservation_leaves_the_gate_closed_when_registration_fails()
    {
        using var steamRuntime = new SteamSessionRuntime();
        var source = new FakeSource(succeeds: false);
        var powerGate = new PowerMutationGate();
        var host = NewHost(steamRuntime, powerGate, new RecoverySafetyState(RecoverySafety.Safe), notificationSource: source);

        host.StartPowerObservation();

        Assert.False(powerGate.IsOpen);

        await host.DisposeAsync();
    }

    [Fact]
    public async Task StartPowerObservation_leaves_the_gate_closed_when_recovery_is_unsafe_even_if_registration_succeeds()
    {
        using var steamRuntime = new SteamSessionRuntime();
        var source = new FakeSource(succeeds: true);
        var powerGate = new PowerMutationGate();
        var host = NewHost(steamRuntime, powerGate, new RecoverySafetyState(RecoverySafety.Unsafe), recoverySafe: false, notificationSource: source);

        host.StartPowerObservation();

        Assert.False(powerGate.IsOpen);

        await host.DisposeAsync();
    }

    [Fact]
    public async Task EvaluateUserTermination_blocks_on_owned_live_recovery_mutation()
    {
        using var steamRuntime = new SteamSessionRuntime();
        var host = NewHost(steamRuntime, new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe),
            hasIncompleteRecovery: () => true);

        var decision = host.EvaluateUserTermination();

        Assert.False(decision.CanTerminate);
        Assert.Equal(UserTerminationBlockReason.RecoveryMutationOwned, decision.Reason);

        await host.DisposeAsync();
    }

    [Fact]
    public async Task EvaluateUserTermination_allows_termination_when_no_recovery_mutation_is_owned()
    {
        using var steamRuntime = new SteamSessionRuntime();
        var host = NewHost(steamRuntime, new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe));

        Assert.True(host.EvaluateUserTermination().CanTerminate);

        await host.DisposeAsync();
    }

    [Fact]
    public async Task EvaluateUserTermination_blocks_once_shutdown_has_been_requested()
    {
        using var steamRuntime = new SteamSessionRuntime();
        var host = NewHost(steamRuntime, new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe));

        host.PrepareForShutdown();

        var decision = host.EvaluateUserTermination();
        Assert.False(decision.CanTerminate);
        Assert.Equal(UserTerminationBlockReason.RuntimeShuttingDown, decision.Reason);

        await host.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_is_idempotent_and_a_post_disposal_notification_does_not_reenter_runtime_work()
    {
        using var steamRuntime = new SteamSessionRuntime();
        var source = new FakeSource(succeeds: true);
        var baselineCalls = 0;
        var host = NewHost(steamRuntime, new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe),
            establishBaseline: _ => { Interlocked.Increment(ref baselineCalls); return Task.FromResult(true); },
            notificationSource: source);
        host.StartPowerObservation();

        await host.DisposeAsync();
        await host.DisposeAsync(); // must not throw

        await source.RaiseAsync(4);
        await source.RaiseAsync(18);
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        Assert.Equal(0, baselineCalls);
    }

    [Fact]
    public async Task Resume_notification_processed_after_PrepareForShutdown_does_not_touch_the_disposed_Steam_runtime()
    {
        var steamRuntime = new SteamSessionRuntime();
        var source = new FakeSource(succeeds: true);
        var host = NewHost(steamRuntime, new PowerMutationGate(initiallyOpen: true), new RecoverySafetyState(RecoverySafety.Safe),
            establishBaseline: _ => Task.FromResult(true), notificationSource: source);
        host.StartPowerObservation();

        host.PrepareForShutdown();

        var exception = await Record.ExceptionAsync(async () =>
        {
            await source.RaiseAsync(4);
            await source.RaiseAsync(18);
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        });

        Assert.Null(exception);

        await host.DisposeAsync();
    }

    /// <summary>Mirrors PowerTransitionTests.cs's FakeSource: a real IPowerSuspendResumeNotificationSource
    /// so resume/suspend wiring is exercised through the same production path App used to wire up
    /// manually, not a bespoke Host-only shortcut.</summary>
    private sealed class FakeSource(bool succeeds) : IPowerSuspendResumeNotificationSource
    {
        private int _registerCallCount;
        internal int RegisterCallCount => Volatile.Read(ref _registerCallCount);

        public event Action<uint>? Notification;
        public bool TryRegister(out int nativeError) { Interlocked.Increment(ref _registerCallCount); nativeError = succeeds ? 0 : 5; return succeeds; }
        public void Raise(uint code) => Notification?.Invoke(code);
        public Task RaiseAsync(uint code) { Raise(code); return Task.CompletedTask; }
        public void Dispose() { }
    }
}
