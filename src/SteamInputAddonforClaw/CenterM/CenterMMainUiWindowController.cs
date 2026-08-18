using System.Runtime.InteropServices;
using System.Text;

namespace SteamInputAddonforClaw.CenterM;

internal enum CenterMMainUiMinimizeResult
{
    Requested,
    NoRecognizedVisibleWindow,
    AmbiguousVisibleWindows,
    Failed
}

/// <summary>Narrow Win32 control seam for the routing-retirement use case only -- distinct from
/// <see cref="IMainUiWindowSnapshotProvider"/>, which deliberately returns counts only and never
/// exposes HWNDs. Never touches a tray-owner window or any unrecognized top-level window.</summary>
internal interface ICenterMMainUiWindowController
{
    /// <summary>Posts a normal minimize command to the single recognized visible MainUI window
    /// owned by <paramref name="processId"/>. Fails closed (no message posted) if zero or more than
    /// one recognized visible candidate is found -- never guesses which window to minimize.
    /// Success means only that the command was queued; the caller must independently verify the
    /// window actually became hidden.</summary>
    CenterMMainUiMinimizeResult TryMinimizeRecognizedMainUi(int processId);
}

internal sealed class Win32CenterMMainUiWindowController : ICenterMMainUiWindowController
{
    private const uint WM_SYSCOMMAND = 0x0112;
    private const nuint SC_MINIMIZE = 0xF020;

    public CenterMMainUiMinimizeResult TryMinimizeRecognizedMainUi(int processId)
    {
        var candidates = new List<IntPtr>();
        var enumerationFailed = false;

        bool Callback(IntPtr hWnd, IntPtr _)
        {
            GetWindowThreadProcessId(hWnd, out var ownerPid);
            if (ownerPid != (uint)processId) return true;
            if (!IsWindowVisible(hWnd)) return true;

            var className = GetClassNameSafe(hWnd);
            var title = GetWindowTextSafe(hWnd);
            if (!MainUiWindowRecognition.IsRecognizedMainUiWindow(title, className)) return true;

            candidates.Add(hWnd);
            return true;
        }

        // The callback above always returns true, so EnumWindows returning false here means
        // enumeration itself failed (never that the callback asked to stop).
        if (!EnumWindows(Callback, IntPtr.Zero)) enumerationFailed = true;
        if (enumerationFailed) return CenterMMainUiMinimizeResult.Failed;
        if (candidates.Count == 0) return CenterMMainUiMinimizeResult.NoRecognizedVisibleWindow;
        if (candidates.Count > 1) return CenterMMainUiMinimizeResult.AmbiguousVisibleWindows;

        // Success here means only that the command was queued (PostMessageW is fire-and-forget) --
        // never proof that the window minimized, that Center M processed the event, or that its
        // GoToXInputMode transition completed. Callers must independently verify each of those.
        return PostMessageW(candidates[0], WM_SYSCOMMAND, (UIntPtr)SC_MINIMIZE, IntPtr.Zero)
            ? CenterMMainUiMinimizeResult.Requested
            : CenterMMainUiMinimizeResult.Failed;
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);
}
