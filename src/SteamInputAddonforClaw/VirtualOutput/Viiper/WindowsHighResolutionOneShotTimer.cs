using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// A Windows high-resolution one-shot waitable timer, exposed as a <see cref="WaitHandle"/> so a
/// dedicated worker thread can <c>WaitHandle.WaitAny</c> on it together with a stop event without any
/// polling, `Task.Delay`, or thread-pool continuation involved.
/// </summary>
/// <remarks>
/// <para>
/// This replaced an earlier periodic-timer design (<c>lPeriod</c> re-arming itself every period). Real
/// MSI Claw hardware testing (Addon 0.1.71, PR #156 diagnostics) showed that design's wake-to-wake
/// interval averaging ~4.33ms against a 4.00ms period -- not because <c>SetState</c> work was slow
/// (~0.043ms average) but because the wait/wakeup boundary itself was consistently a few tenths of a
/// millisecond late (64.9% of wakes exceeded 4.25ms). This describes what was actually observed at that
/// boundary, not a documented guarantee about how the OS internally schedules a periodic timer's
/// re-activation; explicitly tracking a monotonic logical deadline (see
/// <see cref="CanonicalSteamDeckInputPublisher"/>) is a testable alternative to whatever the
/// periodic timer was doing, not a claim that the periodic timer itself was behaving incorrectly.
/// </para>
/// <para>
/// This type instead exposes only a single-shot arm (<see cref="ArmRelative"/>); the caller
/// (<see cref="CanonicalSteamDeckInputPublisher"/>) is responsible for tracking a monotonic
/// absolute logical deadline in <see cref="System.Diagnostics.Stopwatch"/> ticks and re-arming this
/// timer, each time, for only the remaining time until the *next* logical deadline -- so a late wake
/// shortens the following wait instead of shifting the whole schedule forward. The same native timer
/// handle is reused across repeated <see cref="ArmRelative"/> calls; this type does not create or
/// destroy a native timer object per tick.
/// </para>
/// <para>
/// Independently implemented against the documented Win32 waitable-timer API
/// (CreateWaitableTimerExW / SetWaitableTimerEx / CancelWaitableTimer) -- not derived from, or modeled
/// after, any third-party precision-timer implementation.
/// </para>
/// </remarks>
internal sealed class WindowsHighResolutionOneShotTimer : WaitHandle
{
    private readonly IWaitableTimerNativeApi _native;
    private bool _disposed;

    /// <summary>
    /// Creates (but does not arm) a high-resolution waitable timer. Throws immediately -- fail closed,
    /// never falls back to a coarser timer -- if the native timer handle cannot be created.
    /// </summary>
    internal WindowsHighResolutionOneShotTimer(IWaitableTimerNativeApi? nativeApi = null)
    {
        _native = nativeApi ?? Win32WaitableTimerNativeApi.Instance;

        var handle = _native.CreateWaitableTimerEx(NativeConstants.CreateWaitableTimerHighResolution, NativeConstants.TimerAccessRights);
        if (handle.IsInvalid)
        {
            var error = _native.GetLastWin32Error();
            handle.Dispose();
            throw new InvalidOperationException($"CreateWaitableTimerExW failed to create a high-resolution timer. Win32Error={error}");
        }

        SafeWaitHandle = handle;
    }

    /// <summary>
    /// Arms (or re-arms) this timer as a one-shot, firing once after <paramref name="dueIn"/> and not
    /// re-arming itself (<c>lPeriod = 0</c>) -- the caller must call this again for the next wake. The
    /// same native handle is reused; this does not create a new timer object.
    /// </summary>
    /// <remarks>
    /// Unlike the old periodic design's whole-millisecond period restriction, one-shot relative due
    /// times support full 100ns precision (<paramref name="dueIn"/>'s <see cref="TimeSpan.Ticks"/> are
    /// already 100ns units, matching the native due-time unit directly), since the caller needs
    /// sub-millisecond precision to arm for "however much of the 4ms period is actually left."
    /// </remarks>
    internal void ArmRelative(TimeSpan dueIn)
    {
        if (dueIn <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(dueIn), dueIn, "The one-shot due time must be positive.");
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Relative due time in 100ns units, negative per the Win32 convention; TimeSpan.Ticks are
        // already 100ns units. lPeriod = 0 makes this fire exactly once.
        var dueTime = -dueIn.Ticks;
        if (!_native.SetWaitableTimerEx(SafeWaitHandle, dueTime, periodMs: 0))
        {
            var error = _native.GetLastWin32Error();
            throw new InvalidOperationException($"SetWaitableTimerEx failed to arm the high-resolution one-shot timer. Win32Error={error}");
        }
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
    // synchronization timer so each arm only wakes the worker once, not every waiter forever).
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
