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
    private LowLevelKeyboardProc? _callback;
    private IntPtr _hook;
    private int _started, _disposed, _armed;
    private int _winDown, _releasedByCleanup, _suppressG;
    private const long OwnMarker = unchecked((long)0x5349475F57494E47);

    internal WinGSuppressionGuard(
        Func<IntPtr, int, LowLevelKeyboardProc, IntPtr, uint, IntPtr>? install = null,
        Func<IntPtr, int, IntPtr, IntPtr>? next = null,
        Action<IntPtr>? remove = null,
        Func<Input[], uint>? sendInput = null)
    {
        _install = install ?? ((module, thread, callback, _, flags) => SetWindowsHookEx(WhKeyboardLl, callback, module, (uint)thread));
        _next = next ?? ((hook, code, data) => CallNextHookEx(hook, code, data));
        _remove = remove ?? (hook => _ = UnhookWindowsHookEx(hook));
        _sendInput = sendInput ?? (inputs => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()));
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

    internal IntPtr ProcessKey(int vk, bool keyDown, uint flags = 0, long extraInfo = 0)
    {
        var own = extraInfo == OwnMarker;
        if (own) return IntPtr.Zero;
        var bit = vk == VkLWin ? 1 : vk == VkRWin ? 2 : 0;
        if (bit != 0)
        {
            if (keyDown) Interlocked.Or(ref _winDown, bit);
            else
            {
                var syntheticRelease = (Volatile.Read(ref _releasedByCleanup) & bit) != 0;
                if (syntheticRelease)
                    Interlocked.And(ref _releasedByCleanup, ~bit);
                else Interlocked.And(ref _winDown, ~bit);
                if (syntheticRelease) return new(1);
            }
        }

        var blocking = IsArmed || Volatile.Read(ref _suppressG) != 0;
        if (vk == VkG && keyDown && blocking && Volatile.Read(ref _winDown) != 0)
        {
            Interlocked.Exchange(ref _suppressG, 1);
            SendCleanup(Volatile.Read(ref _winDown));
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
            inputs.Add(Input.Key((ushort)vk, KeyUp | (bit == 2 ? Extended : 0), OwnMarker, (ushort)scan));
            Interlocked.Or(ref _releasedByCleanup, bit);
        }
        var sent = _sendInput(inputs.ToArray());
        if (sent == 1)
        {
            try { _sendInput([inputs[1]]); AppLog.Debug("Wing.Input", "Cleanup fallback attempted."); }
            catch (Exception exception) { AppLog.Warn("Wing.Input", "Cleanup fallback failed.", exception); }
        }
        if (sent != inputs.Count)
            AppLog.Warn("Wing.Input", "Partial SendInput during Win+G cleanup.", fields: [("RequestedInputs", inputs.Count), ("SentInputs", sent), ("Win32Error", Marshal.GetLastWin32Error())]);
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return _next(_hook, code, lParam);
        try
        {
            var data = Marshal.PtrToStructure<KeyboardData>(lParam);
            var result = ProcessKey((int)data.VkCode, (wParam.ToInt64() & 1) == 0, data.Flags, data.ExtraInfo);
            return result != IntPtr.Zero ? result : _next(_hook, code, lParam);
        }
        catch (Exception exception) { AppLog.Warn("Wing.Guard", "Win+G hook callback was contained.", exception); return _next(_hook, code, lParam); }
    }

    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardData { internal uint VkCode, ScanCode, Flags, Time; internal long ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { internal uint Type; internal KeyInput Data; internal static Input Key(ushort vk, uint flags, long extra, ushort scan = 0) => new() { Type = 1, Data = new() { Vk = vk, Scan = scan, Flags = flags, ExtraInfo = extra } }; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyInput { internal ushort Vk, Scan; internal uint Flags, Time; internal long ExtraInfo; }
    internal delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(uint id, LowLevelKeyboardProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
