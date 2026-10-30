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

    [Fact]
    public void Initial_registration_failure_retries_on_the_window_timer_and_recovers()
    {
        var commands = new List<uint>();
        var addAttempts = 0;
        var timerStarts = 0;
        var timerStops = 0;
        var tray = CreateTray(command =>
        {
            commands.Add(command);
            if (command == 0) return ++addAttempts > 1;
            return command == 4;
        }, () => { timerStarts++; return true; }, () => timerStops++);

        Assert.False(tray.IsAvailable);
        Assert.Equal(1, timerStarts);
        tray.ProcessWindowMessage(0x0113, new IntPtr(0x51A7));

        Assert.True(tray.IsAvailable);
        Assert.Equal([0u, 0u, 4u], commands);
        Assert.Equal(1, timerStops);
        tray.Dispose();
    }

    [Fact]
    public void Failed_setversion_with_failed_delete_retries_without_duplicate_add()
    {
        var commands = new List<uint>();
        var setVersionAttempts = 0;
        var timerStops = 0;
        var tray = CreateTray(command =>
        {
            commands.Add(command);
            return command switch
            {
                0 => true,
                4 => ++setVersionAttempts > 1,
                2 => false,
                _ => false,
            };
        }, () => true, () => timerStops++);

        Assert.False(tray.IsAvailable);
        Assert.Equal([0u, 4u, 2u], commands);
        tray.ProcessWindowMessage(0x0113, new IntPtr(0x51A7));

        Assert.True(tray.IsAvailable);
        Assert.Equal([0u, 4u, 2u, 4u], commands);
        Assert.Equal(1, timerStops);
        tray.Dispose();
    }

    [Fact]
    public void Setversion_failure_retries_are_bounded_and_do_not_repeat_add_while_icon_remains_registered()
    {
        var addAttempts = 0;
        var setVersionAttempts = 0;
        var timerStops = 0;
        var tray = CreateTray(command => command switch
        {
            0 => ++addAttempts >= 1,
            4 => ++setVersionAttempts > 16,
            2 => false,
            _ => false,
        }, () => true, () => timerStops++);

        for (var attempt = 0; attempt < 15; attempt++)
            tray.ProcessWindowMessage(0x0113, new IntPtr(0x51A7));

        Assert.False(tray.IsAvailable);
        Assert.Equal(1, addAttempts);
        Assert.Equal(16, setVersionAttempts);
        Assert.Equal(1, timerStops);

        tray.ProcessWindowMessage(0xC001);
        Assert.True(tray.IsAvailable);
        Assert.Equal(2, addAttempts);
        Assert.Equal(17, setVersionAttempts);
        tray.Dispose();
    }

    [Fact]
    public void Taskbar_created_re_registers_once_and_keeps_the_original_activation_callback()
    {
        var commands = new List<uint>();
        var openCount = 0;
        var tray = CreateTray(command => { commands.Add(command); return true; }, () => true, () => { }, () => openCount++);

        tray.ProcessWindowMessage(0xC001);

        Assert.True(tray.IsAvailable);
        Assert.Equal([0u, 4u, 0u, 4u], commands);
        Assert.Equal(0, openCount);
        tray.ProcessWindowMessage(0x8001, lParam: new IntPtr(NIN_SELECT));
        Assert.Equal(1, openCount);
        tray.Dispose();
    }

    [Fact]
    public void Retry_exhaustion_stops_timer_but_later_taskbar_created_starts_a_new_attempt()
    {
        var addAttempts = 0;
        var timerStarts = 0;
        var timerStops = 0;
        var tray = CreateTray(command => { if (command == 0) addAttempts++; return false; },
            () => { timerStarts++; return true; }, () => timerStops++);

        for (var attempt = 0; attempt < 15; attempt++)
            tray.ProcessWindowMessage(0x0113, new IntPtr(0x51A7));

        Assert.False(tray.IsAvailable);
        Assert.Equal(16, addAttempts); // initial attempt plus the bounded 15 retries
        Assert.Equal(1, timerStarts);
        Assert.Equal(1, timerStops);
        tray.ProcessWindowMessage(0x0113, new IntPtr(0x51A7));
        Assert.Equal(16, addAttempts); // exhausted timer messages no longer trigger shell work

        tray.ProcessWindowMessage(0xC001);
        Assert.Equal(17, addAttempts);
        Assert.Equal(2, timerStarts);
        Assert.False(tray.IsAvailable);
        tray.ProcessWindowMessage(0x0113, new IntPtr(0x51A7));
        Assert.Equal(18, addAttempts); // a later taskbar restart starts a fresh bounded retry window
        tray.Dispose();
    }

    [Fact]
    public void Dispose_stops_pending_retry_removes_subclass_and_icon_and_ignores_later_messages()
    {
        var removedSubclass = 0;
        var destroyedIcon = 0;
        var stoppedTimer = 0;
        var openCount = 0;
        var tray = CreateTray(_ => false, () => true, () => stoppedTimer++, () => openCount++,
            removeSubclass: () => removedSubclass++, destroyIcon: _ => destroyedIcon++);

        tray.Dispose();
        tray.ProcessWindowMessage(0xC001);
        tray.ProcessWindowMessage(0x8001, lParam: new IntPtr(NIN_SELECT));

        Assert.Equal(1, stoppedTimer);
        Assert.Equal(1, removedSubclass);
        Assert.Equal(1, destroyedIcon);
        Assert.Equal(0, openCount);
    }

    [Fact]
    public void Dispose_removes_a_registered_icon_before_releasing_the_native_resources()
    {
        var commands = new List<uint>();
        var tray = CreateTray(command => { commands.Add(command); return true; }, () => true, () => { });

        Assert.True(tray.IsAvailable);
        tray.Dispose();

        Assert.Equal([0u, 4u, 2u], commands);
    }

    [Fact]
    public void Tray_registration_timer_failure_keeps_host_constructed_and_unavailable()
    {
        var tray = CreateTray(_ => false, () => false, () => { });

        Assert.False(tray.IsAvailable);
        tray.Dispose();
    }

    [Fact]
    public void Subclass_construction_failure_releases_the_extracted_icon()
    {
        var destroyedIcon = 0;
        var hooks = new SystemTrayIconTestHooks(
            NotifyIcon: _ => throw new Xunit.Sdk.XunitException("Shell registration must not run before subclassing."),
            StartRetryTimer: () => true,
            StopRetryTimer: () => { },
            InstallSubclass: () => false,
            RemoveSubclass: () => { },
            Icon: new IntPtr(123),
            DestroyIcon: _ => destroyedIcon++,
            TaskbarCreatedMessage: 0xC001);

        Assert.Throws<InvalidOperationException>(() => new SystemTrayIcon(IntPtr.Zero, () => { }, () => { }, () => default, hooks));
        Assert.Equal(1, destroyedIcon);
    }

    private static SystemTrayIcon CreateTray(
        Func<uint, bool> notify,
        Func<bool> startTimer,
        Action stopTimer,
        Action? open = null,
        Action? removeSubclass = null,
        Action<IntPtr>? destroyIcon = null)
    {
        var hooks = new SystemTrayIconTestHooks(
            notify,
            startTimer,
            stopTimer,
            InstallSubclass: () => true,
            RemoveSubclass: removeSubclass ?? (() => { }),
            Icon: new IntPtr(123),
            DestroyIcon: destroyIcon ?? (_ => { }),
            TaskbarCreatedMessage: 0xC001);
        return new SystemTrayIcon(IntPtr.Zero, open ?? (() => { }), () => { }, () => default, hooks);
    }

    // ---- Tray restart/overlay cleanup work order sections 5/6/10: the menu is built natively via
    // Win32 AppendMenuW against a real HWND, so it is proven from source rather than a live popup. ----

    private static string ShowMenuBody()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Lifecycle/SystemTrayIcon.cs"));
        var body = source[source.IndexOf("private void ShowMenu(POINT point)", StringComparison.Ordinal)..];
        return body[..body.IndexOf("internal static uint TerminationMenuFlags", StringComparison.Ordinal)];
    }

    [Fact]
    public void Tray_menu_is_exactly_Open_separator_RestartAddon()
    {
        var menu = ShowMenuBody();

        Assert.Contains("\"Open\"", menu, StringComparison.Ordinal);
        Assert.Contains("MF_SEPARATOR", menu, StringComparison.Ordinal);
        Assert.Contains("\"Restart Addon\"", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("Overlay POC", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Exit\"", menu, StringComparison.Ordinal);
        // Open before the separator before Restart Addon.
        var open = menu.IndexOf("\"Open\"", StringComparison.Ordinal);
        var separator = menu.IndexOf("MF_SEPARATOR", StringComparison.Ordinal);
        var restart = menu.IndexOf("\"Restart Addon\"", StringComparison.Ordinal);
        Assert.True(open < separator && separator < restart);
    }

    [Fact]
    public void Tray_no_longer_takes_an_exit_or_overlay_toggle_delegate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Lifecycle/SystemTrayIcon.cs"));

        Assert.Contains("public SystemTrayIcon(IntPtr windowHandle, Action open, Action restart, Func<UserTerminationDecision> restartDecision)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_exit", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_overlayToggle", source, StringComparison.Ordinal);
        Assert.DoesNotContain("overlayToggle", source, StringComparison.Ordinal);
    }
}
