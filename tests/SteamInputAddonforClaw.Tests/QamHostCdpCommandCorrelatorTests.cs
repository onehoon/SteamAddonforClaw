using SteamInputAddonforClaw.QamHost;
using System.Text.Json;
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

    [Fact]
    public async Task FailConnectionCompletesPendingCommandsImmediately()
    {
        var correlator = new CdpCommandCorrelator();
        var id = correlator.NextId();
        var responseTask = correlator.RegisterAsync(id, CancellationToken.None);

        correlator.FailConnection(new IOException("closed"));

        await Assert.ThrowsAsync<IOException>(() => responseTask);
    }

    [Fact]
    public async Task RegisterAfterFailConnectionFailsImmediately()
    {
        var correlator = new CdpCommandCorrelator();
        correlator.FailConnection(new IOException("closed"));

        var responseTask = correlator.RegisterAsync(correlator.NextId(), CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => responseTask);
    }

    [Fact]
    public void RecognizesDocumentContentLoadedEvent()
    {
        using var document = JsonDocument.Parse("{\"method\":\"Page.domContentEventFired\",\"params\":{}} ");

        Assert.True(SteamGamepadUiCdpClient.IsDocumentLoadedEvent(document.RootElement));
    }

    [Fact]
    public void RecoveryWindowStopsRetryingAfterItsSingleDeadline()
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        Assert.True(QamHostRecovery.IsOpen(deadline - TimeSpan.FromMilliseconds(1), deadline));
        Assert.False(QamHostRecovery.IsOpen(deadline, deadline));
        Assert.False(QamHostRecovery.IsOpen(deadline + TimeSpan.FromMinutes(1), deadline));
    }

    [Fact]
    public void HealthyManagedSessionFailureStartsOneFiniteRecoveryWindow()
    {
        var now = DateTimeOffset.UtcNow;
        var deadline = QamHostRecovery.BeginAfterSessionFailure(true, null, now, TimeSpan.FromSeconds(10));

        Assert.Equal(now.AddSeconds(10), deadline);
        Assert.False(QamHostRecovery.IsOpen(deadline!.Value, deadline));
    }

    [Fact]
    public void NonManagedSessionFailureDoesNotEnterReconnectRecovery()
    {
        var deadline = QamHostRecovery.BeginAfterSessionFailure(false, null, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));

        Assert.Null(deadline);
    }
}
