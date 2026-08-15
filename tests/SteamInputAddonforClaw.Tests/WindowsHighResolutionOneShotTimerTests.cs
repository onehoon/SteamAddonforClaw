using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class WindowsHighResolutionOneShotTimerTests
{
    [Fact]
    public void Constructor_RequestsHighResolutionFlagWithoutManualReset()
    {
        var fake = new FakeNativeApi();
        using var timer = new WindowsHighResolutionOneShotTimer(fake);

        // 0x00000002 = CREATE_WAITABLE_TIMER_HIGH_RESOLUTION only; the manual-reset bit (0x00000001)
        // must never be OR'd in -- this must stay an auto-reset/synchronization timer.
        Assert.Equal(0x00000002u, fake.CreateFlags);
        Assert.Equal(0u, fake.CreateFlags & 0x00000001u);
    }

    [Fact]
    public void Constructor_RequestsOnlySynchronizeAndTimerModifyState()
    {
        var fake = new FakeNativeApi();
        using var timer = new WindowsHighResolutionOneShotTimer(fake);

        Assert.Equal(0x00100002u, fake.CreateAccess);
        // Must not request TIMER_ALL_ACCESS (0x1F0003) or any bit beyond SYNCHRONIZE|TIMER_MODIFY_STATE.
        Assert.Equal(0u, fake.CreateAccess & ~0x00100002u);
    }

    [Fact]
    public void Constructor_DoesNotArm()
    {
        // Unlike the old periodic timer, construction only creates the native handle -- arming is a
        // separate, explicit ArmRelative call so the caller controls exactly when the first deadline is
        // established.
        var fake = new FakeNativeApi();
        using var timer = new WindowsHighResolutionOneShotTimer(fake);

        Assert.Equal(0, fake.SetWaitableTimerExCallCount);
    }

    [Fact]
    public void Constructor_CreateFailureClosesHandleAndThrows()
    {
        var fake = new FakeNativeApi { FailCreate = true };

        var exception = Assert.Throws<InvalidOperationException>(() => new WindowsHighResolutionOneShotTimer(fake));

        Assert.Contains("CreateWaitableTimerExW", exception.Message);
        Assert.True(fake.LastCreatedHandle?.IsClosed);
    }

    [Fact]
    public void ArmRelative_UsesNegativeRelativeDueTimeAndZeroPeriod()
    {
        var fake = new FakeNativeApi();
        using var timer = new WindowsHighResolutionOneShotTimer(fake);

        timer.ArmRelative(TimeSpan.FromMilliseconds(4));

        // 4 ms in 100ns units = 40,000; relative due times are negative per the Win32 convention.
        Assert.Equal(-40_000L, fake.ArmedDueTime100ns);
        // lPeriod = 0: fires once and does not re-arm itself.
        Assert.Equal(0, fake.ArmedPeriodMs);
        Assert.Equal(1, fake.SetWaitableTimerExCallCount);
    }

    [Fact]
    public void ArmRelative_AcceptsSubMillisecondDurationWithoutLosingPrecision()
    {
        // Unlike the old periodic timer's whole-millisecond-only period, one-shot due times must support
        // full 100ns precision -- the scheduler needs to arm for e.g. "3.6712ms remaining."
        var fake = new FakeNativeApi();
        using var timer = new WindowsHighResolutionOneShotTimer(fake);

        var subMs = TimeSpan.FromTicks(12_345); // 1.2345 ms, not a whole millisecond
        timer.ArmRelative(subMs);

        Assert.Equal(-12_345L, fake.ArmedDueTime100ns);
    }

    [Fact]
    public void ArmRelative_RejectsZeroDuration()
    {
        var fake = new FakeNativeApi();
        using var timer = new WindowsHighResolutionOneShotTimer(fake);

        Assert.Throws<ArgumentOutOfRangeException>(() => timer.ArmRelative(TimeSpan.Zero));
        Assert.Equal(0, fake.SetWaitableTimerExCallCount);
    }

    [Fact]
    public void ArmRelative_RejectsNegativeDuration()
    {
        var fake = new FakeNativeApi();
        using var timer = new WindowsHighResolutionOneShotTimer(fake);

        Assert.Throws<ArgumentOutOfRangeException>(() => timer.ArmRelative(TimeSpan.FromMilliseconds(-1)));
        Assert.Equal(0, fake.SetWaitableTimerExCallCount);
    }

    [Fact]
    public void ArmRelative_FailureThrowsWithWin32Error()
    {
        var fake = new FakeNativeApi { FailArm = true };
        using var timer = new WindowsHighResolutionOneShotTimer(fake);

        var exception = Assert.Throws<InvalidOperationException>(() => timer.ArmRelative(TimeSpan.FromMilliseconds(4)));

        Assert.Contains("SetWaitableTimerEx", exception.Message);
    }

    [Fact]
    public void ArmRelative_RepeatedCallsReuseTheSameNativeHandle()
    {
        // The whole point of the one-shot redesign: the native timer object is created once and re-armed
        // repeatedly, not recreated every ~4ms.
        var fake = new FakeNativeApi();
        using var timer = new WindowsHighResolutionOneShotTimer(fake);

        timer.ArmRelative(TimeSpan.FromMilliseconds(4));
        timer.ArmRelative(TimeSpan.FromMilliseconds(3.7));
        timer.ArmRelative(TimeSpan.FromMilliseconds(4.1));

        Assert.Equal(1, fake.CreateWaitableTimerExCallCount);
        Assert.Equal(3, fake.SetWaitableTimerExCallCount);
    }

    [Fact]
    public void ArmRelative_AfterDisposeThrows()
    {
        var fake = new FakeNativeApi();
        var timer = new WindowsHighResolutionOneShotTimer(fake);
        timer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => timer.ArmRelative(TimeSpan.FromMilliseconds(4)));
    }

    [Fact]
    public void Dispose_CancelsTheTimerOnce()
    {
        var fake = new FakeNativeApi();
        var timer = new WindowsHighResolutionOneShotTimer(fake);
        timer.ArmRelative(TimeSpan.FromMilliseconds(4));

        timer.Dispose();

        Assert.True(fake.CancelWaitableTimerCalled);
        Assert.Equal(1, fake.CancelWaitableTimerCallCount);
    }

    [Fact]
    public void Dispose_CalledTwiceCancelsOnlyOnceAndDoesNotThrow()
    {
        var fake = new FakeNativeApi();
        var timer = new WindowsHighResolutionOneShotTimer(fake);

        timer.Dispose();
        var exception = Record.Exception(timer.Dispose);

        Assert.Null(exception);
        Assert.Equal(1, fake.CancelWaitableTimerCallCount);
    }

    [Fact]
    public void Timer_IsAWaitHandle()
    {
        // The whole point of deriving from WaitHandle: it must compose with WaitHandle.WaitAny alongside
        // a stop event on the worker thread, not need any bespoke waiting API of its own. (Actually
        // waiting on it here would require a real, valid OS timer handle rather than this test's fake
        // one, so this checks the structural contract instead -- see the real-timer publisher tests for
        // exercised wait behavior via the manual tick seam.)
        var fake = new FakeNativeApi();
        using var timer = new WindowsHighResolutionOneShotTimer(fake);
        Assert.IsAssignableFrom<WaitHandle>(timer);
    }

    [Fact]
    public void RealWin32Api_CreatesArmsWaitsAndDisposesSuccessfully()
    {
        // The one test that exercises the actual P/Invoke signatures against the real OS, so a marshalling
        // mistake (wrong flag value, wrong struct layout, wrong calling convention) fails here rather than
        // only ever being caught by real-hardware testing. Deterministic: it waits for one real tick with
        // a generous timeout rather than asserting a specific rate.
        using var timer = new WindowsHighResolutionOneShotTimer();
        timer.ArmRelative(TimeSpan.FromMilliseconds(4));
        using var stopEvent = new ManualResetEvent(false);

        var signaledIndex = WaitHandle.WaitAny([timer, stopEvent], TimeSpan.FromSeconds(2));

        Assert.Equal(0, signaledIndex);
    }

    private sealed class FakeNativeApi : IWaitableTimerNativeApi
    {
        internal bool FailCreate;
        internal bool FailArm;
        internal uint CreateFlags;
        internal uint CreateAccess;
        internal long ArmedDueTime100ns;
        internal int ArmedPeriodMs;
        internal int SetWaitableTimerExCallCount;
        internal int CreateWaitableTimerExCallCount;
        internal bool CancelWaitableTimerCalled;
        internal int CancelWaitableTimerCallCount;
        internal SafeWaitHandle? LastCreatedHandle;

        public SafeWaitHandle CreateWaitableTimerEx(uint flags, uint desiredAccess)
        {
            CreateWaitableTimerExCallCount++;
            CreateFlags = flags;
            CreateAccess = desiredAccess;
            // A real, harmless, owned handle so SafeHandle disposal/close tracking works in tests
            // without depending on any actual OS timer object existing.
            var handle = new SafeWaitHandle(FailCreate ? IntPtr.Zero : new IntPtr(12345), ownsHandle: false);
            LastCreatedHandle = handle;
            return handle;
        }

        public bool SetWaitableTimerEx(SafeWaitHandle handle, long dueTime100ns, int periodMs)
        {
            SetWaitableTimerExCallCount++;
            ArmedDueTime100ns = dueTime100ns;
            ArmedPeriodMs = periodMs;
            return !FailArm;
        }

        public bool CancelWaitableTimer(SafeWaitHandle handle)
        {
            CancelWaitableTimerCalled = true;
            CancelWaitableTimerCallCount++;
            return true;
        }

        public int GetLastWin32Error() => FailCreate || FailArm ? 5 : 0;
    }
}
