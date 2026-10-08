using System.Diagnostics;
using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Processes;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// Focused coverage for the two real action executors the dispatcher tests otherwise always fake
/// out: <see cref="Oem1ApplicationLauncher"/>'s scope restriction to a plain executable, and
/// <see cref="Oem1KeyboardHotkeyExecutor"/>'s best-effort key-release cleanup on a partial failure.
/// </summary>
public sealed class FrontButtonActionExecutorLauncherTests
{
    // ---- Oem1ApplicationLauncher (review fix, MAJOR: executable-only, no shell resolution) ----

    [Theory]
    [InlineData(@"C:\Users\test\notes.txt")]
    [InlineData(@"C:\Users\test\shortcut.lnk")]
    [InlineData(@"C:\Users\test\script.ps1")]
    [InlineData(@"C:\Users\test\noextension")]
    public void Launch_refuses_a_non_executable_target(string path)
    {
        var application = new FrontButtonLaunchApplicationBinding(path);

        var exception = Assert.Throws<InvalidOperationException>(() => Oem1ApplicationLauncher.Launch(application));
        Assert.Contains(".exe", exception.Message);
    }

    [Fact]
    public void Launch_is_a_no_op_for_an_unconfigured_binding()
    {
        // Empty path -- nothing configured yet -- must not even reach the extension check.
        var exception = Record.Exception(() => Oem1ApplicationLauncher.Launch(FrontButtonLaunchApplicationBinding.Empty));
        Assert.Null(exception);
    }

    [Fact]
    public void Launch_null_binding_throws_argument_null()
    {
        Assert.Throws<ArgumentNullException>(() => Oem1ApplicationLauncher.Launch(null!));
    }

    [Fact]
    public void Application_adapter_passes_path_arguments_and_admin_choice_to_the_shared_launcher()
    {
        ProcessStartInfo? captured = null;
        bool? requestedAdmin = null;
        var launcher = new UserProcessLauncher((startInfo, runAsAdministrator) =>
        {
            captured = startInfo;
            requestedAdmin = runAsAdministrator;
            return true;
        });
        var binding = new FrontButtonLaunchApplicationBinding(@"C:\Tools\Tool.exe", "--example", true);

        Oem1ApplicationLauncher.Launch(binding, launcher);

        Assert.NotNull(captured);
        Assert.Equal(@"C:\Tools\Tool.exe", captured!.FileName);
        Assert.Equal("--example", captured.Arguments);
        Assert.False(captured.UseShellExecute);
        Assert.True(requestedAdmin);
    }

    [Fact]
    public void Xbox_app_activation_is_delegated_to_the_interactive_shell()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var launcher = File.ReadAllText(Path.Combine(dir!.FullName,
            "src/SteamInputAddonforClaw/CenterM/FrontButtonXboxAppLauncher.cs"));

        Assert.Contains("LaunchXboxApp()", launcher, StringComparison.Ordinal);
        Assert.Contains("userProcessLauncher ?? UserProcessLauncher.Shared", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("IApplicationActivationManager", launcher, StringComparison.Ordinal);
    }

    // ---- Oem1KeyboardHotkeyExecutor (review fix, MAJOR: best-effort release cleanup) ----

    [Fact]
    public void Send_releases_every_pressed_key_even_when_an_earlier_release_fails()
    {
        // Modifiers press in Control, Shift, Alt, Windows order, then the key; releases run in
        // reverse (key, Windows, Alt, Shift, Control). Make the FIRST release attempted (the key
        // itself) fail, and prove every modifier release after it is still attempted -- the exact
        // bug: a throwing release used to abort the whole cleanup loop immediately.
        var released = new List<(ushort VirtualKey, bool KeyUp)>();
        const ushort keyVk = 0x53; // 'S'
        Oem1KeyboardHotkeyExecutor.TestOnly_SendKeyOverride = (virtualKey, keyUp) =>
        {
            released.Add((virtualKey, keyUp));
            if (keyUp && virtualKey == keyVk)
                throw new InvalidOperationException("Simulated release failure for the key.");
        };
        try
        {
            var hotkey = new FrontButtonHotkeyBinding(
                FrontButtonHotkeyModifiers.Control | FrontButtonHotkeyModifiers.Shift | FrontButtonHotkeyModifiers.Alt | FrontButtonHotkeyModifiers.Windows,
                FrontButtonHotkeyKey.S);

            var exception = Assert.Throws<InvalidOperationException>(() => Oem1KeyboardHotkeyExecutor.Send(hotkey));
            Assert.Contains("key-up", exception.Message, StringComparison.OrdinalIgnoreCase);

            var keyUps = released.Where(entry => entry.KeyUp).Select(entry => entry.VirtualKey).ToList();
            // All five keys (4 modifiers + the key itself) must have a release ATTEMPTED, regardless
            // of the first one having thrown.
            Assert.Equal(5, keyUps.Count);
            Assert.Contains((ushort)0x11, keyUps); // Control
            Assert.Contains((ushort)0x10, keyUps); // Shift
            Assert.Contains((ushort)0x12, keyUps); // Alt
            Assert.Contains((ushort)0x5B, keyUps); // Windows
            Assert.Contains(keyVk, keyUps);
        }
        finally
        {
            Oem1KeyboardHotkeyExecutor.TestOnly_SendKeyOverride = null;
        }
    }

    [Fact]
    public void Send_succeeds_silently_when_every_press_and_release_succeeds()
    {
        var events = new List<(ushort VirtualKey, bool KeyUp)>();
        Oem1KeyboardHotkeyExecutor.TestOnly_SendKeyOverride = (virtualKey, keyUp) => events.Add((virtualKey, keyUp));
        try
        {
            var hotkey = new FrontButtonHotkeyBinding(FrontButtonHotkeyModifiers.Control, FrontButtonHotkeyKey.S);

            var exception = Record.Exception(() => Oem1KeyboardHotkeyExecutor.Send(hotkey));

            Assert.Null(exception);
            // Press order: Control, then S. Release order: S, then Control.
            Assert.Equal([(0x11, false), (0x53, false), (0x53, true), (0x11, true)], events.Select(e => ((int)e.VirtualKey, e.KeyUp)));
        }
        finally
        {
            Oem1KeyboardHotkeyExecutor.TestOnly_SendKeyOverride = null;
        }
    }

    [Fact]
    public void Send_is_a_no_op_for_an_unconfigured_hotkey()
    {
        var called = false;
        Oem1KeyboardHotkeyExecutor.TestOnly_SendKeyOverride = (_, _) => called = true;
        try
        {
            Oem1KeyboardHotkeyExecutor.Send(FrontButtonHotkeyBinding.Empty);
            Assert.False(called);
        }
        finally
        {
            Oem1KeyboardHotkeyExecutor.TestOnly_SendKeyOverride = null;
        }
    }
}
