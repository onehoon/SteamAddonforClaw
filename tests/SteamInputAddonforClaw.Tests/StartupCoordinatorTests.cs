using SteamInputAddonforClaw.Startup;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class StartupCoordinatorTests
{
    [Fact]
    public async Task CanStartRuntimeAsync_WhenNoUpdateExists_WaitsForEnvironmentAfterUpdateGate()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeUpdateGate(events, UpdateGateResult.Continue),
            new FakeEnvironmentDetector(events),
            new FakeEnvironmentWaiter(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.True(result.ShouldStartRuntime);
        Assert.Equal(["UpdateGate", "EnvironmentDetector", "EnvironmentWaiter"], events);
    }

    [Fact]
    public async Task CanStartRuntimeAsync_WhenUpdateIsScheduled_DoesNotInitializeEnvironment()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeUpdateGate(events, UpdateGateResult.RestartScheduled),
            new FakeEnvironmentDetector(events),
            new FakeEnvironmentWaiter(events));

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.ShouldStartRuntime);
        Assert.Equal(["UpdateGate"], events);
    }

    private sealed class FakeUpdateGate(List<string> events, UpdateGateResult result) : IUpdateGate
    {
        public Task<UpdateGateResult> RunAsync(CancellationToken cancellationToken)
        {
            events.Add("UpdateGate");
            return Task.FromResult(result);
        }
    }

    private sealed class FakeEnvironmentWaiter(List<string> events) : IControllerEnvironmentWaiter
    {
        public Task<ControllerEnvironmentReadiness> WaitUntilStableAsync(CancellationToken cancellationToken)
        {
            events.Add("EnvironmentWaiter");
            return Task.FromResult(ControllerEnvironmentReadiness.Stable);
        }
    }

    private sealed class FakeEnvironmentDetector(List<string> events) : IControllerEnvironmentDetector
    {
        public ClawTweaksState DetectClawTweaksState()
        {
            events.Add("EnvironmentDetector");
            return ClawTweaksState.NotInstalled;
        }
    }
}
