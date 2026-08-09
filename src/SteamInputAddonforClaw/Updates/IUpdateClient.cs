namespace SteamInputAddonforClaw.Updates;

internal interface IUpdateClient
{
    bool IsInstalled { get; }

    Task<bool> CheckForUpdatesAsync(CancellationToken cancellationToken);

    Task DownloadUpdatesAsync(CancellationToken cancellationToken);

    void WaitExitThenApplyUpdates(string[]? restartArguments);
}
