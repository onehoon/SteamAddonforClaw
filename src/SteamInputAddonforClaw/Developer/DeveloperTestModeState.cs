namespace SteamInputAddonforClaw.Developer;

using SteamInputAddonforClaw.Diagnostics;

public sealed class DeveloperTestModeState
{
    private readonly Lock _sync = new();
    private bool _enabled;

    public bool IsEnabled
    {
        get { lock (_sync) return _enabled; }
    }

    public event EventHandler? Changed;

    public void SetEnabled(bool enabled)
    {
        EventHandler? handlers;
        lock (_sync)
        {
            if (_enabled == enabled) return;
            _enabled = enabled;
            handlers = Changed;
        }

        AppLog.Info("Developer.TestMode", "Developer Test Mode changed.", ("Enabled", enabled));
        foreach (var handler in handlers?.GetInvocationList().OfType<EventHandler>() ?? [])
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception exception) { AppLog.Warn("Developer.TestMode", "Test Mode subscriber failed.", exception); }
        }
    }
}
