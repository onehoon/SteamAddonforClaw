using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SteamInputAddonforClaw.Steam;

internal sealed record SteamBigPictureProbeResult(bool IsActive, bool IsReliable, string Reason);

internal interface ISteamBigPictureWindowProbe
{
    SteamBigPictureProbeResult Capture();
}

internal sealed class SteamBigPictureWindowProbe : ISteamBigPictureWindowProbe
{
    private const string ProcessName = "steamwebhelper";
    private const string WindowClass = "SDL_app";
    private const string TitlePrefix = "Steam Big Picture";

    public SteamBigPictureProbeResult Capture()
    {
        try
        {
            var found = false;
            var reliable = true;
            EnumWindows((window, _) =>
            {
                if (!IsBigPictureWindow(window, out var windowReliable))
                {
                    reliable &= windowReliable;
                    return true;
                }

                found = true;
                return false;
            }, IntPtr.Zero);
            return new(found, reliable, found ? "Active" : "Inactive");
        }
        catch (Exception exception)
        {
            return new(false, false, exception.GetType().Name);
        }
    }

    private static bool IsBigPictureWindow(IntPtr window, out bool reliable)
    {
        reliable = true;
        var className = ReadWindowText(GetClassName, window);
        var title = ReadWindowText(GetWindowText, window);
        if (!string.Equals(className, WindowClass, StringComparison.Ordinal)) return false;
        if (!title.StartsWith(TitlePrefix, StringComparison.Ordinal)) return false;
        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) { reliable = false; return false; }
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return string.Equals(process.ProcessName, ProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            reliable = false;
            return false;
        }
    }

    private static string ReadWindowText(Func<IntPtr, StringBuilder, int, int> reader, IntPtr window)
    {
        var buffer = new StringBuilder(512);
        reader(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}

public sealed class SteamBigPictureWatcher : IDisposable
{
    private readonly ISteamBigPictureWindowProbe _probe;
    private readonly object _sync = new();
    private WinEventDelegate? _callback;
    private IntPtr _hook;
    private bool _started;
    private bool _disposed;
    private bool _isActive;

    internal SteamBigPictureWatcher(ISteamBigPictureWindowProbe? probe = null) => _probe = probe ?? new SteamBigPictureWindowProbe();
    public event EventHandler? StateChanged;
    public bool IsActive { get { lock (_sync) return _isActive; } }

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            _started = true;
            _callback = OnWinEvent;
            _hook = SetWinEventHook(0x8000, 0x800C, IntPtr.Zero, _callback, 0, 0, 0);
            RefreshCore();
        }
    }

    public void Refresh() => RefreshCore();

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint time) => RefreshCore();

    private void RefreshCore()
    {
        var result = _probe.Capture();
        if (!result.IsReliable) return;
        bool changed;
        lock (_sync)
        {
            if (_disposed || !_started || _isActive == result.IsActive) return;
            _isActive = result.IsActive;
            changed = true;
        }
        if (changed) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _started = false;
            if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
            _callback = null;
        }
        GC.SuppressFinalize(this);
    }

    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint minEvent, uint maxEvent, IntPtr module, WinEventDelegate callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
}
