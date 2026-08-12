namespace SteamInputAddonforClaw.Routing;

internal static class RoutingTraceContext
{
    private static readonly AsyncLocal<int?> CurrentExecution = new();

    internal static int? Current => CurrentExecution.Value;

    internal static IDisposable Begin(int executionId)
    {
        var previous = CurrentExecution.Value;
        CurrentExecution.Value = executionId;
        return new Scope(previous);
    }

    private sealed class Scope(int? previous) : IDisposable
    {
        public void Dispose() => CurrentExecution.Value = previous;
    }
}
