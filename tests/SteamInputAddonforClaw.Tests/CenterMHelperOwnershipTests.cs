using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CenterMHelperOwnershipTests
{
    [Fact]
    public void SuccessfulStart_CallsStepsInMandatoryOrder()
    {
        var api = new RecordingApi();
        var ownership = new CenterMHelperOwnership(api);

        var result = ownership.Start(@"C:\fake\MSI Center M.exe");

        Assert.Equal(HelperStartResult.Started, result);
        Assert.Equal(
            ["CreateSuspended", "CreateJobObject", "SetKillOnJobClose", "AssignProcessToJob", "ResumeThread"],
            api.Calls);
        Assert.True(ownership.IsOwned);
        Assert.Equal(api.LastProcessId, ownership.ProcessId);
    }

    [Fact]
    public void CreateProcessFailure_NoFurtherStepsCalled()
    {
        var api = new RecordingApi { CreateSucceeds = false };
        var ownership = new CenterMHelperOwnership(api);

        var result = ownership.Start(@"C:\fake\MSI Center M.exe");

        Assert.Equal(HelperStartResult.CreateProcessFailed, result);
        Assert.Equal(["CreateSuspended"], api.Calls);
        Assert.False(ownership.IsOwned);
    }

    [Fact]
    public void CreateJobObjectFailure_TerminatesSuspendedHelper_NoLeak()
    {
        var api = new RecordingApi { CreateJobSucceeds = false };
        var ownership = new CenterMHelperOwnership(api);

        var result = ownership.Start(@"C:\fake\MSI Center M.exe");

        Assert.Equal(HelperStartResult.JobObjectFailed, result);
        Assert.Equal(["CreateSuspended", "CreateJobObject", "Terminate", "WaitForExit"], api.Calls);
        Assert.False(ownership.IsOwned);
    }

    [Fact]
    public void SetKillOnJobCloseFailure_TerminatesSuspendedHelper_NoLeak()
    {
        var api = new RecordingApi { SetLimitSucceeds = false };
        var ownership = new CenterMHelperOwnership(api);

        var result = ownership.Start(@"C:\fake\MSI Center M.exe");

        Assert.Equal(HelperStartResult.JobLimitFailed, result);
        Assert.Equal(["CreateSuspended", "CreateJobObject", "SetKillOnJobClose", "Terminate", "WaitForExit"], api.Calls);
        Assert.False(ownership.IsOwned);
    }

    [Fact]
    public void AssignFailure_TerminatesSuspendedHelper_NoLeak()
    {
        var api = new RecordingApi { AssignSucceeds = false };
        var ownership = new CenterMHelperOwnership(api);

        var result = ownership.Start(@"C:\fake\MSI Center M.exe");

        Assert.Equal(HelperStartResult.AssignFailed, result);
        Assert.Equal(["CreateSuspended", "CreateJobObject", "SetKillOnJobClose", "AssignProcessToJob", "Terminate", "WaitForExit"], api.Calls);
        Assert.False(ownership.IsOwned);
    }

    [Fact]
    public void ResumeFailure_NeverTerminatesDirectly_ReliesOnJobCloseToKill()
    {
        // After a successful Assign, the helper must never be resumed unassigned nor terminated
        // by any other mechanism -- closing the Job (which KILL_ON_JOB_CLOSE was already set on)
        // is the only cleanup for this specific failure.
        var api = new RecordingApi { ResumeSucceeds = false };
        var ownership = new CenterMHelperOwnership(api);

        var result = ownership.Start(@"C:\fake\MSI Center M.exe");

        Assert.Equal(HelperStartResult.ResumeFailed, result);
        Assert.Equal(["CreateSuspended", "CreateJobObject", "SetKillOnJobClose", "AssignProcessToJob", "ResumeThread", "WaitForExit"], api.Calls);
        Assert.DoesNotContain("Terminate", api.Calls);
        Assert.False(ownership.IsOwned);
    }

    [Theory]
    [InlineData(false, true)] // TerminateProcess itself fails
    [InlineData(true, false)] // Terminate succeeds, exit wait times out
    public void JobObjectFailure_CleanupUnconfirmed_RetainsOwnership_NoNameOrPidRediscoveryNeeded(bool terminateSucceeds, bool waitForExitSucceeds)
    {
        var api = new RecordingApi { CreateJobSucceeds = false, TerminateSucceeds = terminateSucceeds, WaitForExitSucceeds = waitForExitSucceeds };
        var ownership = new CenterMHelperOwnership(api);

        var result = ownership.Start(@"C:\fake\MSI Center M.exe");

        Assert.Equal(HelperStartResult.PartialCleanupUnconfirmed, result);
        Assert.True(ownership.IsOwned);
        Assert.Equal(api.LastProcessId, ownership.ProcessId);

        // The retained handle from construction can still resolve this later, through the exact
        // same Stop() path as a normal owned helper -- never process-name or PID rediscovery.
        api.TerminateSucceeds = true;
        api.WaitForExitSucceeds = true;
        var stopped = ownership.Stop(TimeSpan.FromSeconds(1));
        Assert.True(stopped);
        Assert.False(ownership.IsOwned);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void JobLimitFailure_CleanupUnconfirmed_RetainsOwnership(bool terminateSucceeds, bool waitForExitSucceeds)
    {
        var api = new RecordingApi { SetLimitSucceeds = false, TerminateSucceeds = terminateSucceeds, WaitForExitSucceeds = waitForExitSucceeds };
        var ownership = new CenterMHelperOwnership(api);

        var result = ownership.Start(@"C:\fake\MSI Center M.exe");

        Assert.Equal(HelperStartResult.PartialCleanupUnconfirmed, result);
        Assert.True(ownership.IsOwned);
        api.TerminateSucceeds = true;
        api.WaitForExitSucceeds = true;
        Assert.True(ownership.Stop(TimeSpan.FromSeconds(1)));
        Assert.False(ownership.IsOwned);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AssignFailure_CleanupUnconfirmed_RetainsOwnership(bool terminateSucceeds, bool waitForExitSucceeds)
    {
        var api = new RecordingApi { AssignSucceeds = false, TerminateSucceeds = terminateSucceeds, WaitForExitSucceeds = waitForExitSucceeds };
        var ownership = new CenterMHelperOwnership(api);

        var result = ownership.Start(@"C:\fake\MSI Center M.exe");

        Assert.Equal(HelperStartResult.PartialCleanupUnconfirmed, result);
        Assert.True(ownership.IsOwned);
        api.TerminateSucceeds = true;
        api.WaitForExitSucceeds = true;
        Assert.True(ownership.Stop(TimeSpan.FromSeconds(1)));
        Assert.False(ownership.IsOwned);
    }

    [Fact]
    public void ResumeFailure_ExitWaitTimesOut_RetainsOwnership_NotReportedAsCleanNativeFallback()
    {
        var api = new RecordingApi { ResumeSucceeds = false, WaitForExitSucceeds = false };
        var ownership = new CenterMHelperOwnership(api);

        var result = ownership.Start(@"C:\fake\MSI Center M.exe");

        Assert.Equal(HelperStartResult.PartialCleanupUnconfirmed, result);
        Assert.NotEqual(HelperStartResult.ResumeFailed, result); // must not imply clean resolution
        Assert.True(ownership.IsOwned);
        Assert.Equal(api.LastProcessId, ownership.ProcessId);

        // Repeated Stop()/Dispose() after the partial-cleanup failure must still resolve ownership
        // through the retained handle.
        api.WaitForExitSucceeds = true;
        var stopped = ownership.Stop(TimeSpan.FromSeconds(1));
        var stoppedAgain = ownership.Stop(TimeSpan.FromSeconds(1));
        Assert.True(stopped);
        Assert.True(stoppedAgain);
        Assert.False(ownership.IsOwned);
    }

    [Fact]
    public void SecondStart_WhileAlreadyOwned_IsRefused_NoNativeCallsAndFirstOwnershipRetained()
    {
        var api = new RecordingApi();
        var ownership = new CenterMHelperOwnership(api);
        var firstResult = ownership.Start(@"C:\fake\MSI Center M.exe");
        var firstProcessId = ownership.ProcessId;
        var callsAfterFirstStart = api.Calls.Count;

        var secondResult = ownership.Start(@"C:\fake\MSI Center M.exe");

        Assert.Equal(HelperStartResult.Started, firstResult);
        Assert.Equal(HelperStartResult.AlreadyOwned, secondResult);
        Assert.Equal(callsAfterFirstStart, api.Calls.Count); // no further native calls at all
        Assert.Equal(firstProcessId, ownership.ProcessId);
        Assert.True(ownership.IsOwned);
    }

    [Fact]
    public async Task ConcurrentStart_OnlyOneCreateSuspendedSequenceRuns_LoserGetsAlreadyOwned()
    {
        var blocker = new ManualResetEventSlim(false);
        var api = new RecordingApi { CreateSuspendedBlocker = blocker };
        var ownership = new CenterMHelperOwnership(api);

        var firstTask = Task.Run(() => ownership.Start(@"C:\fake\MSI Center M.exe"));
        // Wait until the first Start() is genuinely inside TryCreateSuspended (holding the lock),
        // not just scheduled.
        Assert.True(api.CreateSuspendedEntered.Wait(TimeSpan.FromSeconds(5)));

        // The second Start() must now block on the same lock -- give it a moment to actually
        // attempt that (a Task that "hasn't run yet" would make this test meaningless).
        var secondTask = Task.Run(() => ownership.Start(@"C:\fake\MSI Center M.exe"));
        await Task.Delay(50);

        blocker.Set();

        var firstResult = await firstTask.WaitAsync(TimeSpan.FromSeconds(5));
        var secondResult = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HelperStartResult.Started, firstResult);
        Assert.Equal(HelperStartResult.AlreadyOwned, secondResult);
        Assert.Equal(1, api.CreateSuspendedCallCount);
        Assert.True(ownership.IsOwned);
    }

    [Fact]
    public void Stop_TerminatesViaRetainedHandle_Idempotent()
    {
        var api = new RecordingApi();
        var ownership = new CenterMHelperOwnership(api);
        ownership.Start(@"C:\fake\MSI Center M.exe");

        var stopped = ownership.Stop(TimeSpan.FromSeconds(1));
        var stoppedAgain = ownership.Stop(TimeSpan.FromSeconds(1));

        Assert.True(stopped);
        Assert.True(stoppedAgain); // no-op, still reports success
        Assert.False(ownership.IsOwned);
        Assert.Equal(1, api.Calls.Count(c => c == "Terminate"));
    }

    private sealed class RecordingApi : IHelperProcessNativeApi
    {
        internal List<string> Calls { get; } = [];
        internal int LastProcessId { get; private set; } = 12345;

        internal bool CreateSucceeds { get; init; } = true;
        internal bool CreateJobSucceeds { get; init; } = true;
        internal bool SetLimitSucceeds { get; init; } = true;
        internal bool AssignSucceeds { get; init; } = true;
        internal bool ResumeSucceeds { get; init; } = true;
        internal bool TerminateSucceeds { get; set; } = true;
        internal bool WaitForExitSucceeds { get; set; } = true;

        /// <summary>When set, TryCreateSuspended signals <see cref="CreateSuspendedEntered"/> and
        /// blocks until this is released -- lets a test prove a concurrent Start() call is
        /// genuinely serialized behind the lock, not merely observed to "happen to" run second.</summary>
        internal ManualResetEventSlim? CreateSuspendedBlocker { get; init; }
        internal ManualResetEventSlim CreateSuspendedEntered { get; } = new(false);
        internal int CreateSuspendedCallCount { get; private set; }

        public bool TryCreateSuspended(string imagePath, out int processId, out SafeProcessHandle? processHandle, out SafeHandle? threadHandle, out int win32Error)
        {
            Calls.Add("CreateSuspended");
            CreateSuspendedCallCount++;
            CreateSuspendedEntered.Set();
            CreateSuspendedBlocker?.Wait(TimeSpan.FromSeconds(10));
            win32Error = 0;
            if (!CreateSucceeds)
            {
                processId = 0;
                processHandle = null;
                threadHandle = null;
                return false;
            }
            processId = LastProcessId;
            processHandle = new SafeProcessHandle(GetCurrentProcessHandle(), false);
            threadHandle = new SafeGenericHandle(GetCurrentProcessHandle());
            return true;
        }

        public bool TryCreateJobObject(out SafeHandle? jobHandle, out int win32Error)
        {
            Calls.Add("CreateJobObject");
            win32Error = 0;
            if (!CreateJobSucceeds) { jobHandle = null; return false; }
            jobHandle = new SafeGenericHandle(GetCurrentProcessHandle());
            return true;
        }

        public bool TrySetKillOnJobClose(SafeHandle jobHandle, out int win32Error)
        {
            Calls.Add("SetKillOnJobClose");
            win32Error = 0;
            return SetLimitSucceeds;
        }

        public bool TryAssignProcessToJob(SafeHandle jobHandle, SafeProcessHandle processHandle, out int win32Error)
        {
            Calls.Add("AssignProcessToJob");
            win32Error = 0;
            return AssignSucceeds;
        }

        public bool TryResumeThread(SafeHandle threadHandle, out int win32Error)
        {
            Calls.Add("ResumeThread");
            win32Error = 0;
            return ResumeSucceeds;
        }

        public bool TryTerminate(SafeProcessHandle processHandle, out int win32Error)
        {
            Calls.Add("Terminate");
            win32Error = TerminateSucceeds ? 0 : 5;
            return TerminateSucceeds;
        }

        public bool WaitForExit(SafeProcessHandle processHandle, TimeSpan timeout)
        {
            Calls.Add("WaitForExit");
            return WaitForExitSucceeds;
        }

        private static IntPtr GetCurrentProcessHandle() => System.Diagnostics.Process.GetCurrentProcess().Handle;
    }
}
