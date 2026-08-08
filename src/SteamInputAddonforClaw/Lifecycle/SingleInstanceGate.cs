namespace SteamInputAddonforClaw.Lifecycle;

internal sealed class SingleInstanceGate : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private RegisteredWaitHandle? _activationRegistration;
    private bool _disposed;

    internal SingleInstanceGate(string mutexName, string activationEventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        ArgumentException.ThrowIfNullOrWhiteSpace(activationEventName);

        // Create the event before claiming the mutex so a second launch can always notify the primary process.
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, activationEventName);
        _mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        IsPrimaryInstance = createdNew;
    }

    internal static SingleInstanceGate CreateForCurrentUser() => new(
        @"Local\SteamInputAddonforClaw.SingleInstance",
        @"Local\SteamInputAddonforClaw.ActivateExistingInstance");

    internal bool IsPrimaryInstance { get; }

    internal void ActivatePrimaryInstance()
    {
        if (!IsPrimaryInstance)
        {
            _activationEvent.Set();
        }
    }

    internal void RegisterActivation(Action activationHandler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(activationHandler);

        if (!IsPrimaryInstance)
        {
            throw new InvalidOperationException("Only the primary instance can receive activation requests.");
        }

        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                {
                    activationHandler();
                }
            },
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activationRegistration?.Unregister(null);
        _activationEvent.Dispose();
        _mutex.Dispose();
    }
}
