using System.Diagnostics;
using System.Runtime.InteropServices;

using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.GameBar;

internal readonly record struct GameBarIdentityInspection(bool IsGameBar);

internal interface IGameBarForegroundProbe
{
    GameBarIdentityInspection Inspect(IntPtr hwnd);
}

internal sealed class GameBarForegroundProbe : IGameBarForegroundProbe
{
    internal const string PackageFamilyName = "Microsoft.XboxGamingOverlay_8wekyb3d8bbwe";
    private const string ExecutableName = "GameBar.exe";

    public GameBarIdentityInspection Inspect(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return new(false);
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return new(false);

        try
        {
            using var process = Process.GetProcessById((int)pid);
            var executable = Path.GetFileName(process.MainModule?.FileName);
            if (!string.Equals(executable, ExecutableName, StringComparison.OrdinalIgnoreCase)) return new(false);
            if (!TryGetPackageFamilyName(process.Handle, out var familyName)) return new(false);
            return new(IsExpectedPackageFamily(familyName));
        }
        catch
        {
            return new(false);
        }
    }

    internal static bool IsExpectedPackageFamily(string? familyName) =>
        string.Equals(familyName, PackageFamilyName, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetPackageFamilyName(IntPtr process, out string? familyName)
    {
        familyName = null;
        uint length = 0;
        var result = GetPackageFamilyName(process, ref length, null);
        if (result != ErrorInsufficientBuffer || length == 0) return false;
        var buffer = new char[length];
        if (GetPackageFamilyName(process, ref length, buffer) != 0) return false;
        familyName = new string(buffer, 0, checked((int)length - 1));
        return true;
    }

    private const uint ErrorInsufficientBuffer = 122;
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetPackageFamilyName(IntPtr process, ref uint packageFamilyNameLength, char[]? packageFamilyName);
}

internal sealed class GameBarForegroundWatcher : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint OutOfContext = 0;
    private readonly Func<uint, uint, WinEventCallback, IntPtr, uint, uint, uint, IntPtr> _hook;
    private readonly Func<IntPtr> _foreground;
    private readonly Action<IntPtr> _unhook;
    private readonly IGameBarForegroundProbe _probe;
    private WinEventCallback? _callback;
    private IntPtr _hookHandle;
    private int _started;
    private int _disposed;
    private bool _isForeground;

    internal GameBarForegroundWatcher(
        IGameBarForegroundProbe? probe = null,
        Func<uint, uint, WinEventCallback, IntPtr, uint, uint, uint, IntPtr>? hook = null,
        Func<IntPtr>? foreground = null,
        Action<IntPtr>? unhook = null)
    {
        _probe = probe ?? new GameBarForegroundProbe();
        _hook = hook ?? ((min, max, callback, module, process, thread, flags) => SetWinEventHook(min, max, module, callback, process, thread, flags));
        _foreground = foreground ?? GetForegroundWindow;
        _unhook = unhook ?? (hookHandle => _ = UnhookWinEvent(hookHandle));
    }

    internal bool IsForeground => Volatile.Read(ref _isForeground);
    internal event EventHandler? StateChanged;

    internal void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0 || Volatile.Read(ref _disposed) != 0) return;
        _callback = OnForegroundEvent;
        try
        {
            _hookHandle = _hook(EventSystemForeground, EventSystemForeground, _callback, IntPtr.Zero, 0, 0, OutOfContext);
            if (_hookHandle == IntPtr.Zero)
            {
                AppLog.Warn("GameBar", "Game Bar foreground hook unavailable.");
                return;
            }
            AppLog.Info("GameBar", "Game Bar foreground detector started.");
            Publish(_foreground());
        }
        catch (Exception exception)
        {
            AppLog.Warn("GameBar", "Game Bar foreground hook unavailable.", exception);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var hook = Interlocked.Exchange(ref _hookHandle, IntPtr.Zero);
        if (hook != IntPtr.Zero)
        {
            try { _unhook(hook); } catch { }
        }
        _callback = null;
    }

    private void OnForegroundEvent(IntPtr _, uint __, IntPtr hwnd, int ___, int ____, uint _____, uint ______)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        try { Publish(_foreground()); }
        catch (Exception exception) { AppLog.Warn("GameBar", "Game Bar foreground event was contained.", exception); }
    }

    private void Publish(IntPtr authoritativeHwnd)
    {
        var value = authoritativeHwnd != IntPtr.Zero && _probe.Inspect(authoritativeHwnd).IsGameBar;
        if (value == Volatile.Read(ref _isForeground) || Volatile.Read(ref _disposed) != 0) return;
        Volatile.Write(ref _isForeground, value);
        AppLog.Info("GameBar", value ? "Game Bar entered foreground." : "Game Bar left foreground.");
        try { StateChanged?.Invoke(this, EventArgs.Empty); } catch (Exception exception) { AppLog.Warn("GameBar", "Game Bar state subscriber failed.", exception); }
    }

    internal delegate void WinEventCallback(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint threadId, uint eventTime);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint minEvent, uint maxEvent, IntPtr module, WinEventCallback callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
