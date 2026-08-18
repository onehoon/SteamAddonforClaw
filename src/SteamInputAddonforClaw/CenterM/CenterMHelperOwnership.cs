using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

internal enum HelperStartResult
{
    Started,
    AlreadyOwned,
    CreateProcessFailed,
    JobObjectFailed,
    JobLimitFailed,
    AssignFailed,
    ResumeFailed,
    /// <summary>Construction failed (Job/limit/assign/resume) and the follow-up cleanup attempt
    /// (TerminateProcess and/or the bounded exit wait) could not confirm the helper actually
    /// exited. The only authoritative process handle is retained (<see cref="CenterMHelperOwnership.IsOwned"/>
    /// is true) rather than discarded, so a later <see cref="CenterMHelperOwnership.Stop"/> or
    /// <see cref="CenterMHelperOwnership.Dispose"/> attempts real termination through that same
    /// retained handle -- never by process-name or PID rediscovery. Neither call is guaranteed to
    /// succeed on any given attempt: if termination still cannot be confirmed and no Job
    /// (KILL_ON_JOB_CLOSE) fail-safe exists for this identity, ownership is deliberately left
    /// retained rather than silently discarded, so a further retry remains possible. Callers must
    /// not treat this result as equivalent to a clean native fallback: a same-name process may
    /// still be alive and suppressing native Center M launch until resolution is actually
    /// confirmed.</summary>
    PartialCleanupUnconfirmed
}

/// <summary>
/// Owns exactly one helper process via CREATE_SUSPENDED + a private Job Object with
/// JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE, in the mandatory order required for crash-safe fail-open:
/// CreateProcess(SUSPENDED) -&gt; CreateJobObject -&gt; SetInformationJobObject(KILL_ON_JOB_CLOSE) -&gt;
/// AssignProcessToJobObject -&gt; ResumeThread. The Job never contains any other process. Ownership
/// authority for later stop/cleanup is the retained process handle and job handle -- never the
/// process name (research handoff sections 16-17).
/// </summary>
internal sealed class CenterMHelperOwnership(IHelperProcessNativeApi? api = null) : IDisposable
{
    private readonly IHelperProcessNativeApi _api = api ?? new Win32HelperProcessNativeApi();
    // Serializes Start/Stop/Dispose so the "exactly one owned helper" invariant holds regardless
    // of caller scheduling, not merely against a strictly sequential caller: without this, two
    // concurrent Start() calls could both observe IsOwned == false, both create/assign/resume a
    // suspended helper, and only afterward race to overwrite the owned handles/ProcessId --
    // recreating the same-name invariant violation and lost-ownership bug this class exists to
    // prevent. The lock is held across each method's entire native-call sequence (not just the
    // IsOwned check), so only one such sequence can ever be in flight at a time.
    private readonly Lock _sync = new();
    private SafeProcessHandle? _processHandle;
    private SafeHandle? _jobHandle;

    internal int? ProcessId { get; private set; }
    internal bool IsOwned => _processHandle is not null;

    /// <summary>Starts and takes ownership of exactly one helper. Refuses (returns
    /// <see cref="HelperStartResult.AlreadyOwned"/>, no native calls made) while a helper is
    /// already owned -- a duplicate arm/reconcile call must never create a second helper and
    /// silently lose ownership of the first, which would both violate the Armed same-name
    /// invariant and leave the first Job/process handles to nondeterministic finalization instead
    /// of explicit lifecycle ownership. Callers that intend to replace an owned helper must call
    /// <see cref="Stop"/> first. Internally serialized: this remains true even under concurrent
    /// callers.</summary>
    internal HelperStartResult Start(string imagePath)
    {
        lock (_sync) return StartCore(imagePath);
    }

    private HelperStartResult StartCore(string imagePath)
    {
        if (IsOwned) return HelperStartResult.AlreadyOwned;

        if (!_api.TryCreateSuspended(imagePath, out var processId, out var processHandle, out var threadHandle, out var createError))
        {
            AppLog.Warn("CenterM.Helper", "Helper CreateProcess(SUSPENDED) failed.", null, ("Win32Error", createError));
            return HelperStartResult.CreateProcessFailed;
        }

        using var ownedThreadHandle = threadHandle!;
        var ownedProcessHandle = processHandle!;

        if (!_api.TryCreateJobObject(out var jobHandle, out var jobError))
        {
            AppLog.Warn("CenterM.Helper", "CreateJobObject failed; terminating suspended helper.", null, ("Win32Error", jobError));
            return CleanupAfterConstructionFailure(processId, ownedProcessHandle, HelperStartResult.JobObjectFailed);
        }
        var ownedJobHandle = jobHandle!;

        if (!_api.TrySetKillOnJobClose(ownedJobHandle, out var limitError))
        {
            AppLog.Warn("CenterM.Helper", "SetInformationJobObject(KILL_ON_JOB_CLOSE) failed; terminating suspended helper.", null, ("Win32Error", limitError));
            ownedJobHandle.Dispose(); // never assigned to the process -- safe to discard unconditionally
            return CleanupAfterConstructionFailure(processId, ownedProcessHandle, HelperStartResult.JobLimitFailed);
        }

        if (!_api.TryAssignProcessToJob(ownedJobHandle, ownedProcessHandle, out var assignError))
        {
            // The helper must never run unassigned even momentarily -- terminate rather than let
            // it continue outside crash-cleanup protection.
            AppLog.Warn("CenterM.Helper", "AssignProcessToJobObject failed; terminating suspended helper.", null, ("Win32Error", assignError));
            ownedJobHandle.Dispose(); // never assigned to the process -- safe to discard unconditionally
            return CleanupAfterConstructionFailure(processId, ownedProcessHandle, HelperStartResult.AssignFailed);
        }

        if (!_api.TryResumeThread(ownedThreadHandle, out var resumeError))
        {
            // Already assigned to the Job: closing the Job (KILL_ON_JOB_CLOSE) requests the kill
            // of the still-suspended helper. The process handle is retained until a bounded wait
            // confirms the kill actually completed -- deterministic evidence the helper is gone,
            // not just a request that it should be. The Job itself is already closed/invalid at
            // this point, so only the process handle is a candidate for retained ownership below.
            AppLog.Warn("CenterM.Helper", "ResumeThread failed; closing Job to kill suspended helper.", null, ("Win32Error", resumeError));
            ownedJobHandle.Dispose();
            if (_api.WaitForExit(ownedProcessHandle, TimeSpan.FromSeconds(5)))
            {
                ownedProcessHandle.Dispose();
                return HelperStartResult.ResumeFailed;
            }

            AppLog.Warn("CenterM.Helper", "Helper did not confirm exit after Job close following ResumeThread failure; retaining ownership.", null, ("ProcessId", processId));
            ProcessId = processId;
            _processHandle = ownedProcessHandle;
            return HelperStartResult.PartialCleanupUnconfirmed;
        }

        ProcessId = processId;
        _processHandle = ownedProcessHandle;
        _jobHandle = ownedJobHandle;
        AppLog.Info("CenterM.Helper", "Helper armed.", ("ProcessId", processId));
        return HelperStartResult.Started;
    }

    /// <summary>Stops the owned helper via its retained handle (never by re-querying PID) and
    /// releases ownership -- but only once termination is actually confirmed, or a Job
    /// (KILL_ON_JOB_CLOSE) is present as a crash-safety net that itself guarantees the kill when
    /// closed. If neither holds (a job-less retained handle -- e.g. a
    /// <see cref="HelperStartResult.PartialCleanupUnconfirmed"/> identity from before Job
    /// assignment -- whose termination attempt fails again), ownership is deliberately NOT
    /// released: the handle is the only authority left, and discarding it would abandon a possibly
    /// still-alive same-name process with no way to resolve it later except process-name/PID
    /// rediscovery, which this class never does. Idempotent when it does resolve. Internally
    /// serialized against Start/Dispose.</summary>
    internal bool Stop(TimeSpan waitTimeout)
    {
        lock (_sync) return StopCore(waitTimeout);
    }

    private bool StopCore(TimeSpan waitTimeout)
    {
        if (_processHandle is null) return true;

        // TerminateProcess is a best-effort request, not the confirmation itself: Windows
        // documents that it fails with ERROR_ACCESS_DENIED once the target has already exited, so
        // its own return value must never gate whether the authoritative exit check even runs.
        // WaitForExit on the exact retained handle is what actually confirms exit -- signaled
        // means gone, regardless of why (this call, an external actor, a prior Job kill, ...).
        _api.TryTerminate(_processHandle, out _);
        var terminated = _api.WaitForExit(_processHandle, waitTimeout);
        AppLog.Info("CenterM.Helper", "Helper stop attempted.", ("Terminated", terminated), ("ProcessId", ProcessId), ("JobBacked", _jobHandle is not null));

        if (terminated || _jobHandle is not null)
        {
            // Either confirmed exit directly, or a Job with KILL_ON_JOB_CLOSE is present -- closing
            // it here is itself a guaranteed, effective kill even when the graceful
            // TerminateProcess/wait above did not confirm exit in time.
            DisposeCore();
            return terminated;
        }

        AppLog.Warn("CenterM.Helper", "Stop could not confirm helper termination and no Job fail-safe exists; ownership retained for retry.", null, ("ProcessId", ProcessId));
        return false;
    }

    /// <summary>Attempts to terminate a still-suspended helper left over from a construction
    /// failure. If cleanup is confirmed, the handle is discarded and <paramref name="failureResult"/>
    /// is returned as-is. If it cannot be confirmed (TerminateProcess failed, or the bounded wait
    /// timed out), the handle is retained instead of discarded -- <see cref="IsOwned"/> becomes
    /// true -- so a later <see cref="Stop"/> can still resolve it, and
    /// <see cref="HelperStartResult.PartialCleanupUnconfirmed"/> is returned instead so callers
    /// cannot mistake this for a clean native fallback.</summary>
    private HelperStartResult CleanupAfterConstructionFailure(int processId, SafeProcessHandle processHandle, HelperStartResult failureResult)
    {
        // TerminateProcess is a best-effort request; a signaled retained handle afterward is
        // authoritative exit evidence regardless of whether this particular call succeeded (it
        // legitimately fails with ERROR_ACCESS_DENIED against an already-exited process).
        _api.TryTerminate(processHandle, out _);
        if (_api.WaitForExit(processHandle, TimeSpan.FromSeconds(2)))
        {
            processHandle.Dispose();
            return failureResult;
        }

        AppLog.Warn("CenterM.Helper", "Suspended-helper cleanup after construction failure could not be confirmed; retaining ownership.", null, ("ProcessId", processId), ("FailureReason", failureResult));
        ProcessId = processId;
        _processHandle = processHandle;
        return HelperStartResult.PartialCleanupUnconfirmed;
    }

    /// <summary>
    /// Releases owned resources. For a Job-backed helper (the normal successfully-armed case),
    /// this alone is a safe, guaranteed kill: closing the Job triggers KILL_ON_JOB_CLOSE. For a
    /// job-less retained handle (a <see cref="HelperStartResult.PartialCleanupUnconfirmed"/>
    /// identity from before Job assignment), simply closing the process handle would NOT terminate
    /// the process -- so this attempts a real termination through the retained handle first
    /// (exact-handle, never process-name/PID rediscovery). If that attempt also cannot confirm
    /// exit, ownership is intentionally left retained (<see cref="IsOwned"/> stays true) rather
    /// than silently discarding the only authority over a process that may still be alive.
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_processHandle is null) { DisposeCore(); return; }
            if (!StopCore(TimeSpan.FromSeconds(5)))
                AppLog.Warn("CenterM.Helper", "Dispose could not confirm termination for a job-less retained handle; ownership intentionally left retained rather than silently discarded.", null, ("ProcessId", ProcessId));
        }
    }

    private void DisposeCore()
    {
        _processHandle?.Dispose();
        _jobHandle?.Dispose();
        _processHandle = null;
        _jobHandle = null;
        ProcessId = null;
    }
}
