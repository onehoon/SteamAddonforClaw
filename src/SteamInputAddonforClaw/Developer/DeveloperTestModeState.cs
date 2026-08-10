namespace SteamInputAddonforClaw.Developer;

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

        handlers?.Invoke(this, EventArgs.Empty);
    }
}
