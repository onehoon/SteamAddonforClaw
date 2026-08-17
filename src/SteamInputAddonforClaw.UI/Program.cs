using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.UI.Lifecycle;
using WinRT;

namespace SteamInputAddonforClaw.UI;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var gate = UiSingleInstanceGate.CreateForCurrentUser();
        if (!gate.IsPrimaryInstance)
        {
            gate.ActivatePrimaryInstance();
            return;
        }

        ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var synchronizationContext = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(synchronizationContext);
            var app = new App(gate);
        });
    }
}
