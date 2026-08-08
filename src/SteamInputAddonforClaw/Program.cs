using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;
using WinRT;
using SteamInputAddonforClaw.Diagnostics;
using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            var launchMode = args.Contains("--background", StringComparer.OrdinalIgnoreCase) ? "Background" : "Manual";
            AppLog.Info("App", "Application launch header.", ("Version", typeof(Program).Assembly.GetName().Version), ("LaunchMode", launchMode), ("PID", Environment.ProcessId), ("ProcessArchitecture", RuntimeInformation.ProcessArchitecture), ("OSArchitecture", RuntimeInformation.OSArchitecture), ("OS", Environment.OSVersion), ("Runtime", Environment.Version), ("ProcessPath", Environment.ProcessPath), ("BaseDirectory", AppContext.BaseDirectory));
            AppLog.Info("Velopack bootstrap starting.");
            VelopackApp.Build().Run();
            AppLog.Info("Velopack bootstrap completed.");
            AppLog.Info("COM wrapper initialization starting.");
            ComWrappersSupport.InitializeComWrappers();
            AppLog.Info("COM wrapper initialization completed.");
            var winUiAssets = WinUiRuntimeAssetProbe.Inspect(AppContext.BaseDirectory);
            AppLog.Info("Startup", "WinUI runtime assets.",
                ("BaseDirectory", AppContext.BaseDirectory),
                ("AppXbf", winUiAssets[0].Exists), ("AppXbfBytes", winUiAssets[0].SizeBytes),
                ("MainWindowXbf", winUiAssets[1].Exists), ("MainWindowXbfBytes", winUiAssets[1].SizeBytes),
                ("Pri", winUiAssets[2].Exists), ("PriBytes", winUiAssets[2].SizeBytes),
                ("AppIcon", winUiAssets[3].Exists), ("AppIconBytes", winUiAssets[3].SizeBytes));
            AppLog.Info("XAML Application.Start entering.");
            Application.Start(_ =>
            {
                AppLog.Info("XAML startup callback entered.");
                var synchronizationContext = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(synchronizationContext);
                AppLog.Info("Creating App instance.");
                var app = new App(args);
                app.UnhandledException += (_, eventArgs) => AppLog.Error("Unhandled XAML exception.", eventArgs.Exception);
                AppLog.Info("App instance created.");
            });
        }
        catch (Exception exception)
        {
            AppLog.Fatal("Startup", "Fatal startup exception.", exception);
            throw;
        }
    }
}
