using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;
using WinRT;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            AppLog.Info($"Application starting. Arguments: {string.Join(' ', args)}");
            AppLog.Info("Velopack bootstrap starting.");
            VelopackApp.Build().Run();
            AppLog.Info("Velopack bootstrap completed.");
            AppLog.Info("COM wrapper initialization starting.");
            ComWrappersSupport.InitializeComWrappers();
            AppLog.Info("COM wrapper initialization completed.");
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
            AppLog.Error("Fatal startup exception.", exception);
            throw;
        }
    }
}
