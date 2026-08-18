using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.FrontendTransport;

namespace SteamInputAddonforClaw.Lifecycle;

internal enum FrontendOpenReason { InitialManualLaunch, RuntimeActivation, Tray }

internal sealed class FrontendProcessLauncher
{
    private readonly object _sync = new();
    private readonly string _executablePath;
    private readonly string _logDirectory;
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private bool _runtimeReady;
    private bool _pendingOpen;
    private FrontendOpenReason? _pendingReason;
    private bool _stopping;

    internal FrontendProcessLauncher(string runtimeBaseDirectory, string logDirectory, Func<ProcessStartInfo, Process?>? startProcess = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeBaseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        _executablePath = Path.Combine(runtimeBaseDirectory, "ui", "SteamInputAddonforClaw.UI.exe");
        _logDirectory = logDirectory;
        _startProcess = startProcess ?? Process.Start;
    }

    internal string ExecutablePath => _executablePath;

    internal void RequestOpen(FrontendOpenReason reason)
    {
        lock (_sync)
        {
            if (_stopping) return;
            AppLog.Info("Frontend", "Frontend open requested.", ("Reason", reason));
            if (!_runtimeReady)
            {
                _pendingOpen = true;
                _pendingReason = reason;
                AppLog.Info("Frontend", "Frontend open queued until Runtime readiness.");
                return;
            }
            LaunchLocked(reason);
        }
    }

    internal void MarkRuntimeReady()
    {
        lock (_sync)
        {
            if (_stopping || _runtimeReady) return;
            _runtimeReady = true;
            if (_pendingOpen)
            {
                _pendingOpen = false;
                var reason = _pendingReason ?? FrontendOpenReason.InitialManualLaunch;
                _pendingReason = null;
                AppLog.Info("Frontend", "Queued frontend open released.");
                LaunchLocked(reason);
            }
        }
    }

    internal void StopAcceptingRequests()
    {
        lock (_sync)
        {
            if (_stopping) return;
            _stopping = true;
            _pendingOpen = false;
            _pendingReason = null;
            AppLog.Info("Frontend", "Frontend launcher stopping.");
        }
    }

    private void LaunchLocked(FrontendOpenReason reason)
    {
        var startInfo = new ProcessStartInfo(_executablePath) { UseShellExecute = false };
        startInfo.ArgumentList.Add(FrontendLaunchArguments.LogDirectoryOption);
        startInfo.ArgumentList.Add(_logDirectory);
        AppLog.Info("Frontend", "Frontend process launch attempted.", ("Reason", reason), ("Path", _executablePath));
        try
        {
            if (_startProcess(startInfo) is null)
            {
                AppLog.Warn("Frontend", "Frontend process launch returned no process; Runtime remains active.", null, ("Reason", reason), ("Path", _executablePath));
                return;
            }

            AppLog.Info("Frontend", "Frontend process launch succeeded.", ("Reason", reason));
        }
        catch (Exception exception) { AppLog.Warn("Frontend", "Frontend process launch failed; Runtime remains active.", exception, ("Reason", reason), ("Path", _executablePath)); }
    }
}
