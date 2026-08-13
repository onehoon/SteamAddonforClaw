namespace SteamInputAddonforClaw.Steam;

using SteamInputAddonforClaw.Diagnostics;

public sealed class SteamSessionWatcher : IDisposable
{
    private readonly IRunningAppIdSource _runningAppIdSource;
    private readonly Lock _stateLock = new();
    private SteamSessionState _state = SteamSessionState.FromRunningAppId(0);
    private bool _isStarted;
    private bool _isDisposed;

    public SteamSessionWatcher(IRunningAppIdSource runningAppIdSource)
    {
        _runningAppIdSource = runningAppIdSource ?? throw new ArgumentNullException(nameof(runningAppIdSource));
    }

    public event EventHandler? StateChanged;

    public SteamSessionState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    public void Start()
    {
        ThrowIfDisposed();

        lock (_stateLock)
        {
            if (_isStarted)
            {
                return;
            }

            _isStarted = true;
            _runningAppIdSource.Changed += OnRunningAppIdChanged;
            _state = SteamSessionState.FromRunningAppId(_runningAppIdSource.GetRunningAppId());
            AppLog.Info("Steam", "SteamSessionWatcher started.", ("RunningAppID", _state.RunningAppId), ("IsActive", _state.IsActive));
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            if (!_isStarted)
            {
                return;
            }

            _runningAppIdSource.Changed -= OnRunningAppIdChanged;
            _isStarted = false;
            AppLog.Info("Steam", "SteamSessionWatcher stopped.");
        }
    }

    public void Refresh()
    {
        ThrowIfDisposed();
        RefreshCore();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Stop();
        _isDisposed = true;
        AppLog.Info("Steam", "SteamSessionWatcher disposed.");
        GC.SuppressFinalize(this);
    }

    private void OnRunningAppIdChanged(object? sender, EventArgs e)
    {
        if (_isDisposed) return;
        RefreshCore();
    }

    private void RefreshCore()
    {
        SteamSessionState? changedState = null;

        lock (_stateLock)
        {
            if (!_isStarted)
            {
                return;
            }

            var nextState = SteamSessionState.FromRunningAppId(_runningAppIdSource.GetRunningAppId());
            if (nextState == _state)
            {
                AppLog.Debug("Steam", "Registry notification produced no state change.", ("RunningAppID", nextState.RunningAppId));
                return;
            }

            AppLog.Info("Steam", "Steam session state changed.", ("PreviousRunningAppID", _state.RunningAppId), ("CurrentRunningAppID", nextState.RunningAppId), ("PreviousActive", _state.IsActive), ("CurrentActive", nextState.IsActive));
            _state = nextState;
            changedState = nextState;
        }

        if (changedState is not null)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
