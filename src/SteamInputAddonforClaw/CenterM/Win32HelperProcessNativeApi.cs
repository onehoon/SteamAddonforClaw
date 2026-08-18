using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>Generic CloseHandle-backed SafeHandle for thread/job kernel objects.</summary>
internal sealed class SafeGenericHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeGenericHandle() : base(true) { }
    internal SafeGenericHandle(IntPtr handle) : base(true) => SetHandle(handle);
    protected override bool ReleaseHandle() => CloseHandle(handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}

internal sealed class Win32HelperProcessNativeApi : IHelperProcessNativeApi
{
    private const uint CREATE_SUSPENDED = 0x00000004;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000;
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const uint WAIT_OBJECT_0 = 0;
    private const int ERROR_ACCESS_DENIED = 5;

    public bool TryCreateSuspended(string imagePath, out int processId, out SafeProcessHandle? processHandle, out SafeHandle? threadHandle, out int win32Error)
    {
        var startupInfo = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
        // CreateProcessW may mutate the command line buffer; imagePath is quoted defensively even
        // though the helper takes no arguments.
        var commandLine = new System.Text.StringBuilder($"\"{imagePath}\"");

        // If the Addon's own process happens to be running inside an outer Job Object (some
        // launch/shell/container environments do this), a plain child inherits membership in
        // that job. Assigning it into our own dedicated Job as well can then interact with the
        // outer job's own limits/cleanup semantics in ways this Addon does not control. Breaking
        // away first, when the outer job permits it (JOB_OBJECT_LIMIT_BREAKAWAY_OK), keeps the
        // helper's lifetime governed only by the Job this class itself owns and closes.
        var created = CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
            CREATE_SUSPENDED | CREATE_NO_WINDOW | CREATE_BREAKAWAY_FROM_JOB, IntPtr.Zero, null, ref startupInfo, out var info);

        if (!created && Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED)
        {
            // The outer job does not permit breakaway -- fall back to ordinary (nested-job)
            // creation rather than failing outright.
            commandLine = new System.Text.StringBuilder($"\"{imagePath}\"");
            created = CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                CREATE_SUSPENDED | CREATE_NO_WINDOW, IntPtr.Zero, null, ref startupInfo, out info);
        }

        if (!created)
        {
            win32Error = Marshal.GetLastWin32Error();
            processId = 0;
            processHandle = null;
            threadHandle = null;
            return false;
        }

        win32Error = 0;
        processId = info.dwProcessId;
        processHandle = new SafeProcessHandle(info.hProcess, true);
        threadHandle = new SafeGenericHandle(info.hThread);
        return true;
    }

    public bool TryCreateJobObject(out SafeHandle? jobHandle, out int win32Error)
    {
        var handle = CreateJobObjectW(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
        {
            win32Error = Marshal.GetLastWin32Error();
            jobHandle = null;
            return false;
        }
        win32Error = 0;
        jobHandle = new SafeGenericHandle(handle);
        return true;
    }

    public bool TrySetKillOnJobClose(SafeHandle jobHandle, out int win32Error)
    {
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE }
        };
        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            if (SetInformationJobObject(jobHandle, JobObjectExtendedLimitInformation, buffer, (uint)size))
            {
                win32Error = 0;
                return true;
            }
            win32Error = Marshal.GetLastWin32Error();
            return false;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public bool TryAssignProcessToJob(SafeHandle jobHandle, SafeProcessHandle processHandle, out int win32Error)
    {
        if (AssignProcessToJobObject(jobHandle, processHandle)) { win32Error = 0; return true; }
        win32Error = Marshal.GetLastWin32Error();
        return false;
    }

    public bool TryResumeThread(SafeHandle threadHandle, out int win32Error)
    {
        if (ResumeThread(threadHandle) != unchecked((uint)-1)) { win32Error = 0; return true; }
        win32Error = Marshal.GetLastWin32Error();
        return false;
    }

    public bool TryTerminate(SafeProcessHandle processHandle, out int win32Error)
    {
        if (TerminateProcess(processHandle, 1)) { win32Error = 0; return true; }
        win32Error = Marshal.GetLastWin32Error();
        return false;
    }

    public bool WaitForExit(SafeProcessHandle processHandle, TimeSpan timeout) =>
        WaitForSingleObject(processHandle, (uint)timeout.TotalMilliseconds) == WAIT_OBJECT_0;

    private const uint WAIT_TIMEOUT = 0x102;

    public LiveProcessProbeStatus PollLiveness(SafeProcessHandle processHandle)
    {
        var result = WaitForSingleObject(processHandle, 0);
        if (result == WAIT_OBJECT_0) return LiveProcessProbeStatus.Exited;
        if (result == WAIT_TIMEOUT) return LiveProcessProbeStatus.Alive;
        // WAIT_FAILED (or any other unexpected return) -- deliberately never conflated with either
        // Alive or Exited; the caller must fail open.
        return LiveProcessProbeStatus.Uncertain;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(
        string? lpApplicationName, System.Text.StringBuilder lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeHandle jobHandle, int jobObjectInfoClass, IntPtr jobObjectInfo, uint jobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeHandle jobHandle, SafeProcessHandle processHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(SafeHandle threadHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(SafeProcessHandle processHandle, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
}
