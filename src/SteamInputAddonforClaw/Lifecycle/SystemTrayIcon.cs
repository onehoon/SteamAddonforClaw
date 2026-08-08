using System.Drawing;
using System.Windows.Forms;

namespace SteamInputAddonforClaw.Lifecycle;

internal sealed class SystemTrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public SystemTrayIcon(Action open, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => open());
        menu.Items.Add("Exit", null, (_, _) => exit());
        _notifyIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
            Text = "Steam Input Addon for Claw",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => open();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
