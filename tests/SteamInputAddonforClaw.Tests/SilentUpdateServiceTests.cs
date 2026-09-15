using System.Net.Http;
using System.Net.Sockets;
using SteamInputAddonforClaw.Updates;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SilentUpdateServiceTests
{
    [Fact]
    public async Task CheckAndDownloadAsync_WhenNotInstalled_SkipsUpdateOperations()
    {
        var client = new FakeUpdateClient(isInstalled: false);
        var downloaded = await new SilentUpdateService(client).CheckAndDownloadAsync(CancellationToken.None);
        Assert.False(downloaded);
        Assert.Equal(0, client.CheckCount);
    }

    [Fact]
    public async Task CheckAndDownloadAsync_WhenNoUpdateExists_DoesNotDownload()
    {
        var client = new FakeUpdateClient(isInstalled: true);
        var downloaded = await new SilentUpdateService(client).CheckAndDownloadAsync(CancellationToken.None);
        Assert.False(downloaded);
        Assert.Equal(1, client.CheckCount);
        Assert.Equal(0, client.DownloadCount);
    }

    [Fact]
    public async Task CheckAndDownloadAsync_WhenUpdateExists_DownloadsWithoutApplying()
    {
        var client = new FakeUpdateClient(isInstalled: true, updateAvailable: true);
        var downloaded = await new SilentUpdateService(client).CheckAndDownloadAsync(CancellationToken.None);
        Assert.True(downloaded);
        Assert.Equal(1, client.DownloadCount);
    }

    [Fact]
    public async Task CheckAndDownloadAsync_WhenCancelledDuringCheck_DoesNotDownload()
    {
        var client = new FakeUpdateClient(isInstalled: true, updateAvailable: true) { CheckCompletion = new TaskCompletionSource<bool>() };
        using var cancellationTokenSource = new CancellationTokenSource();
        var operation = new SilentUpdateService(client).CheckAndDownloadAsync(cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();
        client.CheckCompletion.SetResult(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
        Assert.Equal(0, client.DownloadCount);
    }

    [Fact]
    public async Task CheckAndDownloadAsync_WhenCancelledDuringDownload_StopsWithoutCompleting()
    {
        var client = new FakeUpdateClient(isInstalled: true, updateAvailable: true) { DownloadCompletion = new TaskCompletionSource() };
        using var cancellationTokenSource = new CancellationTokenSource();
        var operation = new SilentUpdateService(client).CheckAndDownloadAsync(cancellationTokenSource.Token);
        await client.DownloadStarted.Task;
        cancellationTokenSource.Cancel();
        client.DownloadCompletion.SetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
    }

    [Fact]
    public async Task CheckAndDownloadAsync_TransientFailureThenSuccess_RetriesExactlyOnce()
    {
        var client = new SequencedCheckUpdateClient([new HttpRequestException("transient")], updateAvailable: false);
        var delay = new RecordingDelay();
        var downloaded = await new SilentUpdateService(client, delay.DelayAsync).CheckAndDownloadAsync(CancellationToken.None);
        Assert.False(downloaded);
        Assert.Equal(2, client.CheckCount);
        Assert.Equal([TimeSpan.FromSeconds(2)], delay.Delays);
    }

    [Fact]
    public async Task CheckAndDownloadAsync_MultipleTransientFailuresThenSuccess_UsesTwoAndFiveSecondBackoff()
    {
        var client = new SequencedCheckUpdateClient([new HttpRequestException("first"), new SocketException()], updateAvailable: true);
        var delay = new RecordingDelay();
        var downloaded = await new SilentUpdateService(client, delay.DelayAsync).CheckAndDownloadAsync(CancellationToken.None);
        Assert.True(downloaded);
        Assert.Equal(3, client.CheckCount);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)], delay.Delays);
        Assert.Equal(1, client.DownloadCount);
    }

    [Fact]
    public async Task CheckAndDownloadAsync_TransientFailuresExhausted_PropagatesAfterBoundedAttempts()
    {
        var client = new SequencedCheckUpdateClient([new HttpRequestException("1"), new HttpRequestException("2"), new HttpRequestException("3")]);
        var delay = new RecordingDelay();
        await Assert.ThrowsAsync<HttpRequestException>(() => new SilentUpdateService(client, delay.DelayAsync).CheckAndDownloadAsync(CancellationToken.None));
        Assert.Equal(3, client.CheckCount);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)], delay.Delays);
        Assert.Equal(0, client.DownloadCount);
    }

    [Fact]
    public async Task CheckAndDownloadAsync_NonTransientException_DoesNotRetry()
    {
        var client = new SequencedCheckUpdateClient([new InvalidOperationException("not transient")]);
        var delay = new RecordingDelay();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SilentUpdateService(client, delay.DelayAsync).CheckAndDownloadAsync(CancellationToken.None));
        Assert.Equal(1, client.CheckCount);
        Assert.Empty(delay.Delays);
    }

    [Fact]
    public async Task CheckAndDownloadAsync_ApplicationCancellationDuringCheck_DoesNotRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new SequencedCheckUpdateClient([new OperationCanceledException(cancellation.Token)]);
        var delay = new RecordingDelay();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new SilentUpdateService(client, delay.DelayAsync).CheckAndDownloadAsync(cancellation.Token));
        Assert.Equal(1, client.CheckCount);
        Assert.Empty(delay.Delays);
    }

    [Fact]
    public async Task CheckAndDownloadAsync_DownloadTransientFailureThenSuccess_RetriesDownload()
    {
        var client = new SequencedDownloadUpdateClient([new HttpRequestException("transient")]);
        var delay = new RecordingDelay();
        var downloaded = await new SilentUpdateService(client, delay.DelayAsync).CheckAndDownloadAsync(CancellationToken.None);
        Assert.True(downloaded);
        Assert.Equal(1, client.CheckCount);
        Assert.Equal(2, client.DownloadCount);
        Assert.Equal([TimeSpan.FromSeconds(2)], delay.Delays);
    }

    [Fact]
    public async Task CheckAndDownloadAsync_DownloadTransientFailuresExhausted_PropagatesWithoutApplyPath()
    {
        var client = new SequencedDownloadUpdateClient([new HttpRequestException("1"), new HttpRequestException("2"), new HttpRequestException("3")]);
        var delay = new RecordingDelay();
        await Assert.ThrowsAsync<HttpRequestException>(() => new SilentUpdateService(client, delay.DelayAsync).CheckAndDownloadAsync(CancellationToken.None));
        Assert.Equal(3, client.DownloadCount);
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)], delay.Delays);
    }

    private sealed class SequencedDownloadUpdateClient : IUpdateClient
    {
        private readonly Queue<Exception> _downloadFailures;
        public SequencedDownloadUpdateClient(IEnumerable<Exception> downloadFailures) => _downloadFailures = new(downloadFailures);
        public bool IsInstalled => true;
        public int CheckCount { get; private set; }
        public int DownloadCount { get; private set; }
        public Task<bool> CheckForUpdatesAsync(CancellationToken cancellationToken) { CheckCount++; return Task.FromResult(true); }
        public Task DownloadUpdatesAsync(CancellationToken cancellationToken)
        {
            DownloadCount++;
            if (_downloadFailures.Count > 0) throw _downloadFailures.Dequeue();
            return Task.CompletedTask;
        }
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
            _checkFailures = new(checkFailures);
            _updateAvailable = updateAvailable;
        }
        public bool IsInstalled => true;
        public int CheckCount { get; private set; }
        public int DownloadCount { get; private set; }
        public Task<bool> CheckForUpdatesAsync(CancellationToken cancellationToken)
        {
            CheckCount++;
            if (_checkFailures.Count > 0) throw _checkFailures.Dequeue();
            return Task.FromResult(_updateAvailable);
        }
        public Task DownloadUpdatesAsync(CancellationToken cancellationToken) { DownloadCount++; return Task.CompletedTask; }
    }

    private sealed class FakeUpdateClient(bool isInstalled, bool updateAvailable = false) : IUpdateClient
    {
        public bool IsInstalled { get; } = isInstalled;
        public int CheckCount { get; private set; }
        public int DownloadCount { get; private set; }
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
    }
}
