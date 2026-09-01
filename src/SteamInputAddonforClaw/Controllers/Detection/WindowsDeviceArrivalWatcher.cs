using System.Management;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Controllers.Detection;

/// <summary>
/// Narrow seam over the real <see cref="ManagementEventWatcher"/> lifecycle so the PnP-return
/// recovery trigger can be unit tested deterministically without a WMI provider. Carries no event
/// payload: a Windows Device Arrival is only a wake-up, never proof of which device arrived
/// (work order PR10 section 7).
/// </summary>
internal interface IDeviceArrivalWatcherAdapter : IDisposable
{
    event Action? DeviceArrived;
    bool TryStart(out Exception? error);
}

internal sealed class Win32DeviceChangeWatcherAdapter : IDeviceArrivalWatcherAdapter
{
    // Win32_DeviceChangeEvent.EventType == 2 is Microsoft-documented as "Device Arrival". A broad
    // arrival notification is acceptable because it is not authority -- it only wakes the existing
    // recovery, which then re-enumerates and requires the same strong MSI Claw identity itself.
    private readonly ManagementEventWatcher _watcher = new(
        new ManagementScope(@"\\.\root\CIMV2"),
        new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2"));
    private int _started;

    public event Action? DeviceArrived;

    internal Win32DeviceChangeWatcherAdapter() => _watcher.EventArrived += OnEventArrived;

    private void OnEventArrived(object sender, EventArrivedEventArgs e) => DeviceArrived?.Invoke();

    public bool TryStart(out Exception? error)
    {
        try
        {
            _watcher.Start();
            _started = 1;
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            error = ex;
            return false;
        }
    }

    public void Dispose()
    {
        try { if (_started != 0) _watcher.Stop(); } catch { /* best-effort */ }
        _watcher.EventArrived -= OnEventArrived;
        _watcher.Dispose();
    }
}

/// <summary>
/// One Runtime-owned, event-driven Windows Device Arrival observer. It never polls, holds no device
/// database, and does no VID/PID interpretation -- it only raises <see cref="DeviceArrived"/> so the
/// existing owned-controller recovery can re-run and re-prove the strong MSI Claw identity itself
/// (work order PR10 section 6). On a start failure the Runtime keeps its current ownership and simply
/// has no PnP-return trigger until the next restart; there is deliberately no polling fallback.
/// </summary>
internal sealed class WindowsDeviceArrivalWatcher : IDisposable
{
    private readonly IDeviceArrivalWatcherAdapter _adapter;
    // Serializes Start/Dispose against each other and against in-flight callbacks, mirroring
    // WmiMsiEventSource: Dispose blocks until any admitted callback finishes, so a WMI callback can
    // never escape after the watcher's owner is disposed.
    private readonly Lock _sync = new();
    private readonly ManualResetEventSlim _drained = new(true);
    private int _activeCallbacks;
    private bool _disposed;
    private bool _started;
    private bool _adapterDisposed;

    internal WindowsDeviceArrivalWatcher(IDeviceArrivalWatcherAdapter? adapter = null)
        => _adapter = adapter ?? new Win32DeviceChangeWatcherAdapter();

    /// <summary>Fired on the WMI callback thread for every Windows Device Arrival. Never carries the
    /// arriving device's identity.</summary>
    internal event Action? DeviceArrived;

    /// <summary>One-shot: a repeated call while already started, or any call after
    /// <see cref="Dispose"/>, is refused (returns false, no adapter interaction).</summary>
    internal bool Start()
    {
        lock (_sync)
        {
            if (_disposed || _started) return false;
            _started = true;

            _adapter.DeviceArrived += OnDeviceArrived;
            if (_adapter.TryStart(out var error))
            {
                AppLog.Info("ControllerDetection", "Device arrival watcher started.", ("Event", "DeviceArrivalWatcherStarted"));
                return true;
            }

            _adapter.DeviceArrived -= OnDeviceArrived;
            _started = false;
            AppLog.Warn("ControllerDetection",
                "Device arrival watcher unavailable; PnP-return recovery is disabled until the next Runtime restart.",
                error, ("Event", "DeviceArrivalWatcherUnavailable"));
            return false;
        }
    }

    private void OnDeviceArrived()
    {
        lock (_sync)
        {
            if (_disposed) return;
            if (_activeCallbacks == 0) _drained.Reset();
            _activeCallbacks++;
        }

        try
        {
            DeviceArrived?.Invoke();
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerDetection", "Device arrival handler threw.", exception);
        }
        finally
        {
            lock (_sync)
            {
                _activeCallbacks--;
                if (_activeCallbacks == 0) _drained.Set();
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _disposed = true;
                _adapter.DeviceArrived -= OnDeviceArrived;
                DeviceArrived = null;
            }
        }

        // Wait outside the lock so an admitted callback can decrement the count and signal drain.
        _drained.Wait();

        lock (_sync)
        {
            if (_adapterDisposed) return;
            _adapterDisposed = true;
        }
        _adapter.Dispose();
        AppLog.Info("ControllerDetection", "Device arrival watcher stopped.", ("Event", "DeviceArrivalWatcherStopped"));
    }
}
