using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace SteamInputAddonforClaw.Overlay;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var synchronizationContext = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(synchronizationContext);
            var app = new App();
        });
    }
}
