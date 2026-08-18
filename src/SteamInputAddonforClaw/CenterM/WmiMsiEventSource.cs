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
    private int _disposed;
    private int _started;

    internal WmiMsiEventSource(IManagementEventWatcherAdapter? adapter = null) =>
        _adapter = adapter ?? new ManagementEventWatcherAdapter();

    public event Action<MsiOemEvent>? EventReceived;

    /// <summary>One-shot: a repeated call while already started, or any call after
    /// <see cref="Dispose"/>, is refused (returns false, no adapter interaction) rather than
    /// risking a duplicated subscription or touching an already-disposed native watcher.</summary>
    public bool Start()
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return false;

        _adapter.MsiEventArrived += OnMsiEventArrived;
        if (_adapter.TryStart(out var error)) return true;

        _adapter.MsiEventArrived -= OnMsiEventArrived;
        _started = 0;
        AppLog.Warn("CenterM.MsiEvent", "MSI_Event WMI watcher failed to start.", error);
        return false;
    }

    private void OnMsiEventArrived(object? propertyValue)
    {
        // Dispose-race guard: a callback already in flight when Dispose() runs must not raise
        // EventReceived to subscribers that may have already torn down.
        if (Volatile.Read(ref _disposed) != 0) return;
        if (!TryParseRawCode(propertyValue, out var rawCode)) return;
        EventReceived?.Invoke(new MsiOemEvent(rawCode, CenterMOemEventMapper.Classify(rawCode)));
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _adapter.MsiEventArrived -= OnMsiEventArrived;
        _adapter.Dispose();
    }
}
