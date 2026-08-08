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
            new FakeEnvironmentWaiter(events));

        var canStart = await coordinator.CanStartRuntimeAsync(CancellationToken.None);

        Assert.True(canStart);
        Assert.Equal(["UpdateGate", "EnvironmentWaiter"], events);
    }

    [Fact]
    public async Task CanStartRuntimeAsync_WhenUpdateIsScheduled_DoesNotInitializeEnvironment()
    {
        var events = new List<string>();
        var coordinator = new StartupCoordinator(
            new FakeUpdateGate(events, UpdateGateResult.RestartScheduled),
            new FakeEnvironmentWaiter(events));

        var canStart = await coordinator.CanStartRuntimeAsync(CancellationToken.None);

        Assert.False(canStart);
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
        public Task WaitUntilStableAsync(CancellationToken cancellationToken)
        {
            events.Add("EnvironmentWaiter");
            return Task.CompletedTask;
        }
    }
}
