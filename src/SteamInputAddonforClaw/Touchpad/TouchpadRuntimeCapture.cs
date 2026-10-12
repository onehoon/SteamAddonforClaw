using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Touchpad;

internal interface ITouchpadCaptureWindow : IAsyncDisposable
{
    bool IsVisible { get; }
    TouchpadPixelRect Region { get; }
    uint Dpi { get; }
    Task<bool> StartAsync(CancellationToken cancellationToken);
    Task<bool> StopAsync();
    void RequestClose();
}

internal sealed class TouchpadRuntimeCapture : IAsyncDisposable
{
    private readonly Func<TouchpadContactHandoff, ITouchpadCaptureWindow> _windowFactory;
    private readonly object _admissionSync = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly TouchpadContactHandoff _handoff = new();
    private volatile ITouchpadCaptureWindow? _window;
    private int _admitted;
    private int _disposed;

    internal TouchpadRuntimeCapture(Func<TouchpadContactHandoff, ITouchpadCaptureWindow>? windowFactory = null) =>
        _windowFactory = windowFactory ?? (handoff => new TouchpadRightCaptureWindow(handoff));

    internal bool IsArmed => Volatile.Read(ref _admitted) != 0 && _window?.IsVisible == true;
    internal Func<SteamDeckRightPadContact> RightPadProvider => ReadForPublisherTick;

    internal async Task<bool> SetArmedAsync(bool shouldArm, string reason, CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
                return !shouldArm;

            if (!shouldArm)
            {
                var hadContact = _handoff.HasContact;
                Volatile.Write(ref _admitted, 0);
                _handoff.Clear();
                var oldWindow = _window;
                if (oldWindow is null)
                    return true;

                var stopped = await oldWindow.StopAsync().ConfigureAwait(false);
                if (stopped)
                    _window = null;
                AppLog.Info("Touchpad", "Capture disarmed and pending contact state cleared.",
                    ("Event", "TouchpadDisarmed"), ("Reason", reason), ("HadContact", hadContact),
                    ("PendingTransitionsDiscarded", hadContact));
                return stopped;
            }

            if (_window is { IsVisible: true } && Volatile.Read(ref _admitted) != 0)
                return true;

            if (_window is { } staleWindow)
            {
                Volatile.Write(ref _admitted, 0);
                _handoff.Clear();
                if (!await staleWindow.StopAsync().ConfigureAwait(false))
                    return false;
                _window = null;
            }

            _handoff.Clear();
            var window = _windowFactory(_handoff);
            lock (_admissionSync)
            {
                _window = window;
                Volatile.Write(ref _admitted, 0);
            }
            var started = await window.StartAsync(cancellationToken).ConfigureAwait(false);
            var admitted = false;
            if (started)
            {
                lock (_admissionSync)
                {
                    if (window.IsVisible && Volatile.Read(ref _disposed) == 0)
                    {
                        Volatile.Write(ref _admitted, 1);
                        admitted = true;
                    }
                }
            }
            if (!admitted)
            {
                lock (_admissionSync)
                    Volatile.Write(ref _admitted, 0);
                _handoff.Clear();
                if (await window.StopAsync().ConfigureAwait(false))
                    _window = null;
                return false;
            }

            AppLog.Info("Touchpad", "Runtime right-quarter touch capture armed.",
                ("Event", "TouchpadArmed"), ("Reason", reason), ("Presentation", "SteamDeck"),
                ("Region", $"{window.Region.X},{window.Region.Y},{window.Region.Width},{window.Region.Height}"),
                ("Dpi", window.Dpi),
                ("WindowFlags", "WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW|WS_EX_NOREDIRECTIONBITMAP|WS_EX_TOPMOST"));
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Volatile.Write(ref _admitted, 0);
            _handoff.Clear();
            AppLog.Warn("Touchpad", "Touch capture lifecycle operation failed; controller presentation remains unchanged.", exception,
                ("Event", "TouchpadCaptureUnavailable"), ("Reason", reason));
            return false;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    internal void CloseAdmission()
    {
        lock (_admissionSync)
        {
            Volatile.Write(ref _admitted, 0);
            _handoff.Clear();
            _window?.RequestClose();
        }
    }

    private SteamDeckRightPadContact ReadForPublisherTick() =>
        _handoff.ReadForPublisherTick(Volatile.Read(ref _admitted) != 0);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Volatile.Write(ref _admitted, 0);
        _handoff.Clear();
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_window is { } window)
            {
                if (await window.StopAsync().ConfigureAwait(false))
                    _window = null;
            }
        }
        finally
        {
            _lifecycle.Release();
            _lifecycle.Dispose();
        }
    }
}
