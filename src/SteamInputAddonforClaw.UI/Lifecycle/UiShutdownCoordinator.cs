namespace SteamInputAddonforClaw.UI.Lifecycle;

internal sealed class UiShutdownCoordinator
{
    private readonly Func<Task> _disposeFrontend;
    private readonly Action _requestExit;
    private readonly TimeSpan _cleanupTimeout;
    private int _started;

    internal UiShutdownCoordinator(Func<Task> disposeFrontend, Action requestExit, TimeSpan cleanupTimeout)
    {
        _disposeFrontend = disposeFrontend ?? throw new ArgumentNullException(nameof(disposeFrontend));
        _requestExit = requestExit ?? throw new ArgumentNullException(nameof(requestExit));
        _cleanupTimeout = cleanupTimeout;
    }

    internal async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        Task? cleanup = null;
        try
        {
            cleanup = _disposeFrontend();
            await cleanup.WaitAsync(_cleanupTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException) when (cleanup is not null) { ObserveLateFailure(cleanup); }
        catch (Exception) { }
        finally { _requestExit(); }
    }

    private static void ObserveLateFailure(Task cleanup)
        => _ = cleanup.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
