namespace SteamInputAddonforClaw.QamHost;

/// <summary>
/// Matches CDP JSON-RPC responses back to the request that sent them, by numeric id.
/// Extracted from <see cref="SteamGamepadUiCdpClient"/> so it can be unit tested without a
/// real WebSocket connection.
/// </summary>
public sealed class CdpCommandCorrelator
{
    private readonly object _gate = new();
    private readonly Dictionary<int, TaskCompletionSource<string>> _pending = new();
    private Exception? _connectionFailure;
    private int _nextId;

    public int NextId() => Interlocked.Increment(ref _nextId);

    public Task<string> RegisterAsync(int id, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_connectionFailure is { } failure)
                return Task.FromException<string>(failure);

            if (_pending.ContainsKey(id))
                throw new InvalidOperationException($"CDP command id {id} is already pending.");

            _pending.Add(id, tcs);
        }

        cancellationToken.Register(() => Cancel(id));
        return tcs.Task;
    }

    /// <summary>Delivers a raw response payload with the given id to its waiter, if any is pending.</summary>
    public bool TryComplete(int id, string rawJson)
    {
        TaskCompletionSource<string>? tcs;
        lock (_gate)
            _pending.Remove(id, out tcs);

        if (tcs is not null)
        {
            return tcs.TrySetResult(rawJson);
        }

        return false;
    }

    public void FailConnection(Exception exception)
    {
        List<TaskCompletionSource<string>> pending;
        lock (_gate)
        {
            _connectionFailure ??= exception;
            pending = [.. _pending.Values];
            _pending.Clear();
        }

        foreach (var tcs in pending)
            tcs.TrySetException(_connectionFailure!);
    }

    private void Cancel(int id)
    {
        TaskCompletionSource<string>? tcs;
        lock (_gate)
            _pending.Remove(id, out tcs);

        if (tcs is not null)
        {
            tcs.TrySetCanceled();
        }
    }
}
