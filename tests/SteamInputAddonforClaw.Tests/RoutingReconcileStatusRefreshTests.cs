using SteamInputAddonforClaw.Routing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RoutingReconcileStatusRefreshTests
{
    [Fact]
    public async Task RequestsStatusRefreshAfterReconcileCompletes()
    {
        var events = new List<string>();

        await RoutingReconcileStatusRefresh.RunAsync(
            async () =>
            {
                events.Add("reconcile-started");
                await Task.Yield();
                events.Add("reconcile-completed");
            },
            () => events.Add("status-refresh"));

        Assert.Equal(["reconcile-started", "reconcile-completed", "status-refresh"], events);
    }

    [Fact]
    public async Task RequestsStatusRefreshAfterReconcileThrows()
    {
        var refreshRequested = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => RoutingReconcileStatusRefresh.RunAsync(
            () => throw new InvalidOperationException("reconcile failed"),
            () => refreshRequested = true));

        Assert.True(refreshRequested);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResumeFreshReconcileRequestsStatusRefreshForSuccessAndFailure(bool freshSucceeded)
    {
        var refreshRequested = false;
        var deferredReconcileRequested = false;

        var result = await RoutingReconcileStatusRefresh.RunResumeFreshAsync(
            _ => Task.FromResult(freshSucceeded),
            _ => false,
            () =>
            {
                deferredReconcileRequested = true;
                return Task.CompletedTask;
            },
            () => refreshRequested = true,
            CancellationToken.None);

        Assert.Equal(freshSucceeded, result);
        Assert.True(refreshRequested);
        Assert.False(deferredReconcileRequested);
    }
}
