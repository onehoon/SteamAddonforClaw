using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Updates;

/// <summary>Owns the Main UI update card's check/download state. Applying an update is deliberately
/// not performed here: the existing safe primary-process startup path applies the downloaded update
/// after this coordinator requests a normal Runtime restart.</summary>
internal sealed class FrontendUpdateCoordinator
{
    private readonly VelopackUpdateClient _updateClient;
    private readonly SilentUpdateService _updateService;
    private readonly Func<bool> _requestRestart;
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private FrontendUpdateSnapshot _snapshot;
    private int _backgroundStarted;
    private int _installPendingResponse;

    internal FrontendUpdateCoordinator(
        VelopackUpdateClient updateClient,
        Func<bool> requestRestart,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _updateClient = updateClient ?? throw new ArgumentNullException(nameof(updateClient));
        _updateService = new SilentUpdateService(_updateClient, delay);
        _requestRestart = requestRestart ?? throw new ArgumentNullException(nameof(requestRestart));
        _snapshot = _updateClient.IsInstalled
            ? new(FrontendUpdateState.Idle, "Select Check to look for updates.")
            : FrontendUpdateSnapshot.Unavailable;
    }

    internal event EventHandler? StateInvalidated;

    internal FrontendUpdateSnapshot Capture()
    {
        lock (_stateGate)
        {
            if (_snapshot.State is FrontendUpdateState.Idle or FrontendUpdateState.UpToDate && _updateClient.HasPendingUpdate)
                _snapshot = new(FrontendUpdateState.ReadyToInstall, "An update is downloaded and ready to install.");
            return _snapshot;
        }
    }

    internal async Task<FrontendUpdateSnapshot> CheckAndDownloadAsync(CancellationToken cancellationToken)
    {
        if (!_updateClient.IsInstalled)
            return SetState(FrontendUpdateSnapshot.Unavailable);

        if (!await _operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return Capture();

        try
        {
            SetState(new(FrontendUpdateState.Checking, "Checking for updates…"));
            var downloaded = await _updateService.CheckAndDownloadAsync(cancellationToken).ConfigureAwait(false);
            var next = _updateClient.HasPendingUpdate
                ? new FrontendUpdateSnapshot(FrontendUpdateState.ReadyToInstall, "An update is downloaded and ready to install.")
                : downloaded
                    ? new FrontendUpdateSnapshot(FrontendUpdateState.Failed, "The update was downloaded but is not ready to install. Try again.")
                    : new FrontendUpdateSnapshot(FrontendUpdateState.UpToDate, "You are up to date.");
            return SetState(next);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppLog.Warn("Update", "Interactive update check failed; the Main UI remains available.", exception,
                ("ExceptionType", exception.GetType().Name));
            return SetState(new(FrontendUpdateState.Failed, "The update check failed. Try again."));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    internal Task<FrontendUpdateSnapshot> StartBackgroundAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _backgroundStarted, 1) != 0)
            return Task.FromResult(Capture());

        return RunBackgroundAsync(cancellationToken);
    }

    internal bool InstallPendingResponse => Volatile.Read(ref _installPendingResponse) != 0;

    internal Task<FrontendUpdateInstallResult> InstallAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_updateClient.IsInstalled)
            return Task.FromResult(new FrontendUpdateInstallResult(FrontendUpdateInstallOutcome.Unavailable, SetState(FrontendUpdateSnapshot.Unavailable), "Updates are unavailable in this installation."));

        if (!_updateClient.HasPendingUpdate)
            return Task.FromResult(new FrontendUpdateInstallResult(FrontendUpdateInstallOutcome.NoUpdateReady, SetState(new(FrontendUpdateState.UpToDate, "No downloaded update is ready to install.")), null));

        lock (_stateGate)
        {
            if (_snapshot.State == FrontendUpdateState.Installing || Interlocked.Exchange(ref _installPendingResponse, 1) != 0)
                return Task.FromResult(new FrontendUpdateInstallResult(FrontendUpdateInstallOutcome.Failed, _snapshot, "An update installation is already in progress."));
            _snapshot = new(FrontendUpdateState.Installing, "Restarting to install the update…");
        }
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new FrontendUpdateInstallResult(FrontendUpdateInstallOutcome.Scheduled, Capture(), null));
    }

    /// <summary>Called only after the Install response has been written to the Main UI pipe.</summary>
    internal Task CompleteInstallAfterResponseAsync()
    {
        if (Interlocked.Exchange(ref _installPendingResponse, 0) == 0)
            return Task.CompletedTask;

        if (_requestRestart())
            return Task.CompletedTask;

        SetState(new(FrontendUpdateState.ReadyToInstall, "The Runtime could not be restarted. Try Install update again."));
        return Task.CompletedTask;
    }

    private async Task<FrontendUpdateSnapshot> RunBackgroundAsync(CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            AppLog.Info("Update", "Update.BackgroundCheckStarted", ("Action", "CheckAndDownload"));
            return await CheckAndDownloadAsync(timeoutCancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AppLog.Debug("Update", "Background update operation canceled during Runtime shutdown.", ("ElapsedMs", stopwatch.ElapsedMilliseconds));
            return Capture();
        }
        catch (OperationCanceledException exception)
        {
            AppLog.Warn("Update", "Update.BackgroundCheckFailed", exception,
                ("ElapsedMs", stopwatch.ElapsedMilliseconds), ("ExceptionType", exception.GetType().Name), ("Action", "Continue"));
            return Capture();
        }
        catch (Exception exception)
        {
            AppLog.Warn("Update", "Update.BackgroundCheckFailed", exception,
                ("ElapsedMs", stopwatch.ElapsedMilliseconds), ("ExceptionType", exception.GetType().Name), ("Action", "Continue"));
            return Capture();
        }
    }

    private FrontendUpdateSnapshot SetState(FrontendUpdateSnapshot snapshot)
    {
        lock (_stateGate) _snapshot = snapshot;
        StateInvalidated?.Invoke(this, EventArgs.Empty);
        return snapshot;
    }
}
