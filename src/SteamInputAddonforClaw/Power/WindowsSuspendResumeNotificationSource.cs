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
        var result = PowerRegisterSuspendResumeNotification(DeviceNotifyCallback, ref parameters, out _registration);
        nativeError = unchecked((int)result); return result == 0;
    }
    private uint OnNativeNotification(nint context, uint type, nint setting) { try { Notification?.Invoke(type); } catch { } return 0; }
    public void Dispose() { var registration = Interlocked.Exchange(ref _registration, 0); if (registration != 0) _ = PowerUnregisterSuspendResumeNotification(registration); }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceNotifySubscribeParameters { public nint Callback; public nint Context; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint DeviceNotifyCallbackRoutine(nint context, uint type, nint setting);
    [DllImport("powrprof.dll", SetLastError = true)] private static extern uint PowerRegisterSuspendResumeNotification(uint flags, ref DeviceNotifySubscribeParameters recipient, out nint registration);
    [DllImport("powrprof.dll", SetLastError = true)] private static extern uint PowerUnregisterSuspendResumeNotification(nint registration);
}
