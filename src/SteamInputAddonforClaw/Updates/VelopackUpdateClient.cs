using Velopack;
using Velopack.Sources;

namespace SteamInputAddonforClaw.Updates;

internal sealed class VelopackUpdateClient : IUpdateClient
{
    private const string RepositoryUrl = "https://github.com/onehoon/SteamInputAddonforClaw";
    private readonly UpdateManager _updateManager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    private UpdateInfo? _availableUpdate;

    public bool IsInstalled => _updateManager.IsInstalled;

    public async Task<bool> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _availableUpdate = await _updateManager.CheckForUpdatesAsync().ConfigureAwait(false);
        return _availableUpdate is not null;
    }

    public Task DownloadUpdatesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _updateManager.DownloadUpdatesAsync(_availableUpdate ?? throw new InvalidOperationException("No update is available to download."));
    }

    public void WaitExitThenApplyUpdates() =>
        _updateManager.WaitExitThenApplyUpdates((_availableUpdate ?? throw new InvalidOperationException("No update is available to apply.")).TargetFullRelease, silent: true, restart: true, restartArgs: null);
}
