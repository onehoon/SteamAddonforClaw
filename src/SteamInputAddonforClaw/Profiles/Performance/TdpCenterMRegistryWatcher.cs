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
    private readonly Dictionary<ITdpCenterMRegistryEventSource, Action> _handlers = [];
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
            Action handler = () => OnChanged(source.ValueName);
            _handlers[source] = handler;
            source.Changed += handler;
            try
            {
                if (source.TryStart(out var error))
                {
                    started = true;
                    continue;
                }

                source.Changed -= handler;
                AppLog.Warn("Profiles.Tdp", "Center M registry watcher failed to start; other values remain active.",
                    error, ("ValueName", source.ValueName));
            }
            catch (Exception exception)
            {
                source.Changed -= handler;
                AppLog.Warn("Profiles.Tdp", "Center M registry watcher registration threw; other values remain active.",
                    exception, ("ValueName", source.ValueName));
            }
        }

        if (!started)
            AppLog.Warn("Profiles.Tdp", "No Center M registry watcher is active; TDP lifecycle remains available.");
        return started;
    }

    private void OnChanged(string valueName)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            try
            {
                AppLog.Debug("Profiles.Tdp", "Center M change detected", ("ValueName", valueName));
                _onChanged();
            }
            catch (Exception exception) { AppLog.Error("Profiles.Tdp", "Center M registry change handling failed.", exception); }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var source in _sources)
        {
            if (_handlers.TryGetValue(source, out var handler)) source.Changed -= handler;
            try { source.Dispose(); } catch (Exception exception) { AppLog.Warn("Profiles.Tdp", "Center M registry watcher disposal failed.", exception, ("ValueName", source.ValueName)); }
        }
    }

    private static IReadOnlyList<ITdpCenterMRegistryEventSource> CreateSources() =>
        new[]
        {
            new WindowsTdpCenterMRegistryEventSource(WindowsTdpCenterMRegistryEventSource.UserScenarioKeyPath, "Mode"),
            new WindowsTdpCenterMRegistryEventSource(WindowsTdpCenterMRegistryEventSource.UserScenarioKeyPath, "ManualPL1AC"),
            new WindowsTdpCenterMRegistryEventSource(WindowsTdpCenterMRegistryEventSource.UserScenarioKeyPath, "ManualPL2AC"),
            new WindowsTdpCenterMRegistryEventSource(WindowsTdpCenterMRegistryEventSource.UserScenarioKeyPath, "ManualPL1DC"),
            new WindowsTdpCenterMRegistryEventSource(WindowsTdpCenterMRegistryEventSource.UserScenarioKeyPath, "ManualPL2DC"),
            new WindowsTdpCenterMRegistryEventSource(WindowsTdpCenterMRegistryEventSource.AiEngineKeyPath, "AIModeM")
        };
}

internal sealed class WindowsTdpCenterMRegistryEventSource : ITdpCenterMRegistryEventSource
{
    internal const string Hive = "HKEY_LOCAL_MACHINE";
    internal const string UserScenarioKeyPath = @"SOFTWARE\WOW6432Node\MSI\MSI Center M\Component\User Scenario";
    internal const string AiEngineKeyPath = @"SOFTWARE\WOW6432Node\MSI\MSI Center M\Component\AI Engine";
    private readonly ManagementEventWatcher _watcher;

    internal WindowsTdpCenterMRegistryEventSource(string keyPath, string valueName)
    {
        KeyPath = keyPath ?? throw new ArgumentNullException(nameof(keyPath));
        ValueName = valueName ?? throw new ArgumentNullException(nameof(valueName));
        _watcher = new(new ManagementScope(@"\\.\root\default"), new EventQuery(BuildQuery(keyPath, valueName)));
        _watcher.EventArrived += OnEventArrived;
    }

    internal string KeyPath { get; }
    public string ValueName { get; }
    public event Action? Changed;
    internal static string BuildQuery(string keyPath, string valueName)
    {
        var escapedKeyPath = keyPath.Replace(@"\", @"\\");
        return $"SELECT * FROM RegistryValueChangeEvent WHERE Hive = '{WindowsTdpCenterMRegistryEventSource.Hive}' AND KeyPath = '{escapedKeyPath}' AND ValueName = '{valueName}'";
    }

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
