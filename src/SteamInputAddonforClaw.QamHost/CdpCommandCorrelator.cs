using System.Collections.Concurrent;

namespace SteamInputAddonforClaw.QamHost;

/// <summary>
/// Matches CDP JSON-RPC responses back to the request that sent them, by numeric id.
/// Extracted from <see cref="SteamGamepadUiCdpClient"/> so it can be unit tested without a
/// real WebSocket connection.
/// </summary>
public sealed class CdpCommandCorrelator
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<string>> _pending = new();
    private int _nextId;

    public int NextId() => Interlocked.Increment(ref _nextId);

    public Task<string> RegisterAsync(int id, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, tcs))
        {
            throw new InvalidOperationException($"CDP command id {id} is already pending.");
        }

        cancellationToken.Register(() => Cancel(id));
        return tcs.Task;
    }

    /// <summary>Delivers a raw response payload with the given id to its waiter, if any is pending.</summary>
    public bool TryComplete(int id, string rawJson)
    {
        if (_pending.TryRemove(id, out var tcs))
        {
            return tcs.TrySetResult(rawJson);
        }

        return false;
    }

    private void Cancel(int id)
    {
        if (_pending.TryRemove(id, out var tcs))
        {
            tcs.TrySetCanceled();
        }
    }
}
