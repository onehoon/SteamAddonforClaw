using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.GameBar;

internal sealed class WinGSuppressionGuard : IDisposable
{
    private const int VkLWin = 0x5B, VkRWin = 0x5C, VkG = 0x47;
    private const uint LlkhfInjected = 0x10, LlkhfLowerIlInjected = 0x02;
    private const uint KeyUp = 0x0002, Extended = 0x0001;
    private const uint WhKeyboardLl = 13;
    private readonly Func<IntPtr, int, LowLevelKeyboardProc, IntPtr, uint, IntPtr> _install;
    private readonly Func<IntPtr, int, IntPtr, IntPtr> _next;
    private readonly Action<IntPtr> _remove;
    private readonly Func<Input[], uint> _sendInput;
    private readonly Func<int, short> _getAsyncKeyState;
    private LowLevelKeyboardProc? _callback;
    private IntPtr _hook;
    private int _started, _disposed, _armed;
    private int _winDown, _releasedByCleanup, _suppressG;
    private const long OwnMarker = unchecked((long)0x5349475F57494E47);

    internal WinGSuppressionGuard(
        Func<IntPtr, int, LowLevelKeyboardProc, IntPtr, uint, IntPtr>? install = null,
        Func<IntPtr, int, IntPtr, IntPtr>? next = null,
        Action<IntPtr>? remove = null,
        Func<Input[], uint>? sendInput = null,
        Func<int, short>? getAsyncKeyState = null)
    {
        _install = install ?? ((module, thread, callback, _, flags) => SetWindowsHookEx(WhKeyboardLl, callback, module, (uint)thread));
        _next = next ?? ((hook, code, data) => CallNextHookEx(hook, code, data));
        _remove = remove ?? (hook => _ = UnhookWindowsHookEx(hook));
        _sendInput = sendInput ?? (inputs => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()));
        _getAsyncKeyState = getAsyncKeyState ?? (vk => GetAsyncKeyState(vk));
    }

    internal bool IsArmed => Volatile.Read(ref _armed) != 0 && Volatile.Read(ref _hook) != IntPtr.Zero;
    internal bool IsHookInstalled => Volatile.Read(ref _hook) != IntPtr.Zero;

    internal void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0 || Volatile.Read(ref _disposed) != 0) return;
        _callback = HookCallback;
        try
        {
            var hook = _install(IntPtr.Zero, 0, _callback, IntPtr.Zero, 0);
            if (hook == IntPtr.Zero)
            {
                AppLog.Warn("Wing.Guard", "Win+G hook installation failed.", fields: [("Win32Error", Marshal.GetLastWin32Error())]);
                return;
            }
            Volatile.Write(ref _hook, hook);
            AppLog.Info("Wing.Guard", "Win+G hook installed.");
        }
        catch (Exception exception) { AppLog.Warn("Wing.Guard", "Win+G hook installation failed.", exception); }
    }

    internal bool EnsureArmed()
    {
        if (!IsHookInstalled || Volatile.Read(ref _disposed) != 0) return false;
        var currentWins = 0;
        if ((_getAsyncKeyState(VkLWin) & unchecked((short)0x8000)) != 0) currentWins |= 1;
        if ((_getAsyncKeyState(VkRWin) & unchecked((short)0x8000)) != 0) currentWins |= 2;
        Interlocked.Exchange(ref _winDown, currentWins);
        if (Interlocked.Exchange(ref _armed, 1) == 0) AppLog.Info("Wing.Guard", "Win+G suppression armed.");
        return true;
    }

    internal void Disarm()
    {
        if (Interlocked.Exchange(ref _armed, 0) != 0) AppLog.Info("Wing.Guard", "Win+G suppression disarmed.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Volatile.Write(ref _armed, 0);
        var hook = Interlocked.Exchange(ref _hook, IntPtr.Zero);
        if (hook != IntPtr.Zero)
        {
            try { _remove(hook); } catch { }
            AppLog.Info("Wing.Guard", "Win+G hook removed.");
        }
        _callback = null;
    }

    internal IntPtr ProcessKey(int vk, bool keyDown, uint flags = 0, long extraInfo = 0, uint scan = 0)
    {
        var own = extraInfo == OwnMarker;
        if (own)
        {
            if (AppLog.IsEnabled(AppLogLevel.Debug) && (vk is VkLWin or VkRWin or 0xFF))
                AppLog.Debug("Wing.Input", "Own synthetic cleanup event bypassed.", ("Vk", vk), ("Scan", scan), ("Flags", $"0x{flags:X}"), ("KeyDown", keyDown));
            return IntPtr.Zero;
        }
        var bit = vk == VkLWin ? 1 : vk == VkRWin ? 2 : 0;
        if (bit != 0)
        {
            if (keyDown) Interlocked.Or(ref _winDown, bit);
            else
            {
                var syntheticRelease = (Volatile.Read(ref _releasedByCleanup) & bit) != 0;
                Interlocked.And(ref _winDown, ~bit);
                if (syntheticRelease)
                {
                    Interlocked.And(ref _releasedByCleanup, ~bit);
                    LogRelevant(vk, keyDown, flags, extraInfo, scan);
                    AppLog.Debug("Wing.Input", "Physical Win-up consumed after synthetic release.", ("Vk", vk));
                    return new(1);
                }
            }
        }

        var blocking = IsArmed || Volatile.Read(ref _suppressG) != 0;
        if (blocking && (bit != 0 || vk == VkG))
            LogRelevant(vk, keyDown, flags, extraInfo, scan);
        if (vk == VkG && keyDown && blocking && Volatile.Read(ref _winDown) != 0)
        {
            Interlocked.Exchange(ref _suppressG, 1);
            try { SendCleanup(Volatile.Read(ref _winDown)); }
            catch (Exception exception) { AppLog.Warn("Wing.Input", "Win+G modifier cleanup failed after suppression was committed.", exception); }
            AppLog.Debug("Wing.Input", "Win+G suppressed.", ("Vk", vk), ("KeyDown", keyDown));
            return new(1);
        }
        if (vk == VkG && !keyDown && Interlocked.Exchange(ref _suppressG, 0) != 0) return new(1);
        if (!keyDown && bit != 0 && (Volatile.Read(ref _releasedByCleanup) & bit) != 0) return new(1);
        return IntPtr.Zero;
    }

    private void SendCleanup(int wins)
    {
        var inputs = new List<Input> { Input.Key(0xFF, 0, OwnMarker), Input.Key(0xFF, KeyUp, OwnMarker) };
        foreach (var bit in new[] { 1, 2 }) if ((wins & bit) != 0)
        {
            var vk = bit == 1 ? VkLWin : VkRWin;
            var scan = MapVirtualKey((uint)vk, 0);
            inputs.Add(Input.Key((ushort)vk, KeyUp | Extended, OwnMarker, (ushort)scan));
        }
        var sent = _sendInput(inputs.ToArray());
        if (sent == 1)
        {
            try { _sendInput([inputs[1]]); AppLog.Debug("Wing.Input", "Cleanup fallback attempted."); }
            catch (Exception exception) { AppLog.Warn("Wing.Input", "Cleanup fallback failed.", exception); }
        }
        if (wins is 1 or 2 or 3)
        {
            if ((wins & 1) != 0 && sent > 2) Interlocked.Or(ref _releasedByCleanup, 1);
            if ((wins & 2) != 0 && sent > (wins == 3 ? 3 : 2)) Interlocked.Or(ref _releasedByCleanup, 2);
        }
        AppLog.Debug("Wing.Input", "Modifier cleanup completed.", ("RequestedInputs", inputs.Count), ("SentInputs", sent));
        if (sent != inputs.Count)
            AppLog.Warn("Wing.Input", "Partial SendInput during Win+G cleanup.", fields: [("RequestedInputs", inputs.Count), ("SentInputs", sent), ("Win32Error", Marshal.GetLastWin32Error())]);
    }

    private void LogRelevant(int vk, bool keyDown, uint flags, long extraInfo, uint scan)
    {
        if (!AppLog.IsEnabled(AppLogLevel.Debug)) return;
        AppLog.Debug("Wing.Input", "Relevant keyboard hook event.",
            ("Vk", vk), ("Scan", scan), ("Flags", $"0x{flags:X}"),
            ("Injected", (flags & LlkhfInjected) != 0),
            ("LowerIlInjected", (flags & LlkhfLowerIlInjected) != 0),
            ("ExtraInfo", $"0x{unchecked((ulong)extraInfo):X}"),
            ("KeyDown", keyDown),
            ("LeftWinDown", (Volatile.Read(ref _winDown) & 1) != 0),
            ("RightWinDown", (Volatile.Read(ref _winDown) & 2) != 0),
            ("Armed", IsArmed));
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return _next(_hook, code, lParam);
        try
        {
            var data = Marshal.PtrToStructure<KeyboardData>(lParam);
            var result = ProcessKey((int)data.VkCode, (wParam.ToInt64() & 1) == 0, data.Flags, data.ExtraInfo, data.ScanCode);
            return result != IntPtr.Zero ? result : _next(_hook, code, lParam);
        }
        catch (Exception exception) { AppLog.Warn("Wing.Guard", "Win+G hook callback was contained.", exception); return _next(_hook, code, lParam); }
    }

    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardData { internal uint VkCode, ScanCode, Flags, Time; internal nint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { internal uint Type; internal InputUnion Data; internal static Input Key(ushort vk, uint flags, long extra, ushort scan = 0) => new() { Type = 1, Data = new() { Keyboard = new() { Vk = vk, Scan = scan, Flags = flags, ExtraInfo = (nint)extra } } }; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion { [FieldOffset(0)] internal KeyInput Keyboard; [FieldOffset(0)] internal MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyInput { internal ushort Vk, Scan; internal uint Flags, Time; internal nint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { internal int X, Y; internal uint MouseData, Flags, Time; internal nint ExtraInfo; }
    internal delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(uint id, LowLevelKeyboardProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
