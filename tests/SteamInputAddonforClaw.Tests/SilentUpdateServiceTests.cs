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
