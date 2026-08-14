namespace SteamInputAddonforClaw.Routing;

/// <summary>
/// Keeps the status view synchronized with the completion of a routing reconciliation,
/// including fail-closed and exception paths.
/// </summary>
internal static class RoutingReconcileStatusRefresh
{
    internal static async Task RunAsync(Func<Task> reconcile, Action requestStatusRefresh)
    {
        ArgumentNullException.ThrowIfNull(reconcile);
        ArgumentNullException.ThrowIfNull(requestStatusRefresh);

        try
        {
            await reconcile().ConfigureAwait(false);
        }
        finally
        {
            requestStatusRefresh();
        }
    }

    internal static async Task<bool> RunResumeFreshAsync(
        Func<CancellationToken, Task<bool>> freshReconcile,
        Func<bool, bool> completeSuppression,
        Func<Task> deferredReconcile,
        Action requestStatusRefresh,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(freshReconcile);
        ArgumentNullException.ThrowIfNull(completeSuppression);
        ArgumentNullException.ThrowIfNull(deferredReconcile);
        ArgumentNullException.ThrowIfNull(requestStatusRefresh);

        var freshSucceeded = false;
        try
        {
            freshSucceeded = await freshReconcile(cancellationToken).ConfigureAwait(false);
            return freshSucceeded;
        }
        finally
        {
            requestStatusRefresh();
            if (completeSuppression(freshSucceeded)) _ = deferredReconcile();
        }
    }
}
