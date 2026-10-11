using SteamInputAddonforClaw.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class WindowsSessionEndMessageTests
{
    [Fact]
    public void Only_committed_os_shutdown_or_restart_end_session_is_eligible()
    {
        Assert.False(WindowsSessionEndMessage.IsRealWindowsPowerSessionEnd(
            WindowsSessionEndMessage.WmQueryEndSession, new IntPtr(1), IntPtr.Zero));
        Assert.False(WindowsSessionEndMessage.IsRealWindowsPowerSessionEnd(
            WindowsSessionEndMessage.WmEndSession, IntPtr.Zero, IntPtr.Zero));
        Assert.True(WindowsSessionEndMessage.IsRealWindowsPowerSessionEnd(
            WindowsSessionEndMessage.WmEndSession, new IntPtr(1), IntPtr.Zero));
        Assert.True(WindowsSessionEndMessage.IsRealWindowsPowerSessionEnd(
            WindowsSessionEndMessage.WmEndSession, new IntPtr(1), new IntPtr(0x40000000)));
    }

    [Fact]
    public void Logoff_and_application_close_flags_are_excluded_as_a_bitmask()
    {
        var logoff = new IntPtr(unchecked((int)WindowsSessionEndMessage.EndSessionLogoff));
        var closeApp = new IntPtr(unchecked((int)WindowsSessionEndMessage.EndSessionCloseApp));
        var combined = new IntPtr(unchecked((int)(WindowsSessionEndMessage.EndSessionLogoff | WindowsSessionEndMessage.EndSessionCloseApp)));

        Assert.False(WindowsSessionEndMessage.IsRealWindowsPowerSessionEnd(WindowsSessionEndMessage.WmEndSession, new IntPtr(1), logoff));
        Assert.False(WindowsSessionEndMessage.IsRealWindowsPowerSessionEnd(WindowsSessionEndMessage.WmEndSession, new IntPtr(1), closeApp));
        Assert.False(WindowsSessionEndMessage.IsRealWindowsPowerSessionEnd(WindowsSessionEndMessage.WmEndSession, new IntPtr(1), combined));
    }

    [Fact]
    public void Eligible_end_session_callback_can_start_only_once_and_rejected_messages_do_not_consume_it()
    {
        var started = 0;

        Assert.False(WindowsSessionEndMessage.TryStartOnce(
            ref started, WindowsSessionEndMessage.WmEndSession, new IntPtr(1),
            new IntPtr(unchecked((int)WindowsSessionEndMessage.EndSessionLogoff))));
        Assert.True(WindowsSessionEndMessage.TryStartOnce(
            ref started, WindowsSessionEndMessage.WmEndSession, new IntPtr(1), IntPtr.Zero));
        Assert.False(WindowsSessionEndMessage.TryStartOnce(
            ref started, WindowsSessionEndMessage.WmEndSession, new IntPtr(1), IntPtr.Zero));
        Assert.Equal(1, started);
    }
}
