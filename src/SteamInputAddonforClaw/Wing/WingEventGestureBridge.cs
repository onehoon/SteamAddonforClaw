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

    internal WingEventGestureBridge(IMsiEventSource source, WingGestureRecognizer recognizer, Func<WinGProtectionRoutingStage.AuthoritySnapshot> authority, WingActionDispatcher dispatcher)
    { _source = source; _recognizer = recognizer; _authority = authority; _dispatcher = dispatcher; _source.EventReceived += OnEvent; _recognizer.GestureRecognized += OnGesture; }

    private void OnEvent(MsiOemEvent e)
    {
        if (e.Code != CenterMOemCode.Oem2) return;
        var current = _authority();
        lock (_gate)
        {
            if (_disposed || !current.Active) { AppLog.Debug("Wing.Event", "Event88IgnoredNoRouteAuthority"); return; }
        }
        AppLog.Debug("Wing.Event", "Event88Accepted", ("AuthorityEpoch", current.Epoch));
        try { _recognizer.OnPress(current.Epoch); }
        catch (Exception exception)
        {
            AppLog.Warn("Wing.Event", "WING gesture recognition failed; routing continues.", exception,
                ("AuthorityEpoch", current.Epoch));
        }
    }

    private void OnGesture(WingGestureDelivery delivery)
    {
        lock (_deliveryGate)
        {
            var current = _authority();
            lock (_gate)
            {
                if (_disposed || !current.Active || current.Epoch != delivery.AuthorityEpoch)
                { AppLog.Debug("Wing.Event", "GestureDiscardedAuthorityChanged", ("AuthorityEpoch", delivery.AuthorityEpoch)); return; }
            }
            _dispatcher.Dispatch(delivery.Gesture);
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
