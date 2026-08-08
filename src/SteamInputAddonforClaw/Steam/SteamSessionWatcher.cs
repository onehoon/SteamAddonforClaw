namespace SteamInputAddonforClaw.Steam;

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
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Stop();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    private void OnRunningAppIdChanged(object? sender, EventArgs e)
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
                return;
            }

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
