using System.Diagnostics;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Contracts.Overlay;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.FrontendTransport;

namespace SteamInputAddonforClaw.Lifecycle;

internal sealed class OverlayProcessController : IAsyncDisposable
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);
    private readonly object _sync = new();
    private readonly SemaphoreSlim _transition = new(1, 1);
    // SF-V2-02 section 17.1: keeps captures/publications sequential when a Show publish and a
    // StateInvalidated refresh land close together. Not a cross-feature transaction -- just avoids
    // two concurrent CaptureDeviceQuickSettingsAsync calls racing to write the connection.
    private readonly SemaphoreSlim _deviceRefreshGate = new(1, 1);
    private readonly string _executablePath;
    private readonly string _logDirectory;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private readonly Func<string, NamedPipeOverlayServer> _serverFactory;
    // OQ5-UI-09: bound once by AddonProcessHost onto the ONE StartupSettingsCoordinator before warm
    // start. FrontendTransport never sees the coordinator -- only these two narrow operations.
    private Func<IReadOnlyList<OverlayTabId>>? _getTabOrder;
    private Func<IReadOnlyList<OverlayTabId>, bool>? _tryChangeTabOrder;
    // SF-V2-02 section 14: bound once by AddonProcessHost onto the ONE _frontendControl. `_mutateDevice`
    // is handed to each new NamedPipeOverlayServer so a request arriving on its read loop can reach
    // Runtime; `_captureDeviceQuickSettings` is used here for the Runtime-initiated publish path.
    private Func<CancellationToken, Task<FrontendDeviceQuickSettingsSnapshot>>? _captureDeviceQuickSettings;
    private Func<OverlayDeviceMutationRequest, CancellationToken, Task<OverlayDeviceMutationResponse>>? _mutateDevice;
    private NamedPipeOverlayServer? _server;
    private Process? _process;
    private bool _visible;
    private bool _stopping;
    private int _disposed;

    // OQ4 section 10: the process/window/transport owner no longer independently finishes a visible
    // Hide on outside-click -- it validates and raises one narrow signal to AddonProcessHost, which
    // runs the unified Overlay-capture retirement path (stop navigation -> Hide -> release gate ->
    // resume). VisibleSessionLost fires only for the concrete crash/disconnect-while-visible case.
    internal event Action? OverlayDismissRequested;
    internal event Action? VisibleSessionLost;

    internal OverlayProcessController(string runtimeBaseDirectory, string logDirectory,
        Func<ProcessStartInfo, Process?>? startProcess = null,
        Func<string, NamedPipeOverlayServer>? serverFactory = null)
    {
        _executablePath = Path.Combine(runtimeBaseDirectory, "overlay", "SteamInputAddonforClaw.Overlay.exe");
        _logDirectory = logDirectory;
        _startProcess = startProcess ?? Process.Start;
        // The default factory reads the bound authority at connection time (StartCoreAsync), which
        // always runs after AddonProcessHost has called BindTabOrderAuthority.
        _serverFactory = serverFactory ?? (pipeName => new NamedPipeOverlayServer(pipeName, _getTabOrder, _tryChangeTabOrder, _mutateDevice));
    }

    // OQ5-UI-09: wire the Overlay tab-order transport to the Runtime settings authority. Must be
    // called before the first warm start; a later call replaces the delegates for the next connection.
    internal void BindTabOrderAuthority(
        Func<IReadOnlyList<OverlayTabId>> getTabOrder,
        Func<IReadOnlyList<OverlayTabId>, bool> tryChangeTabOrder)
    {
        _getTabOrder = getTabOrder ?? throw new ArgumentNullException(nameof(getTabOrder));
        _tryChangeTabOrder = tryChangeTabOrder ?? throw new ArgumentNullException(nameof(tryChangeTabOrder));
    }

    // SF-V2-02 section 14: wire the Overlay Device transport to the ONE _frontendControl. Must be
    // called before the first warm start, same as BindTabOrderAuthority; a later call replaces the
    // delegates for the next connection only (an already-connected Overlay keeps its bound mutate
    // delegate for its lifetime).
    internal void BindDeviceQuickSettingsAuthority(
        Func<CancellationToken, Task<FrontendDeviceQuickSettingsSnapshot>> capture,
        Func<OverlayDeviceMutationRequest, CancellationToken, Task<OverlayDeviceMutationResponse>> mutate)
    {
        _captureDeviceQuickSettings = capture ?? throw new ArgumentNullException(nameof(capture));
        _mutateDevice = mutate ?? throw new ArgumentNullException(nameof(mutate));
    }

    internal string ExecutablePath => _executablePath;
    internal bool HasTrackedProcess { get { lock (_sync) return _process is { HasExited: false }; } }

    // OQ3-A: the single Overlay visibility fact, exposed so AddonProcessHost can enforce the
    // Main UI <-> Overlay visible-surface ordering explicitly instead of relying on a blind toggle.
    internal bool IsVisible { get { lock (_sync) return _visible; } }

    internal async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        var startupRequested = Stopwatch.StartNew();
        AppLog.Debug("Overlay", "Overlay warm start requested.", ("Path", _executablePath));
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
            var started = await StartAsyncUnderTransitionAsync(cancellationToken).ConfigureAwait(false);
            AppLog.Info("Overlay", started ? "Overlay warm start completed." : "Overlay warm start did not become ready.",
                ("ElapsedMs", startupRequested.ElapsedMilliseconds), ("Path", _executablePath));
            return started;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.Warn("Overlay", "Overlay startup failed; continuing without Overlay POC.", exception);
            await StopCurrentAsync().ConfigureAwait(false);
            return false;
        }
        finally { _transition.Release(); }
    }

    // Thin compatibility/test wrapper: flip whatever the current visibility is.
    internal Task ToggleForPocAsync() => SetVisibilityAsync(requestedShow: null);

    // OQ3-A: explicit Show, for the coordinated Main UI -> Overlay path.
    internal Task<bool> ShowAsync() => SetVisibilityAsync(requestedShow: true);

    // OQ3-A: explicit Hide/retire, for the coordinated Overlay -> Main UI path. Idempotent when the
    // Overlay is already hidden. A failed Hide reuses the existing bounded session retirement.
    internal Task<bool> EnsureHiddenAsync() => SetVisibilityAsync(requestedShow: false);

    // OQ4 section 8.1: Runtime -> Overlay semantic navigation. Fire-and-forget, no acknowledgement;
    // the server only delivers it while the connection is Ready and the surface is Visible.
    internal async Task<bool> SendNavigationAsync(OverlayNavigationAction action)
    {
        NamedPipeOverlayServer? server;
        lock (_sync) server = _server;
        if (server is null) return false;
        try { return await server.SendNavigationAsync(action).ConfigureAwait(false); }
        catch (Exception exception)
        {
            AppLog.Warn("Overlay", "Overlay navigation send failed; Overlay remains Runtime-owned.", exception, ("Action", action));
            return false;
        }
    }

    // SF-V2-02 sections 16/17: best-effort Runtime -> Overlay Device state publish. Called after OQ4
    // capture commits on Show, and from a StateInvalidated handler while a captured session stays
    // visible. Never awaited by a caller that must not be delayed -- feature snapshot work is always
    // less important than OQ4 capture/lifecycle timing (section 4.6).
    internal async Task RefreshDeviceQuickSettingsAsync()
    {
        NamedPipeOverlayServer? server;
        lock (_sync) server = _server;
        var capture = _captureDeviceQuickSettings;
        if (server is null || capture is null) return;
        // A cheap pre-check avoids most no-op captures; SendDeviceQuickSettingsStateAsync still
        // re-checks Ready/Visible at write time (section 17.2), which is the actual authority.
        if (!server.IsReady || server.State != OverlayState.Visible) return;

        await _deviceRefreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            FrontendDeviceQuickSettingsSnapshot snapshot;
            try { snapshot = await capture(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception exception)
            {
                // Section 16.3: a whole aggregate capture failure is feature-local -- deliver
                // Unavailable rather than silently skipping the refresh, unless delivery itself is
                // impossible (checked by the send call below via its own live Ready/Visible check).
                AppLog.Warn("Overlay", "Overlay Device Quick Settings capture failed.", exception);
                snapshot = FrontendDeviceQuickSettingsSnapshot.Unavailable;
            }
            try { await server.SendDeviceQuickSettingsStateAsync(snapshot).ConfigureAwait(false); }
            catch (Exception exception)
            {
                AppLog.Warn("Overlay", "Overlay Device Quick Settings publish failed.", exception);
            }
        }
        finally { _deviceRefreshGate.Release(); }
    }

    private async Task<bool> SetVisibilityAsync(bool? requestedShow)
    {
        await _transition.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_sync) if (_stopping) return false;
            if (!await StartAsyncUnderTransitionAsync().ConfigureAwait(false)) return false;

            NamedPipeOverlayServer? server;
            bool show;
            lock (_sync)
            {
                if (requestedShow is { } target && target == _visible) return true;
                server = _server;
                show = requestedShow ?? !_visible;
            }
            if (server is null) return false;
            var command = show ? OverlayCommand.Show : OverlayCommand.Hide;
            var pid = GetProcessId(process: null);
            var requested = Stopwatch.StartNew();
            AppLog.Info("Overlay", "Overlay command requested.", ("Command", command), ("PID", pid));
            if (!await server.SendCommandAsync(command).ConfigureAwait(false))
            {
                AppLog.Warn("Overlay", "Overlay POC command was not acknowledged; retiring the current Overlay session.",
                    null, ("Command", command), ("PID", pid), ("ElapsedMs", requested.ElapsedMilliseconds), ("Action", "RetireSession"));
                await StopCurrentAsync().ConfigureAwait(false);
                return false;
            }
            lock (_sync) _visible = show;
            AppLog.Info("Overlay", "Overlay command acknowledged.", ("Command", command), ("PID", pid), ("ElapsedMs", requested.ElapsedMilliseconds));
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLog.Warn("Overlay", "Overlay POC toggle failed; the next explicit toggle may relaunch it.", exception);
            await StopCurrentAsync().ConfigureAwait(false);
            return false;
        }
        finally { _transition.Release(); }
    }

    // OQ4 section 10: validate the dismissal is for the current visible session, then hand it to
    // AddonProcessHost. The controller no longer sends Hide itself -- that would bypass the
    // release-to-resume gate now that controller capture exists.
    private void OnDismissRequested(NamedPipeOverlayServer source)
    {
        bool relevant;
        lock (_sync) relevant = !_stopping && _visible && ReferenceEquals(_server, source);
        if (!relevant) return;
        AppLog.Info("Overlay", "Overlay dismiss request received; routing to Runtime capture retirement.",
            ("PID", GetProcessId(process: null)), ("Reason", "OutsideClick"));
        OverlayDismissRequested?.Invoke();
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
            _deviceRefreshGate.Dispose();
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
        var server = _serverFactory(FrontendPipeEndpoint.CreateOverlayForCurrentUser());
        var startup = Stopwatch.StartNew();
        await server.StartAsync(cancellationToken).ConfigureAwait(false);
        AppLog.Info("Overlay", "Overlay server ready; launch about to begin.", ("Path", _executablePath));
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
        server.DismissRequested += OnDismissRequested;
        AppLog.Info("Overlay", "Overlay process started.", ("PID", process.Id), ("Path", _executablePath));
        process.EnableRaisingEvents = true;
        process.Exited += OnProcessExited;
        if (!await server.WaitForReadyAsync(ReadyTimeout, cancellationToken).ConfigureAwait(false))
        {
            await StopCurrentAsync().ConfigureAwait(false);
            return false;
        }
        AppLog.Info("Overlay", "Overlay Ready confirmed.", ("PID", process.Id), ("ElapsedMs", startup.ElapsedMilliseconds));
        return true;
    }

    private async void OnProcessExited(object? sender, EventArgs args)
    {
        if (_disposed != 0) return;
        var process = sender as Process;
        bool visible;
        bool stopping;
        int? pid = null;
        int? exitCode = null;
        lock (_sync)
        {
            visible = _visible;
            stopping = _stopping;
            try { pid = process?.Id; } catch { }
            try { exitCode = process?.HasExited == true ? process.ExitCode : null; } catch { }
        }
        AppLog.Warn("Overlay", "Overlay process exited; Overlay POC is disabled until the next explicit toggle.", null,
            ("PID", pid), ("ExitCode", exitCode), ("WasVisible", visible), ("Stopping", stopping));
        // OQ4 section 10.1: an unexpected exit while the surface was visible -- the Runtime must
        // retire any active capture (no Hide needed, the surface is gone). A normal StopCurrentAsync
        // detaches this handler first, so this only fires for real crash/disconnect.
        if (visible && !stopping)
            VisibleSessionLost?.Invoke();
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
            server.DismissRequested -= OnDismissRequested;
            if (sendShutdown)
            {
                var shutdown = Stopwatch.StartNew();
                var sent = await server.SendCommandAsync(OverlayCommand.Shutdown).ConfigureAwait(false);
                AppLog.Info("Overlay", sent ? "Overlay graceful shutdown requested." : "Overlay graceful shutdown command unavailable.",
                    ("PID", GetProcessId(process)), ("Sent", sent), ("ElapsedMs", shutdown.ElapsedMilliseconds));
            }
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
                {
                    AppLog.Warn("Overlay", "Overlay graceful shutdown timed out; process tree termination requested.", null, ("PID", process.Id));
                    process.Kill(entireProcessTree: true);
                    AppLog.Warn("Overlay", "Overlay process tree termination completed.", null, ("PID", process.Id));
                }
                else
                    AppLog.Info("Overlay", "Overlay process exited gracefully.", ("PID", process.Id));
            }
        }
        catch (Exception exception) { AppLog.Warn("Overlay", "Overlay process cleanup failed.", exception); }
        finally { process.Dispose(); }
    }

    private int? GetProcessId(Process? process)
    {
        lock (_sync)
        {
            try { return (process ?? _process)?.Id; } catch { return null; }
        }
    }
}
