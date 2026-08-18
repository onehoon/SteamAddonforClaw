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
    [InlineData(true, false)] // TerminateProcess succeeds, exit wait still times out
    [InlineData(false, false)] // TerminateProcess fails, exit wait times out (still genuinely running)
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
        api.WaitForExitSucceeds = true;
        var stopped = ownership.Stop(TimeSpan.FromSeconds(1));
        Assert.True(stopped);
        Assert.False(ownership.IsOwned);
    }

    [Fact]
    public void JobObjectFailure_TerminateFailsButRetainedHandleAlreadySignaled_IsClassifiedResolved_NotPartialCleanup()
    {
        // TerminateProcess legitimately fails (e.g. ERROR_ACCESS_DENIED) once a process has already
        // exited -- that failure alone must never be treated as ambiguous. The exact retained
        // handle being signaled (WaitForExit confirms it) is authoritative regardless of whether
        // this particular TerminateProcess call is what caused the exit.
        var api = new RecordingApi { CreateJobSucceeds = false, TerminateSucceeds = false, WaitForExitSucceeds = true };
        var ownership = new CenterMHelperOwnership(api);

        var result = ownership.Start(@"C:\fake\MSI Center M.exe");

        Assert.Equal(HelperStartResult.JobObjectFailed, result);
        Assert.NotEqual(HelperStartResult.PartialCleanupUnconfirmed, result);
        Assert.False(ownership.IsOwned);
    }

    [Fact]
    public void JobObjectFailure_SecondStopAttempt_ProcessNowExited_TerminateFailsButWaitConfirms_ResolvesAndClearsOwnership()
    {
        // Job-less PartialCleanupUnconfirmed identity, then the process exits naturally (or is
        // killed by an external actor) before the next Stop() call: TerminateProcess now fails
        // (access denied against an already-exited process) but the retained handle's own wait
        // confirms exit -- Stop() must resolve and clear ownership, not stay stuck retained
        // forever.
        var api = new RecordingApi { CreateJobSucceeds = false, WaitForExitSucceeds = false };
        var ownership = new CenterMHelperOwnership(api);
        Assert.Equal(HelperStartResult.PartialCleanupUnconfirmed, ownership.Start(@"C:\fake\MSI Center M.exe"));
        Assert.True(ownership.IsOwned);

        api.TerminateSucceeds = false;
        api.WaitForExitSucceeds = true;
        var stopped = ownership.Stop(TimeSpan.FromSeconds(1));

        Assert.True(stopped);
        Assert.False(ownership.IsOwned);
    }

    [Fact]
    public void JobObjectFailure_SecondStopAttempt_StillGenuinelyRunning_ReturnsFalse_OwnershipRemainsRetained()
    {
        var api = new RecordingApi { CreateJobSucceeds = false, WaitForExitSucceeds = false };
        var ownership = new CenterMHelperOwnership(api);
        var initial = ownership.Start(@"C:\fake\MSI Center M.exe");
        Assert.Equal(HelperStartResult.PartialCleanupUnconfirmed, initial);
        Assert.True(ownership.IsOwned);

        // Second attempt: still genuinely running (wait still times out) -- must not discard the
        // only retained handle.
        var stopped = ownership.Stop(TimeSpan.FromSeconds(1));

        Assert.False(stopped);
        Assert.True(ownership.IsOwned);

        api.WaitForExitSucceeds = true;
        Assert.True(ownership.Stop(TimeSpan.FromSeconds(1)));
        Assert.False(ownership.IsOwned);
    }

    [Fact]
    public void JobObjectFailure_DisposeAfterPartialCleanup_AttemptsRealTermination_NotJustHandleDrop()
    {
        // Regression: Dispose() must not simply close a job-less retained process handle (which
        // would NOT terminate the process) -- it must attempt a real TerminateProcess through that
        // handle first, exactly like Stop() does.
        var api = new RecordingApi { CreateJobSucceeds = false, WaitForExitSucceeds = false };
        var ownership = new CenterMHelperOwnership(api);
        var initial = ownership.Start(@"C:\fake\MSI Center M.exe");
        Assert.Equal(HelperStartResult.PartialCleanupUnconfirmed, initial);
        var callsBeforeDispose = api.Calls.Count;

        api.WaitForExitSucceeds = true;
        ownership.Dispose();

        Assert.False(ownership.IsOwned);
        Assert.Contains("Terminate", api.Calls.Skip(callsBeforeDispose));
        Assert.Contains("WaitForExit", api.Calls.Skip(callsBeforeDispose));
    }

    [Fact]
    public void JobObjectFailure_DisposeCannotConfirmTermination_OwnershipIntentionallyRetained()
    {
        // Regression: if Dispose()'s own termination attempt also cannot confirm exit for a
        // job-less retained handle, it must NOT silently report ownership as resolved -- IsOwned
        // must remain true so the identity is not lost/abandoned.
        var api = new RecordingApi { CreateJobSucceeds = false, WaitForExitSucceeds = false };
        var ownership = new CenterMHelperOwnership(api);
        var initial = ownership.Start(@"C:\fake\MSI Center M.exe");
        Assert.Equal(HelperStartResult.PartialCleanupUnconfirmed, initial);

        ownership.Dispose();

        Assert.True(ownership.IsOwned, "Dispose() must not discard a job-less retained handle whose termination could not be confirmed.");

        // The identity is still resolvable afterward -- Dispose() is not a dead end. Same scenario
        // as the Stop() case: process exits before the retry, TerminateProcess fails (already
        // exited) but the retained handle's wait confirms it.
        api.TerminateSucceeds = false;
        api.WaitForExitSucceeds = true;
        ownership.Dispose();
        Assert.False(ownership.IsOwned);
    }

    [Fact]
    public void ResumeFailure_SecondStopAttemptAlsoUnconfirmed_OwnershipRemainsRetained_NoJobFailSafeLeft()
    {
        // Post-Assign/Resume-failure case: the Job was already closed as part of the ResumeThread
        // failure path, so a subsequent Stop()/Dispose() has no Job fail-safe either -- the exact
        // same "retain until confirmed" contract must apply here too.
        var api = new RecordingApi { ResumeSucceeds = false, WaitForExitSucceeds = false };
        var ownership = new CenterMHelperOwnership(api);
        var initial = ownership.Start(@"C:\fake\MSI Center M.exe");
        Assert.Equal(HelperStartResult.PartialCleanupUnconfirmed, initial);
        Assert.True(ownership.IsOwned);

        var stopped = ownership.Stop(TimeSpan.FromSeconds(1));

        Assert.False(stopped);
        Assert.True(ownership.IsOwned);

        api.WaitForExitSucceeds = true;
        Assert.True(ownership.Stop(TimeSpan.FromSeconds(1)));
        Assert.False(ownership.IsOwned);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void JobLimitFailure_CleanupUnconfirmed_RetainsOwnership(bool terminateSucceeds, bool waitForExitSucceeds)
    {
        var api = new RecordingApi { SetLimitSucceeds = false, TerminateSucceeds = terminateSucceeds, WaitForExitSucceeds = waitForExitSucceeds };
        var ownership = new CenterMHelperOwnership(api);

        var result = ownership.Start(@"C:\fake\MSI Center M.exe");

        Assert.Equal(HelperStartResult.PartialCleanupUnconfirmed, result);
        Assert.True(ownership.IsOwned);
        api.WaitForExitSucceeds = true;
        Assert.True(ownership.Stop(TimeSpan.FromSeconds(1)));
        Assert.False(ownership.IsOwned);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void AssignFailure_CleanupUnconfirmed_RetainsOwnership(bool terminateSucceeds, bool waitForExitSucceeds)
    {
        var api = new RecordingApi { AssignSucceeds = false, TerminateSucceeds = terminateSucceeds, WaitForExitSucceeds = waitForExitSucceeds };
        var ownership = new CenterMHelperOwnership(api);

        var result = ownership.Start(@"C:\fake\MSI Center M.exe");

        Assert.Equal(HelperStartResult.PartialCleanupUnconfirmed, result);
        Assert.True(ownership.IsOwned);
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
    public void Stop_JobBackedHelper_GracefulTerminateUnconfirmed_JobCloseFallback_SecondWaitConfirms_ResolvesAndClearsOwnership()
    {
        // For a fully-armed (Job-backed) helper, TerminateProcess/wait not confirming exit is not
        // the end of the story: closing the Job (KILL_ON_JOB_CLOSE, already set during Start()) is
        // a valid kill *request*, but the exact process handle must stay open and a second bounded
        // wait on that same handle must confirm exit before ownership is released -- matching the
        // production DISARM ordering (terminate -> confirm exit -> close handles), not "Job closed,
        // therefore assume gone."
        var api = new RecordingApi { WaitForExitResults = new Queue<bool>([false, true]) };
        var ownership = new CenterMHelperOwnership(api);
        Assert.Equal(HelperStartResult.Started, ownership.Start(@"C:\fake\MSI Center M.exe"));

        var stopped = ownership.Stop(TimeSpan.FromSeconds(1));

        Assert.True(stopped); // the second, post-Job-close wait on the exact handle confirmed exit
        Assert.False(ownership.IsOwned);
        Assert.Equal(2, api.Calls.Count(c => c == "WaitForExit"));
    }

    [Fact]
    public void Stop_JobBackedHelper_GracefulTerminateUnconfirmed_JobCloseFallbackAlsoUnconfirmed_OwnershipRemainsRetained()
    {
        // If even the post-Job-close wait cannot confirm exit, the exact process handle is the only
        // remaining authority (the Job is already gone) -- ownership must stay retained rather than
        // being discarded just because the Job fail-safe was requested, so a later retry can still
        // resolve it via the same exact handle.
        var api = new RecordingApi { WaitForExitResults = new Queue<bool>([false, false]) };
        var ownership = new CenterMHelperOwnership(api);
        Assert.Equal(HelperStartResult.Started, ownership.Start(@"C:\fake\MSI Center M.exe"));

        var stopped = ownership.Stop(TimeSpan.FromSeconds(1));

        Assert.False(stopped);
        Assert.True(ownership.IsOwned); // exact handle retained -- no name/PID rediscovery needed
        Assert.Equal(2, api.Calls.Count(c => c == "WaitForExit"));

        // A later retry whose exact-handle wait finally succeeds resolves it.
        api.WaitForExitResults!.Enqueue(true);
        var retried = ownership.Stop(TimeSpan.FromSeconds(1));

        Assert.True(retried);
        Assert.False(ownership.IsOwned);
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

    [Fact]
    public void PollLiveness_NotOwned_ReturnsNotOwned_NoNativeCall()
    {
        var api = new RecordingApi();
        var ownership = new CenterMHelperOwnership(api);

        Assert.Equal(HelperLivenessState.NotOwned, ownership.PollLiveness());
        Assert.DoesNotContain("PollLiveness", api.Calls);
    }

    [Theory]
    [InlineData(0, 1)] // Alive -> Alive
    [InlineData(1, 2)] // Exited -> Exited
    [InlineData(2, 3)] // Uncertain -> Uncertain
    public void PollLiveness_Owned_MapsNativeResultExactly_NeverConflatesUncertainWithEitherExtreme(int nativeRaw, int expectedRaw)
    {
        var native = (LiveProcessProbeStatus)nativeRaw;
        var expected = (HelperLivenessState)expectedRaw;
        var api = new RecordingApi { PollLivenessResult = native };
        var ownership = new CenterMHelperOwnership(api);
        Assert.Equal(HelperStartResult.Started, ownership.Start(@"C:\fake\MSI Center M.exe"));

        Assert.Equal(expected, ownership.PollLiveness());
    }

    [Fact]
    public void RetireConfirmedExited_ClearsOwnership_WithoutCallingTerminateOrWaitForExit()
    {
        var api = new RecordingApi();
        var ownership = new CenterMHelperOwnership(api);
        Assert.Equal(HelperStartResult.Started, ownership.Start(@"C:\fake\MSI Center M.exe"));
        var callsBefore = api.Calls.Count;

        ownership.RetireConfirmedExited();

        Assert.False(ownership.IsOwned);
        Assert.Null(ownership.ProcessId);
        Assert.Equal(callsBefore, api.Calls.Count); // no Terminate/WaitForExit issued for an already-confirmed-dead handle
    }

    [Fact]
    public void RetireConfirmedExited_NotOwned_IsNoOp()
    {
        var api = new RecordingApi();
        var ownership = new CenterMHelperOwnership(api);

        ownership.RetireConfirmedExited();

        Assert.False(ownership.IsOwned);
        Assert.Empty(api.Calls);
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

        /// <summary>When set, successive <see cref="WaitForExit"/> calls dequeue their result from
        /// here instead of using <see cref="WaitForExitSucceeds"/> -- lets a test model a first
        /// wait that times out followed by a second (post-Job-close) wait with a different
        /// outcome.</summary>
        internal Queue<bool>? WaitForExitResults { get; init; }

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
            return WaitForExitResults is { Count: > 0 } queue ? queue.Dequeue() : WaitForExitSucceeds;
        }

        internal Queue<LiveProcessProbeStatus>? PollLivenessResults { get; init; }
        internal LiveProcessProbeStatus PollLivenessResult { get; set; } = LiveProcessProbeStatus.Alive;

        public LiveProcessProbeStatus PollLiveness(SafeProcessHandle processHandle)
        {
            Calls.Add("PollLiveness");
            return PollLivenessResults is { Count: > 0 } queue ? queue.Dequeue() : PollLivenessResult;
        }

        private static IntPtr GetCurrentProcessHandle() => System.Diagnostics.Process.GetCurrentProcess().Handle;
    }
}
