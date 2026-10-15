using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.UI.Lifecycle;
using WinRT;

namespace SteamInputAddonforClaw.UI;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var managedEntryTimestamp = Stopwatch.GetTimestamp();
        UiLog.ConfigureDirectory(args);
        UiLog.Debug("Startup", "UI managed entry.",
            ("PID", Environment.ProcessId),
            ("ElapsedMs", Stopwatch.GetElapsedTime(managedEntryTimestamp).TotalMilliseconds));

        var retentionTimestamp = Stopwatch.GetTimestamp();
        AddonLogRetention.PruneDirectory(UiLog.DirectoryPath);
        UiLog.Debug("Startup", "Log retention completed.",
            ("DurationMs", Stopwatch.GetElapsedTime(retentionTimestamp).TotalMilliseconds));

        using var gate = UiSingleInstanceGate.CreateForCurrentUser();
        UiLog.Debug("Startup", "Single-instance gate checked.",
            ("IsPrimaryInstance", gate.IsPrimaryInstance),
            ("ElapsedMs", Stopwatch.GetElapsedTime(managedEntryTimestamp).TotalMilliseconds));
        if (!gate.IsPrimaryInstance)
        {
            gate.ActivatePrimaryInstance();
            return;
        }

        var comInitializationTimestamp = Stopwatch.GetTimestamp();
        ComWrappersSupport.InitializeComWrappers();
        UiLog.Debug("Startup", "COM wrapper initialization completed.",
            ("DurationMs", Stopwatch.GetElapsedTime(comInitializationTimestamp).TotalMilliseconds),
            ("ElapsedMs", Stopwatch.GetElapsedTime(managedEntryTimestamp).TotalMilliseconds));

        UiLog.Debug("Startup", "Application.Start beginning.",
            ("ElapsedMs", Stopwatch.GetElapsedTime(managedEntryTimestamp).TotalMilliseconds));
        Application.Start(_ =>
        {
            UiLog.Debug("Startup", "Application.Start callback entered.",
                ("ElapsedMs", Stopwatch.GetElapsedTime(managedEntryTimestamp).TotalMilliseconds));
            var synchronizationContext = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(synchronizationContext);
            var app = new App(gate);
        });
    }
}
