using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Contracts.Oem1;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// Focused coverage for the two real action executors the dispatcher tests otherwise always fake
/// out: <see cref="Oem1ApplicationLauncher"/>'s scope restriction to a plain executable, and
/// <see cref="Oem1KeyboardHotkeyExecutor"/>'s best-effort key-release cleanup on a partial failure.
/// </summary>
public sealed class Oem1ActionExecutorTests
{
    // ---- Oem1ApplicationLauncher (review fix, MAJOR: executable-only, no shell resolution) ----

    [Theory]
    [InlineData(@"C:\Users\test\notes.txt")]
    [InlineData(@"C:\Users\test\shortcut.lnk")]
    [InlineData(@"C:\Users\test\script.ps1")]
    [InlineData(@"C:\Users\test\noextension")]
    public void Launch_refuses_a_non_executable_target(string path)
    {
        var application = new Oem1LaunchApplicationBinding(path);

        var exception = Assert.Throws<InvalidOperationException>(() => Oem1ApplicationLauncher.Launch(application));
        Assert.Contains(".exe", exception.Message);
    }

    [Fact]
    public void Launch_is_a_no_op_for_an_unconfigured_binding()
    {
        // Empty path -- nothing configured yet -- must not even reach the extension check.
        var exception = Record.Exception(() => Oem1ApplicationLauncher.Launch(Oem1LaunchApplicationBinding.Empty));
        Assert.Null(exception);
    }

    [Fact]
    public void Launch_null_binding_throws_argument_null()
    {
        Assert.Throws<ArgumentNullException>(() => Oem1ApplicationLauncher.Launch(null!));
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
            var hotkey = new Oem1HotkeyBinding(
                Oem1HotkeyModifiers.Control | Oem1HotkeyModifiers.Shift | Oem1HotkeyModifiers.Alt | Oem1HotkeyModifiers.Windows,
                Oem1HotkeyKey.S);

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
            var hotkey = new Oem1HotkeyBinding(Oem1HotkeyModifiers.Control, Oem1HotkeyKey.S);

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
            Oem1KeyboardHotkeyExecutor.Send(Oem1HotkeyBinding.Empty);
            Assert.False(called);
        }
        finally
        {
            Oem1KeyboardHotkeyExecutor.TestOnly_SendKeyOverride = null;
        }
    }
}
