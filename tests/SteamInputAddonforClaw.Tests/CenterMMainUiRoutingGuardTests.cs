using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CenterMMainUiRoutingGuardTests
{
    [Fact]
    public async Task Real_main_ui_already_present_prevents_arm_without_starting_anything()
    {
        var snapshots = new FakeSnapshotSource([[new ProcessSnapshotEntry(999, "MSI Center M", null)]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.RealMainUiPresent, result);
        Assert.False(guard.IsArmed);
        Assert.False(stager.Called);
        Assert.Empty(helperApi.Calls);
        Assert.Equal(0, mutexFactory.CreateCallCount);
    }

    [Fact]
    public async Task Normal_arm_starts_helper_acquires_mutex_and_becomes_armed()
    {
        var snapshots = new FakeSnapshotSource([[], [new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null)]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, result);
        Assert.True(guard.IsArmed);
        Assert.Equal(1, mutexFactory.CreateCallCount);
        Assert.Contains("ResumeThread", helperApi.Calls);
    }

    [Fact]
    public async Task Repeated_arm_while_already_armed_is_idempotent()
    {
        var snapshots = new FakeSnapshotSource([[], [new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null)]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);
        await guard.ArmAsync();

        var second = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, second);
        Assert.Equal(1, mutexFactory.CreateCallCount);
        Assert.Equal(1, helperApi.Calls.Count(call => call == "CreateSuspended"));
    }

    [Fact]
    public async Task Helper_staging_failure_prevents_mutex_acquisition()
    {
        var snapshots = new FakeSnapshotSource([[]]);
        var stager = new FakeStager(null);
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.HelperFailure, result);
        Assert.False(guard.IsArmed);
        Assert.Empty(helperApi.Calls);
        Assert.Equal(0, mutexFactory.CreateCallCount);
    }

    [Fact]
    public async Task Helper_start_failure_prevents_mutex_acquisition()
    {
        var snapshots = new FakeSnapshotSource([[]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi { CreateSucceeds = false };
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.HelperFailure, result);
        Assert.False(guard.IsArmed);
        Assert.Equal(0, mutexFactory.CreateCallCount);
    }

    [Fact]
    public async Task Mutex_failure_after_helper_start_stops_the_helper_and_never_arms()
    {
        var snapshots = new FakeSnapshotSource([[]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory { NextCreatedNew = false };
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.MutexFailure, result);
        Assert.False(guard.IsArmed);
        Assert.Contains("Terminate", helperApi.Calls);
    }

    [Fact]
    public async Task Invariant_failure_after_helper_and_mutex_releases_both_and_never_arms()
    {
        // The owned helper vanished from the fresh post-mutex snapshot (HelperMissing) -- distinct
        // from a foreign process appearing, which is covered separately below.
        var snapshots = new FakeSnapshotSource([[], []]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.InvariantFailure, result);
        Assert.False(guard.IsArmed);
        Assert.Contains("Terminate", helperApi.Calls);
        Assert.True(mutexFactory.LastHandle!.Disposed);
    }

    [Fact]
    public async Task Foreign_main_ui_appearing_during_arm_aborts_without_terminating_it()
    {
        var snapshots = new FakeSnapshotSource(
        [
            [],
            [
                new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null),
                new ProcessSnapshotEntry(555, "MSI Center M", null)
            ]
        ]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.RealMainUiPresent, result);
        Assert.False(guard.IsArmed);
        // Phase 1 policy: never terminate a foreign real MainUI -- only this attempt's OWN
        // resources unwind (the owned helper, terminated via its retained handle -- not the
        // foreign process, which this fake never even models a termination call for).
        Assert.Contains("Terminate", helperApi.Calls);
        Assert.True(mutexFactory.LastHandle!.Disposed);
    }

    [Fact]
    public async Task Uncertain_enumeration_before_start_fails_closed_without_starting_anything()
    {
        var snapshots = new FakeSnapshotSource(uncertainFirst: true);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.Uncertain, result);
        Assert.False(guard.IsArmed);
        Assert.Empty(helperApi.Calls);
    }

    [Fact]
    public async Task Disarm_after_arm_stops_the_helper_and_releases_the_mutex()
    {
        var snapshots = new FakeSnapshotSource([[], [new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null)]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);
        await guard.ArmAsync();

        var disarmed = await guard.DisarmAsync();

        Assert.True(disarmed);
        Assert.False(guard.IsArmed);
        Assert.Contains("Terminate", helperApi.Calls);
        Assert.True(mutexFactory.LastHandle!.Disposed);
    }

    [Fact]
    public async Task Disarm_without_ever_arming_is_a_safe_no_op()
    {
        var guard = Create(new FakeSnapshotSource([[]]), new FakeStager(null), new RecordingHelperApi(), new FakeMutexFactory());

        var disarmed = await guard.DisarmAsync();

        Assert.True(disarmed);
        Assert.False(guard.IsArmed);
    }

    [Fact]
    public async Task DisposeAsync_hands_unresolved_exact_helper_ownership_to_orphan_registry()
    {
        CenterMOrphanedHelperRegistry.TestOnly_Clear();
        try
        {
            var snapshots = new FakeSnapshotSource([[], [new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null)]]);
            var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
            // Every WaitForExit remains unconfirmed, so Stop() can never resolve -- the exact
            // handle must stay retained (never discarded) and end up in the registry.
            var helperApi = new RecordingHelperApi { WaitForExitSucceeds = false };
            var helperOwnership = new CenterMHelperOwnership(helperApi);
            var guard = new CenterMMainUiRoutingGuard(
                publishRootProvider: () => "C:\\fake\\publish",
                processSnapshotSource: snapshots,
                helperOwnership: helperOwnership,
                mutexOwnership: new CenterMMainUiMutexOwnership(new FakeMutexFactory()),
                stager: stager.Stage,
                helperStopTimeout: TimeSpan.Zero);
            Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, await guard.ArmAsync());

            await guard.DisposeAsync();

            Assert.True(helperOwnership.IsOwned);
            Assert.True(CenterMOrphanedHelperRegistry.Contains(helperOwnership));
        }
        finally
        {
            CenterMOrphanedHelperRegistry.TestOnly_Clear();
        }
    }

    [Fact]
    public async Task DisposeAsync_does_not_register_a_helper_whose_termination_is_confirmed()
    {
        CenterMOrphanedHelperRegistry.TestOnly_Clear();
        try
        {
            var snapshots = new FakeSnapshotSource([[], [new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null)]]);
            var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
            var helperApi = new RecordingHelperApi();
            var helperOwnership = new CenterMHelperOwnership(helperApi);
            var guard = new CenterMMainUiRoutingGuard(
                publishRootProvider: () => "C:\\fake\\publish",
                processSnapshotSource: snapshots,
                helperOwnership: helperOwnership,
                mutexOwnership: new CenterMMainUiMutexOwnership(new FakeMutexFactory()),
                stager: stager.Stage);
            Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, await guard.ArmAsync());

            await guard.DisposeAsync();

            Assert.False(helperOwnership.IsOwned);
            Assert.False(CenterMOrphanedHelperRegistry.Contains(helperOwnership));
            Assert.Equal(0, CenterMOrphanedHelperRegistry.RegisteredCount);
        }
        finally
        {
            CenterMOrphanedHelperRegistry.TestOnly_Clear();
        }
    }

    [Fact]
    public async Task Concurrent_arm_calls_serialize_and_produce_exactly_one_helper_and_mutex()
    {
        var snapshots = new FakeSnapshotSource([[], [new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null)]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var blocker = new ManualResetEventSlim(false);
        var helperApi = new RecordingHelperApi { CreateSuspendedBlocker = blocker };
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var armA = Task.Run(() => guard.ArmAsync());
        Assert.True(helperApi.CreateSuspendedEntered.Wait(TimeSpan.FromSeconds(5)));

        var armB = Task.Run(() => guard.ArmAsync());
        // B must be genuinely blocked behind A's still-in-flight transaction, not merely
        // happening to run after it.
        await Task.Delay(50);
        Assert.False(armB.IsCompleted);

        blocker.Set();
        var resultA = await armA;
        var resultB = await armB;

        Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, resultA);
        Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, resultB);
        Assert.True(guard.IsArmed);
        Assert.Equal(1, helperApi.Calls.Count(call => call == "CreateSuspended"));
        Assert.Equal(1, mutexFactory.CreateCallCount);
    }

    [Fact]
    public async Task Overlapping_disarm_never_races_an_in_flight_arm()
    {
        var snapshots = new FakeSnapshotSource([[], [new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null)]]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var blocker = new ManualResetEventSlim(false);
        var helperApi = new RecordingHelperApi { CreateSuspendedBlocker = blocker };
        var mutexFactory = new FakeMutexFactory();
        var guard = Create(snapshots, stager, helperApi, mutexFactory);

        var arming = Task.Run(() => guard.ArmAsync());
        Assert.True(helperApi.CreateSuspendedEntered.Wait(TimeSpan.FromSeconds(5)));

        var disarming = Task.Run(() => guard.DisarmAsync());
        await Task.Delay(50);
        Assert.False(disarming.IsCompleted);

        blocker.Set();
        var armResult = await arming;
        var disarmResult = await disarming;

        // Disarm was queued behind the already-in-flight Arm transaction, so it can only ever run
        // to completion after Arm has already published (and then immediately retracted) Armed --
        // never interleaved with it.
        Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, armResult);
        Assert.True(disarmResult);
        Assert.False(guard.IsArmed);
        Assert.Contains("Terminate", helperApi.Calls);
    }

    [Fact]
    public async Task Existing_tray_resident_mainui_is_retired_before_helper_and_mutex_still_arm()
    {
        const int existingMainUiPid = 999;
        const string expectedPath = @"C:\Program Files\WindowsApps\9426MICRO-STARINTERNATION.64797CC12EF8E_3.0.60630.0_x64__kzh8wxbdkxb8p\MSI Center M\MSI Center M.exe";

        // Shared by the guard's own beforeSnapshot/afterSnapshot checks AND retirement's internal
        // discovery/recheck/absence calls -- exactly like production, where they are the same
        // IProcessSnapshotSource instance.
        var snapshots = new FakeSnapshotSource(
        [
            [new ProcessSnapshotEntry(existingMainUiPid, "MSI Center M", expectedPath)], // guard beforeSnapshot
            [new ProcessSnapshotEntry(existingMainUiPid, "MSI Center M", expectedPath)], // retirement discovery
            [new ProcessSnapshotEntry(existingMainUiPid, "MSI Center M", expectedPath)], // terminator fresh recheck
            [], // fresh absence after termination
            [new ProcessSnapshotEntry(RecordingHelperApi.FixedProcessId, "MSI Center M", null)] // guard afterSnapshot (owned helper)
        ]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory();

        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, existingMainUiPid, "MSI Center M", expectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 0)]);
        var terminateInvoker = new RecordingTerminateInvoker();
        var terminator = new CenterMMainUiRoutingTerminator(terminateInvoker, identity, window, snapshots);
        var retirement = new CenterMMainUiRoutingRetirement(
            new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput),
            processSnapshotSource: snapshots,
            handleOpener: new RealSelfProcessHandleOpener(),
            identityInspector: identity,
            windowSnapshotProvider: window,
            terminator: terminator,
            minimizeWaitTimeout: TimeSpan.FromMilliseconds(200),
            xInputWaitTimeout: TimeSpan.FromMilliseconds(200),
            terminateWaitTimeout: TimeSpan.FromMilliseconds(50));

        var guard = Create(snapshots, stager, helperApi, mutexFactory, retirement);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.Armed, result);
        Assert.Equal(1, terminateInvoker.TerminateCallCount);
        Assert.Equal(1, mutexFactory.CreateCallCount);
    }

    [Fact]
    public async Task Retirement_failure_prevents_arm_and_never_starts_the_helper()
    {
        const int existingMainUiPid = 999;
        var snapshots = new FakeSnapshotSource(
        [
            [new ProcessSnapshotEntry(existingMainUiPid, "MSI Center M", null)], // guard beforeSnapshot
            [new ProcessSnapshotEntry(existingMainUiPid, "MSI Center M", null)] // retirement discovery
        ]);
        var stager = new FakeStager("C:\\fake\\MSI Center M.exe");
        var helperApi = new RecordingHelperApi();
        var mutexFactory = new FakeMutexFactory();

        // Package path is unrecognized -> identity mismatch -> retirement refuses to touch it.
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, existingMainUiPid, "MSI Center M", @"C:\evil\MSI Center M.exe")]);
        var retirement = new CenterMMainUiRoutingRetirement(
            new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput),
            processSnapshotSource: snapshots,
            handleOpener: new RealSelfProcessHandleOpener(),
            identityInspector: identity);

        var guard = Create(snapshots, stager, helperApi, mutexFactory, retirement);

        var result = await guard.ArmAsync();

        Assert.Equal(CenterMMainUiRoutingGuardResult.RealMainUiPresent, result);
        Assert.False(guard.IsArmed);
        Assert.False(stager.Called);
        Assert.Empty(helperApi.Calls);
    }

    private static CenterMMainUiRoutingGuard Create(
        FakeSnapshotSource snapshots, FakeStager stager, RecordingHelperApi helperApi, FakeMutexFactory mutexFactory,
        CenterMMainUiRoutingRetirement? retirement = null) =>
        new(
            publishRootProvider: () => "C:\\fake\\publish",
            processSnapshotSource: snapshots,
            helperOwnership: new CenterMHelperOwnership(helperApi),
            mutexOwnership: new CenterMMainUiMutexOwnership(mutexFactory),
            stager: stager.Stage,
            retirement: retirement);

    private sealed class QueueIdentityInspector(IEnumerable<LiveProcessIdentity> responses) : IProcessIdentityInspector
    {
        private readonly Queue<LiveProcessIdentity> _queue = new(responses);
        private LiveProcessIdentity _last;

        public LiveProcessIdentity Inspect(SafeProcessHandle handle)
        {
            if (_queue.Count > 0) _last = _queue.Dequeue();
            return _last;
        }
    }

    private sealed class QueueWindowSnapshotProvider(IEnumerable<MainUiWindowSnapshot?> responses) : IMainUiWindowSnapshotProvider
    {
        private readonly Queue<MainUiWindowSnapshot?> _queue = new(responses);
        private MainUiWindowSnapshot? _last;

        public MainUiWindowSnapshot? Capture(int processId)
        {
            if (_queue.Count > 0) _last = _queue.Dequeue();
            return _last;
        }
    }

    private sealed class FixedNativeModeProbe(CenterMNativeModeProbeResult result) : ICenterMNativeModeProbe
    {
        public Task<CenterMNativeModeProbeResult> CaptureAsync(CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class RecordingTerminateInvoker : ITerminateProcessInvoker
    {
        internal int TerminateCallCount { get; private set; }
        public bool TryTerminate(SafeProcessHandle handle, out int win32Error) { TerminateCallCount++; win32Error = 0; return true; }
        public bool WaitForExit(SafeProcessHandle handle, TimeSpan timeout) => true;
    }

    /// <summary>Opens a REAL (non-pseudo) handle to this test process itself -- the
    /// <c>GetCurrentProcess()</c> pseudo-handle (-1) trips <see cref="SafeProcessHandle"/>'s own
    /// IsInvalid check when routed through the production <see cref="TrackedCenterMMainUi.Create"/>
    /// path.</summary>
    private sealed class RealSelfProcessHandleOpener : IProcessHandleOpener
    {
        public SafeProcessHandle Open(int processId, uint desiredAccess) => OpenProcess(desiredAccess, false, Environment.ProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);
    }

    private sealed class FakeSnapshotSource : IProcessSnapshotSource
    {
        private readonly Queue<IReadOnlyList<ProcessSnapshotEntry>?> _snapshots;
        private readonly bool _uncertainFirst;

        internal FakeSnapshotSource(IEnumerable<IReadOnlyList<ProcessSnapshotEntry>>? snapshots = null, bool uncertainFirst = false)
        {
            _snapshots = new Queue<IReadOnlyList<ProcessSnapshotEntry>?>(snapshots ?? []);
            _uncertainFirst = uncertainFirst;
        }

        public IReadOnlyList<ProcessSnapshotEntry>? GetProcessesByName(string processName)
        {
            if (_uncertainFirst) return null;
            return _snapshots.Count > 0 ? _snapshots.Dequeue() : [];
        }
    }

    private sealed class FakeStager(string? result)
    {
        internal bool Called { get; private set; }
        internal string? Stage(string publishRoot) { Called = true; return result; }
    }

    private sealed class FakeMutexFactory : ICenterMMainUiMutexFactory
    {
        internal bool NextCreatedNew { get; set; } = true;
        internal int CreateCallCount { get; private set; }
        internal FakeHandle? LastHandle { get; private set; }

        public (ICenterMMainUiMutexHandle Handle, bool CreatedNew) Create(string name)
        {
            CreateCallCount++;
            var handle = new FakeHandle();
            LastHandle = handle;
            return (handle, NextCreatedNew);
        }

        internal sealed class FakeHandle : ICenterMMainUiMutexHandle
        {
            internal bool Disposed { get; private set; }
            public void ReleaseMutex() { }
            public void Dispose() => Disposed = true;
        }
    }

    /// <summary>Minimal always-succeeds-unless-configured fake, mirroring CenterMHelperOwnershipTests'
    /// own RecordingApi but scoped to what these guard tests need.</summary>
    private sealed class RecordingHelperApi : IHelperProcessNativeApi
    {
        internal const int FixedProcessId = 4242;
        internal List<string> Calls { get; } = [];
        internal bool CreateSucceeds { get; init; } = true;
        internal bool WaitForExitSucceeds { get; init; } = true;

        /// <summary>When set, the first <see cref="TryCreateSuspended"/> call signals
        /// <see cref="CreateSuspendedEntered"/> and then blocks until this is released -- lets a
        /// test deterministically prove a competing Arm/Disarm call is genuinely serialized behind
        /// the guard's gate, not merely observed to "happen to" run second.</summary>
        internal ManualResetEventSlim? CreateSuspendedBlocker { get; init; }
        internal ManualResetEventSlim CreateSuspendedEntered { get; } = new(false);

        public bool TryCreateSuspended(string imagePath, out int processId, out SafeProcessHandle? processHandle, out SafeHandle? threadHandle, out int win32Error)
        {
            Calls.Add("CreateSuspended");
            CreateSuspendedEntered.Set();
            CreateSuspendedBlocker?.Wait(TimeSpan.FromSeconds(10));
            win32Error = 0;
            if (!CreateSucceeds) { processId = 0; processHandle = null; threadHandle = null; return false; }
            processId = FixedProcessId;
            processHandle = new SafeProcessHandle(GetCurrentProcessHandle(), false);
            threadHandle = new SafeFileHandle(GetCurrentProcessHandle(), false);
            return true;
        }

        public bool TryCreateJobObject(out SafeHandle? jobHandle, out int win32Error)
        {
            Calls.Add("CreateJobObject");
            win32Error = 0;
            jobHandle = new SafeFileHandle(GetCurrentProcessHandle(), false);
            return true;
        }

        public bool TrySetKillOnJobClose(SafeHandle jobHandle, out int win32Error)
        {
            Calls.Add("SetKillOnJobClose");
            win32Error = 0;
            return true;
        }

        public bool TryAssignProcessToJob(SafeHandle jobHandle, SafeProcessHandle processHandle, out int win32Error)
        {
            Calls.Add("AssignProcessToJob");
            win32Error = 0;
            return true;
        }

        public bool TryResumeThread(SafeHandle threadHandle, out int win32Error)
        {
            Calls.Add("ResumeThread");
            win32Error = 0;
            return true;
        }

        public bool TryTerminate(SafeProcessHandle processHandle, out int win32Error)
        {
            Calls.Add("Terminate");
            win32Error = 0;
            return true;
        }

        public bool WaitForExit(SafeProcessHandle processHandle, TimeSpan timeout)
        {
            Calls.Add("WaitForExit");
            return WaitForExitSucceeds;
        }

        public LiveProcessProbeStatus PollLiveness(SafeProcessHandle processHandle)
        {
            Calls.Add("PollLiveness");
            return LiveProcessProbeStatus.Alive;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
        private static IntPtr GetCurrentProcessHandle() => GetCurrentProcess();
    }
}
