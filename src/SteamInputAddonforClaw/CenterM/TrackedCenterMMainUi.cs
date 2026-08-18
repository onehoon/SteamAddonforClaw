using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>Native OpenProcess seam, isolated so <see cref="TrackedCenterMMainUi"/>'s two-tier
/// rights-acquisition fallback (full rights, then observation-only, then uncertain) can be unit
/// tested deterministically for both outcomes without depending on real process ACLs.</summary>
internal interface IProcessHandleOpener
{
    SafeProcessHandle Open(int processId, uint desiredAccess);
}

internal sealed class Win32ProcessHandleOpener : IProcessHandleOpener
{
    public SafeProcessHandle Open(int processId, uint desiredAccess) => OpenProcess(desiredAccess, false, processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);
}

/// <summary>
/// Identity of a real (non-helper) MSI Center M MainUI process, established the moment it is
/// first identified. Ownership authority for later safe termination is the retained
/// <see cref="Handle"/> plus <see cref="ProcessId"/> -- never the process name alone, and never a
/// PID re-queried later (PID reuse race).
/// </summary>
internal sealed class TrackedCenterMMainUi : IDisposable
{
    private const uint PROCESS_TERMINATE = 0x0001;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint SYNCHRONIZE = 0x00100000;

    internal int ProcessId { get; }
    internal string? ExecutablePath { get; }
    internal SafeProcessHandle Handle { get; }

    /// <summary>False when even the minimal-privilege, observation-only OpenProcess (SYNCHRONIZE |
    /// QUERY_LIMITED_INFORMATION) failed -- <see cref="Handle"/> is invalid and this identity
    /// cannot be tracked at all; callers must treat this as an uncertain identity.</summary>
    internal bool HasRetainedHandle => !Handle.IsInvalid;

    /// <summary>False when TERMINATE rights specifically were unavailable (observation-only handle
    /// retained instead). <see cref="SafeMainUiTerminator"/> must fail-open (AccessDenied) rather
    /// than attempt any other termination mechanism when this is false.</summary>
    internal bool HasTerminateRights { get; }

    private TrackedCenterMMainUi(int processId, string? executablePath, SafeProcessHandle handle, bool hasTerminateRights)
    {
        ProcessId = processId;
        ExecutablePath = executablePath;
        Handle = handle;
        HasTerminateRights = hasTerminateRights;
    }

    /// <summary>
    /// Acquires and retains the exact process-object identity for <paramref name="processId"/>.
    /// TERMINATE is optional for observation but full rights are always attempted first; only if
    /// that fails does this fall back to an observation-only handle (SYNCHRONIZE |
    /// QUERY_LIMITED_INFORMATION), so a process this Addon cannot terminate is still trackable for
    /// lifecycle/natural-exit purposes and PID-reuse resistance. If even the observation-only open
    /// fails, <see cref="HasRetainedHandle"/> is false and the identity must be treated as
    /// uncertain -- never a substitute for a real retained handle.
    /// </summary>
    internal static TrackedCenterMMainUi Create(int processId, string? executablePath, IProcessHandleOpener? opener = null)
    {
        opener ??= new Win32ProcessHandleOpener();

        var fullRightsHandle = opener.Open(processId, PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE);
        if (!fullRightsHandle.IsInvalid)
            return new TrackedCenterMMainUi(processId, executablePath, fullRightsHandle, hasTerminateRights: true);
        fullRightsHandle.Dispose();

        var observationHandle = opener.Open(processId, PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE);
        return new TrackedCenterMMainUi(processId, executablePath, observationHandle, hasTerminateRights: false);
    }

    /// <summary>Test-only factory: wraps the current process's own pseudo-handle (always valid, no
    /// extra OS resource) when <paramref name="hasTerminateRights"/> is true, so
    /// <see cref="SafeMainUiTerminator"/> tests never need a real target process. The pseudo-handle
    /// is never actually passed to a real TerminateProcess call in tests -- termination itself is
    /// exercised through a fake <see cref="ITerminateProcessInvoker"/>.</summary>
    internal static TrackedCenterMMainUi CreateForTesting(int processId, string? executablePath, bool hasTerminateRights)
    {
        var handle = hasTerminateRights ? new SafeProcessHandle(GetCurrentProcess(), false) : new SafeProcessHandle(IntPtr.Zero, false);
        return new TrackedCenterMMainUi(processId, executablePath, handle, hasTerminateRights);
    }

    public void Dispose() => Handle.Dispose();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
}
