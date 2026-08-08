using Velopack;

namespace SteamInputAddonforClaw.Updates;

internal sealed class SilentUpdateService
{
    private readonly IUpdateClient _updateClient;

    public SilentUpdateService(IUpdateClient updateClient)
    {
        _updateClient = updateClient ?? throw new ArgumentNullException(nameof(updateClient));
    }

    public async Task<bool> CheckDownloadAndScheduleAsync(CancellationToken cancellationToken)
    {
        if (!_updateClient.IsInstalled)
        {
            return false;
        }

        if (!await _updateClient.CheckForUpdatesAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _updateClient.DownloadUpdatesAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        _updateClient.WaitExitThenApplyUpdates();
        return true;
    }
}
