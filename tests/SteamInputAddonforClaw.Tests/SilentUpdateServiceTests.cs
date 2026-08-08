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

    private sealed class FakeUpdateClient(bool isInstalled, bool updateAvailable = false) : IUpdateClient
    {
        public bool IsInstalled { get; } = isInstalled;
        public int CheckCount { get; private set; }
        public int DownloadCount { get; private set; }
        public int ApplyCount { get; private set; }

        public Task<bool> CheckForUpdatesAsync(CancellationToken cancellationToken)
        {
            CheckCount++;
            return Task.FromResult(updateAvailable);
        }

        public Task DownloadUpdatesAsync(CancellationToken cancellationToken)
        {
            DownloadCount++;
            return Task.CompletedTask;
        }

        public void WaitExitThenApplyUpdates()
        {
            ApplyCount++;
        }
    }
}
