using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SteamInputAddonforClaw.CenterM;

internal enum LiveProcessProbeStatus { Alive, Exited, Uncertain }

internal readonly record struct LiveProcessIdentity(LiveProcessProbeStatus Status, int? ProcessId, string? ProcessName, string? ExecutablePath);

/// <summary>Inspects a retained process handle directly (GetProcessId, WaitForSingleObject(0),
/// QueryFullProcessImageName) rather than re-querying by PID, so a fresh identity check is always
/// anchored to the exact kernel object <see cref="TrackedCenterMMainUi"/> retained -- immune to PID
/// reuse. Isolated behind an interface so <see cref="SafeMainUiTerminator"/>'s fresh-capture
/// termination logic can be unit tested deterministically.</summary>
internal interface IProcessIdentityInspector
{
    LiveProcessIdentity Inspect(SafeProcessHandle handle);
}

internal sealed class Win32ProcessIdentityInspector : IProcessIdentityInspector
{
    private const uint WAIT_OBJECT_0 = 0;
    private const uint WAIT_TIMEOUT = 0x102;

    public LiveProcessIdentity Inspect(SafeProcessHandle handle)
    {
        if (handle.IsInvalid) return new LiveProcessIdentity(LiveProcessProbeStatus.Uncertain, null, null, null);

        var waitResult = WaitForSingleObject(handle, 0);
        if (waitResult == WAIT_OBJECT_0) return new LiveProcessIdentity(LiveProcessProbeStatus.Exited, null, null, null);
        if (waitResult != WAIT_TIMEOUT) return new LiveProcessIdentity(LiveProcessProbeStatus.Uncertain, null, null, null);

        var processId = GetProcessId(handle);
        if (processId == 0) return new LiveProcessIdentity(LiveProcessProbeStatus.Uncertain, null, null, null);

        var path = QueryImagePath(handle);
        var name = path is null ? null : System.IO.Path.GetFileNameWithoutExtension(path);
        return new LiveProcessIdentity(LiveProcessProbeStatus.Alive, (int)processId, name, path);
    }

    private static string? QueryImagePath(SafeProcessHandle handle)
    {
        var buffer = new StringBuilder(1024);
        var size = (uint)buffer.Capacity;
        return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString() : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetProcessId(SafeProcessHandle handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle handle, uint flags, StringBuilder buffer, ref uint size);
}
