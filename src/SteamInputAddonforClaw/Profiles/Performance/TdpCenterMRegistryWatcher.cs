using System.Management;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Profiles.Performance;

internal interface ITdpCenterMRegistryEventSource : IDisposable
{
    string ValueName { get; }
    event Action? Changed;
    bool TryStart(out Exception? error);
}

internal sealed class TdpCenterMRegistryWatcher : IDisposable
{
    private readonly IReadOnlyList<ITdpCenterMRegistryEventSource> _sources;
    private readonly Action _onChanged;
    private int _disposed;

    internal TdpCenterMRegistryWatcher(Action onChanged, IReadOnlyList<ITdpCenterMRegistryEventSource>? sources = null)
    {
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _sources = sources ?? CreateSources();
    }

    internal bool Start()
    {
        var started = false;
        foreach (var source in _sources)
        {
            source.Changed += OnChanged;
            try
            {
                if (source.TryStart(out var error))
                {
                    started = true;
                    continue;
                }

                source.Changed -= OnChanged;
                AppLog.Warn("Profiles.Tdp", "Center M registry watcher failed to start; other values remain active.",
                    error, ("ValueName", source.ValueName));
            }
            catch (Exception exception)
            {
                source.Changed -= OnChanged;
                AppLog.Warn("Profiles.Tdp", "Center M registry watcher registration threw; other values remain active.",
                    exception, ("ValueName", source.ValueName));
            }
        }

        if (!started)
            AppLog.Warn("Profiles.Tdp", "No Center M registry watcher is active; TDP lifecycle remains available.");
        return started;
    }

    private void OnChanged()
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            try { _onChanged(); }
            catch (Exception exception) { AppLog.Error("Profiles.Tdp", "Center M registry change handling failed.", exception); }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var source in _sources)
        {
            source.Changed -= OnChanged;
            try { source.Dispose(); } catch (Exception exception) { AppLog.Warn("Profiles.Tdp", "Center M registry watcher disposal failed.", exception, ("ValueName", source.ValueName)); }
        }
    }

    private static IReadOnlyList<ITdpCenterMRegistryEventSource> CreateSources() =>
        new[] { "Mode", "ManualPL1AC", "ManualPL2AC", "ManualPL1DC", "ManualPL2DC" }
            .Select(valueName => (ITdpCenterMRegistryEventSource)new WindowsTdpCenterMRegistryEventSource(valueName))
            .ToArray();
}

internal sealed class WindowsTdpCenterMRegistryEventSource : ITdpCenterMRegistryEventSource
{
    internal const string Hive = "HKEY_LOCAL_MACHINE";
    internal const string KeyPath = @"SOFTWARE\WOW6432Node\MSI\MSI Center M\Component\User Scenario";
    private readonly ManagementEventWatcher _watcher;

    internal WindowsTdpCenterMRegistryEventSource(string valueName)
    {
        ValueName = valueName ?? throw new ArgumentNullException(nameof(valueName));
        _watcher = new(new ManagementScope(@"\\.\root\default"), new EventQuery(BuildQuery(valueName)));
        _watcher.EventArrived += OnEventArrived;
    }

    public string ValueName { get; }
    public event Action? Changed;
    internal static string BuildQuery(string valueName) =>
        $"SELECT * FROM RegistryValueChangeEvent WHERE Hive = '{Hive}' AND KeyPath = '{KeyPath}' AND ValueName = '{valueName}'";

    public bool TryStart(out Exception? error)
    {
        try { _watcher.Start(); error = null; return true; }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        { error = exception; return false; }
    }

    private void OnEventArrived(object sender, EventArrivedEventArgs args) => Changed?.Invoke();

    public void Dispose()
    {
        _watcher.EventArrived -= OnEventArrived;
        try { _watcher.Stop(); } catch { }
        _watcher.Dispose();
    }
}
