using SteamInputAddonforClaw.QamHost;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public class QamHostCdpCommandCorrelatorTests
{
    [Fact]
    public async Task DeliversResponseToTheWaiterWithTheMatchingId()
    {
        var correlator = new CdpCommandCorrelator();
        var id = correlator.NextId();
        var responseTask = correlator.RegisterAsync(id, CancellationToken.None);

        var delivered = correlator.TryComplete(id, "{\"id\":" + id + "}");

        Assert.True(delivered);
        var result = await responseTask;
        Assert.Contains(id.ToString(), result);
    }

    [Fact]
    public void TryCompleteReturnsFalseForAnUnknownId()
    {
        var correlator = new CdpCommandCorrelator();

        var delivered = correlator.TryComplete(9999, "{}");

        Assert.False(delivered);
    }

    [Fact]
    public async Task RegisterAsyncThrowsWhenCancelledBeforeAResponseArrives()
    {
        var correlator = new CdpCommandCorrelator();
        var id = correlator.NextId();
        using var cts = new CancellationTokenSource();

        var responseTask = correlator.RegisterAsync(id, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => responseTask);
    }

    [Fact]
    public void NextIdProducesDistinctIncreasingValues()
    {
        var correlator = new CdpCommandCorrelator();

        var first = correlator.NextId();
        var second = correlator.NextId();

        Assert.True(second > first);
    }
}
