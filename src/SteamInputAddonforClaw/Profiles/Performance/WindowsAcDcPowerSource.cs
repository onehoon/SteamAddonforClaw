using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.Profiles.Performance;

internal enum AcDcPowerSource { AC, DC }

internal static class WindowsAcDcPowerSource
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    internal static AcDcPowerSource? Read() =>
        !GetSystemPowerStatus(out var status)
            ? null
            : status.ACLineStatus switch
            {
                1 => AcDcPowerSource.AC,
                0 => AcDcPowerSource.DC,
                _ => null,
            };
}

/// <summary>One process-owned, event-driven AC/DC notification source. It reports only power-source
/// changes; suspend/resume remains owned by the existing lifecycle paths.</summary>
internal sealed class WindowsAcDcPowerNotificationSource : IDisposable
{
    private static readonly Guid AcDc = new("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");
    private readonly DeviceNotifyCallbackRoutine _callback;
    private nint _registration;

    internal event Action? Changed;

    internal WindowsAcDcPowerNotificationSource() => _callback = OnNotification;

    internal bool TryRegister()
    {
        var guid = AcDc;
        var parameters = new Parameters
        {
            Callback = Marshal.GetFunctionPointerForDelegate(_callback),
        };
        var result = PowerSettingRegisterNotification(ref guid, 2, ref parameters, out _registration);
        return result == 0;
    }

    private uint OnNotification(nint context, uint type, nint setting)
    {
        if (type != 4 && type != 7 && type != 18)
            Changed?.Invoke();
        return 0;
    }

    public void Dispose()
    {
        var registration = Interlocked.Exchange(ref _registration, 0);
        if (registration != 0)
            _ = PowerSettingUnregisterNotification(registration);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Parameters
    {
        public nint Callback;
        public nint Context;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint DeviceNotifyCallbackRoutine(nint context, uint type, nint setting);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSettingRegisterNotification(ref Guid guid, uint flags, ref Parameters recipient, out nint handle);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSettingUnregisterNotification(nint handle);
}
