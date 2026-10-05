using SteamInputAddonforClaw.Diagnostics;
using GameBarApi = global::Windows.Gaming.UI.GameBar;

namespace SteamInputAddonforClaw.GameBar;

internal sealed class GameBarStateDiagnosticObserver : IAsyncDisposable
{
    private const string Category = "GameBar.State";
    private readonly Task _initialization;
    private int _disposeRequested;
    private int _visibilityChangedSubscribed;
    private int _inputRedirectedChangedSubscribed;

    private GameBarStateDiagnosticObserver()
    {
        _initialization = Task.Run(Initialize);
    }

    internal static GameBarStateDiagnosticObserver? Start()
    {
        if (!AppLog.IsEnabled(AppLogLevel.Debug)) return null;

        try
        {
            return new GameBarStateDiagnosticObserver();
        }
        catch (Exception exception)
        {
            AppLog.Debug(Category, "ObserverUnavailable",
                ("Operation", "Start"),
                ("Exception", exception.GetType().Name));
            return null;
        }
    }

    private void Initialize()
    {
        if (Volatile.Read(ref _disposeRequested) != 0) return;

        try
        {
            GameBarApi.VisibilityChanged += OnVisibilityChanged;
            Volatile.Write(ref _visibilityChangedSubscribed, 1);
            GameBarApi.IsInputRedirectedChanged += OnIsInputRedirectedChanged;
            Volatile.Write(ref _inputRedirectedChangedSubscribed, 1);
        }
        catch (Exception exception)
        {
            RemoveSubscriptions();
            AppLog.Debug(Category, "ObserverUnavailable",
                ("Operation", "Subscribe"),
                ("Exception", exception.GetType().Name));
            return;
        }

        ReadAndLogState("ObserverStarted");
    }

    private void OnVisibilityChanged(object? sender, object args) => ReadAndLogState("VisibilityChanged");

    private void OnIsInputRedirectedChanged(object? sender, object args) => ReadAndLogState("IsInputRedirectedChanged");

    private void ReadAndLogState(string trigger)
    {
        if (Volatile.Read(ref _disposeRequested) != 0) return;

        try
        {
            var visible = GameBarApi.Visible;
            var isInputRedirected = GameBarApi.IsInputRedirected;
            AppLog.Debug(Category,
                trigger == "ObserverStarted" ? "ObserverStarted" : "StateChanged",
                ("Trigger", trigger),
                ("Visible", visible),
                ("IsInputRedirected", isInputRedirected));
        }
        catch (Exception exception)
        {
            AppLog.Debug(Category, "StateReadFailed",
                ("Trigger", trigger),
                ("Exception", exception.GetType().Name));
        }
    }

    private void RemoveSubscriptions()
    {
        if (Interlocked.Exchange(ref _visibilityChangedSubscribed, 0) != 0)
        {
            try { GameBarApi.VisibilityChanged -= OnVisibilityChanged; }
            catch (Exception exception)
            {
                AppLog.Debug(Category, "ObserverUnavailable",
                    ("Operation", "Unsubscribe"),
                    ("Event", "VisibilityChanged"),
                    ("Exception", exception.GetType().Name));
            }
        }

        if (Interlocked.Exchange(ref _inputRedirectedChangedSubscribed, 0) != 0)
        {
            try { GameBarApi.IsInputRedirectedChanged -= OnIsInputRedirectedChanged; }
            catch (Exception exception)
            {
                AppLog.Debug(Category, "ObserverUnavailable",
                    ("Operation", "Unsubscribe"),
                    ("Event", "IsInputRedirectedChanged"),
                    ("Exception", exception.GetType().Name));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposeRequested, 1);
        try { await _initialization.ConfigureAwait(false); }
        catch (Exception exception)
        {
            AppLog.Debug(Category, "ObserverUnavailable",
                ("Operation", "Initialize"),
                ("Exception", exception.GetType().Name));
        }
        RemoveSubscriptions();
    }
}
