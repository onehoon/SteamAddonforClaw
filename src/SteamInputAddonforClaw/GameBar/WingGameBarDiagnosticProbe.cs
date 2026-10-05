using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.GameBar;

internal static class WingGameBarDiagnosticProbe
{
    private const string Category = "Wing.GameBarDiag";
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(25);
    private static long _nextProbeId;

    internal static void Start()
    {
        if (!AppLog.IsEnabled(AppLogLevel.Debug)) return;

        var probeId = Interlocked.Increment(ref _nextProbeId);
        var triggerTimestamp = Stopwatch.GetTimestamp();
        WingGameBarForegroundIdentity initialIdentity;
        try
        {
            initialIdentity = CaptureIdentity();
        }
        catch (Exception exception)
        {
            AppLog.Debug(Category, "ProbeStartFailed", ("ProbeId", probeId), ("Exception", exception.GetType().Name));
            return;
        }

        try { _ = Task.Run(() => ObserveAsync(probeId, triggerTimestamp, initialIdentity)); }
        catch (Exception exception)
        {
            AppLog.Debug(Category, "ProbeStartFailed", ("ProbeId", probeId), ("Exception", exception.GetType().Name));
        }
    }

    private static async Task ObserveAsync(long probeId, long triggerTimestamp, WingGameBarForegroundIdentity initialIdentity)
    {
        try
        {
            var current = CaptureSnapshot(initialIdentity);
            LogForeground("ForegroundSnapshot", probeId, ElapsedMilliseconds(triggerTimestamp), current);

            var changeCount = 0;
            while (Stopwatch.GetElapsedTime(triggerTimestamp) < Duration)
            {
                await Task.Delay(SampleInterval).ConfigureAwait(false);
                var identity = CaptureIdentity();
                if (identity == current.Identity) continue;

                current = CaptureSnapshot(identity);
                changeCount++;
                LogForeground("ForegroundChanged", probeId, ElapsedMilliseconds(triggerTimestamp), current);
            }

            AppLog.Debug(Category, "ProbeCompleted",
                ("ProbeId", probeId),
                ("Trigger", "Event88Accepted"),
                ("DurationMs", ElapsedMilliseconds(triggerTimestamp)),
                ("ForegroundChangeCount", changeCount),
                ("FinalHwnd", FormatHwnd(current.Identity.Hwnd)),
                ("FinalPid", current.Identity.Pid),
                ("FinalProcessName", current.ProcessName));
        }
        catch (Exception exception)
        {
            AppLog.Debug(Category, "ProbeFailed", ("ProbeId", probeId), ("Exception", exception.GetType().Name));
        }
    }

    private static WingGameBarForegroundSnapshot CaptureSnapshot(WingGameBarForegroundIdentity identity) => new(
        identity,
        CaptureProcessName(identity.Pid),
        CaptureWindowClass(identity.Hwnd),
        CaptureWindowTitle(identity.Hwnd));

    private static WingGameBarForegroundIdentity CaptureIdentity()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return new(hwnd, 0);

        _ = GetWindowThreadProcessId(hwnd, out var processId);
        return new(hwnd, processId);
    }

    private static string CaptureProcessName(uint processId)
    {
        if (processId == 0) return string.Empty;

        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName;
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static string CaptureWindowClass(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return string.Empty;

        try
        {
            var buffer = new StringBuilder(256);
            var length = GetClassName(hwnd, buffer, buffer.Capacity);
            return length > 0 ? buffer.ToString() : string.Empty;
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static string CaptureWindowTitle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return string.Empty;

        try
        {
            var buffer = new StringBuilder(1024);
            var length = GetWindowText(hwnd, buffer, buffer.Capacity);
            return length > 0 ? buffer.ToString() : string.Empty;
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static void LogForeground(string message, long probeId, long elapsedMs, WingGameBarForegroundSnapshot snapshot) =>
        AppLog.Debug(Category, message,
            ("ProbeId", probeId),
            ("Trigger", "Event88Accepted"),
            ("ElapsedMs", elapsedMs),
            ("Hwnd", FormatHwnd(snapshot.Identity.Hwnd)),
            ("Pid", snapshot.Identity.Pid),
            ("ProcessName", snapshot.ProcessName),
            ("WindowClass", snapshot.WindowClass),
            ("WindowTitle", snapshot.WindowTitle));

    private static long ElapsedMilliseconds(long triggerTimestamp) =>
        (long)Stopwatch.GetElapsedTime(triggerTimestamp).TotalMilliseconds;

    private static string FormatHwnd(IntPtr hwnd) => $"0x{unchecked((ulong)hwnd.ToInt64()):X16}";

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder windowText, int maxCount);
}

internal readonly record struct WingGameBarForegroundIdentity(IntPtr Hwnd, uint Pid);

internal sealed record WingGameBarForegroundSnapshot(
    WingGameBarForegroundIdentity Identity,
    string ProcessName,
    string WindowClass,
    string WindowTitle);
