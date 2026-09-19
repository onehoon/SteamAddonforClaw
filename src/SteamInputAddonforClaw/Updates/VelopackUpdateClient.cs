using Velopack;
using Velopack.Sources;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Updates;

internal interface IVelopackUpdateOperations
{
    bool IsInstalled { get; }
    VelopackAsset? UpdatePendingRestart { get; }
    Task<UpdateInfo?> CheckForUpdatesAsync();
    Task DownloadUpdatesAsync(UpdateInfo update, CancellationToken cancellationToken);
    void WaitExitThenApplyUpdates(VelopackAsset update, string[]? restartArguments);
}

internal sealed class VelopackUpdateClient : IUpdateClient
{
    private const string RepositoryUrl = "https://github.com/onehoon/SteamAddonforClaw";
    private readonly IVelopackUpdateOperations _operations;
    private UpdateInfo? _availableUpdate;

    public VelopackUpdateClient() : this(new VelopackUpdateOperations(new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false)))) { }
    internal VelopackUpdateClient(IVelopackUpdateOperations operations) => _operations = operations ?? throw new ArgumentNullException(nameof(operations));

    public bool IsInstalled => _operations.IsInstalled;

    internal bool HasPendingUpdate => _operations.IsInstalled && _operations.UpdatePendingRestart is not null;

    internal bool TrySchedulePendingUpdateApply(string[]? restartArguments)
    {
        if (!_operations.IsInstalled)
            return false;

        try
        {
            var pending = _operations.UpdatePendingRestart;
            if (pending is null)
                return false;

            AppLog.Info("Update", "Update.PendingApplyDetected", ("Action", "ApplyBeforeRuntimeStartup"));
            _operations.WaitExitThenApplyUpdates(pending, restartArguments);
            AppLog.Info("Update", "Update.PendingApplyScheduled", ("Action", "ExitAndRestart"));
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Warn("Update", "Pending update apply could not be scheduled; continuing Runtime startup.", exception,
                ("ExceptionType", exception.GetType().Name), ("Action", "Continue"));
            return false;
        }
    }

    public async Task<bool> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        _availableUpdate = null;
        cancellationToken.ThrowIfCancellationRequested();
        var checkTask = _operations.CheckForUpdatesAsync();
        try
        {
            var update = await checkTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            _availableUpdate = update;
            return update is not null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ObserveLateCheckFailure(checkTask);
            throw;
        }
    }

    public async Task DownloadUpdatesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _operations.DownloadUpdatesAsync(_availableUpdate ?? throw new InvalidOperationException("No update is available to download."), cancellationToken).ConfigureAwait(false);
    }

    private static void ObserveLateCheckFailure(Task<UpdateInfo?> checkTask)
        => _ = checkTask.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private sealed class VelopackUpdateOperations(UpdateManager updateManager) : IVelopackUpdateOperations
    {
        public bool IsInstalled => updateManager.IsInstalled;
        public VelopackAsset? UpdatePendingRestart => updateManager.UpdatePendingRestart;
        public Task<UpdateInfo?> CheckForUpdatesAsync() => updateManager.CheckForUpdatesAsync();
        public Task DownloadUpdatesAsync(UpdateInfo update, CancellationToken cancellationToken) => updateManager.DownloadUpdatesAsync(update, progress: null, cancelToken: cancellationToken);
        public void WaitExitThenApplyUpdates(VelopackAsset update, string[]? restartArguments) => updateManager.WaitExitThenApplyUpdates(update, silent: true, restart: true, restartArgs: restartArguments);
    }
}
