using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.GameBar;

internal sealed class WinGSuppressionGuard : IDisposable
{
    private const int VkLWin = 0x5B, VkRWin = 0x5C, VkG = 0x47;
    private const uint KeyUp = 0x0002, Extended = 0x0001;
    private const uint WhKeyboardLl = 13, WmQuit = 0x0012, PmNoRemove = 0;
    private const int HookStartupTimeoutMilliseconds = 1_000, HookShutdownTimeoutMilliseconds = 1_000;
    private readonly Func<IntPtr, int, LowLevelKeyboardProc, IntPtr, uint, IntPtr> _install;
    private readonly Func<IntPtr, int, IntPtr, IntPtr, IntPtr> _next;
    private readonly Func<IntPtr, bool> _remove;
    private readonly Func<Input[], uint, uint> _sendInput;
    private readonly Func<int, short> _getAsyncKeyState;
    private readonly ManualResetEventSlim _hookStartupCompleted = new(false);
    private readonly object _hookStartupSync = new();
    private readonly int _hookStartupTimeoutMilliseconds;
    private LowLevelKeyboardProc? _callback;
    private Thread? _hookThread;
    private IntPtr _hook;
    private uint _hookThreadId;
    private int _started, _disposed, _armed, _hookStartupFailed;
    private int _winDown, _releasedByCleanup, _suppressG;
    private const long OwnMarker = unchecked((long)0x5349475F57494E47);

    internal WinGSuppressionGuard(
        Func<IntPtr, int, LowLevelKeyboardProc, IntPtr, uint, IntPtr>? install = null,
        Func<IntPtr, int, IntPtr, IntPtr, IntPtr>? next = null,
        Func<IntPtr, bool>? remove = null,
        Func<Input[], uint, uint>? sendInput = null,
        Func<int, short>? getAsyncKeyState = null,
        int hookStartupTimeoutMilliseconds = HookStartupTimeoutMilliseconds)
    {
        _install = install ?? ((module, thread, callback, _, _) => SetWindowsHookEx(WhKeyboardLl, callback, module, (uint)thread));
        _next = next ?? ((hook, code, wParam, data) => CallNextHookEx(hook, code, wParam, data));
        _remove = remove ?? UnhookWindowsHookEx;
        _sendInput = sendInput ?? ((inputs, count) => SendInput(count, inputs, Marshal.SizeOf<Input>()));
        _getAsyncKeyState = getAsyncKeyState ?? (vk => GetAsyncKeyState(vk));
        _hookStartupTimeoutMilliseconds = hookStartupTimeoutMilliseconds;
    }

    internal bool IsArmed => Volatile.Read(ref _armed) != 0 && Volatile.Read(ref _hook) != IntPtr.Zero;
    internal bool IsHookInstalled => Volatile.Read(ref _hook) != IntPtr.Zero;

    internal void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0 || Volatile.Read(ref _disposed) != 0) return;
        _callback = HookCallback;
        var thread = new Thread(HookThreadMain)
        {
            Name = "WinGSuppressionHook",
            IsBackground = true,
            Priority = ThreadPriority.Highest
        };

        lock (_hookStartupSync)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                _callback = null;
                return;
            }

            _hookThread = thread;
            try { thread.Start(); }
            catch (Exception exception)
            {
                Volatile.Write(ref _hookStartupFailed, 1);
                _callback = null;
                _hookStartupCompleted.Set();
                AppLog.Warn("Wing.Guard", "Win+G hook thread failed to start.", exception);
                return;
            }
        }

        if (_hookStartupCompleted.Wait(_hookStartupTimeoutMilliseconds)) return;

        var timedOut = false;
        lock (_hookStartupSync)
        {
            if (Volatile.Read(ref _hook) != IntPtr.Zero || _hookStartupCompleted.IsSet) return;
            Volatile.Write(ref _hookStartupFailed, 1);
            timedOut = true;
        }

        if (!timedOut) return;
        AppLog.Warn("Wing.Guard", "Win+G hook installation did not complete before the startup deadline.",
            fields: [("TimeoutMs", _hookStartupTimeoutMilliseconds)]);
        RequestHookThreadStop(thread, waitForExit: false);
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

        Thread? thread;
        lock (_hookStartupSync) thread = _hookThread;
        if (thread is not null && thread.IsAlive)
            RequestHookThreadStop(thread, waitForExit: true);
        else if (Volatile.Read(ref _hook) == IntPtr.Zero)
            _callback = null;
    }

    internal IntPtr ProcessKey(int vk, bool keyDown, long extraInfo = 0)
    {
        if (extraInfo == OwnMarker) return IntPtr.Zero;

        if (vk == VkG && Volatile.Read(ref _suppressG) != 0)
        {
            if (!keyDown) Interlocked.Exchange(ref _suppressG, 0);
            return new(1);
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
                    return new(1);
                }
            }
        }

        if (vk == VkG && keyDown && IsArmed && Volatile.Read(ref _winDown) != 0)
        {
            Interlocked.Exchange(ref _suppressG, 1);
            try { SendCleanup(Volatile.Read(ref _winDown)); }
            catch (Exception exception) { AppLog.Warn("Wing.Input", "Win+G modifier cleanup failed after suppression was committed.", exception); }
            if (AppLog.IsEnabled(AppLogLevel.Debug)) AppLog.Debug("Wing.Input", "Win+G chord suppressed.");
            return new(1);
        }

        if (!keyDown && bit != 0 && (Volatile.Read(ref _releasedByCleanup) & bit) != 0) return new(1);
        return IntPtr.Zero;
    }

    internal IntPtr InvokeForTest(int vk, bool keyDown, IntPtr wParam, IntPtr lParam)
    {
        var data = Marshal.AllocHGlobal(Marshal.SizeOf<KeyboardData>());
        try
        {
            Marshal.StructureToPtr(new KeyboardData { VkCode = (uint)vk }, data, false);
            return HookCallback(0, wParam, data);
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    internal IntPtr InvokeHookForTest(IntPtr wParam, IntPtr lParam) => HookCallback(0, wParam, lParam);

    private void HookThreadMain()
    {
        var hook = IntPtr.Zero;
        try
        {
            _ = PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);
            Volatile.Write(ref _hookThreadId, GetCurrentThreadId());
            if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _hookStartupFailed) != 0) return;

            var module = GetModuleHandle(null);
            if (module == IntPtr.Zero)
            {
                AppLog.Warn("Wing.Guard", "Win+G hook installation failed: module handle unavailable.", fields: [("Win32Error", Marshal.GetLastWin32Error())]);
                return;
            }

            hook = _install(module, 0, _callback!, IntPtr.Zero, 0);
            if (hook == IntPtr.Zero)
            {
                AppLog.Warn("Wing.Guard", "Win+G hook installation failed.", fields: [("Win32Error", Marshal.GetLastWin32Error())]);
                return;
            }

            lock (_hookStartupSync)
            {
                if (Volatile.Read(ref _disposed) == 0 && Volatile.Read(ref _hookStartupFailed) == 0)
                    Volatile.Write(ref _hook, hook);
            }

            if (Volatile.Read(ref _hook) != hook) return;
            _hookStartupCompleted.Set();

            var messageResult = RunMessagePump();
            if (messageResult < 0)
                AppLog.Warn("Wing.Guard", "Win+G hook message pump failed.", fields: [("Win32Error", Marshal.GetLastWin32Error())]);
            else if (messageResult == 0 && Volatile.Read(ref _disposed) == 0)
                AppLog.Warn("Wing.Guard", "Win+G hook message pump received an unexpected quit request.");
        }
        catch (Exception exception)
        {
            AppLog.Warn("Wing.Guard", "Win+G hook worker failed.", exception);
        }
        finally
        {
            Volatile.Write(ref _armed, 0);
            if (hook != IntPtr.Zero)
            {
                var removed = TryRemoveHook(hook);
                if (Volatile.Read(ref _hook) == hook) Interlocked.Exchange(ref _hook, IntPtr.Zero);
                if (removed) _callback = null;
            }
            else
            {
                _callback = null;
            }

            Volatile.Write(ref _hookThreadId, 0);
            _hookStartupCompleted.Set();
        }
    }

    private int RunMessagePump()
    {
        while (true)
        {
            var result = GetMessage(out var message, IntPtr.Zero, 0, 0);
            if (result <= 0) return result;
            _ = TranslateMessage(ref message);
            _ = DispatchMessage(ref message);
        }
    }

    private void RequestHookThreadStop(Thread thread, bool waitForExit)
    {
        var threadId = Volatile.Read(ref _hookThreadId);
        if (threadId != 0 && thread.IsAlive && !PostThreadMessage(threadId, WmQuit, UIntPtr.Zero, IntPtr.Zero))
            AppLog.Warn("Wing.Guard", "Win+G hook thread stop message could not be posted.", fields: [("Win32Error", Marshal.GetLastWin32Error())]);

        if (waitForExit && thread.IsAlive && !thread.Join(HookShutdownTimeoutMilliseconds))
            AppLog.Warn("Wing.Guard", "Win+G hook thread did not stop before the shutdown deadline.", fields: [("TimeoutMs", HookShutdownTimeoutMilliseconds)]);
    }

    private bool TryRemoveHook(IntPtr hook)
    {
        try
        {
            if (_remove(hook))
            {
                AppLog.Info("Wing.Guard", "Win+G hook removed.");
                return true;
            }
            AppLog.Warn("Wing.Guard", "Win+G hook removal failed; callback remains rooted until process exit.", fields: [("Win32Error", Marshal.GetLastWin32Error())]);
        }
        catch (Exception exception)
        {
            AppLog.Warn("Wing.Guard", "Win+G hook removal failed; callback remains rooted until process exit.", exception);
        }
        return false;
    }

    private void SendCleanup(int wins)
    {
        var winKeyCount = (wins & 1) != 0 ? 1 : 0;
        if ((wins & 2) != 0) winKeyCount++;
        var inputs = new Input[2 + winKeyCount];
        inputs[0] = Input.Key(0xFF, 0, OwnMarker);
        inputs[1] = Input.Key(0xFF, KeyUp, OwnMarker);
        var inputCount = 2;
        if ((wins & 1) != 0)
        {
            var scan = MapVirtualKey(VkLWin, 0);
            inputs[inputCount++] = Input.Key(VkLWin, KeyUp | Extended, OwnMarker, (ushort)scan);
        }
        if ((wins & 2) != 0)
        {
            var scan = MapVirtualKey(VkRWin, 0);
            inputs[inputCount++] = Input.Key(VkRWin, KeyUp | Extended, OwnMarker, (ushort)scan);
        }

        var sent = _sendInput(inputs, (uint)inputCount);
        var primaryError = sent == (uint)inputCount ? 0 : Marshal.GetLastWin32Error();
        if (sent == 1)
        {
            try
            {
                var fallbackSent = _sendInput([inputs[1]], 1);
                var fallbackError = fallbackSent == 1 ? 0 : Marshal.GetLastWin32Error();
                if (AppLog.IsEnabled(AppLogLevel.Debug)) AppLog.Debug("Wing.Input", "Dummy cleanup fallback completed.", ("SentInputs", fallbackSent), ("Win32Error", fallbackError));
                if (fallbackSent != 1)
                    AppLog.Warn("Wing.Input", "Dummy cleanup fallback failed.", fields: [("SentInputs", fallbackSent), ("Win32Error", fallbackError)]);
            }
            catch (Exception exception) { AppLog.Warn("Wing.Input", "Cleanup fallback failed.", exception); }
        }
        if (wins is 1 or 2 or 3)
        {
            if ((wins & 1) != 0 && sent > 2) Interlocked.Or(ref _releasedByCleanup, 1);
            if ((wins & 2) != 0 && sent > (wins == 3 ? 3u : 2u)) Interlocked.Or(ref _releasedByCleanup, 2);
        }
        if (AppLog.IsEnabled(AppLogLevel.Debug)) AppLog.Debug("Wing.Input", "Modifier cleanup completed.", ("RequestedInputs", inputCount), ("SentInputs", sent));
        if (sent != (uint)inputCount)
            AppLog.Warn("Wing.Input", "Partial SendInput during Win+G cleanup.", fields: [("RequestedInputs", inputCount), ("SentInputs", sent), ("Win32Error", primaryError)]);
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return _next(_hook, code, wParam, lParam);
        try
        {
            var data = Marshal.PtrToStructure<KeyboardData>(lParam);
            var result = ProcessKey((int)data.VkCode, (wParam.ToInt64() & 1) == 0, data.ExtraInfo);
            return result != IntPtr.Zero ? result : _next(_hook, code, wParam, lParam);
        }
        catch (Exception exception) { AppLog.Warn("Wing.Guard", "Win+G hook callback was contained.", exception); return _next(_hook, code, wParam, lParam); }
    }

    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardData { internal uint VkCode, ScanCode, Flags, Time; internal nint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeMessage { internal IntPtr Window; internal uint Message; internal UIntPtr WParam; internal IntPtr LParam; internal uint Time; internal NativePoint Point; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { internal uint Type; internal InputUnion Data; internal static Input Key(ushort vk, uint flags, long extra, ushort scan = 0) => new() { Type = 1, Data = new() { Keyboard = new() { Vk = vk, Scan = scan, Flags = flags, ExtraInfo = (nint)extra } } }; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion { [FieldOffset(0)] internal KeyInput Keyboard; [FieldOffset(0)] internal MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyInput { internal ushort Vk, Scan; internal uint Flags, Time; internal nint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { internal int X, Y; internal uint MouseData, Flags, Time; internal nint ExtraInfo; }
    internal delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(uint id, LowLevelKeyboardProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetMessage(out NativeMessage message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref NativeMessage message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref NativeMessage message);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
