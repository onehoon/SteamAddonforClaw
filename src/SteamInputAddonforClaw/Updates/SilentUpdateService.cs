using Velopack;
using SteamInputAddonforClaw.Diagnostics;

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
            AppLog.Info("Update check skipped because the application is not installed.");
            return false;
        }

        if (!await _updateClient.CheckForUpdatesAsync(cancellationToken).ConfigureAwait(false))
        {
            AppLog.Info("No update is available.");
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _updateClient.DownloadUpdatesAsync(cancellationToken).ConfigureAwait(false);
        AppLog.Info("Update download completed; scheduling silent apply.");
        cancellationToken.ThrowIfCancellationRequested();
        _updateClient.WaitExitThenApplyUpdates();
        return true;
    }
}
