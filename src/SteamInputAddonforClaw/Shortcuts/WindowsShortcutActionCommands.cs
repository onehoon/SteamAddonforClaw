using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.Shortcuts;

internal readonly record struct ShortcutKeyboardInput(ushort VirtualKey, bool KeyUp);

/// <summary>Small Windows-only commands used by the built-in Shortcut actions.</summary>
internal static class WindowsShortcutActionCommands
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const ushort VirtualKeyD = 0x44;
    private const ushort VirtualKeyLeftWindows = 0x5B;
    private const uint ClsctxAll = 0x17;

    private static readonly Guid TipInvocationClassId = new("4ce576fa-83dc-4F88-951c-9d0782b4e376");
    private static readonly Guid TipInvocationInterfaceId = new("37c994e7-432b-4834-a2f7-dce1f13b834b");

    internal static bool ToggleTouchKeyboard()
    {
        // InputPane.TryShow is foreground-app-only; this best-effort Shell COM toggle runs after the Overlay hands off focus.
        ITipInvocation? invocation = null;
        try
        {
            var classId = TipInvocationClassId;
            var interfaceId = TipInvocationInterfaceId;
            if (CoCreateInstance(ref classId, 0, ClsctxAll, ref interfaceId, out invocation) < 0
                || invocation is null)
            {
                return false;
            }

            return invocation.Toggle(GetDesktopWindow()) >= 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (invocation is not null && Marshal.IsComObject(invocation))
                _ = Marshal.ReleaseComObject(invocation);
        }
    }

    internal static bool ShowDesktop() => SendWinD(SendKeyboardInput);

    internal static bool SendWinD(Func<ShortcutKeyboardInput[], uint> sendInput)
    {
        ArgumentNullException.ThrowIfNull(sendInput);

        ShortcutKeyboardInput[] sequence =
        [
            new(VirtualKeyLeftWindows, KeyUp: false),
            new(VirtualKeyD, KeyUp: false),
            new(VirtualKeyD, KeyUp: true),
            new(VirtualKeyLeftWindows, KeyUp: true),
        ];

        uint sent;
        try
        {
            sent = sendInput(sequence);
        }
        catch
        {
            return false;
        }

        if (sent == (uint)sequence.Length)
            return true;
        if (sent > (uint)sequence.Length)
            return false;

        var releases = new List<ShortcutKeyboardInput>(2);
        if (sent == 2)
            releases.Add(new(VirtualKeyD, KeyUp: true));
        if (sent is >= 1 and <= 3)
            releases.Add(new(VirtualKeyLeftWindows, KeyUp: true));

        if (releases.Count > 0)
        {
            var releaseEvents = releases.ToArray();
            uint released;
            try { released = sendInput(releaseEvents); }
            catch { released = 0; }

            var remainingStart = Math.Min(released, (uint)releaseEvents.Length);
            if (remainingStart < (uint)releaseEvents.Length)
            {
                try { _ = sendInput(releaseEvents[(int)remainingStart..]); }
                catch { }
            }
        }

        return false;
    }

    private static uint SendKeyboardInput(ShortcutKeyboardInput[] events)
    {
        var nativeInputs = events.Select(input => new NativeInput
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInputData
                {
                    VirtualKey = input.VirtualKey,
                    Flags = input.KeyUp ? KeyEventKeyUp : 0,
                }
            }
        }).ToArray();

        return SendInput((uint)nativeInputs.Length, nativeInputs, Marshal.SizeOf<NativeInput>());
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern uint SendInput(uint count, NativeInput[] inputs, int size);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        ref Guid classId,
        nint outer,
        uint context,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out ITipInvocation? instance);

    [ComImport]
    [Guid("37c994e7-432b-4834-a2f7-dce1f13b834b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation
    {
        [PreserveSig]
        int Toggle(nint windowHandle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        internal uint Type;
        internal InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        internal KeyboardInputData Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        internal ushort VirtualKey;
        internal ushort ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nint ExtraInfo;
    }
}
