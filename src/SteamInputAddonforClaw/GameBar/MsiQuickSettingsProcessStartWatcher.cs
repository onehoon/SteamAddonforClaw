using System.Management;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.GameBar;

internal readonly record struct MsiQuickSettingsProcessStart(uint ProcessId, uint ParentProcessId, uint SessionId);

internal readonly record struct MsiQuickSettingsProcessStartPayload(
    object? ProcessId,
    object? ParentProcessId,
    object? SessionId);

internal interface IMsiQuickSettingsProcessStartWatcherAdapter : IDisposable
{
    event Action<MsiQuickSettingsProcessStartPayload>? ProcessStartArrived;
    bool TryStart(out Exception? error);
}

internal sealed class Win32MsiQuickSettingsProcessStartWatcherAdapter : IMsiQuickSettingsProcessStartWatcherAdapter
{
    private const string ProcessStartQuery =
        "SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName = 'Gamebar_Widget.exe'";

    private readonly ManagementEventWatcher _watcher = new(
        new ManagementScope(@"\\.\root\CIMV2"),
        new WqlEventQuery(ProcessStartQuery));
    private int _started;

    internal Win32MsiQuickSettingsProcessStartWatcherAdapter() => _watcher.EventArrived += OnEventArrived;

    public event Action<MsiQuickSettingsProcessStartPayload>? ProcessStartArrived;

    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        var eventData = e.NewEvent;
        ProcessStartArrived?.Invoke(new(
            ReadProperty(eventData, "ProcessID"),
            ReadProperty(eventData, "ParentProcessID"),
            ReadProperty(eventData, "SessionID")));
    }

    private static object? ReadProperty(ManagementBaseObject? eventData, string propertyName)
    {
        try
        {
            return eventData?[propertyName];
        }
        catch (ManagementException)
        {
            return null;
        }
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
        catch (Exception exception) when (
            exception is ManagementException or COMException or UnauthorizedAccessException)
        {
            error = exception;
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_started != 0)
                _watcher.Stop();
        }
        catch
        {
            // WMI Stop is best-effort; the callback admission gate is owned by the watcher.
        }

        _watcher.EventArrived -= OnEventArrived;
        ProcessStartArrived = null;
        _watcher.Dispose();
    }
}

/// <summary>
/// One Disabled-authority Runtime's event-driven MSI Quick Settings process-start observer. It is
/// scoped to Gamebar_Widget starts, carries only WMI's PID/parent/session identifiers, and drains
/// admitted callbacks before disposal returns.
/// </summary>
internal sealed class MsiQuickSettingsProcessStartWatcher : IDisposable
{
    private readonly IMsiQuickSettingsProcessStartWatcherAdapter _adapter;
    private readonly Lock _sync = new();
    private readonly ManualResetEventSlim _callbacksDrained = new(true);
    private readonly ManualResetEventSlim _disposeCompleted = new(false);
    private int _activeCallbacks;
    private bool _startAttempted;
    private bool _started;
    private bool _callbackAdmissionOpen;
    private bool _disposed;
    private bool _adapterDisposed;

    internal MsiQuickSettingsProcessStartWatcher(IMsiQuickSettingsProcessStartWatcherAdapter? adapter = null)
        => _adapter = adapter ?? new Win32MsiQuickSettingsProcessStartWatcherAdapter();

    internal event Action<MsiQuickSettingsProcessStart>? ProcessStarted;

    internal bool Start()
    {
        Exception? error;
        bool started;
        lock (_sync)
        {
            if (_disposed || _startAttempted)
                return false;

            _startAttempted = true;
            _callbackAdmissionOpen = true;
            _adapter.ProcessStartArrived += OnProcessStartArrived;
            try
            {
                started = _adapter.TryStart(out error);
            }
            catch (Exception exception)
            {
                started = false;
                error = exception;
            }

            if (started)
            {
                _started = true;
            }
            else
            {
                _callbackAdmissionOpen = false;
                _adapter.ProcessStartArrived -= OnProcessStartArrived;
            }
        }

        if (started)
        {
            AppLog.Info("MsiQuickSettings", "MSI Quick Settings process-start watcher started.",
                ("Event", "MsiQuickSettingsProcessWatcherStarted"));
        }
        else
        {
            AppLog.Warn("MsiQuickSettings", "MSI Quick Settings process-start watcher unavailable; startup reconcile remains active and no polling fallback is used.",
                error, ("Event", "MsiQuickSettingsProcessWatcherUnavailable"),
                ("ExceptionType", error?.GetType().Name),
                ("HResult", error?.HResult));
        }

        return started;
    }

    private void OnProcessStartArrived(MsiQuickSettingsProcessStartPayload payload)
    {
        Action<MsiQuickSettingsProcessStart>? handlers;
        lock (_sync)
        {
            if (_disposed || !_callbackAdmissionOpen)
                return;

            if (_activeCallbacks == 0)
                _callbacksDrained.Reset();
            _activeCallbacks++;
            handlers = ProcessStarted;
        }

        try
        {
            if (!TryParsePayload(payload, out var processStart))
            {
                AppLog.Debug("MsiQuickSettings", "Malformed process-start event was ignored.",
                    ("Event", "MsiQuickSettingsProcessStartMalformed"));
                return;
            }

            handlers?.Invoke(processStart);
        }
        catch (Exception exception)
        {
            AppLog.Warn("MsiQuickSettings", "MSI Quick Settings process-start callback failed.", exception,
                ("Event", "MsiQuickSettingsProcessStartHandlerFailed"));
        }
        finally
        {
            lock (_sync)
            {
                _activeCallbacks--;
                if (_activeCallbacks == 0)
                    _callbacksDrained.Set();
            }
        }
    }

    internal static bool TryParsePayload(
        MsiQuickSettingsProcessStartPayload payload,
        out MsiQuickSettingsProcessStart processStart)
    {
        if (payload.ProcessId is uint processId && processId != 0
            && payload.ParentProcessId is uint parentProcessId
            && payload.SessionId is uint sessionId)
        {
            processStart = new(processId, parentProcessId, sessionId);
            return true;
        }

        processStart = default;
        return false;
    }

    public void Dispose()
    {
        bool ownsDisposal;
        bool wasStarted;
        lock (_sync)
        {
            if (_adapterDisposed)
                return;

            if (_disposed)
            {
                ownsDisposal = false;
                wasStarted = false;
            }
            else
            {
                _disposed = true;
                _callbackAdmissionOpen = false;
                _adapter.ProcessStartArrived -= OnProcessStartArrived;
                ProcessStarted = null;
                wasStarted = _started;
                ownsDisposal = true;
            }
        }

        if (!ownsDisposal)
        {
            _disposeCompleted.Wait();
            return;
        }

        _callbacksDrained.Wait();
        try
        {
            _adapter.Dispose();
        }
        catch (Exception exception)
        {
            AppLog.Warn("MsiQuickSettings", "MSI Quick Settings process-start watcher disposal failed.", exception,
                ("Event", "MsiQuickSettingsProcessWatcherDisposeFailed"));
        }
        finally
        {
            lock (_sync)
                _adapterDisposed = true;
            _disposeCompleted.Set();
        }

        if (wasStarted)
        {
            AppLog.Info("MsiQuickSettings", "MSI Quick Settings process-start watcher stopped.",
                ("Event", "MsiQuickSettingsProcessWatcherStopped"));
        }
    }
}
