using System.Net.Http;
using System.Net.Sockets;
using SteamInputAddonforClaw.Updates;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SilentUpdateServiceTests
{
    [Fact]
    public async Task CheckDownloadAndScheduleAsync_WhenNotInstalled_SkipsUpdateOperations()
    {
        var client = new FakeUpdateClient(isInstalled: false);

        var scheduled = await new SilentUpdateService(client).CheckDownloadAndScheduleAsync(CancellationToken.None);

        Assert.False(scheduled);
        Assert.Equal(0, client.CheckCount);
    }

    [Fact]
    public async Task CheckDownloadAndScheduleAsync_WhenNoUpdateExists_DoesNotDownloadOrApply()
    {
        var client = new FakeUpdateClient(isInstalled: true);

        var scheduled = await new SilentUpdateService(client).CheckDownloadAndScheduleAsync(CancellationToken.None);

        Assert.False(scheduled);
        Assert.Equal(1, client.CheckCount);
        Assert.Equal(0, client.DownloadCount);
        Assert.Equal(0, client.ApplyCount);
    }

    [Fact]
    public async Task CheckDownloadAndScheduleAsync_WhenUpdateExists_DownloadsAndSchedulesSilentApply()
    {
        var client = new FakeUpdateClient(isInstalled: true, updateAvailable: true);

        var scheduled = await new SilentUpdateService(client).CheckDownloadAndScheduleAsync(CancellationToken.None);

        Assert.True(scheduled);
        Assert.Equal(1, client.DownloadCount);
        Assert.Equal(1, client.ApplyCount);
    }

    [Fact]
    public async Task CheckDownloadAndScheduleAsync_WhenRestartArgumentsAreProvided_PreservesThemForTheUpdatedApp()
    {
        var client = new FakeUpdateClient(isInstalled: true, updateAvailable: true);

        var scheduled = await new SilentUpdateService(client).CheckDownloadAndScheduleAsync(CancellationToken.None, ["--background"]);

        Assert.True(scheduled);
        var restartArguments = Assert.IsType<string[]>(client.RestartArguments);
        Assert.Equal(["--background"], restartArguments);
    }

    [Fact]
    public async Task CheckDownloadAndScheduleAsync_WhenCancelledDuringCheck_DoesNotDownloadOrApply()
    {
        var client = new FakeUpdateClient(isInstalled: true, updateAvailable: true) { CheckCompletion = new TaskCompletionSource<bool>() };
        using var cancellationTokenSource = new CancellationTokenSource();
        var operation = new SilentUpdateService(client).CheckDownloadAndScheduleAsync(cancellationTokenSource.Token);

        cancellationTokenSource.Cancel();
        client.CheckCompletion.SetResult(true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
        Assert.Equal(0, client.DownloadCount);
        Assert.Equal(0, client.ApplyCount);
    }

    [Fact]
    public async Task CheckDownloadAndScheduleAsync_WhenCancelledDuringDownload_DoesNotApply()
    {
        var client = new FakeUpdateClient(isInstalled: true, updateAvailable: true) { DownloadCompletion = new TaskCompletionSource() };
        using var cancellationTokenSource = new CancellationTokenSource();
        var operation = new SilentUpdateService(client).CheckDownloadAndScheduleAsync(cancellationTokenSource.Token);
        await client.DownloadStarted.Task;

        cancellationTokenSource.Cancel();
        client.DownloadCompletion.SetResult();

        await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
        Assert.Equal(0, client.ApplyCount);
    }

    [Fact]
    public async Task CheckDownloadAndScheduleAsync_TransientFailureThenSuccess_RetriesExactlyOnce()
    {
        var client = new SequencedCheckUpdateClient([new HttpRequestException("transient")], updateAvailable: false);
        var delay = new RecordingDelay();

        var scheduled = await new SilentUpdateService(client, delay.DelayAsync).CheckDownloadAndScheduleAsync(CancellationToken.None);

        Assert.False(scheduled);
        Assert.Equal(2, client.CheckCount);
        Assert.Equal([TimeSpan.FromSeconds(2)], delay.Delays);
    }

    [Fact]
    public async Task CheckDownloadAndScheduleAsync_MultipleTransientFailuresThenSuccess_UsesTwoAndFiveSecondBackoff()
    {
        var client = new SequencedCheckUpdateClient([new HttpRequestException("first"), new SocketException()], updateAvailable: true);
        var delay = new RecordingDelay();

        var scheduled = await new SilentUpdateService(client, delay.DelayAsync).CheckDownloadAndScheduleAsync(CancellationToken.None);

        Assert.True(scheduled);
        Assert.Equal(3, client.CheckCount);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)], delay.Delays);
        Assert.Equal(1, client.DownloadCount);
        Assert.Equal(1, client.ApplyCount);
    }

    [Fact]
    public async Task CheckDownloadAndScheduleAsync_TransientFailuresExhausted_PropagatesAfterBoundedAttempts()
    {
        var client = new SequencedCheckUpdateClient([new HttpRequestException("1"), new HttpRequestException("2"), new HttpRequestException("3")]);
        var delay = new RecordingDelay();

        await Assert.ThrowsAsync<HttpRequestException>(() => new SilentUpdateService(client, delay.DelayAsync).CheckDownloadAndScheduleAsync(CancellationToken.None));

        Assert.Equal(3, client.CheckCount);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)], delay.Delays);
        Assert.Equal(0, client.DownloadCount);
    }

    [Fact]
    public async Task CheckDownloadAndScheduleAsync_NonTransientException_DoesNotRetry()
    {
        var client = new SequencedCheckUpdateClient([new InvalidOperationException("not transient")]);
        var delay = new RecordingDelay();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SilentUpdateService(client, delay.DelayAsync).CheckDownloadAndScheduleAsync(CancellationToken.None));

        Assert.Equal(1, client.CheckCount);
        Assert.Empty(delay.Delays);
    }

    [Fact]
    public async Task CheckDownloadAndScheduleAsync_ApplicationCancellationDuringCheck_DoesNotRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new SequencedCheckUpdateClient([new OperationCanceledException(cancellation.Token)]);
        var delay = new RecordingDelay();

        await Assert.ThrowsAsync<OperationCanceledException>(() => new SilentUpdateService(client, delay.DelayAsync).CheckDownloadAndScheduleAsync(cancellation.Token));

        Assert.Equal(1, client.CheckCount);
        Assert.Empty(delay.Delays);
    }

    private sealed class RecordingDelay
    {
        public List<TimeSpan> Delays { get; } = [];
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class SequencedCheckUpdateClient : IUpdateClient
    {
        private readonly Queue<Exception> _checkFailures;
        private readonly bool _updateAvailable;

        public SequencedCheckUpdateClient(IEnumerable<Exception> checkFailures, bool updateAvailable = true)
        {
            _checkFailures = new Queue<Exception>(checkFailures);
            _updateAvailable = updateAvailable;
        }

        public bool IsInstalled => true;
        public int CheckCount { get; private set; }
        public int DownloadCount { get; private set; }
        public int ApplyCount { get; private set; }

        public Task<bool> CheckForUpdatesAsync(CancellationToken cancellationToken)
        {
            CheckCount++;
            if (_checkFailures.Count > 0) throw _checkFailures.Dequeue();
            return Task.FromResult(_updateAvailable);
        }

        public Task DownloadUpdatesAsync(CancellationToken cancellationToken) { DownloadCount++; return Task.CompletedTask; }
        public void WaitExitThenApplyUpdates(string[]? restartArguments) => ApplyCount++;
    }

    private sealed class FakeUpdateClient(bool isInstalled, bool updateAvailable = false) : IUpdateClient
    {
        public bool IsInstalled { get; } = isInstalled;
        public int CheckCount { get; private set; }
        public int DownloadCount { get; private set; }
        public int ApplyCount { get; private set; }
        public string[]? RestartArguments { get; private set; }
        public TaskCompletionSource<bool>? CheckCompletion { get; init; }
        public TaskCompletionSource? DownloadCompletion { get; init; }
        public TaskCompletionSource DownloadStarted { get; } = new();

        public Task<bool> CheckForUpdatesAsync(CancellationToken cancellationToken)
        {
            CheckCount++;
            return CheckCompletion?.Task ?? Task.FromResult(updateAvailable);
        }

        public Task DownloadUpdatesAsync(CancellationToken cancellationToken)
        {
            DownloadCount++;
            DownloadStarted.TrySetResult();
            return DownloadCompletion?.Task ?? Task.CompletedTask;
        }

        public void WaitExitThenApplyUpdates(string[]? restartArguments)
        {
            ApplyCount++;
            RestartArguments = restartArguments;
        }
    }
}
