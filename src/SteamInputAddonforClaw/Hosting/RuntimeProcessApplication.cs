using System.Diagnostics;
using SteamInputAddonforClaw.CenterMStartup;
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
        _singleInstanceGate.RegisterUninstallRequest(RequestExitForUninstall);
        if (_shouldLaunchFrontend)
            _processHost.RequestFrontendOpen(FrontendOpenReason.InitialManualLaunch);

        try
        {
            var outcome = _processHost.RunStartupAsync().GetAwaiter().GetResult();
            if (outcome is AddonProcessStartupOutcome.UpdateRestartScheduled
                or AddonProcessStartupOutcome.UnsupportedHardware
                or AddonProcessStartupOutcome.IndeterminateHardware)
                return;
            if (outcome != AddonProcessStartupOutcome.RuntimeReady)
                return;

            _processHost.InitializeRuntimeAsync().GetAwaiter().GetResult();
            _processHost.TryInitializeTray(RequestRestart);
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

    private void RequestExitForUninstall()
    {
        AppLog.Info("Lifecycle", "Uninstall shutdown request accepted.");
        // PR12 sections 14/15/17: leave the MSI Claw verified stock-safe (MSI authority restored +
        // mandatory Addon startup task removed) BEFORE the Runtime exits. If preparation does NOT
        // succeed (Partial/Unavailable, stock-baseline / HidHide / Center M failure, or a throw), the
        // only controller Runtime and the mandatory startup guarantee MUST stay alive -- do not shut
        // down. The uninstall request is retryable, so a later attempt can succeed. The final Velopack
        // / Windows uninstall interception that gates file removal on this result is PR13.
        StockUninstallPrepareResult? prepare;
        try
        {
            prepare = _processHost?.PrepareForUninstallAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            AppLog.Error("Lifecycle", "Uninstall stock preparation threw; Runtime will remain active.", exception);
            return;
        }

        if (prepare is not { Succeeded: true })
        {
            AppLog.Warn("Lifecycle", "Uninstall stock preparation did not succeed; Runtime will remain active.", null,
                ("Reason", prepare?.Reason ?? "HostUnavailable"));
            return;
        }

        AppLog.Info("Lifecycle", "Uninstall stock preparation succeeded.", ("Reason", prepare.Reason));
        BeginShutdownAndRequestLoopExit();
    }

    private void RequestRestart()
    {
        var termination = _processHost?.EvaluateUserRestart() ?? new(true, UserTerminationBlockReason.None);
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
        _ = ShutdownRuntimeAndRequestLoopExitAsync();
    }

    private async Task ShutdownRuntimeAndRequestLoopExitAsync()
    {
        try
        {
            if (_processHost is not null)
                await _processHost.ShutdownRuntimeBeforeMessageLoopExitAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Error("Runtime", "Runtime shutdown before message-loop exit failed.", exception);
        }
        finally
        {
            if (!TryRequestMessageLoopExit(() => _messageLoop?.RequestExit()))
                Volatile.Write(ref _shutdownRequested, 0);
        }
    }

    internal static bool TryRequestMessageLoopExit(Action requestExit)
    {
        try
        {
            requestExit();
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Error("Runtime", "Native message loop exit could not be requested; a later Exit/Restart request may retry.", exception);
            return false;
        }
    }

    internal static async Task RunShutdownBeforeMessageLoopExitAsync(Func<Task> shutdown, Action requestExit)
    {
        await shutdown().ConfigureAwait(false);
        requestExit();
    }
}
