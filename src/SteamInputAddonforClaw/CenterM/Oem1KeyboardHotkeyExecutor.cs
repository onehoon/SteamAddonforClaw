using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Contracts.Oem1;

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
    internal static void Send(Oem1HotkeyBinding hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        // An unfinished hotkey configuration is not a failure -- there is simply nothing to press.
        if (!hotkey.IsConfigured) return;

        var keys = BuildKeySequence(hotkey);
        var pressed = 0;
        try
        {
            for (; pressed < keys.Length; pressed++)
                SendKey(keys[pressed], keyUp: false);
        }
        finally
        {
            for (var index = pressed - 1; index >= 0; index--)
                SendKey(keys[index], keyUp: true);
        }
    }

    /// <summary>Modifiers first (in a fixed, conventional order), the single key last.</summary>
    private static ushort[] BuildKeySequence(Oem1HotkeyBinding hotkey)
    {
        var keys = new List<ushort>(5);
        if (hotkey.Modifiers.HasFlag(Oem1HotkeyModifiers.Control)) keys.Add(VkControl);
        if (hotkey.Modifiers.HasFlag(Oem1HotkeyModifiers.Shift)) keys.Add(VkShift);
        if (hotkey.Modifiers.HasFlag(Oem1HotkeyModifiers.Alt)) keys.Add(VkAlt);
        if (hotkey.Modifiers.HasFlag(Oem1HotkeyModifiers.Windows)) keys.Add(VkLeftWindows);
        keys.Add((ushort)hotkey.Key);
        return [.. keys];
    }

    private static void SendKey(ushort virtualKey, bool keyUp)
    {
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
