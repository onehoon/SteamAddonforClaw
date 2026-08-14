using System.Threading.Channels;

namespace SteamInputAddonforClaw.Diagnostics;

/// <summary>
/// A bounded, single-background-writer queue: callers enqueue items and return immediately, and a
/// dedicated task processes them one at a time, in order. Used by <see cref="AppLog"/> to keep disk I/O
/// off realtime-sensitive call sites.
/// </summary>
/// <remarks>
/// Extracted as its own type (independent of AppLog's process-wide static state) so its queueing, drop,
/// drain, and shutdown semantics can be unit tested with throwaway instances, without ever shutting down
/// the real, shared AppLog singleton mid test-suite.
/// </remarks>
internal sealed class BufferedEntryWriter<T> : IDisposable
{
    private const int HighPriorityRetryAttempts = 8;

    private readonly Channel<Envelope> _queue;
    private readonly Task _writerTask;
    private readonly Action<T> _process;
    private readonly Func<T, bool> _isHighPriority;
    private long _droppedLowPriorityCount;
    private long _droppedHighPriorityCount;

    public BufferedEntryWriter(int capacity, Action<T> process, Func<T, bool> isHighPriority)
    {
        _process = process;
        _isHighPriority = isHighPriority;
        _queue = Channel.CreateBounded<Envelope>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
        // LongRunning gets a dedicated thread instead of a thread-pool slot. This writer, its
        // Shutdown()/DrainForTests() blocking waits, and (in tests) many throwaway instances of this
        // type all otherwise compete with the shared thread pool; under heavy concurrent load that
        // showed up as multi-second scheduling delays here and, via general pool contention, in
        // unrelated tests elsewhere. A dedicated thread removes the writer from that contention entirely
        // and keeps drain/shutdown latency independent of how busy the thread pool is.
        _writerTask = Task.Factory.StartNew(RunAsync, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }

    /// <summary>Count of items dropped because the queue was saturated and <see cref="isHighPriority"/> returned false.</summary>
    public long DroppedLowPriorityCount => Interlocked.Read(ref _droppedLowPriorityCount);

    /// <summary>Count of high-priority items dropped after retrying because the queue stayed saturated.</summary>
    public long DroppedHighPriorityCount => Interlocked.Read(ref _droppedHighPriorityCount);

    /// <summary>Enqueues an item without blocking the caller. Never throws.</summary>
    public void Enqueue(T item)
    {
        var envelope = new Envelope(item, null);
        if (_queue.Writer.TryWrite(envelope)) return;

        if (!_isHighPriority(item))
        {
            // Routine/low-priority items never make the caller wait: drop and count.
            Interlocked.Increment(ref _droppedLowPriorityCount);
            return;
        }

        // High-priority items are preserved whenever reasonably possible: a short, bounded number of
        // non-blocking retries gives the writer a chance to drain space. The caller is never blocked
        // indefinitely; if the queue is still saturated afterward, the item is dropped and counted.
        for (var attempt = 0; attempt < HighPriorityRetryAttempts; attempt++)
        {
            Thread.SpinWait(200);
            if (_queue.Writer.TryWrite(envelope)) return;
        }
        Interlocked.Increment(ref _droppedHighPriorityCount);
    }

    /// <summary>
    /// Blocks the calling thread until every item enqueued before this call has been processed.
    /// Deterministic (no arbitrary delay): a sentinel envelope is round-tripped through the same
    /// single-reader queue, so FIFO ordering guarantees everything ahead of it has already run.
    /// </summary>
    public void DrainForTests(TimeSpan? timeout = null) => RunOnWriterThreadForTests(null, timeout);

    /// <summary>
    /// Blocks the calling thread until every item enqueued before this call has been processed, then
    /// runs <paramref name="callback"/> on the writer thread itself before returning. Test-only: lets a
    /// test synchronously and safely touch state that the writer thread otherwise owns exclusively (for
    /// example, closing a file handle so a temp directory can be deleted) without introducing a data race.
    /// </summary>
    public void RunOnWriterThreadForTests(Action? callback, TimeSpan? timeout = null)
    {
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var envelope = new Envelope(default, signal, callback);
        if (!_queue.Writer.TryWrite(envelope))
            _queue.Writer.WriteAsync(envelope).AsTask().GetAwaiter().GetResult();

        if (!signal.Task.Wait(timeout ?? TimeSpan.FromSeconds(5)))
            throw new TimeoutException("BufferedEntryWriter did not drain within the allotted time.");
    }

    /// <summary>
    /// Stops accepting new items and waits (bounded by <paramref name="timeout"/>) for queued items to
    /// be processed. Intended to be called once, from real application shutdown. Never blocks the caller
    /// indefinitely: if processing has not finished within the timeout, this returns anyway.
    /// </summary>
    /// <returns>
    /// True if the writer loop is confirmed to have fully stopped (safe for the caller to then touch any
    /// state the writer thread previously owned exclusively); false if the timeout elapsed first, in
    /// which case the writer may still be running and such state must not be touched.
    /// </returns>
    public bool Shutdown(TimeSpan? timeout = null)
    {
        _queue.Writer.TryComplete();
        try { return _writerTask.Wait(timeout ?? TimeSpan.FromSeconds(2)); }
        catch { return false; }
    }

    public void Dispose() { Shutdown(); }

    private async Task RunAsync()
    {
        await foreach (var envelope in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (envelope.Signal is not null)
                {
                    envelope.Callback?.Invoke();
                    envelope.Signal.TrySetResult(true);
                    continue;
                }
                _process(envelope.Item!);
            }
            catch
            {
                // Best-effort infrastructure: a processing failure must never crash the writer loop, and
                // must never recurse back into whatever logging mechanism owns this writer.
            }
        }
    }

    private readonly record struct Envelope(T? Item, TaskCompletionSource<bool>? Signal, Action? Callback = null);
}
