using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

internal readonly record struct Oem1GesturePolicyRequest(Oem1Gesture Gesture);

/// <summary>
/// Bridges already-classified OEM events to the dormant OEM1 gesture policy boundary.
/// Lifecycle authority is supplied by the owner; this type never queries the lifecycle coordinator.
/// </summary>
internal sealed class Oem1EventGestureBridge : IDisposable
{
    [ThreadStatic]
    private static int _policyDeliveryDepth;

    private readonly object _gate = new();
    private readonly object _recognizerOperationGate = new();
    private readonly object _policyDeliveryGate = new();
    private readonly IMsiEventSource _eventSource;
    private readonly Oem1GestureRecognizer _recognizer;
    private readonly Action? _recognizerOperationEntered;
    private readonly Action? _authorityInvalidationEntered;
    private bool _customAuthority;
    private bool _disposed;
    private long _authorityEpoch;
    private long _activeRecognizerDeliveryEpoch;
    private int _activePolicyDeliveries;

    internal Oem1EventGestureBridge(
        IMsiEventSource eventSource,
        Oem1GestureRecognizer recognizer,
        Action? recognizerOperationEntered = null,
        Action? authorityInvalidationEntered = null)
    {
        _eventSource = eventSource ?? throw new ArgumentNullException(nameof(eventSource));
        _recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));
        _recognizerOperationEntered = recognizerOperationEntered;
        _authorityInvalidationEntered = authorityInvalidationEntered;
        _eventSource.EventReceived += OnMsiEvent;
        _recognizer.GestureRecognizedWithEpoch += OnGestureRecognized;
    }

    internal event Action<Oem1GesturePolicyRequest>? PolicyRequested;

    internal void SetCustomAuthority(bool active)
    {
        var shouldDrain = false;
        lock (_recognizerOperationGate)
        {
            var resetRecognizer = false;
            lock (_gate)
            {
                if (_disposed || _customAuthority == active)
                    return;

                _customAuthority = active;
                _authorityEpoch++;
                resetRecognizer = !active;
                if (active)
                    _activeRecognizerDeliveryEpoch = _recognizer.CurrentDeliveryEpoch;
                shouldDrain = !active;
            }

            if (resetRecognizer)
            {
                _authorityInvalidationEntered?.Invoke();
                _recognizer.InvalidatePending();
            }
        }

        if (shouldDrain)
            WaitForPolicyDeliveriesToDrain();
    }

    public void Dispose()
    {
        var shouldDrain = false;
        lock (_recognizerOperationGate)
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _customAuthority = false;
                _authorityEpoch++;
                shouldDrain = true;
                _eventSource.EventReceived -= OnMsiEvent;
                _recognizer.GestureRecognizedWithEpoch -= OnGestureRecognized;
            }

            _recognizer.InvalidatePending();
        }

        if (shouldDrain)
            WaitForPolicyDeliveriesToDrain();
    }

    private void OnMsiEvent(MsiOemEvent msiEvent)
    {
        if (msiEvent.Code != CenterMOemCode.Oem1)
            return;

        long authorityEpoch;
        lock (_gate)
        {
            if (_disposed || !_customAuthority)
                return;

            authorityEpoch = _authorityEpoch;
        }

        lock (_recognizerOperationGate)
        {
            _recognizerOperationEntered?.Invoke();
            lock (_gate)
            {
                if (_disposed || !_customAuthority || authorityEpoch != _authorityEpoch)
                    return;
            }

            // Do not hold the bridge gate while entering the recognizer. The recognizer
            // may synchronously deliver a gesture back to this bridge.
            _recognizer.OnPress();
        }
    }

    private void OnGestureRecognized(Oem1Gesture gesture, long deliveryEpoch)
    {
        lock (_policyDeliveryGate)
        {
            lock (_gate)
            {
                if (_disposed || !_customAuthority || deliveryEpoch != _activeRecognizerDeliveryEpoch)
                    return;

                _activePolicyDeliveries++;
            }

            _policyDeliveryDepth++;
        }

        try
        {
            PolicyRequested?.Invoke(new Oem1GesturePolicyRequest(gesture));
        }
        catch (Exception exception)
        {
            AppLog.Warn("CenterM.Oem1", "OEM1 gesture policy subscriber failed; observation continues.", exception);
        }
        finally
        {
            _policyDeliveryDepth--;
            lock (_policyDeliveryGate)
            {
                _activePolicyDeliveries--;
                Monitor.PulseAll(_policyDeliveryGate);
            }
        }
    }

    private void WaitForPolicyDeliveriesToDrain()
    {
        if (_policyDeliveryDepth != 0)
            return;

        lock (_policyDeliveryGate)
        {
            while (_activePolicyDeliveries != 0)
                Monitor.Wait(_policyDeliveryGate);
        }
    }
}
