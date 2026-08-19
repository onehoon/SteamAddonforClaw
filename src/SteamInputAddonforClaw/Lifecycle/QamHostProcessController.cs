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
    private Process? _process;
    private bool _stopping;

    internal QamHostProcessController(string runtimeBaseDirectory, string logDirectory)
    {
        _executablePath = Path.Combine(runtimeBaseDirectory, "qam", "SteamInputAddonforClaw.QamHost.exe");
        _logDirectory = logDirectory;
    }

    internal string ExecutablePath => _executablePath;

    internal void OnBigPictureStateChanged(bool active) => _ = TransitionAsync(active);

    internal Task StopAsync() => TransitionAsync(false);

    private async Task TransitionAsync(bool active)
    {
        await _transition.WaitAsync().ConfigureAwait(false);
        try
        {
            if (active) StartLocked();
            else await StopLockedAsync().ConfigureAwait(false);
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
            var process = Process.Start(startInfo);
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
