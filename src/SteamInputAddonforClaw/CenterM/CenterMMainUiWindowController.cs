using System.Runtime.InteropServices;
using System.Text;

namespace SteamInputAddonforClaw.CenterM;

internal enum CenterMMainUiMinimizeResult
{
    Requested,
    NoRecognizedVisibleWindow,
    AmbiguousVisibleWindows,
    /// <summary>The unique recognized visible window changed between the initial enumeration and
    /// the final pre-post revalidation (destroyed, replaced, or a second candidate appeared) -- the
    /// cached HWND from the first pass is never treated as still-authoritative just because it was
    /// valid a moment ago.</summary>
    TargetChanged,
    Failed
}

/// <summary>Narrow Win32 control seam for the routing-retirement use case only -- distinct from
/// <see cref="IMainUiWindowSnapshotProvider"/>, which deliberately returns counts only and never
/// exposes HWNDs. Never touches a tray-owner window or any unrecognized top-level window.</summary>
internal interface ICenterMMainUiWindowController
{
    /// <summary>Posts a normal minimize command to the single recognized visible MainUI window
    /// owned by <paramref name="processId"/>. Fails closed (no message posted) if zero or more than
    /// one recognized visible candidate is found, or if the target changes between discovery and
    /// the final pre-post revalidation -- never guesses which window to minimize, and never posts
    /// to a stale/reused HWND. Success means only that the command was queued; the caller must
    /// independently verify the window actually became hidden.</summary>
    CenterMMainUiMinimizeResult TryMinimizeRecognizedMainUi(int processId);
}

/// <summary>Native window-enumeration/messaging primitives isolated behind an interface so
/// <see cref="Win32CenterMMainUiWindowController"/>'s target-discovery/revalidation/ambiguity logic
/// can be unit tested deterministically without a real desktop.</summary>
internal interface IWin32MainUiWindowApi
{
    /// <summary>Returns every top-level window handle, or <see langword="null"/> if enumeration
    /// itself failed.</summary>
    IReadOnlyList<IntPtr>? EnumerateTopLevelWindows();
    uint GetOwnerProcessId(IntPtr hWnd);
    bool IsWindowVisible(IntPtr hWnd);
    string? GetClassName(IntPtr hWnd);
    string? GetWindowText(IntPtr hWnd);
    bool PostMinimize(IntPtr hWnd);
}

internal sealed class Win32MainUiWindowApi : IWin32MainUiWindowApi
{
    private const uint WM_SYSCOMMAND = 0x0112;
    private const nuint SC_MINIMIZE = 0xF020;

    public IReadOnlyList<IntPtr>? EnumerateTopLevelWindows()
    {
        var windows = new List<IntPtr>();
        bool Callback(IntPtr hWnd, IntPtr _) { windows.Add(hWnd); return true; }
        // The callback above always returns true, so EnumWindows returning false here means
        // enumeration itself failed (never that the callback asked to stop).
        return EnumWindows(Callback, IntPtr.Zero) ? windows : null;
    }

    public uint GetOwnerProcessId(IntPtr hWnd)
    {
        GetWindowThreadProcessId(hWnd, out var ownerPid);
        return ownerPid;
    }

    public bool IsWindowVisible(IntPtr hWnd) => IsWindowVisibleNative(hWnd);

    public string? GetClassName(IntPtr hWnd)
    {
        var buffer = new StringBuilder(256);
        return GetClassNameW(hWnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : null;
    }

    public string? GetWindowText(IntPtr hWnd)
    {
        var length = GetWindowTextLengthW(hWnd);
        if (length <= 0) return null;
        var buffer = new StringBuilder(length + 1);
        return GetWindowTextW(hWnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : null;
    }

    public bool PostMinimize(IntPtr hWnd) => PostMessageW(hWnd, WM_SYSCOMMAND, (UIntPtr)SC_MINIMIZE, IntPtr.Zero);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    private static extern bool IsWindowVisibleNative(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder buffer, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder buffer, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);
}

internal sealed class Win32CenterMMainUiWindowController(IWin32MainUiWindowApi? api = null) : ICenterMMainUiWindowController
{
    private readonly IWin32MainUiWindowApi _api = api ?? new Win32MainUiWindowApi();

    public CenterMMainUiMinimizeResult TryMinimizeRecognizedMainUi(int processId)
    {
        var discovered = EnumerateRecognizedVisibleWindows(processId);
        if (discovered is null) return CenterMMainUiMinimizeResult.Failed;
        if (discovered.Count == 0) return CenterMMainUiMinimizeResult.NoRecognizedVisibleWindow;
        if (discovered.Count > 1) return CenterMMainUiMinimizeResult.AmbiguousVisibleWindows;

        var target = discovered[0];

        // A Win32 HWND is not a retained process-object identity: the discovered window can be
        // destroyed (and its numeric handle reused) between discovery and this point. Re-enumerate
        // once more, immediately before the external mutation, so a stale/reused handle is never
        // treated as still-authoritative just because it was valid a moment ago -- the same
        // fresh-immediately-before-the-destructive-action philosophy the process-termination code
        // already uses.
        var revalidated = EnumerateRecognizedVisibleWindows(processId);
        if (revalidated is null) return CenterMMainUiMinimizeResult.Failed;
        if (revalidated.Count != 1 || revalidated[0] != target) return CenterMMainUiMinimizeResult.TargetChanged;

        // Success here means only that the command was queued (PostMessageW is fire-and-forget) --
        // never proof that the window minimized, that Center M processed the event, or that its
        // GoToXInputMode transition completed. Callers must independently verify each of those.
        return _api.PostMinimize(target) ? CenterMMainUiMinimizeResult.Requested : CenterMMainUiMinimizeResult.Failed;
    }

    private List<IntPtr>? EnumerateRecognizedVisibleWindows(int processId)
    {
        var windows = _api.EnumerateTopLevelWindows();
        if (windows is null) return null;

        var candidates = new List<IntPtr>();
        foreach (var hWnd in windows)
        {
            if (_api.GetOwnerProcessId(hWnd) != (uint)processId) continue;
            if (!_api.IsWindowVisible(hWnd)) continue;

            var className = _api.GetClassName(hWnd);
            var title = _api.GetWindowText(hWnd);
            if (!MainUiWindowRecognition.IsRecognizedMainUiWindow(title, className)) continue;

            candidates.Add(hWnd);
        }

        return candidates;
    }
}
