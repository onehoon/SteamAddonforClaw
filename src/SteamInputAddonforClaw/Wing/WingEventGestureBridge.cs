using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.GameBar;

namespace SteamInputAddonforClaw.Wing;

internal sealed class WingEventGestureBridge : IDisposable
{
    private readonly IMsiEventSource _source;
    private readonly WingGestureRecognizer _recognizer;
    private readonly Func<WinGProtectionRoutingStage.AuthoritySnapshot> _authority;
    private readonly WingActionDispatcher _dispatcher;
    private readonly object _gate = new();
    private readonly object _deliveryGate = new();
    private bool _disposed;
    private long _pendingEpoch;

    internal WingEventGestureBridge(IMsiEventSource source, WingGestureRecognizer recognizer, Func<WinGProtectionRoutingStage.AuthoritySnapshot> authority, WingActionDispatcher dispatcher)
    { _source = source; _recognizer = recognizer; _authority = authority; _dispatcher = dispatcher; _source.EventReceived += OnEvent; _recognizer.GestureRecognized += OnGesture; }

    private void OnEvent(MsiOemEvent e)
    {
        if (e.Code != CenterMOemCode.Oem2) return;
        var current = _authority();
        lock (_gate)
        {
            if (_disposed || !current.Active) { AppLog.Debug("Wing.Event", "Event88IgnoredNoRouteAuthority"); return; }
            if (_recognizer.HasPending && _pendingEpoch != current.Epoch) _recognizer.InvalidatePending();
            _pendingEpoch = current.Epoch;
        }
        AppLog.Debug("Wing.Event", "Event88Accepted", ("AuthorityEpoch", current.Epoch));
        _recognizer.OnPress();
    }

    private void OnGesture(WingGesture gesture)
    {
        lock (_deliveryGate)
        {
            var current = _authority();
            lock (_gate)
            {
                if (_disposed || !current.Active || current.Epoch != _pendingEpoch)
                { AppLog.Debug("Wing.Event", "GestureDiscardedAuthorityChanged", ("AuthorityEpoch", _pendingEpoch)); return; }
            }
            _dispatcher.Dispatch(gesture);
        }
    }

    public void Dispose()
    {
        lock (_deliveryGate)
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _source.EventReceived -= OnEvent;
                _recognizer.GestureRecognized -= OnGesture;
            }
            _recognizer.InvalidatePending();
        }
        _recognizer.Dispose();
        _source.Dispose();
    }
}
