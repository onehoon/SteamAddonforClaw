using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;
using WinRT;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Lifecycle;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;

namespace SteamInputAddonforClaw;

public static class Program
{
    internal static SingleInstanceGate? CurrentSingleInstanceGate { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        // Sole owner of AppLog.Shutdown(): guarantees it runs exactly once, on every exit path out of
        // Main -- the several early `return`s below (elevated prerequisite setup, secondary-instance
        // activation, restart timeout) that never reach a MainWindow at all, the fatal-startup-exception
        // path, and the normal path (Application.Start blocks until the WinUI app -- including
        // App.xaml.cs's OnMainWindowClosed -- has fully exited, so this still runs after that).
        try
        {
            var restartRequested = args.Contains("--restart", StringComparer.OrdinalIgnoreCase);
            VelopackApp.Build().Run();
            var persistedLogLevel = LogLevelBootstrap.Read(VelopackAppPaths.SettingsPath);
            AppLog.MinimumLevelOverride = AppSettingsPolicy.ToAppLogLevel(persistedLogLevel);
            AppLog.Info("App", "Application startup entered.", ("PID", Environment.ProcessId), ("RestartRequested", restartRequested), ("BackgroundRequested", args.Contains("--background", StringComparer.OrdinalIgnoreCase)));
            AppLog.Debug("Velopack", "Velopack bootstrap completed.");
            if (args.Contains(ElevatedPrerequisiteSetup.Argument, StringComparer.OrdinalIgnoreCase))
            {
                Environment.ExitCode = ElevatedPrerequisiteSetup.Run();
                return;
            }

            var restartDeadline = DateTimeOffset.UtcNow.AddSeconds(10);
            var restartAttempt = 0;
            SingleInstanceGate singleInstanceGate;
            while (true)
            {
                AppLog.Info("SingleInstance", "Single-instance check started.", ("RestartRequested", restartRequested), ("Attempt", restartAttempt + 1));
                singleInstanceGate = SingleInstanceGate.CreateForCurrentUser();
                if (singleInstanceGate.IsPrimaryInstance)
                {
                    if (restartRequested)
                    {
                        AppLog.Info("SingleInstance", "Previous instance lock released.", ("Attempt", restartAttempt + 1));
                    }
                    break;
                }

                if (!restartRequested)
                {
                    AppLog.Info("SingleInstance", "Secondary launch detected; activating the existing instance.", ("PID", Environment.ProcessId));
                    singleInstanceGate.ActivatePrimaryInstance();
                    return;
                }

                singleInstanceGate.Dispose();
                restartAttempt++;
                if (DateTimeOffset.UtcNow >= restartDeadline)
                {
                    AppLog.Error("SingleInstance", "Restart timeout while waiting for the previous instance to exit.", new TimeoutException("The previous instance did not release its single-instance lock."), ("Attempts", restartAttempt));
                    return;
                }

                AppLog.Debug("SingleInstance", "Restart waiting for previous instance.", ("Attempt", restartAttempt), ("RemainingMs", (restartDeadline - DateTimeOffset.UtcNow).TotalMilliseconds));
                Thread.Sleep(TimeSpan.FromMilliseconds(100));
            }

            using (singleInstanceGate)
            {
                CurrentSingleInstanceGate = singleInstanceGate;
                var launchMode = args.Contains("--background", StringComparer.OrdinalIgnoreCase) ? "Background" : "Manual";
                AppLog.Info("App", "Application launch header.", ("Version", typeof(Program).Assembly.GetName().Version), ("LaunchMode", launchMode), ("PID", Environment.ProcessId), ("ProcessArchitecture", RuntimeInformation.ProcessArchitecture), ("OSArchitecture", RuntimeInformation.OSArchitecture), ("OS", Environment.OSVersion), ("Runtime", Environment.Version), ("ProcessPath", Environment.ProcessPath), ("BaseDirectory", AppContext.BaseDirectory));
                AppLog.Debug("COM wrapper initialization starting.");
                ComWrappersSupport.InitializeComWrappers();
                AppLog.Debug("COM wrapper initialization completed.");
                var winUiAssets = WinUiRuntimeAssetProbe.Inspect(AppContext.BaseDirectory);
                AppLog.Debug("Startup", "WinUI runtime assets.",
                    ("BaseDirectory", AppContext.BaseDirectory),
                    ("AppXbf", winUiAssets[0].Exists), ("AppXbfBytes", winUiAssets[0].SizeBytes),
                    ("MainWindowXbf", winUiAssets[1].Exists), ("MainWindowXbfBytes", winUiAssets[1].SizeBytes),
                    ("Pri", winUiAssets[2].Exists), ("PriBytes", winUiAssets[2].SizeBytes),
                    ("AppIcon", winUiAssets[3].Exists), ("AppIconBytes", winUiAssets[3].SizeBytes));
                AppLog.Debug("XAML Application.Start entering.");
                Application.Start(_ =>
                {
                    AppLog.Debug("XAML startup callback entered.");
                    var synchronizationContext = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                    SynchronizationContext.SetSynchronizationContext(synchronizationContext);
                    AppLog.Debug("Creating App instance.");
                    var app = new App(args, singleInstanceGate);
                    app.UnhandledException += (_, eventArgs) => AppLog.Error("Unhandled XAML exception.", eventArgs.Exception);
                    AppLog.Debug("App instance created.");
                });
            }
        }
        catch (Exception exception)
        {
            AppLog.Fatal("Startup", "Fatal startup exception.", exception);
            throw;
        }
        finally
        {
            AppLog.Shutdown();
        }
    }
}
