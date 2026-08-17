using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Hosting;
using SteamInputAddonforClaw.Lifecycle;
using System.Diagnostics;

namespace SteamInputAddonforClaw;

public partial class App : Application
{
    private readonly SingleInstanceGate _singleInstanceGate;
    private AddonProcessHost? _processHost;
    private int _shutdownStarted;

    public App() : this(Program.CurrentSingleInstanceGate ?? throw new InvalidOperationException("The single-instance gate was not initialized.")) { }

    internal App(string[]? arguments, SingleInstanceGate singleInstanceGate) : this(singleInstanceGate) { }

    internal App(SingleInstanceGate singleInstanceGate)
    {
        _singleInstanceGate = singleInstanceGate;
        AppLog.Info("Runtime launch mode: headless frontend server.");
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstanceGate.RegisterActivation(static () => AppLog.Info("Runtime activation received; external UI activation is deferred to #207B."));
        _processHost = new AddonProcessHost(["--background"]);
        _ = StartApplicationAsync();
    }

    private async Task StartApplicationAsync()
    {
        try
        {
            var outcome = await _processHost!.RunStartupAsync().ConfigureAwait(true);
            if (outcome == AddonProcessStartupOutcome.UpdateRestartScheduled) { ExitAfterScheduledUpdate(); return; }
            if (outcome == AddonProcessStartupOutcome.RuntimeReady) await StartNormalRuntimeAsync().ConfigureAwait(true);
        }
        catch (Exception exception) { HandleFatalStartupFailure("Runtime startup failed.", exception); }
    }

    private async Task StartNormalRuntimeAsync()
    {
        var processHost = _processHost!;
        await processHost.InitializeRuntimeAsync().ConfigureAwait(true);
        processHost.StartPowerObservation();
        processHost.TryInitializeTray(
            static () => AppLog.Info("Runtime tray Open is deferred to #207B."),
            RestartApplication,
            ExitApplication);
        _ = processHost.ReconcileAsync();
    }

    private void HandleFatalStartupFailure(string message, Exception exception)
    {
        AppLog.Error("Startup", message, exception);
        ShutdownApplicationOnce();
        Exit();
    }

    private void ShutdownApplicationOnce()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return;
        if (_processHost is not null)
        {
            _processHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _processHost = null;
        }
        AppLog.Info("Runtime cleanup completed.");
    }

    private UserTerminationDecision GetUserTerminationDecision() => _processHost?.EvaluateUserTermination() ?? new(true, UserTerminationBlockReason.None);

    private void ExitApplication()
    {
        var termination = GetUserTerminationDecision();
        if (!termination.CanTerminate)
        {
            AppLog.Info("Lifecycle", "Exit request blocked by active routing ownership.", ("Allowed", false), ("Reason", termination.Reason));
            return;
        }
        AppLog.Info("Explicit Runtime exit requested.");
        ShutdownApplicationOnce();
        Exit();
    }

    private void RestartApplication()
    {
        try
        {
            var termination = GetUserTerminationDecision();
            if (!termination.CanTerminate)
            {
                AppLog.Info("Lifecycle", "Restart request blocked by active routing ownership.", ("Allowed", false), ("Reason", termination.Reason));
                return;
            }
            var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("The current executable path is unavailable.");
            var restartInfo = new ProcessStartInfo(executablePath) { UseShellExecute = false };
            foreach (var argument in Environment.GetCommandLineArgs().Skip(1).Where(argument => !string.Equals(argument, "--restart", StringComparison.OrdinalIgnoreCase))) restartInfo.ArgumentList.Add(argument);
            restartInfo.ArgumentList.Add("--restart");
            Process.Start(restartInfo);
            ShutdownApplicationOnce();
            Exit();
        }
        catch (Exception exception) { AppLog.Error("App", "Application restart could not be started.", exception); }
    }

    private void ExitAfterScheduledUpdate()
    {
        _processHost?.CancelStartup();
        Exit();
    }
}
