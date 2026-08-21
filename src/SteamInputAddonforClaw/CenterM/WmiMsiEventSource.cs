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
    private bool _adapterDisposed;

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

        try
        {
            if (!TryParseRawCode(propertyValue, out var rawCode)) return;
            EventReceived?.Invoke(new MsiOemEvent(rawCode, CenterMOemEventMapper.Classify(rawCode)));
        }
        finally
        {
            lock (_sync)
            {
                _activeCallbacks--;
                if (_activeCallbacks == 0)
                    _drained.Set();
            }
        }
    }

    private void DisposeAdapterOnce()
    {
        lock (_sync)
        {
            if (_adapterDisposed) return;
            _adapterDisposed = true;
        }
        _adapter.Dispose();
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

    /// <summary>Closes admission, waits for already-admitted callbacks to drain, and disposes the
    /// single native watcher. The production owner performs teardown externally from the event
    /// callback; callbacks that synchronously dispose their own source are unsupported.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _disposed = true;
                _adapter.MsiEventArrived -= OnMsiEventArrived;
            }

        }

        // Wait outside the lock so an admitted callback can decrement the count and signal drain.
        _drained.Wait();
        DisposeAdapterOnce();
    }
}
