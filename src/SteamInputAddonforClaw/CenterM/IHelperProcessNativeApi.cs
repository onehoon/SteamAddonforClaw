using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SteamInputAddonforClaw.CenterM;

/// <summary>Native Win32 primitives needed for CREATE_SUSPENDED + private Job Object helper
/// ownership, isolated behind an interface so <see cref="CenterMHelperOwnership"/>'s mandatory
/// call ordering and failure-cleanup behavior can be unit tested deterministically without
/// touching the real OS.</summary>
internal interface IHelperProcessNativeApi
{
    bool TryCreateSuspended(string imagePath, out int processId, out SafeProcessHandle? processHandle, out SafeHandle? threadHandle, out int win32Error);
    bool TryCreateJobObject(out SafeHandle? jobHandle, out int win32Error);
    bool TrySetKillOnJobClose(SafeHandle jobHandle, out int win32Error);
    bool TryAssignProcessToJob(SafeHandle jobHandle, SafeProcessHandle processHandle, out int win32Error);
    bool TryResumeThread(SafeHandle threadHandle, out int win32Error);
    bool TryTerminate(SafeProcessHandle processHandle, out int win32Error);
    bool WaitForExit(SafeProcessHandle processHandle, TimeSpan timeout);
}
