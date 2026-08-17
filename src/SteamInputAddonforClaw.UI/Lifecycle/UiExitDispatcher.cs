namespace SteamInputAddonforClaw.UI.Lifecycle;

internal sealed class UiExitDispatcher
{
    private readonly Func<bool> _hasUiThreadAccess;
    private readonly Func<bool> _enqueueUiExit;
    private readonly Action _exitOnUiThread;
    private readonly Action _terminateWithoutXaml;

    internal UiExitDispatcher(Func<bool> hasUiThreadAccess, Func<bool> enqueueUiExit, Action exitOnUiThread, Action terminateWithoutXaml)
    {
        _hasUiThreadAccess = hasUiThreadAccess;
        _enqueueUiExit = enqueueUiExit;
        _exitOnUiThread = exitOnUiThread;
        _terminateWithoutXaml = terminateWithoutXaml;
    }

    internal void RequestExit()
    {
        if (_hasUiThreadAccess()) { _exitOnUiThread(); return; }
        if (_enqueueUiExit()) return;
        _terminateWithoutXaml();
    }
}
