using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

internal readonly record struct Oem1GesturePolicyRequest(Oem1Gesture Gesture);

/// <summary>
/// Bridges already-classified OEM events to the dormant OEM1 gesture policy boundary.
/// Lifecycle authority is supplied by the owner; this type never queries the lifecycle coordinator.
/// </summary>
internal sealed class Oem1EventGestureBridge : IDisposable
{
    private readonly object _gate = new();
    private readonly object _recognizerOperationGate = new();
    private readonly IMsiEventSource _eventSource;
    private readonly Oem1GestureRecognizer _recognizer;
    private bool _customAuthority;
    private bool _disposed;
    private long _authorityEpoch;
    private long _activeRecognizerDeliveryEpoch;

    internal Oem1EventGestureBridge(
        IMsiEventSource eventSource,
        Oem1GestureRecognizer recognizer)
    {
        _eventSource = eventSource ?? throw new ArgumentNullException(nameof(eventSource));
        _recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));
        _eventSource.EventReceived += OnMsiEvent;
        _recognizer.GestureRecognizedWithEpoch += OnGestureRecognized;
    }

    internal event Action<Oem1GesturePolicyRequest>? PolicyRequested;

    internal void SetCustomAuthority(bool active)
    {
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
            }

            if (resetRecognizer)
            {
                _recognizer.InvalidatePending();
            }
        }
    }

    public void Dispose()
    {
        lock (_recognizerOperationGate)
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _customAuthority = false;
                _authorityEpoch++;
                _eventSource.EventReceived -= OnMsiEvent;
                _recognizer.GestureRecognizedWithEpoch -= OnGestureRecognized;
            }

            _recognizer.InvalidatePending();
        }

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

    // Review fix (BLOCKER): the final authority check and the policy-request delivery must be
    // serialized against SetCustomAuthority()/Dispose() using the SAME _recognizerOperationGate those
    // two already hold for their entire duration -- otherwise a gesture that passes the check just
    // before a concurrent revoke/dispose returns could still deliver PolicyRequested afterward,
    // starting the BPM/QAM replacement action after custom authority was already revoked or the
    // bridge was already disposed. _recognizerOperationGate (a plain Monitor lock) is re-entrant, so
    // a production PolicyRequested subscriber may still synchronously call
    // SetCustomAuthority(false)/Dispose() from within this same call without deadlocking.
    private void OnGestureRecognized(Oem1Gesture gesture, long deliveryEpoch)
    {
        lock (_recognizerOperationGate)
        {
            lock (_gate)
            {
                if (_disposed || !_customAuthority || deliveryEpoch != _activeRecognizerDeliveryEpoch)
                    return;
            }

            try
            {
                PolicyRequested?.Invoke(new Oem1GesturePolicyRequest(gesture));
            }
            catch (Exception exception)
            {
                AppLog.Warn("CenterM.Oem1", "OEM1 gesture policy subscriber failed; observation continues.", exception);
            }
        }
    }
}
