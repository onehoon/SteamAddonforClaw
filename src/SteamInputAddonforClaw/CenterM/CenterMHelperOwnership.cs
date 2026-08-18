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
    /// is true) rather than discarded, so a later <see cref="CenterMHelperOwnership.Stop"/> can
    /// still resolve it through the same retained handle -- never by process-name or PID
    /// rediscovery. Callers must not treat this as equivalent to a clean native fallback: a
    /// same-name process may still be alive and suppressing native Center M launch until resolved.</summary>
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
    /// releases ownership. Idempotent. Internally serialized against Start/Dispose.</summary>
    internal bool Stop(TimeSpan waitTimeout)
    {
        lock (_sync)
        {
            if (_processHandle is null) return true;

            var terminated = _api.TryTerminate(_processHandle, out _) && _api.WaitForExit(_processHandle, waitTimeout);
            AppLog.Info("CenterM.Helper", "Helper stopped.", ("Terminated", terminated), ("ProcessId", ProcessId));
            DisposeCore();
            return terminated;
        }
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
        // Both must succeed to count as confirmed: a failed TerminateProcess call followed by a
        // wait that happens to return true is not evidence the helper actually exited.
        if (_api.TryTerminate(processHandle, out _) && _api.WaitForExit(processHandle, TimeSpan.FromSeconds(2)))
        {
            processHandle.Dispose();
            return failureResult;
        }

        AppLog.Warn("CenterM.Helper", "Suspended-helper cleanup after construction failure could not be confirmed; retaining ownership.", null, ("ProcessId", processId), ("FailureReason", failureResult));
        ProcessId = processId;
        _processHandle = processHandle;
        return HelperStartResult.PartialCleanupUnconfirmed;
    }

    public void Dispose()
    {
        lock (_sync) DisposeCore();
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
