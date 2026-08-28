using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal static class PublisherThreadQoS
{
    private const uint ThreadPowerThrottlingExecutionSpeed = 0x1;

    internal static Func<PublisherThreadQoSRequest, (bool Succeeded, int Win32Error)>? NativeCallOverrideForTests { get; set; }

    internal static bool ApplyHighQoS(string publisher)
    {
        var request = new PublisherThreadQoSRequest(
            ThreadInformationClass.ThreadPowerThrottling,
            ThreadPowerThrottlingExecutionSpeed,
            StateMask: 0);

        try
        {
            var result = NativeCallOverrideForTests?.Invoke(request) ?? SetNativeThreadInformation(request);
            if (result.Succeeded)
            {
                AppLog.Debug("SteamOutput", "Publisher worker HighQoS enabled.", ("Publisher", publisher));
                return true;
            }

            AppLog.Warn("SteamOutput", "Publisher worker HighQoS request failed; continuing with existing scheduler.",
                fields: [("Publisher", publisher), ("Win32Error", result.Win32Error)]);
            return false;
        }
        catch (Exception exception)
        {
            AppLog.Warn("SteamOutput", "Publisher worker HighQoS request failed; continuing with existing scheduler.",
                exception, ("Publisher", publisher));
            return false;
        }
    }

    private static (bool Succeeded, int Win32Error) SetNativeThreadInformation(PublisherThreadQoSRequest request)
    {
        var state = new ThreadPowerThrottlingState
        {
            Version = 1,
            ControlMask = request.ControlMask,
            StateMask = request.StateMask,
        };
        var succeeded = SetThreadInformation(
            GetCurrentThread(),
            request.ThreadInformationClass,
            ref state,
            (uint)Marshal.SizeOf<ThreadPowerThrottlingState>());
        return (succeeded, succeeded ? 0 : Marshal.GetLastWin32Error());
    }

    internal readonly record struct PublisherThreadQoSRequest(
        ThreadInformationClass ThreadInformationClass,
        uint ControlMask,
        uint StateMask);

    internal enum ThreadInformationClass
    {
        ThreadMemoryPriority = 0,
        ThreadAbsoluteCpuPriority = 1,
        ThreadDynamicCodePolicy = 2,
        ThreadPowerThrottling = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ThreadPowerThrottlingState
    {
        internal uint Version;
        internal uint ControlMask;
        internal uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetThreadInformation(
        IntPtr hThread,
        ThreadInformationClass threadInformationClass,
        ref ThreadPowerThrottlingState threadInformation,
        uint threadInformationSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();
}
