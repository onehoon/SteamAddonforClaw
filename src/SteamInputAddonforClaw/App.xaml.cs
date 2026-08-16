using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Diagnostics;
using System.Diagnostics;
using SteamInputAddonforClaw.Hosting;

namespace SteamInputAddonforClaw;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private DispatcherQueue? _dispatcherQueue;
    private bool _showMainWindow;
    private bool _isExplicitExit;
    private int _shutdownStarted;
    private readonly SingleInstanceGate _singleInstanceGate;
    private AddonProcessHost? _processHost;

    public App()
        : this(arguments: null, Program.CurrentSingleInstanceGate ?? throw new InvalidOperationException("The single-instance gate was not initialized."))
    {
    }

    internal App(string[]? arguments, SingleInstanceGate singleInstanceGate)
    {
        _singleInstanceGate = singleInstanceGate;
        _showMainWindow = ApplicationLifecyclePolicy.ShouldShowMainWindow(arguments ?? []);
        AppLog.Info($"Application launch mode: {(_showMainWindow ? "manual" : "background")}.");
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _singleInstanceGate.RegisterActivation(ShowMainWindow);
        _processHost = new AddonProcessHost(_showMainWindow ? null : ["--background"]);
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        var outcome = await _processHost!.RunStartupAsync().ConfigureAwait(false);
        if (outcome == AddonProcessStartupOutcome.UpdateRestartScheduled)
        {
            AppLog.Info("Startup scheduled an update restart.");
            _dispatcherQueue?.TryEnqueue(ExitAfterScheduledUpdate);
            return;
        }

        if (outcome == AddonProcessStartupOutcome.RuntimeReady)
        {
            if (_dispatcherQueue?.TryEnqueue(StartNormalRuntimeDispatched) != true)
            {
                AppLog.Error(
                    "Startup",
                    "Runtime startup could not be dispatched to the UI thread.",
                    new InvalidOperationException("DispatcherQueue rejected runtime startup."));

                _processHost?.CancelStartup();
            }
        }
    }

    private async void StartNormalRuntimeDispatched()
    {
        try
        {
            await StartNormalRuntimeAsync();
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "Startup",
                "Runtime startup failed.",
                exception);

            _isExplicitExit = true;

            try
            {
                ShutdownApplicationOnce();
            }
            catch (Exception cleanupException)
            {
                AppLog.Error(
                    "Startup",
                    "Runtime cleanup after startup failure failed.",
                    cleanupException);
            }

            Exit();
        }
    }

    private async Task StartNormalRuntimeAsync()
    {
        var processHost = _processHost!;
        processHost.InitializeRuntime();
        processHost.StartPowerObservation();
        var frontend = processHost.FrontendControl;
        // Awaited rather than blocked on: this call is in-process today, but the same contract
        // will be served by a named-pipe client in a later revision, where a blocking
        // .GetAwaiter().GetResult() here would stall the UI thread on real IPC I/O.
        var bootstrap = await frontend.GetBootstrapAsync().ConfigureAwait(true);
        _mainWindow = new MainWindow(frontend, bootstrap);
        _mainWindow.Closed += OnMainWindowClosed;
        _mainWindow.AppWindow.Closing += OnMainWindowClosing;

        if (!processHost.TryInitializeTray(ShowMainWindow, RestartApplication, ExitApplication))
        {
            _showMainWindow = true;
        }
        if (_showMainWindow)
        {
            _mainWindow.Activate();
            AppLog.Info("Main window activated.");
        }
        _ = processHost.ReconcileAsync();

    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (!_isExplicitExit)
        {
            return;
        }

        ShutdownApplicationOnce();
    }

    private void ShutdownApplicationOnce()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        if (_processHost is not null)
        {
            _processHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _processHost = null;
        }
        AppLog.Info("Runtime cleanup completed.");
    }

    private void OnMainWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (ApplicationLifecyclePolicy.OnWindowClose(_isExplicitExit) == ApplicationCloseAction.HideWindow && _processHost?.IsTrayAvailable == true)
        {
            args.Cancel = true;
            _mainWindow?.AppWindow.Hide();
            AppLog.Info("Main window hidden by close request.");
            return;
        }

        if (!_isExplicitExit && _processHost?.IsTrayAvailable != true)
        {
            var termination = GetUserTerminationDecision();
            if (!termination.CanTerminate)
            {
                args.Cancel = true;
                AppLog.Info("Lifecycle", "Window close blocked by active routing ownership.", ("Allowed", false), ("Reason", termination.Reason));
                return;
            }
        }

        _isExplicitExit = true;
    }

    private UserTerminationDecision GetUserTerminationDecision() =>
        _processHost?.EvaluateUserTermination() ?? new(true, UserTerminationBlockReason.None);

    private void ShowMainWindow()
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            _showMainWindow = true;
            _mainWindow?.AppWindow.Show();
            _mainWindow?.Activate();
            AppLog.Info("Main window restored from tray.");
        });
    }

    private void ExitApplication()
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            var termination = GetUserTerminationDecision();
            if (!termination.CanTerminate)
            {
                AppLog.Info("Lifecycle", "Exit request blocked by active routing ownership.", ("Allowed", false), ("Reason", termination.Reason));
                return;
            }
            _isExplicitExit = true;
            AppLog.Info("Explicit application exit requested.");
            _mainWindow?.Close();
            ShutdownApplicationOnce();
            Exit();
        });
    }

    private void RestartApplication()
    {
        _dispatcherQueue?.TryEnqueue(() =>
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
                var restartInfo = new ProcessStartInfo(executablePath)
                {
                    UseShellExecute = false
                };

                foreach (var argument in Environment.GetCommandLineArgs().Skip(1).Where(argument => !string.Equals(argument, "--restart", StringComparison.OrdinalIgnoreCase)))
                {
                    restartInfo.ArgumentList.Add(argument);
                }

                restartInfo.ArgumentList.Add("--restart");
                AppLog.Info("App", "Application restart requested.", ("ExecutablePath", executablePath), ("Background", restartInfo.ArgumentList.Contains("--background")), ("Restart", true));
                var process = Process.Start(restartInfo);
                AppLog.Info("App", "Application restart process started.", ("ProcessId", process?.Id), ("Started", process is not null));
                _isExplicitExit = true;
                _mainWindow?.Close();
                ShutdownApplicationOnce();
                Exit();
            }
            catch (Exception exception)
            {
                AppLog.Error("App", "Application restart could not be started.", exception);
            }
        });
    }

    private void ExitAfterScheduledUpdate()
    {
        _isExplicitExit = true;
        AppLog.Info("Update shutdown requested.");
        _processHost?.CancelStartup();
        Exit();
    }
}
