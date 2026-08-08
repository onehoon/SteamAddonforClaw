using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Lifecycle;

internal sealed class SingleInstanceGate : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly Lock _sync = new();
    private RegisteredWaitHandle? _activationRegistration;
    private bool _disposed;

    internal SingleInstanceGate(string mutexName, string activationEventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        ArgumentException.ThrowIfNullOrWhiteSpace(activationEventName);

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, activationEventName);
        _mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        IsPrimaryInstance = createdNew;
        AppLog.Info("SingleInstance", "Single-instance ownership check completed.", ("Primary", IsPrimaryInstance));
    }

    internal static SingleInstanceGate CreateForCurrentUser() => new(
        @"Local\SteamInputAddonforClaw.SingleInstance",
        @"Local\SteamInputAddonforClaw.ActivateExistingInstance");

    internal bool IsPrimaryInstance { get; }

    internal void ActivatePrimaryInstance()
    {
        if (!IsPrimaryInstance)
        {
            var signaled = _activationEvent.Set();
            AppLog.Info("SingleInstance", "Secondary activation signal sent.", ("Success", signaled));
        }
    }

    internal void RegisterActivation(Action activationHandler)
    {
        ArgumentNullException.ThrowIfNull(activationHandler);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!IsPrimaryInstance)
            {
                throw new InvalidOperationException("Only the primary instance can receive activation requests.");
            }
            if (_activationRegistration is not null)
            {
                throw new InvalidOperationException("An activation handler is already registered.");
            }

            _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
                _activationEvent,
                (_, timedOut) => OnActivationSignaled(activationHandler, timedOut),
                state: null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
    }

    public void Dispose()
    {
        RegisteredWaitHandle? registration;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            registration = _activationRegistration;
            _activationRegistration = null;
        }

        registration?.Unregister(null);
        _activationEvent.Dispose();
        _mutex.Dispose();
        AppLog.Info("SingleInstance", "Single-instance gate disposed.");
    }

    private void OnActivationSignaled(Action activationHandler, bool timedOut)
    {
        if (timedOut)
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
        }

        AppLog.Info("SingleInstance", "Primary activation received.");
        activationHandler();
    }
}
