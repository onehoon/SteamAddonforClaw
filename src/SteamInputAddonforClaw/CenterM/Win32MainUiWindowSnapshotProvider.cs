using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SteamInputAddonforClaw.CenterM;

internal sealed class Win32MainUiWindowSnapshotProvider : IMainUiWindowSnapshotProvider
{
    public MainUiWindowSnapshot? Capture(int processId)
    {
        bool alive;
        try
        {
            using var process = Process.GetProcessById(processId);
            alive = !process.HasExited;
        }
        catch (ArgumentException)
        {
            alive = false;
        }

        if (!alive) return new MainUiWindowSnapshot(false, 0, 0);

        var recognized = 0;
        var visible = 0;
        var enumerationFailed = false;

        bool Callback(IntPtr hWnd, IntPtr _)
        {
            GetWindowThreadProcessId(hWnd, out var ownerPid);
            if (ownerPid != (uint)processId) return true;

            var className = GetClassNameSafe(hWnd);
            var title = GetWindowTextSafe(hWnd);
            if (!MainUiWindowRecognition.IsRecognizedMainUiWindow(title, className)) return true;

            recognized++;
            if (IsWindowVisible(hWnd)) visible++;
            return true;
        }

        // The callback above always returns true, so EnumWindows returning false here means
        // enumeration itself failed (never that the callback asked to stop).
        if (!EnumWindows(Callback, IntPtr.Zero)) enumerationFailed = true;

        return enumerationFailed ? null : new MainUiWindowSnapshot(true, recognized, visible);
    }

    private static string? GetClassNameSafe(IntPtr hWnd)
    {
        var buffer = new StringBuilder(256);
        return GetClassNameW(hWnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : null;
    }

    private static string? GetWindowTextSafe(IntPtr hWnd)
    {
        var length = GetWindowTextLengthW(hWnd);
        if (length <= 0) return null;
        var buffer = new StringBuilder(length + 1);
        return GetWindowTextW(hWnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : null;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder buffer, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder buffer, int maxCount);
}
