using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// A Windows high-resolution periodic waitable timer, exposed as a <see cref="WaitHandle"/> so a
/// dedicated worker thread can <c>WaitHandle.WaitAny</c> on it together with a stop event without any
/// polling, `Task.Delay`, or thread-pool continuation involved.
/// </summary>
/// <remarks>
/// Independently implemented against the documented Win32 waitable-timer API
/// (CreateWaitableTimerExW / SetWaitableTimerEx / CancelWaitableTimer) -- not derived from, or modeled
/// after, any third-party precision-timer implementation.
/// </remarks>
internal sealed class WindowsHighResolutionPeriodicTimer : WaitHandle
{
    private readonly IWaitableTimerNativeApi _native;
    private bool _disposed;

    /// <summary>
    /// Creates and arms a periodic high-resolution waitable timer with the given period. The first due
    /// time is set to exactly one period from now (relative, negative 100ns units per the Win32 API),
    /// and the timer re-fires every <paramref name="period"/> thereafter without needing to be re-armed.
    /// Throws immediately -- fail closed, never falls back to a coarser timer -- if the timer cannot be
    /// created or armed.
    /// </summary>
    internal WindowsHighResolutionPeriodicTimer(TimeSpan period, IWaitableTimerNativeApi? nativeApi = null)
    {
        if (period <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(period), period, "The timer period must be positive.");
        if (period.TotalMilliseconds is < 1 or > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(period), period, "The timer period must be representable as a whole number of milliseconds.");

        _native = nativeApi ?? Win32WaitableTimerNativeApi.Instance;

        var handle = _native.CreateWaitableTimerEx(NativeConstants.CreateWaitableTimerHighResolution, NativeConstants.TimerAccessRights);
        if (handle.IsInvalid)
        {
            var error = _native.GetLastWin32Error();
            handle.Dispose();
            throw new InvalidOperationException($"CreateWaitableTimerExW failed to create a high-resolution periodic timer. Win32Error={error}");
        }

        // Relative due time in 100ns units, negative per the Win32 convention; TimeSpan.Ticks are
        // already 100ns units, so the first due time is exactly one period away.
        var dueTime = -period.Ticks;
        var periodMs = (int)period.TotalMilliseconds;
        if (!_native.SetWaitableTimerEx(handle, dueTime, periodMs))
        {
            var error = _native.GetLastWin32Error();
            handle.Dispose();
            throw new InvalidOperationException($"SetWaitableTimerEx failed to arm the high-resolution periodic timer. Win32Error={error}");
        }

        SafeWaitHandle = handle;
    }

    protected override void Dispose(bool explicitDisposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            var handle = SafeWaitHandle;
            if (handle is { IsInvalid: false, IsClosed: false }) _native.CancelWaitableTimer(handle);
        }
        base.Dispose(explicitDisposing);
    }
}

/// <summary>Abstracts the three Win32 waitable-timer calls this type needs, so timer construction/arm/
/// failure/cleanup logic can be unit tested deterministically without depending on real OS timer
/// creation succeeding (or being able to simulate failure) in a CI environment.</summary>
internal interface IWaitableTimerNativeApi
{
    SafeWaitHandle CreateWaitableTimerEx(uint flags, uint desiredAccess);
    bool SetWaitableTimerEx(SafeWaitHandle handle, long dueTime100ns, int periodMs);
    bool CancelWaitableTimer(SafeWaitHandle handle);
    int GetLastWin32Error();
}

internal static class NativeConstants
{
    // Windows 10 1803+. Falling back silently to the low-resolution variant would defeat the point of
    // this change, so this is the only creation flag used -- no manual-reset bit (CREATE_WAITABLE_TIMER_
    // MANUAL_RESET = 0x00000001 is deliberately never OR'd in; this must stay an auto-reset/
    // synchronization timer so each period only wakes the worker once, not every waiter forever).
    internal const uint CreateWaitableTimerHighResolution = 0x00000002;

    // Only the rights actually used: waiting on the handle (SYNCHRONIZE) and arming/canceling it
    // (TIMER_MODIFY_STATE). Deliberately not TIMER_ALL_ACCESS.
    internal const uint TimerModifyState = 0x0002;
    internal const uint Synchronize = 0x00100000;
    internal const uint TimerAccessRights = TimerModifyState | Synchronize;
}

internal sealed class Win32WaitableTimerNativeApi : IWaitableTimerNativeApi
{
    internal static readonly Win32WaitableTimerNativeApi Instance = new();

    public SafeWaitHandle CreateWaitableTimerEx(uint flags, uint desiredAccess) =>
        NativeMethods.CreateWaitableTimerExW(IntPtr.Zero, null, flags, desiredAccess);

    public bool SetWaitableTimerEx(SafeWaitHandle handle, long dueTime100ns, int periodMs) =>
        NativeMethods.SetWaitableTimerEx(handle, ref dueTime100ns, periodMs, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0);

    public bool CancelWaitableTimer(SafeWaitHandle handle) => NativeMethods.CancelWaitableTimer(handle);

    public int GetLastWin32Error() => Marshal.GetLastWin32Error();

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr lpTimerAttributes, string? lpTimerName, uint dwFlags, uint dwDesiredAccess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWaitableTimerEx(SafeWaitHandle hTimer, ref long lpDueTime, int lPeriod, IntPtr pfnCompletionRoutine, IntPtr lpArgToCompletionRoutine, IntPtr wakeContext, uint tolerableDelayMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CancelWaitableTimer(SafeWaitHandle hTimer);
    }
}
