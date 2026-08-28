using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Lifecycle;

internal sealed class QamHostProcessController : IAsyncDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);
    private readonly object _sync = new();
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly string _executablePath;
    private readonly string _logDirectory;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private Process? _process;
    private bool _bigPictureActive;
    private bool _steamGameActive;
    private bool _stopping;

    internal QamHostProcessController(string runtimeBaseDirectory, string logDirectory, Func<ProcessStartInfo, Process?>? startProcess = null)
    {
        _executablePath = Path.Combine(runtimeBaseDirectory, "qam", "SteamInputAddonforClaw.QamHost.exe");
        _logDirectory = logDirectory;
        _startProcess = startProcess ?? Process.Start;
    }

    internal string ExecutablePath => _executablePath;
    internal bool HasTrackedProcess { get { lock (_sync) return _process is not null; } }

    internal void OnBigPictureStateChanged(bool active)
    {
        lock (_sync)
        {
            if (_stopping) return;
            _bigPictureActive = active;
        }

        _ = ReconcileDesiredStateAsync();
    }

    internal void OnActualRunningAppIdChanged(uint appId)
    {
        lock (_sync)
        {
            if (_stopping) return;
            _steamGameActive = appId != 0;
        }

        _ = ReconcileDesiredStateAsync();
    }

    internal Task StopAsync() => TransitionAsync(forceStop: true);

    private Task ReconcileDesiredStateAsync() => TransitionAsync(forceStop: false);

    private async Task TransitionAsync(bool forceStop)
    {
        await _transition.WaitAsync().ConfigureAwait(false);
        try
        {
            bool desired;
            lock (_sync)
            {
                if (_stopping && !forceStop) return;
                desired = _bigPictureActive || _steamGameActive;
            }

            if (forceStop || !desired) await StopLockedAsync().ConfigureAwait(false);
            else StartLocked();
        }
        finally { _transition.Release(); }
    }

    private void StartLocked()
    {
        lock (_sync)
        {
            if (_stopping) return;
            if (_process is { HasExited: false })
            {
                AppLog.Info("QAM.Host", "QamHost start skipped; already running.", ("PID", _process.Id));
                return;
            }
        }

        if (!File.Exists(_executablePath))
        {
            AppLog.Warn("QAM.Host", "QamHost executable missing.", null, ("Path", _executablePath));
            return;
        }

        AppLog.Info("QAM.Host", "QamHost process launch attempted.", ("Path", _executablePath));
        try
        {
            var startInfo = new ProcessStartInfo(_executablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true
            };
            startInfo.ArgumentList.Add("--managed");
            startInfo.ArgumentList.Add("--log-directory");
            startInfo.ArgumentList.Add(_logDirectory);
            var process = _startProcess(startInfo);
            if (process is null) { AppLog.Warn("QAM.Host", "QamHost process launch returned no process."); return; }
            lock (_sync) _process = process;
            AppLog.Info("QAM.Host", "QamHost process started.", ("PID", process.Id));
        }
        catch (Exception exception)
        {
            AppLog.Warn("QAM.Host", "QamHost process launch failed; Runtime remains active.", exception, ("Path", _executablePath));
        }
    }

    private async Task StopLockedAsync()
    {
        Process? process;
        lock (_sync) process = _process;
        if (process is null) return;

        try
        {
            if (!process.HasExited)
            {
                AppLog.Info("QAM.Host", "QamHost graceful stop requested.", ("PID", process.Id));
                try
                {
                    await process.StandardInput.WriteLineAsync("stop").ConfigureAwait(false);
                    process.StandardInput.Close();
                    await process.WaitForExitAsync().WaitAsync(ShutdownTimeout).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    AppLog.Warn("QAM.Host", "QamHost graceful stop failed; terminating process.", exception, ("PID", process.Id));
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }

            AppLog.Info("QAM.Host", "QamHost process exited.", ("PID", process.Id), ("ExitCode", process.ExitCode));
        }
        catch (Exception exception)
        {
            AppLog.Warn("QAM.Host", "QamHost stop verification failed.", exception, ("PID", process.Id));
        }
        finally
        {
            process.Dispose();
            lock (_sync) { if (ReferenceEquals(_process, process)) _process = null; }
        }
    }

    internal void BeginShutdown() { lock (_sync) _stopping = true; _ = StopAsync(); }

    public async ValueTask DisposeAsync() { lock (_sync) _stopping = true; await StopAsync().ConfigureAwait(false); _transition.Dispose(); }
}
