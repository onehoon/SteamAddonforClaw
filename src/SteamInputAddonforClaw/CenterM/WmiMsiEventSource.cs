using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// Wraps the real <see cref="ManagementEventWatcher"/> lifecycle (Start/Stop/EventArrived/Dispose)
/// behind a narrow seam so <see cref="WmiMsiEventSource"/>'s parsing and start/dispose-race
/// handling can be unit tested deterministically, without a real WMI provider.
/// Carries only the raw "MSIEvt" property value (null if that property could not be read) rather
/// than the real, test-unconstructable <see cref="EventArrivedEventArgs"/> type.
/// </summary>
internal interface IManagementEventWatcherAdapter : IDisposable
{
    event Action<object?>? MsiEventArrived;
    bool TryStart(out Exception? error);
}

internal sealed class ManagementEventWatcherAdapter : IManagementEventWatcherAdapter
{
    private readonly ManagementEventWatcher _watcher = new(
        new ManagementScope(@"\\.\root\WMI"),
        new EventQuery("SELECT * FROM MSI_Event"));
    private int _started;

    public event Action<object?>? MsiEventArrived;

    internal ManagementEventWatcherAdapter() => _watcher.EventArrived += OnEventArrived;

    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        object? propertyValue;
        try { propertyValue = e.NewEvent["MSIEvt"]; }
        catch (ManagementException) { propertyValue = null; }
        MsiEventArrived?.Invoke(propertyValue);
    }

    public bool TryStart(out Exception? error)
    {
        try
        {
            _watcher.Start();
            _started = 1;
            error = null;
            return true;
        }
        catch (ManagementException ex) { error = ex; return false; }
        catch (COMException ex) { error = ex; return false; }
        catch (UnauthorizedAccessException ex) { error = ex; return false; }
    }

    public void Dispose()
    {
        try { if (_started != 0) _watcher.Stop(); } catch { /* best-effort */ }
        _watcher.EventArrived -= OnEventArrived;
        _watcher.Dispose();
    }
}

/// <summary>
/// Production MSI_Event WMI source. Never suppresses, consumes, or delays Event41/Event88 -- this
/// is a passive, observational listener; MSI's own Launcher/Server/MainUI listeners receive the
/// same multicast WMI event whether or not this source exists (see research handoff section 6).
/// </summary>
internal sealed class WmiMsiEventSource : IMsiEventSource
{
    private readonly IManagementEventWatcherAdapter _adapter;
    // Serializes Start/Dispose against each other: without this, Start() could observe
    // "not disposed" and then, mid-way through subscribing/calling into the adapter, lose a race
    // with a concurrent Dispose() that already unsubscribed/disposed the adapter -- reaching
    // TryStart() on an already-disposed native watcher. Holding this lock across each method's
    // entire adapter interaction guarantees subscribe/start and stop/unsubscribe/dispose can never
    // interleave across the disposal boundary, at the cost of Dispose() blocking until any
    // in-flight Start() finishes -- an acceptable, bounded wait for a one-shot lifecycle gate.
    private readonly Lock _sync = new();
    // Signaled whenever no callback is currently admitted (in flight). Dispose() waits on this
    // after closing admission, so it can never return while a callback that was already admitted
    // is still executing/about to invoke EventReceived.
    private readonly ManualResetEventSlim _drained = new(true);
    private int _activeCallbacks;
    private bool _disposed;
    private bool _started;
    // Set/cleared only on the thread currently running an admitted callback, around the
    // subscriber invocation. Lets Dispose() detect it is being called reentrantly from within its
    // own EventReceived subscriber (a subscriber that synchronously triggers teardown) and avoid
    // deadlocking on _drained -- that admitted callback cannot reach its own finally block (which
    // is what would signal _drained) until Dispose() itself returns.
    [ThreadStatic] private static bool _isInsideAdmittedCallback;

    internal WmiMsiEventSource(IManagementEventWatcherAdapter? adapter = null) =>
        _adapter = adapter ?? new ManagementEventWatcherAdapter();

    public event Action<MsiOemEvent>? EventReceived;

    /// <summary>One-shot: a repeated call while already started, or any call after
    /// <see cref="Dispose"/>, is refused (returns false, no adapter interaction) rather than
    /// risking a duplicated subscription or touching an already-disposed native watcher.</summary>
    public bool Start()
    {
        lock (_sync)
        {
            if (_disposed || _started) return false;
            _started = true;

            _adapter.MsiEventArrived += OnMsiEventArrived;
            if (_adapter.TryStart(out var error)) return true;

            _adapter.MsiEventArrived -= OnMsiEventArrived;
            _started = false;
            AppLog.Warn("CenterM.MsiEvent", "MSI_Event WMI watcher failed to start.", error);
            return false;
        }
    }

    private void OnMsiEventArrived(object? propertyValue)
    {
        // Admission: a callback is only let through if not yet disposed, and once admitted it is
        // counted so Dispose() can wait for it to actually finish -- not merely check a flag that
        // could still be true when this method later invokes EventReceived after Dispose() has
        // already returned.
        lock (_sync)
        {
            if (_disposed) return;
            if (_activeCallbacks == 0) _drained.Reset();
            _activeCallbacks++;
        }

        _isInsideAdmittedCallback = true;
        try
        {
            if (!TryParseRawCode(propertyValue, out var rawCode)) return;
            EventReceived?.Invoke(new MsiOemEvent(rawCode, CenterMOemEventMapper.Classify(rawCode)));
        }
        finally
        {
            _isInsideAdmittedCallback = false;
            lock (_sync)
            {
                _activeCallbacks--;
                if (_activeCallbacks == 0) _drained.Set();
            }
        }
    }

    /// <summary>
    /// Parses the MSIEvt property value into a raw event code. A missing (null) or malformed
    /// (non-numeric) property must never be interpreted as OEM1/OEM2 -- this returns false rather
    /// than defaulting to any code.
    /// </summary>
    internal static bool TryParseRawCode(object? propertyValue, out uint rawCode)
    {
        rawCode = 0;
        if (propertyValue is null) return false;

        try
        {
            rawCode = Convert.ToUInt32(propertyValue, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException)
        {
            return false;
        }
    }

    /// <summary>
    /// Closes admission (no callback entering after this point can proceed past its admission
    /// check), then waits for any callback that was already admitted to finish before disposing
    /// the native watcher -- once this returns, no subscriber callback that entered earlier can
    /// still be beginning or completing. Exception: if called reentrantly from within this
    /// source's own admitted callback (e.g. a subscriber that synchronously triggers teardown),
    /// waiting would deadlock against that same callback -- see the reentrant branch below.
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _adapter.MsiEventArrived -= OnMsiEventArrived;
        }

        if (_isInsideAdmittedCallback)
        {
            // Reentrant: this call is running on the same thread as the one and only admitted
            // callback currently in flight (WMI delivers to this source serially). Admission is
            // already closed above, so no further callback can ever be admitted -- it is safe to
            // dispose the native watcher immediately rather than wait for a drain condition that
            // can only become true after this very call returns and the callback unwinds through
            // its own finally block.
            _adapter.Dispose();
            return;
        }

        // Waited outside the lock: an admitted callback needs to acquire _sync itself (on exit) to
        // decrement/signal, so holding it here would deadlock against that. No timeout: a bounded
        // wait that then proceeds anyway would let an already-admitted callback invoke
        // EventReceived after Dispose() has returned -- exactly the state this barrier exists to
        // rule out. Subscriber invocation is expected to be fast (parse + forward); a subscriber
        // that stalls indefinitely is a bug in that subscriber, not something this source should
        // paper over by breaking its own drain guarantee.
        _drained.Wait();
        _adapter.Dispose();
    }
}
