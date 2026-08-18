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
    ResumeFailed
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
    /// <see cref="Stop"/> first.</summary>
    internal HelperStartResult Start(string imagePath)
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
            TerminateSuspended(ownedProcessHandle);
            return HelperStartResult.JobObjectFailed;
        }
        var ownedJobHandle = jobHandle!;

        if (!_api.TrySetKillOnJobClose(ownedJobHandle, out var limitError))
        {
            AppLog.Warn("CenterM.Helper", "SetInformationJobObject(KILL_ON_JOB_CLOSE) failed; terminating suspended helper.", null, ("Win32Error", limitError));
            ownedJobHandle.Dispose();
            TerminateSuspended(ownedProcessHandle);
            return HelperStartResult.JobLimitFailed;
        }

        if (!_api.TryAssignProcessToJob(ownedJobHandle, ownedProcessHandle, out var assignError))
        {
            // The helper must never run unassigned even momentarily -- terminate rather than let
            // it continue outside crash-cleanup protection.
            AppLog.Warn("CenterM.Helper", "AssignProcessToJobObject failed; terminating suspended helper.", null, ("Win32Error", assignError));
            ownedJobHandle.Dispose();
            TerminateSuspended(ownedProcessHandle);
            return HelperStartResult.AssignFailed;
        }

        if (!_api.TryResumeThread(ownedThreadHandle, out var resumeError))
        {
            // Already assigned to the Job: closing the Job (KILL_ON_JOB_CLOSE) requests the kill
            // of the still-suspended helper. The process handle is retained until a bounded wait
            // confirms the kill actually completed -- deterministic evidence the helper is gone,
            // not just a request that it should be.
            AppLog.Warn("CenterM.Helper", "ResumeThread failed; closing Job to kill suspended helper.", null, ("Win32Error", resumeError));
            ownedJobHandle.Dispose();
            var exited = _api.WaitForExit(ownedProcessHandle, TimeSpan.FromSeconds(5));
            if (!exited)
                AppLog.Warn("CenterM.Helper", "Helper did not confirm exit after Job close following ResumeThread failure.", null, ("ProcessId", processId));
            ownedProcessHandle.Dispose();
            return HelperStartResult.ResumeFailed;
        }

        ProcessId = processId;
        _processHandle = ownedProcessHandle;
        _jobHandle = ownedJobHandle;
        AppLog.Info("CenterM.Helper", "Helper armed.", ("ProcessId", processId));
        return HelperStartResult.Started;
    }

    /// <summary>Stops the owned helper via its retained handle (never by re-querying PID) and
    /// releases ownership. Idempotent.</summary>
    internal bool Stop(TimeSpan waitTimeout)
    {
        if (_processHandle is null) return true;

        var terminated = _api.TryTerminate(_processHandle, out _) && _api.WaitForExit(_processHandle, waitTimeout);
        AppLog.Info("CenterM.Helper", "Helper stopped.", ("Terminated", terminated), ("ProcessId", ProcessId));
        Dispose();
        return terminated;
    }

    private void TerminateSuspended(SafeProcessHandle processHandle)
    {
        _api.TryTerminate(processHandle, out _);
        _api.WaitForExit(processHandle, TimeSpan.FromSeconds(2));
        processHandle.Dispose();
    }

    public void Dispose()
    {
        _processHandle?.Dispose();
        _jobHandle?.Dispose();
        _processHandle = null;
        _jobHandle = null;
        ProcessId = null;
    }
}
