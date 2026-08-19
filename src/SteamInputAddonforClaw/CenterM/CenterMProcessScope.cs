using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace SteamInputAddonforClaw.CenterM;

internal enum ProcessScopeProbeStatus
{
    /// <summary>Same Windows session AND same user as this Addon process.</summary>
    Match,
    /// <summary>A different session or a different user -- outside this Addon's
    /// <c>Local\</c>-scoped singleton world, never a routing-retirement candidate.</summary>
    Foreign,
    /// <summary>The candidate process no longer exists.</summary>
    Exited,
    /// <summary>Session/user could not be authoritatively established. Must never be treated as
    /// either <see cref="Match"/> or <see cref="Foreign"/> -- a process whose scope is unknown must
    /// never become (or silently be excluded from being) a termination candidate by assumption.</summary>
    Uncertain
}

/// <summary>Establishes whether a candidate process is in the SAME Windows logon session and SAME
/// user as this Addon process -- the exact scope the real MSI Center M MainUI's own duplicate-instance
/// singleton is built on (research: <c>Local\</c> is a session-local kernel namespace, and the
/// duplicate-instance path additionally builds a same-user process list before deciding another
/// instance exists). Isolated behind an interface so <see cref="ScopedCenterMProcessSnapshotSource"/>
/// can be unit tested deterministically.</summary>
internal interface ICenterMProcessScopeInspector
{
    ProcessScopeProbeStatus Inspect(int processId);
}

internal sealed class Win32CenterMProcessScopeInspector : ICenterMProcessScopeInspector
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;

    private readonly IProcessHandleOpener _handleOpener;
    private readonly uint? _currentSessionId;
    private readonly string? _currentUserSid;

    internal Win32CenterMProcessScopeInspector(IProcessHandleOpener? handleOpener = null)
    {
        _handleOpener = handleOpener ?? new Win32ProcessHandleOpener();
        _currentSessionId = TryGetSessionId((uint)Environment.ProcessId);
        _currentUserSid = TryGetCurrentUserSid();
    }

    public ProcessScopeProbeStatus Inspect(int processId)
    {
        // If this Addon's own session/user identity could not be established, nothing can be
        // authoritatively compared against it.
        if (_currentSessionId is null || _currentUserSid is null) return ProcessScopeProbeStatus.Uncertain;

        var sessionId = TryGetSessionId((uint)processId);
        if (sessionId is null) return ProcessScopeProbeStatus.Uncertain;
        if (sessionId != _currentSessionId) return ProcessScopeProbeStatus.Foreign;

        var userSid = TryGetUserSid(processId);
        if (userSid is null) return ProcessScopeProbeStatus.Uncertain;

        return string.Equals(userSid, _currentUserSid, StringComparison.Ordinal)
            ? ProcessScopeProbeStatus.Match
            : ProcessScopeProbeStatus.Foreign;
    }

    private static string? TryGetCurrentUserSid()
    {
        try { return WindowsIdentity.GetCurrent().User?.Value; }
        catch (SecurityException) { return null; }
    }

    private string? TryGetUserSid(int processId)
    {
        var processHandle = _handleOpener.Open(processId, PROCESS_QUERY_LIMITED_INFORMATION);
        using (processHandle)
        {
            if (processHandle.IsInvalid) return null;
            if (!OpenProcessToken(processHandle.DangerousGetHandle(), TOKEN_QUERY, out var tokenHandle)) return null;
            using (tokenHandle)
            {
                try
                {
                    using var identity = new WindowsIdentity(tokenHandle.DangerousGetHandle());
                    return identity.User?.Value;
                }
                catch (Exception ex) when (ex is SecurityException or ArgumentException or UnauthorizedAccessException)
                {
                    return null;
                }
            }
        }
    }

    private static uint? TryGetSessionId(uint processId) => ProcessIdToSessionId(processId, out var sessionId) ? sessionId : null;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out SafeAccessTokenHandle tokenHandle);
}

/// <summary>Same session/user authority as <see cref="ICenterMProcessScopeInspector"/>, but anchored
/// to an already-retained process handle instead of a PID. A PID-based scope check performed before
/// <see cref="TrackedCenterMMainUi.Create"/> acquires its own handle leaves a race window: if the
/// original process exits and the PID is reused before the retained handle is opened, the retained
/// handle can end up pointing at a DIFFERENT process object than the one whose scope was checked --
/// silently defeating the session/user safety boundary for the exact object about to be mutated.
/// This inspector reads session/user directly off the SAME handle that will be terminated, so scope
/// authority can never drift from the exact process object in play.</summary>
internal interface ICenterMRetainedProcessScopeInspector
{
    ProcessScopeProbeStatus Inspect(SafeProcessHandle processHandle);
}

internal sealed class Win32CenterMRetainedProcessScopeInspector : ICenterMRetainedProcessScopeInspector
{
    private const uint TOKEN_QUERY = 0x0008;
    private const uint WAIT_OBJECT_0 = 0;
    private const uint WAIT_TIMEOUT = 0x102;

    private readonly uint? _currentSessionId;
    private readonly string? _currentUserSid;

    internal Win32CenterMRetainedProcessScopeInspector()
    {
        _currentSessionId = TryGetSessionId((uint)Environment.ProcessId);
        _currentUserSid = TryGetCurrentUserSid();
    }

    public ProcessScopeProbeStatus Inspect(SafeProcessHandle processHandle)
    {
        if (_currentSessionId is null || _currentUserSid is null) return ProcessScopeProbeStatus.Uncertain;
        if (processHandle is null || processHandle.IsInvalid) return ProcessScopeProbeStatus.Uncertain;

        // Check exact-handle liveness first -- a real MainUI can exit naturally between the earlier
        // identity inspection and this scope check, and that must be classified as the already-
        // supported benign natural-exit race (Exited), never conflated with an unrelated scope
        // lookup failure (Uncertain).
        var waitResult = WaitForSingleObject(processHandle, 0);
        if (waitResult == WAIT_OBJECT_0) return ProcessScopeProbeStatus.Exited;
        if (waitResult != WAIT_TIMEOUT) return ProcessScopeProbeStatus.Uncertain;

        // GetProcessId returns the PID directly (DWORD) -- it has no BOOL success/out-param ABI.
        var processId = GetProcessId(processHandle);
        if (processId == 0) return ProcessScopeProbeStatus.Uncertain;

        var sessionId = TryGetSessionId(processId);
        if (sessionId is null) return ProcessScopeProbeStatus.Uncertain;
        if (sessionId != _currentSessionId) return ProcessScopeProbeStatus.Foreign;

        if (!OpenProcessToken(processHandle.DangerousGetHandle(), TOKEN_QUERY, out var tokenHandle))
            return ProcessScopeProbeStatus.Uncertain;
        using (tokenHandle)
        {
            string? userSid;
            try
            {
                using var identity = new WindowsIdentity(tokenHandle.DangerousGetHandle());
                userSid = identity.User?.Value;
            }
            catch (Exception ex) when (ex is SecurityException or ArgumentException or UnauthorizedAccessException)
            {
                return ProcessScopeProbeStatus.Uncertain;
            }

            if (userSid is null) return ProcessScopeProbeStatus.Uncertain;

            return string.Equals(userSid, _currentUserSid, StringComparison.Ordinal)
                ? ProcessScopeProbeStatus.Match
                : ProcessScopeProbeStatus.Foreign;
        }
    }

    private static string? TryGetCurrentUserSid()
    {
        try { return WindowsIdentity.GetCurrent().User?.Value; }
        catch (SecurityException) { return null; }
    }

    private static uint? TryGetSessionId(uint processId) => ProcessIdToSessionId(processId, out var sessionId) ? sessionId : null;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle processHandle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetProcessId(SafeProcessHandle processHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out SafeAccessTokenHandle tokenHandle);
}

/// <summary>
/// Filters an inner <see cref="IProcessSnapshotSource"/> to only the same-session/same-user
/// candidates -- the exact scope of the real MSI Center M MainUI's own <c>Local\</c> singleton
/// world. Deliberately a separate wrapper rather than a change to <see cref="Win32ProcessSnapshotSource"/>
/// itself, so OEM1 backend discovery (a different, still-dormant use case) is not silently altered.
/// A process whose scope cannot be authoritatively established makes the ENTIRE result uncertain
/// (<see langword="null"/>) rather than guessing -- a same-name process that might or might not be
/// in scope must never be silently excluded from (or included in) the candidate set.
/// </summary>
internal sealed class ScopedCenterMProcessSnapshotSource(IProcessSnapshotSource inner, ICenterMProcessScopeInspector scopeInspector) : IProcessSnapshotSource
{
    public IReadOnlyList<ProcessSnapshotEntry>? GetProcessesByName(string processName)
    {
        var snapshot = inner.GetProcessesByName(processName);
        if (snapshot is null) return null;

        var result = new List<ProcessSnapshotEntry>();
        foreach (var entry in snapshot)
        {
            switch (scopeInspector.Inspect(entry.ProcessId))
            {
                case ProcessScopeProbeStatus.Match:
                    result.Add(entry);
                    break;
                case ProcessScopeProbeStatus.Foreign:
                case ProcessScopeProbeStatus.Exited:
                    break;
                default:
                    return null;
            }
        }

        return result;
    }
}
