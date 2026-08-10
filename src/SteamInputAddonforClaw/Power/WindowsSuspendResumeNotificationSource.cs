using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.Power;

internal sealed class WindowsSuspendResumeNotificationSource : IPowerSuspendResumeNotificationSource
{
    private const uint DeviceNotifyCallback = 2;
    private readonly DeviceNotifyCallbackRoutine _callback;
    private nint _registration;
    public event Action<uint>? Notification;
    public WindowsSuspendResumeNotificationSource() => _callback = OnNativeNotification;
    public bool TryRegister(out int nativeError)
    {
        var parameters = new DeviceNotifySubscribeParameters { Callback = Marshal.GetFunctionPointerForDelegate(_callback), Context = 0 };
        var result = PowerRegisterSuspendResumeNotification(GetCurrentProcess(), DeviceNotifyCallback, ref parameters, out _registration);
        nativeError = result ? 0 : Marshal.GetLastWin32Error(); return result;
    }
    private uint OnNativeNotification(nint context, uint type, nint setting) { try { Notification?.Invoke(type); } catch { } return 0; }
    public void Dispose() { var registration = Interlocked.Exchange(ref _registration, 0); if (registration != 0) PowerUnregisterSuspendResumeNotification(registration); }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceNotifySubscribeParameters { public nint Callback; public nint Context; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint DeviceNotifyCallbackRoutine(nint context, uint type, nint setting);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("powrprof.dll", SetLastError = true)] private static extern bool PowerRegisterSuspendResumeNotification(nint recipient, uint flags, ref DeviceNotifySubscribeParameters parameters, out nint registration);
    [DllImport("powrprof.dll", SetLastError = true)] private static extern bool PowerUnregisterSuspendResumeNotification(nint registration);
}
