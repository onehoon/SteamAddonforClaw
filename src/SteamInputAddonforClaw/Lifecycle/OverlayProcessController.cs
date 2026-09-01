using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.FrontendTransport;

namespace SteamInputAddonforClaw.Lifecycle;

internal sealed class OverlayProcessController : IAsyncDisposable
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);
    private readonly object _sync = new();
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly string _executablePath;
    private readonly string _logDirectory;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private NamedPipeOverlayServer? _server;
    private Process? _process;
    private bool _visible;
    private bool _stopping;
    private int _disposed;

    internal OverlayProcessController(string runtimeBaseDirectory, string logDirectory, Func<ProcessStartInfo, Process?>? startProcess = null)
    {
        _executablePath = Path.Combine(runtimeBaseDirectory, "overlay", "SteamInputAddonforClaw.Overlay.exe");
        _logDirectory = logDirectory;
        _startProcess = startProcess ?? Process.Start;
    }

    internal string ExecutablePath => _executablePath;
    internal bool HasTrackedProcess { get { lock (_sync) return _process is { HasExited: false }; } }

    internal async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        await _transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync) if (_stopping) return false;
            if (!File.Exists(_executablePath))
            {
                AppLog.Warn("Overlay", "Overlay executable is unavailable; continuing without Overlay POC.", null,
                    ("Path", _executablePath));
                return false;
            }
            return await StartAsyncUnderTransitionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.Warn("Overlay", "Overlay startup failed; continuing without Overlay POC.", exception);
            await StopCurrentAsync().ConfigureAwait(false);
            return false;
        }
        finally { _transition.Release(); }
    }

    internal async Task ToggleForPocAsync()
    {
        await _transition.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_sync) if (_stopping) return;
            if (!await StartAsyncUnderTransitionAsync().ConfigureAwait(false)) return;

            NamedPipeOverlayServer? server;
            bool show;
            lock (_sync)
            {
                server = _server;
                show = !_visible;
            }
            if (server is null) return;
            var command = show ? OverlayCommand.Show : OverlayCommand.Hide;
            if (await server.SendCommandAsync(command).ConfigureAwait(false))
            {
                lock (_sync) _visible = show;
                AppLog.Info("Overlay", $"Overlay POC {(show ? "shown" : "hidden")}.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.Warn("Overlay", "Overlay POC toggle failed; the next explicit toggle may relaunch it.", exception);
            await StopCurrentAsync().ConfigureAwait(false);
        }
        finally { _transition.Release(); }
    }

    internal void BeginShutdown()
    {
        lock (_sync) _stopping = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        BeginShutdown();
        await _transition.WaitAsync().ConfigureAwait(false);
        try { await StopCurrentAsync(sendShutdown: true).ConfigureAwait(false); }
        finally
        {
            _transition.Release();
            _transition.Dispose();
        }
    }

    private async Task<bool> StartAsyncUnderTransitionAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_stopping) return false;
            if (_process is { HasExited: false } && _server?.IsReady == true) return true;
        }

        await StopCurrentAsync().ConfigureAwait(false);
        return await StartCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> StartCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_executablePath)) return false;
        var server = new NamedPipeOverlayServer(FrontendPipeEndpoint.CreateOverlayForCurrentUser());
        await server.StartAsync(cancellationToken).ConfigureAwait(false);
        Process? process;
        try
        {
            process = _startProcess(new ProcessStartInfo
            {
                FileName = _executablePath,
                Arguments = $"--log-directory \"{_logDirectory}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_executablePath) ?? AppContext.BaseDirectory
            });
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        if (process is null)
        {
            await server.DisposeAsync().ConfigureAwait(false);
            return false;
        }
        lock (_sync) { _server = server; _process = process; _visible = false; }
        process.EnableRaisingEvents = true;
        process.Exited += OnProcessExited;
        if (!await server.WaitForReadyAsync(ReadyTimeout, cancellationToken).ConfigureAwait(false))
        {
            await StopCurrentAsync().ConfigureAwait(false);
            return false;
        }
        return true;
    }

    private async void OnProcessExited(object? sender, EventArgs args)
    {
        if (_disposed != 0) return;
        AppLog.Warn("Overlay", "Overlay process exited; Overlay POC is disabled until the next explicit toggle.");
        await _transition.WaitAsync().ConfigureAwait(false);
        try { await StopCurrentAsync().ConfigureAwait(false); }
        finally { _transition.Release(); }
    }

    private async Task StopCurrentAsync(bool sendShutdown = false)
    {
        NamedPipeOverlayServer? server;
        Process? process;
        lock (_sync) { server = _server; process = _process; _server = null; _process = null; _visible = false; }
        if (server is not null)
        {
            if (sendShutdown) await server.SendCommandAsync(OverlayCommand.Shutdown).ConfigureAwait(false);
            await server.DisposeAsync().ConfigureAwait(false);
        }
        if (process is null) return;
        try
        {
            process.Exited -= OnProcessExited;
            if (!process.HasExited)
            {
                var exited = process.WaitForExitAsync();
                if (await Task.WhenAny(exited, Task.Delay(ShutdownTimeout)).ConfigureAwait(false) != exited && !process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) { AppLog.Warn("Overlay", "Overlay process cleanup failed.", exception); }
        finally { process.Dispose(); }
    }
}
