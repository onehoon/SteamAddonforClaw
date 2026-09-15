using System.Net.Http;
using System.Net.Sockets;
using Velopack;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Updates;

internal sealed class SilentUpdateService
{
    // At most two retries (three total attempts): immediately, then after 2s, then after 5s.
    // This adds at most ~7s of deliberate backoff, well inside the overall two-minute update
    // background update timeout is enforced by AddonProcessHost around this operation.
    private static readonly TimeSpan[] TransientRetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    private readonly IUpdateClient _updateClient;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public SilentUpdateService(IUpdateClient updateClient, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _updateClient = updateClient ?? throw new ArgumentNullException(nameof(updateClient));
        _delay = delay ?? Task.Delay;
    }

    public async Task<bool> CheckAndDownloadAsync(CancellationToken cancellationToken)
    {
        if (!_updateClient.IsInstalled)
        {
            AppLog.Info("Update", "Update.NoUpdate", ("Reason", "NotInstalled"));
            return false;
        }

        if (!await ExecuteWithTransientRetryAsync("check", () => _updateClient.CheckForUpdatesAsync(cancellationToken), cancellationToken).ConfigureAwait(false))
        {
            AppLog.Info("Update", "Update.NoUpdate", ("Reason", "NoUpdate"));
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await ExecuteWithTransientRetryAsync("download", async () => { await _updateClient.DownloadUpdatesAsync(cancellationToken).ConfigureAwait(false); return true; }, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        AppLog.Info("Update", "Update.DownloadCompleted", ("Action", "ApplyOnNextSafePrimaryStartup"));
        return true;
    }

    private async Task<T> ExecuteWithTransientRetryAsync<T>(string operationName, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception exception) when (attempt <= TransientRetryDelays.Length && IsTransientNetworkFailure(exception))
            {
                var retryDelay = TransientRetryDelays[attempt - 1];
                AppLog.Warn("Update", $"Silent update {operationName} attempt failed with a transient network error; retry scheduled.", exception,
                    ("Attempt", attempt), ("MaxAttempts", TransientRetryDelays.Length + 1), ("RetryDelayMs", retryDelay.TotalMilliseconds), ("ExceptionType", exception.GetType().Name));
                await _delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // Only exception shapes that plausibly represent transient network transport failures are
    // retried. Cancellation (whether the caller's own token or the overall update gate timeout)
    // is deliberately excluded here -- both surface as OperationCanceledException on the token
    // this service was given, and neither should ever be converted into a retry. Everything else
    // (invalid application/update state, programming errors, local filesystem errors) is left to
    // the background runner rather than broadened into a retry.
    private static bool IsTransientNetworkFailure(Exception exception)
    {
        if (exception is OperationCanceledException) return false;
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException or SocketException) return true;
        }
        return false;
    }
}
