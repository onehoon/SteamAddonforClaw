using SteamInputAddonforClaw.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SystemTrayIconTests
{
    private const uint NIN_SELECT = 0x0400;
    private const uint NIN_KEYSELECT = 0x0401;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;

    [Fact]
    public void NIN_SELECT_classifies_as_activate()
    {
        Assert.Equal(TrayNotificationAction.Activate, SystemTrayIcon.ClassifyNotification(NIN_SELECT));
    }

    [Fact]
    public void NIN_KEYSELECT_classifies_as_activate()
    {
        Assert.Equal(TrayNotificationAction.Activate, SystemTrayIcon.ClassifyNotification(NIN_KEYSELECT));
    }

    [Fact]
    public void WM_CONTEXTMENU_classifies_as_context_menu()
    {
        Assert.Equal(TrayNotificationAction.ContextMenu, SystemTrayIcon.ClassifyNotification(WM_CONTEXTMENU));
    }

    [Fact]
    public void Raw_WM_LBUTTONUP_is_ignored()
    {
        Assert.Equal(TrayNotificationAction.None, SystemTrayIcon.ClassifyNotification(WM_LBUTTONUP));
    }

    [Fact]
    public void Raw_WM_LBUTTONDBLCLK_is_ignored()
    {
        Assert.Equal(TrayNotificationAction.None, SystemTrayIcon.ClassifyNotification(WM_LBUTTONDBLCLK));
    }

    [Fact]
    public void Raw_WM_RBUTTONUP_is_ignored()
    {
        Assert.Equal(TrayNotificationAction.None, SystemTrayIcon.ClassifyNotification(WM_RBUTTONUP));
    }

    [Fact]
    public void GetNotificationCode_extracts_the_low_word_of_lParam()
    {
        // High word carries no notification-code meaning for v4 callbacks -- only the low word
        // (the notification/event code) must be extracted.
        var lParam = new IntPtr(unchecked((int)0x1234_0401));

        Assert.Equal(NIN_KEYSELECT, SystemTrayIcon.GetNotificationCode(lParam));
    }

    [Theory]
    [InlineData(100, 200)]
    [InlineData(0, 0)]
    [InlineData(-100, 500)]
    [InlineData(500, -100)]
    [InlineData(-1, -1)]
    public void DecodeCallbackPoint_round_trips_signed_screen_coordinates(int x, int y)
    {
        var packed = ((uint)(ushort)y << 16) | (uint)(ushort)x;
        var wParam = new IntPtr(unchecked((int)packed));

        var point = SystemTrayIcon.DecodeCallbackPoint(wParam);

        Assert.Equal(x, point.X);
        Assert.Equal(y, point.Y);
    }
}
