using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SteamInputAddonforClaw.CenterM;

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

    /// <summary>False when the minimal-privilege OpenProcess (SYNCHRONIZE | QUERY_LIMITED_INFORMATION
    /// | TERMINATE) failed -- observation of this identity still proceeds, but
    /// <see cref="SafeMainUiTerminator"/> must fail-open (AccessDenied) rather than attempt any
    /// other termination mechanism.</summary>
    internal bool HasTerminateRights { get; }

    private TrackedCenterMMainUi(int processId, string? executablePath, SafeProcessHandle handle, bool hasTerminateRights)
    {
        ProcessId = processId;
        ExecutablePath = executablePath;
        Handle = handle;
        HasTerminateRights = hasTerminateRights;
    }

    internal static TrackedCenterMMainUi Create(int processId, string? executablePath)
    {
        var handle = OpenProcess(PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, false, processId);
        return new TrackedCenterMMainUi(processId, executablePath, handle, !handle.IsInvalid);
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
}
