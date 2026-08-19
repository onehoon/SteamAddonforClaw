namespace SteamInputAddonforClaw.QamHost;

internal sealed class QamHostManagedLifetime : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _stopTask;

    private QamHostManagedLifetime(Func<Task<string?>> readLineAsync)
    {
        _stopTask = Task.Run(async () =>
        {
            while (await readLineAsync().ConfigureAwait(false) is { } line &&
                   !string.Equals(line.Trim(), "stop", StringComparison.OrdinalIgnoreCase)) { }
            _cancellation.Cancel();
        });
    }

    internal CancellationToken Token => _cancellation.Token;
    internal Task StopTask => _stopTask;

    internal static QamHostManagedLifetime Start(Func<Task<string?>> readLineAsync) =>
        new(readLineAsync);

    public void Dispose() => _cancellation.Dispose();
}
