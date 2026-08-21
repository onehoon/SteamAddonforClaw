using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Lifecycle;

namespace SteamInputAddonforClaw.Hosting;

internal sealed class RuntimeProcessApplication
{
    private readonly SingleInstanceGate _singleInstanceGate;
    private readonly bool _shouldLaunchFrontend;
    private AddonProcessHost? _processHost;
    private NativeMessageLoop? _messageLoop;
    private int _shutdownRequested;

    internal RuntimeProcessApplication(string[] arguments, SingleInstanceGate singleInstanceGate)
    {
        _singleInstanceGate = singleInstanceGate;
        _shouldLaunchFrontend = ApplicationLifecyclePolicy.ShouldLaunchFrontend(arguments);
    }

    internal void Run()
    {
        AppLog.Info("Runtime", "True-headless Runtime shell entered.", ("LaunchMode", _shouldLaunchFrontend ? "Manual" : "Background"));
        _messageLoop = new NativeMessageLoop();
        _processHost = new AddonProcessHost(_shouldLaunchFrontend ? null : ["--background"]);
        _singleInstanceGate.RegisterActivation(() => _processHost?.RequestFrontendOpen(FrontendOpenReason.RuntimeActivation));
        if (_shouldLaunchFrontend)
            _processHost.RequestFrontendOpen(FrontendOpenReason.InitialManualLaunch);

        try
        {
            var outcome = _processHost.RunStartupAsync().GetAwaiter().GetResult();
            if (outcome == AddonProcessStartupOutcome.UpdateRestartScheduled)
                return;
            if (outcome != AddonProcessStartupOutcome.RuntimeReady)
                return;

            _processHost.InitializeRuntimeAsync().GetAwaiter().GetResult();
            _processHost.TryInitializeTray(RequestRestart, RequestExit);
            _messageLoop.Run(() =>
            {
                _processHost.StartRuntimeEventWatchers();
                // Return to GetMessageW before unrelated startup/reconcile work can hold the hook thread.
                _processHost.StartDeferredRuntimeStartup();
            });
        }
        catch (Exception exception)
        {
            AppLog.Error("Startup", "Runtime startup or message-loop execution failed; exiting cleanly.", exception);
        }
        finally
        {
            AppLog.Info("Runtime", "Runtime process cleanup started.");
            _processHost.BeginProcessShutdown();
            _processHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            AppLog.Info("Runtime", "Runtime process cleanup completed.");
        }
    }

    private void RequestExit()
    {
        var termination = _processHost?.EvaluateUserTermination() ?? new(true, UserTerminationBlockReason.None);
        if (!termination.CanTerminate)
        {
            AppLog.Info("Lifecycle", "Exit request blocked.", ("Reason", termination.Reason));
            return;
        }

        AppLog.Info("Lifecycle", "Exit request accepted.");
        BeginShutdownAndRequestLoopExit();
    }

    private void RequestRestart()
    {
        var termination = _processHost?.EvaluateUserTermination() ?? new(true, UserTerminationBlockReason.None);
        if (!termination.CanTerminate)
        {
            AppLog.Info("Lifecycle", "Restart request blocked.", ("Reason", termination.Reason));
            return;
        }

        try
        {
            var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("The current executable path is unavailable.");
            var restartInfo = new ProcessStartInfo(executablePath) { UseShellExecute = false };
            foreach (var argument in Environment.GetCommandLineArgs().Skip(1).Where(argument => !string.Equals(argument, "--restart", StringComparison.OrdinalIgnoreCase)))
                restartInfo.ArgumentList.Add(argument);
            restartInfo.ArgumentList.Add("--restart");
            if (Process.Start(restartInfo) is null)
                throw new InvalidOperationException("Process.Start returned no replacement process.");

            AppLog.Info("Lifecycle", "Restart request accepted.");
            BeginShutdownAndRequestLoopExit();
        }
        catch (Exception exception)
        {
            AppLog.Error("Lifecycle", "Application restart could not be started.", exception);
        }
    }

    private void BeginShutdownAndRequestLoopExit()
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0) return;
        _processHost?.BeginProcessShutdown();
        try
        {
            _messageLoop?.RequestExit();
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _shutdownRequested, 0);
            AppLog.Error("Runtime", "Native message loop exit could not be requested; a later exit request may retry.", exception);
        }
    }
}
