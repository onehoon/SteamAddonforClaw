using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Overlay.Diagnostics;
using WinRT;
using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.Overlay;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        OverlayLog.ConfigureDirectory(args);
        AddonLogRetention.PruneDirectory(OverlayLog.DirectoryPath);
        OverlayLog.Info("App", "Overlay process starting",
            ("PID", Environment.ProcessId),
            ("Version", typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown"),
            ("ProcessPath", Environment.ProcessPath ?? "unknown"),
            ("BaseDirectory", AppContext.BaseDirectory),
            ("OS", Environment.OSVersion.VersionString),
            ("Runtime", RuntimeInformation.FrameworkDescription),
            ("Architecture", RuntimeInformation.ProcessArchitecture));
        try
        {
            ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ =>
            {
                var synchronizationContext = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(synchronizationContext);
                var app = new App();
            });
        }
        catch (Exception exception)
        {
            OverlayLog.Error("App", "WinUI application startup failed.", exception);
            throw;
        }
    }
}
