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
}
