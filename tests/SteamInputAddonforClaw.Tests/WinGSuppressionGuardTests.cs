using System.Runtime.InteropServices;
using System.Reflection;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.GameBar;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class WinGSuppressionGuardTests
{
    [Fact] public void DisarmedWinGPasses() { using var g = Create(); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x5B, true)); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x47, true)); }
    [Fact] public void ArmedLeftWinGIsSuppressed() => AssertChord(0x5B);
    [Fact] public void ArmedRightWinGIsSuppressed() => AssertChord(0x5C);
    [Fact] public void GWithoutWinPasses() { using var g = Create(); g.Start(); Assert.True(g.IsHookInstalled); Assert.True(g.EnsureArmed()); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x47, true)); }
    [Fact] public void OtherWinShortcutsPass() { using var g = Create(); g.EnsureArmed(); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x5B, true)); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x52, true)); }
    [Fact] public void PassThroughForwardsOriginalHookArguments() { IntPtr seenW = IntPtr.Zero, seenL = IntPtr.Zero; using var g = Create(next: (_, _, w, l) => { seenW = w; seenL = l; return IntPtr.Zero; }); var data = Marshal.AllocHGlobal(Marshal.SizeOf<WinGSuppressionGuard.KeyboardData>()); try { Marshal.StructureToPtr(new WinGSuppressionGuard.KeyboardData { VkCode = 0x52 }, data, false); g.InvokeHookForTest(new(0x123), data); Assert.Equal(new(0x123), seenW); Assert.Equal(data, seenL); } finally { Marshal.FreeHGlobal(data); } }
    [Fact] public void ExternalInjectedWinGIsSuppressed() { using var g = Create(); g.EnsureArmed(); Assert.Equal(IntPtr.Zero, Invoke(g, 0x5B, true, 0x10, 123)); Assert.NotEqual(IntPtr.Zero, Invoke(g, 0x47, true, 0x10, 123)); }
    [Fact] public void OwnMarkerBypassesGuard() { using var g = Create(); g.EnsureArmed(); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x5B, true, extraInfo: OwnMarker)); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x47, true, extraInfo: OwnMarker)); }
    [Fact] public void CleanupConsumesMatchingWinAndGUp() { using var g = Create(); g.EnsureArmed(); g.ProcessKey(0x5B, true); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, true)); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, false)); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x5B, false)); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x52, true)); }
    [Fact] public void PartialCleanupAttemptsDummyUpAndStillSuppresses() { var calls = new List<WinGSuppressionGuard.Input[]>(); using var g = Create(calls, (_, _) => 1); g.EnsureArmed(); g.ProcessKey(0x5B, true); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, true)); Assert.Equal(2, calls.Count); Assert.Single(calls[1]); Assert.Equal((ushort)0xFF, calls[1][0].Data.Keyboard.Vk); Assert.Equal(0x0002u, calls[1][0].Data.Keyboard.Flags); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x5B, false)); }
    [Fact] public void RapidConsecutiveChordsAreBothSuppressed() { using var g = Create(); g.EnsureArmed(); for (var i = 0; i < 2; i++) { g.ProcessKey(0x5B, true); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, true)); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, false)); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x5B, false)); } }
    [Fact] public void DisarmLeavesCurrentResidueConsumedThenPassesNewChord() { using var g = Create(); g.EnsureArmed(); g.ProcessKey(0x5B, true); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, true)); g.Disarm(); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, false)); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x5B, false)); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x5B, true)); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x47, true)); }
    [Fact] public void StartAndDisposeAreIdempotentAndHookLivesOnDedicatedHighestPriorityThread()
    {
        var installs = 0;
        var removes = 0;
        var installThread = 0;
        var removeThread = 0;
        var installPriority = ThreadPriority.Normal;
        using var g = new WinGSuppressionGuard(
            (_, thread, _, _, _) =>
            {
                Interlocked.Increment(ref installs);
                Assert.Equal(0, thread);
                installThread = Environment.CurrentManagedThreadId;
                installPriority = Thread.CurrentThread.Priority;
                return new(1);
            },
            (_, _, _, _) => IntPtr.Zero,
            _ =>
            {
                Interlocked.Increment(ref removes);
                removeThread = Environment.CurrentManagedThreadId;
                return true;
            });

        g.Start();
        g.Start();
        Assert.True(g.EnsureArmed());
        Assert.NotEqual(Environment.CurrentManagedThreadId, installThread);
        Assert.Equal(ThreadPriority.Highest, installPriority);
        g.Dispose();
        g.Dispose();

        Assert.Equal(1, installs);
        Assert.Equal(1, removes);
        Assert.Equal(installThread, removeThread);
        Assert.False(g.IsHookInstalled);
        Assert.False(g.IsArmed);
    }

    [Fact] public void FailedDisposeClearsReadinessButKeepsCallbackRooted()
    {
        var removes = 0;
        using var g = new WinGSuppressionGuard((_, _, _, _, _) => new(1), (_, _, _, _) => IntPtr.Zero,
            _ => { removes++; return false; });
        g.Start();
        Assert.True(g.EnsureArmed());
        g.Dispose();
        var callback = typeof(WinGSuppressionGuard).GetField("_callback", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(g);
        Assert.False(g.IsHookInstalled);
        Assert.False(g.IsArmed);
        Assert.NotNull(callback);
        Assert.Equal(1, removes);
    }
    [Fact] public void FailedInstallationCannotArm() { using var g = new WinGSuppressionGuard((_, _, _, _, _) => IntPtr.Zero); g.Start(); Assert.False(g.EnsureArmed()); Assert.False(g.IsArmed); }
    [Fact] public void EnsureArmedResynchronizesWinHeldBeforeHookObservation() { using var g = Create(getAsync: vk => vk == 0x5B ? unchecked((short)0x8000) : (short)0); Assert.True(g.EnsureArmed()); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, true)); }
    [Fact] public void InputMatchesNativeWin32Layout() => Assert.Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf<WinGSuppressionGuard.Input>());
    [Fact] public void CleanupExceptionStillSuppressesG() { using var g = Create(send: (_, _) => throw new InvalidOperationException()); g.EnsureArmed(); g.ProcessKey(0x5B, true); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, true)); }
    [Fact] public void FailedDummyFallbackIsObservedWithoutReleasingPhysicalWin() { var calls = 0; using var g = Create(send: (_, _) => ++calls == 1 ? 1u : 0u); g.EnsureArmed(); g.ProcessKey(0x5B, true); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, true)); Assert.Equal(2, calls); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x5B, false)); }
    [Fact] public void SuccessfulCleanupDoesNotPoisonLaterPlainG() { using var g = Create(); g.EnsureArmed(); g.ProcessKey(0x5B, true); g.ProcessKey(0x47, true); g.ProcessKey(0x47, false); g.ProcessKey(0x5B, false); Assert.Equal(IntPtr.Zero, g.ProcessKey(0x47, true)); }
    [Fact] public async Task Suppressed_G_auto_repeat_is_consumed_once_without_per_event_debug_logging()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            AppLog.DirectoryOverride = directory;
            AppLog.MinimumLevelOverride = AppLogLevel.Debug;
            using var g = Create();
            g.EnsureArmed();
            g.ProcessKey(0x5B, true);
            Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, true));
            for (var i = 0; i < 20; i++) Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, true));
            Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, false));
            Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x5B, false));
            Assert.Equal(IntPtr.Zero, g.ProcessKey(0x47, true));
            await Task.Run(() => AppLog.DrainForTests());
            var log = AppLog.ReadAllTextForTests(Path.Combine(directory, AppLog.CurrentLogFileName));
            Assert.Equal(1, CountOccurrences(log, "Win+G chord suppressed."));
            Assert.DoesNotContain("Relevant keyboard hook event.", log, StringComparison.Ordinal);
        }
        finally
        {
            AppLog.MinimumLevelOverride = AppLogLevel.Off;
            AppLog.DrainForTests();
            AppLog.DirectoryOverride = null;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Late_hook_install_after_startup_timeout_is_removed_and_never_published()
    {
        using var installEntered = new ManualResetEventSlim();
        using var allowInstallToReturn = new ManualResetEventSlim();
        var removes = 0;
        using var g = new WinGSuppressionGuard(
            (_, _, _, _, _) =>
            {
                installEntered.Set();
                Assert.True(allowInstallToReturn.Wait(TimeSpan.FromSeconds(5)));
                return new(1);
            },
            (_, _, _, _) => IntPtr.Zero,
            _ => { Interlocked.Increment(ref removes); return true; },
            hookStartupTimeoutMilliseconds: 100);

        var start = Task.Run(g.Start);
        Assert.True(installEntered.Wait(TimeSpan.FromSeconds(2)));
        await start.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(g.IsHookInstalled);
        Assert.False(g.EnsureArmed());

        allowInstallToReturn.Set();
        g.Dispose();
        Assert.False(g.IsHookInstalled);
        Assert.Equal(1, removes);
    }

    [Fact]
    public void Unexpected_hook_pump_exit_removes_hook_and_clears_readiness()
    {
        uint hookThreadId = 0;
        var removes = 0;
        using var g = new WinGSuppressionGuard(
            (_, _, _, _, _) => { hookThreadId = GetCurrentThreadId(); return new(1); },
            (_, _, _, _) => IntPtr.Zero,
            _ => { Interlocked.Increment(ref removes); return true; });
        g.Start();
        Assert.True(g.EnsureArmed());

        Assert.True(PostThreadMessage(hookThreadId, 0x0012, UIntPtr.Zero, IntPtr.Zero));
        Assert.True(SpinWait.SpinUntil(() => !g.IsHookInstalled, TimeSpan.FromSeconds(2)));
        Assert.False(g.IsArmed);
        Assert.Equal(1, removes);
    }

    [Theory]
    [InlineData(0x5B)]
    [InlineData(0x5C)]
    public void WinTab_long_press_events_pass_through_without_cleanup(int winKey)
    {
        var forwarded = new List<IntPtr>();
        var cleanup = new List<WinGSuppressionGuard.Input[]>();
        using var g = Create(cleanup, next: (_, _, wParam, _) => { forwarded.Add(wParam); return IntPtr.Zero; });
        g.EnsureArmed();

        Assert.Equal(IntPtr.Zero, Invoke(g, winKey, true));
        Assert.Equal(IntPtr.Zero, Invoke(g, 0x09, true));
        Assert.Equal(IntPtr.Zero, Invoke(g, 0x09, false));
        Assert.Equal(IntPtr.Zero, Invoke(g, winKey, false));

        Assert.Equal(new long[] { 0x100, 0x100, 0x101, 0x101 }, forwarded.Select(value => value.ToInt64()));
        Assert.Empty(cleanup);
    }

    [Fact]
    public void Independent_WinTab_and_WinR_work_after_suppressed_WinG()
    {
        var forwarded = new List<IntPtr>();
        using var g = Create(next: (_, _, wParam, _) => { forwarded.Add(wParam); return IntPtr.Zero; });
        g.EnsureArmed();

        Assert.Equal(IntPtr.Zero, Invoke(g, 0x5B, true));
        Assert.NotEqual(IntPtr.Zero, Invoke(g, 0x47, true));
        Assert.NotEqual(IntPtr.Zero, Invoke(g, 0x47, false));
        Assert.NotEqual(IntPtr.Zero, Invoke(g, 0x5B, false));

        Assert.Equal(IntPtr.Zero, Invoke(g, 0x5B, true));
        Assert.Equal(IntPtr.Zero, Invoke(g, 0x09, true));
        Assert.Equal(IntPtr.Zero, Invoke(g, 0x09, false));
        Assert.Equal(IntPtr.Zero, Invoke(g, 0x5B, false));
        Assert.Equal(IntPtr.Zero, Invoke(g, 0x5B, true));
        Assert.Equal(IntPtr.Zero, Invoke(g, 0x52, true));
        Assert.Equal(IntPtr.Zero, Invoke(g, 0x5B, false));

        Assert.Equal(new long[] { 0x100, 0x100, 0x100, 0x101, 0x101, 0x100, 0x100, 0x101 }, forwarded.Select(value => value.ToInt64()));
    }

    private static void AssertChord(int win)
    {
        using var g = Create(); g.EnsureArmed(); g.ProcessKey(win, true); Assert.NotEqual(IntPtr.Zero, g.ProcessKey(0x47, true));
    }

    private static IntPtr Invoke(WinGSuppressionGuard guard, int vk, bool keyDown, uint flags = 0, long extraInfo = 0)
    {
        var data = Marshal.AllocHGlobal(Marshal.SizeOf<WinGSuppressionGuard.KeyboardData>());
        try
        {
            Marshal.StructureToPtr(new WinGSuppressionGuard.KeyboardData { VkCode = (uint)vk, Flags = flags, ExtraInfo = (nint)extraInfo }, data, false);
            return guard.InvokeHookForTest(new(keyDown ? 0x100 : 0x101), data);
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    private static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0; index += fragment.Length) count++;
        return count;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

    private static WinGSuppressionGuard Create(List<WinGSuppressionGuard.Input[]>? calls = null, Func<WinGSuppressionGuard.Input[], uint, uint>? send = null, Func<int, short>? getAsync = null, Func<IntPtr, int, IntPtr, IntPtr, IntPtr>? next = null)
    {
        var guard = new WinGSuppressionGuard((_, _, _, _, _) => new(1), next, _ => true, (inputs, count) => { calls?.Add(inputs); return send?.Invoke(inputs, count) ?? count; }, getAsync ?? (_ => 0));
        guard.Start();
        return guard;
    }

    private const long OwnMarker = unchecked((long)0x5349475F57494E47);
}
