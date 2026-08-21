using System.ComponentModel;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Lifecycle;

internal sealed class NativeMessageLoop
{
    private const uint PM_NOREMOVE = 0x0000;
    private const uint WM_QUIT = 0x0012;
    private const uint WM_RUNTIME_READY = 0x8001;
    private readonly uint _ownerThreadId;
    private readonly Func<uint, bool> _postThreadMessage;
    private int _exitRequested;

    internal NativeMessageLoop() : this(PostThreadMessage)
    {
    }

    internal NativeMessageLoop(Func<uint, bool> postThreadMessage, uint? ownerThreadId = null)
    {
        _postThreadMessage = postThreadMessage ?? throw new ArgumentNullException(nameof(postThreadMessage));
        _ownerThreadId = ownerThreadId ?? GetCurrentThreadId();
        // PeekMessageW creates the queue even when it returns false because no message is pending.
        _ = PeekMessageW(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
    }

    internal void Run(Action? onReady = null)
    {
        AppLog.Info("Runtime", "Native message loop entering.", ("ThreadId", _ownerThreadId));
        if (onReady is not null && !PostThreadMessageW(_ownerThreadId, WM_RUNTIME_READY, IntPtr.Zero, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not post Runtime ready message.");
        while (true)
        {
            var result = GetMessageW(out var message, IntPtr.Zero, 0, 0);
            if (result == 0) break;
            if (result == -1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The Runtime message loop failed.");

            if (message.Message == WM_RUNTIME_READY)
            {
                onReady?.Invoke();
                continue;
            }

            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }

        AppLog.Info("Runtime", "Native message loop exited.");
    }

    internal void RequestExit()
    {
        if (Interlocked.Exchange(ref _exitRequested, 1) != 0) return;
        AppLog.Info("Runtime", "Native message loop exit requested.", ("ThreadId", _ownerThreadId));
        if (GetCurrentThreadId() == _ownerThreadId)
        {
            PostQuitMessage(0);
            return;
        }

        if (!_postThreadMessage(_ownerThreadId))
        {
            Volatile.Write(ref _exitRequested, 0);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not post Runtime message-loop exit.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        internal IntPtr Hwnd;
        internal uint Message;
        internal UIntPtr WParam;
        internal IntPtr LParam;
        internal uint Time;
        internal POINT Point;
        internal uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { internal int X; internal int Y; }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PeekMessageW(out MSG message, IntPtr window, uint minFilter, uint maxFilter, uint removeMessage);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessageW(out MSG message, IntPtr window, uint minFilter, uint maxFilter);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG message);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessageW(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    private static bool PostThreadMessage(uint threadId) => PostThreadMessageW(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
}
