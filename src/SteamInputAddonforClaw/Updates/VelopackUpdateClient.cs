using Velopack;
using Velopack.Sources;

namespace SteamInputAddonforClaw.Updates;

internal interface IVelopackUpdateOperations
{
    bool IsInstalled { get; }
    Task<UpdateInfo?> CheckForUpdatesAsync();
    Task DownloadUpdatesAsync(UpdateInfo update, CancellationToken cancellationToken);
    void WaitExitThenApplyUpdates(UpdateInfo update, string[]? restartArguments);
}

internal sealed class VelopackUpdateClient : IUpdateClient
{
    private const string RepositoryUrl = "https://github.com/onehoon/SteamAddonforClaw";
    private readonly IVelopackUpdateOperations _operations;
    private UpdateInfo? _availableUpdate;

    public VelopackUpdateClient() : this(new VelopackUpdateOperations(new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false)))) { }
    internal VelopackUpdateClient(IVelopackUpdateOperations operations) => _operations = operations ?? throw new ArgumentNullException(nameof(operations));

    public bool IsInstalled => _operations.IsInstalled;

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

    public void WaitExitThenApplyUpdates(string[]? restartArguments) =>
        _operations.WaitExitThenApplyUpdates(_availableUpdate ?? throw new InvalidOperationException("No update is available to apply."), restartArguments);

    private static void ObserveLateCheckFailure(Task<UpdateInfo?> checkTask)
        => _ = checkTask.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private sealed class VelopackUpdateOperations(UpdateManager updateManager) : IVelopackUpdateOperations
    {
        public bool IsInstalled => updateManager.IsInstalled;
        public Task<UpdateInfo?> CheckForUpdatesAsync() => updateManager.CheckForUpdatesAsync();
        public Task DownloadUpdatesAsync(UpdateInfo update, CancellationToken cancellationToken) => updateManager.DownloadUpdatesAsync(update, progress: null, cancelToken: cancellationToken);
        public void WaitExitThenApplyUpdates(UpdateInfo update, string[]? restartArguments) => updateManager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: true, restartArgs: restartArguments);
    }
}
