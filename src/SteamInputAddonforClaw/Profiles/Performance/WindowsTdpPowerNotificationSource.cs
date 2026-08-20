using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.Profiles.Performance;

internal sealed class WindowsTdpPowerNotificationSource : ITdpPowerNotificationSource
{
    private static readonly Guid AcDcPowerSource = new("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");
    private readonly DeviceNotifyCallbackRoutine _callback;
    private nint _powerSettingRegistration;
    private nint _suspendRegistration;

    internal WindowsTdpPowerNotificationSource() => _callback = OnNativeNotification;

    public event Action<TdpPowerNotification>? Notification;

    public bool TryRegister(out int nativeError)
    {
        nativeError = 0;
        var parameters = new DeviceNotifySubscribeParameters
        {
            Callback = Marshal.GetFunctionPointerForDelegate(_callback),
            Context = 0
        };
        var settingGuid = AcDcPowerSource;
        var powerResult = PowerSettingRegisterNotification(ref settingGuid, DeviceNotifyCallback,
            ref parameters, out _powerSettingRegistration);
        if (powerResult != 0)
        {
            nativeError = unchecked((int)powerResult);
            return false;
        }

        var suspendResult = PowerRegisterSuspendResumeNotification(DeviceNotifyCallback, ref parameters, out _suspendRegistration);
        if (suspendResult != 0)
        {
            nativeError = unchecked((int)suspendResult);
            Dispose();
            return false;
        }
        return true;
    }

    private uint OnNativeNotification(nint context, uint type, nint setting)
    {
        try
        {
            if (type == 4) Notification?.Invoke(TdpPowerNotification.Suspend);
            else if (type == 18) Notification?.Invoke(TdpPowerNotification.ResumeAutomatic);
            else if (type == 7) Notification?.Invoke(TdpPowerNotification.ResumeSuspend);
            else Notification?.Invoke(TdpPowerNotification.PowerSourceChanged);
        }
        catch { }
        return 0;
    }

    public void Dispose()
    {
        var power = Interlocked.Exchange(ref _powerSettingRegistration, 0);
        if (power != 0) _ = PowerSettingUnregisterNotification(power);
        var suspend = Interlocked.Exchange(ref _suspendRegistration, 0);
        if (suspend != 0) _ = PowerUnregisterSuspendResumeNotification(suspend);
    }

    private const uint DeviceNotifyCallback = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceNotifySubscribeParameters
    {
        public nint Callback;
        public nint Context;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint DeviceNotifyCallbackRoutine(nint context, uint type, nint setting);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint PowerSettingRegisterNotification(ref Guid settingGuid, uint flags,
        ref DeviceNotifySubscribeParameters recipient, out nint handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PowerSettingUnregisterNotification(nint handle);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerRegisterSuspendResumeNotification(uint flags,
        ref DeviceNotifySubscribeParameters recipient, out nint registration);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerUnregisterSuspendResumeNotification(nint registration);
}
