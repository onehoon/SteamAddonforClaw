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

    private static CenterMMainUiRoutingGuard Create(
        FakeSnapshotSource snapshots, FakeStager stager, RecordingHelperApi helperApi, FakeMutexFactory mutexFactory) =>
        new(
            publishRootProvider: () => "C:\\fake\\publish",
            processSnapshotSource: snapshots,
            helperOwnership: new CenterMHelperOwnership(helperApi),
            mutexOwnership: new CenterMMainUiMutexOwnership(mutexFactory),
            stager: stager.Stage);

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

        public bool TryCreateSuspended(string imagePath, out int processId, out SafeProcessHandle? processHandle, out SafeHandle? threadHandle, out int win32Error)
        {
            Calls.Add("CreateSuspended");
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
            return true;
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
