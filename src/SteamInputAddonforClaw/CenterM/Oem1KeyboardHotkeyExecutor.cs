using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Contracts.FrontButtons;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>
/// Sends the one configured hotkey (optional modifiers + exactly one key) as a single
/// press-then-release through <c>SendInput</c>.
/// </summary>
/// <remarks>
/// Deliberately not a macro engine: there are no sequences, no configurable delays, no repeat, and
/// no hold mode, because the mapping model has nowhere to express any of those. Modifiers are
/// released in reverse press order so a failure part-way cannot leave a modifier logically stuck
/// down; the release batch is issued unconditionally in a <c>finally</c> for the same reason.
/// <c>Oem1BigPictureLauncher</c> is the sibling for the Steam action -- both are plain statics
/// invoked through a delegate the dispatcher receives, so tests never touch the real OS.
/// </remarks>
internal static class Oem1KeyboardHotkeyExecutor
{
    /// <summary>Test-only seam: when set, intercepts every individual key press/release in place of
    /// the real <c>SendInput</c> call, so a test can deterministically make one specific release fail
    /// without touching the real OS input queue. Null (the default) in production.</summary>
    internal static Action<ushort, bool>? TestOnly_SendKeyOverride;

    internal static void Send(FrontButtonHotkeyBinding hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        // An unfinished hotkey configuration is not a failure -- there is simply nothing to press.
        if (!hotkey.IsConfigured) return;

        var keys = BuildKeySequence(hotkey);
        var pressed = 0;
        Exception? releaseFailure = null;
        try
        {
            for (; pressed < keys.Length; pressed++)
                SendKey(keys[pressed], keyUp: false);
        }
        finally
        {
            // Review fix (MAJOR): a throwing SendKey here used to abort the release loop immediately,
            // so a failure releasing (say) the FIRST-pressed key meant every later modifier in the
            // sequence was never even attempted -- leaving Ctrl/Shift/Alt/Win logically held down in
            // Windows even though the OEM1 action itself correctly reports failure and fails open.
            // Release every successfully pressed key best-effort regardless of an earlier release
            // failure, and surface only the first such failure once cleanup is done.
            for (var index = pressed - 1; index >= 0; index--)
            {
                try { SendKey(keys[index], keyUp: true); }
                catch (Exception exception) { releaseFailure ??= exception; }
            }
        }

        if (releaseFailure is not null)
            throw new InvalidOperationException("One or more OEM1 hotkey key-up events failed.", releaseFailure);
    }

    /// <summary>Modifiers first (in a fixed, conventional order), the single key last.</summary>
    private static ushort[] BuildKeySequence(FrontButtonHotkeyBinding hotkey)
    {
        var keys = new List<ushort>(5);
        if (hotkey.Modifiers.HasFlag(FrontButtonHotkeyModifiers.Control)) keys.Add(VkControl);
        if (hotkey.Modifiers.HasFlag(FrontButtonHotkeyModifiers.Shift)) keys.Add(VkShift);
        if (hotkey.Modifiers.HasFlag(FrontButtonHotkeyModifiers.Alt)) keys.Add(VkAlt);
        if (hotkey.Modifiers.HasFlag(FrontButtonHotkeyModifiers.Windows)) keys.Add(VkLeftWindows);
        keys.Add((ushort)hotkey.Key);
        return [.. keys];
    }

    private static void SendKey(ushort virtualKey, bool keyUp)
    {
        if (TestOnly_SendKeyOverride is { } testOverride)
        {
            testOverride(virtualKey, keyUp);
            return;
        }

        var input = new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = keyUp ? KeyEventFKeyUp : 0u
                }
            }
        };

        if (SendInput(1, [input], Marshal.SizeOf<Input>()) == 0)
            throw new InvalidOperationException($"SendInput failed for virtual key 0x{virtualKey:X2} (Win32 error {Marshal.GetLastWin32Error()}).");
    }

    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkAlt = 0x12;
    private const ushort VkLeftWindows = 0x5B;
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        internal uint Type;
        internal InputUnion Data;
    }

    /// <summary>Only the keyboard member is ever written, but the mouse member must still be
    /// declared: Win32 validates <c>SendInput</c>'s cbSize against the full INPUT size, which is
    /// determined by MOUSEINPUT (the largest union member), not by KEYBDINPUT.</summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] internal KeyboardInput Keyboard;
        [FieldOffset(0)] internal MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        internal int X;
        internal int Y;
        internal uint Data;
        internal uint Flags;
        internal uint Time;
        internal nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        internal ushort VirtualKey;
        internal ushort ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
