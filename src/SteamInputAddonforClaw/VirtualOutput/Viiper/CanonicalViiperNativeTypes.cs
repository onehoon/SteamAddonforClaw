using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal enum ViiperLogLevel : int
{
    Debug = -4,
    Info = 0,
    Warn = 4,
    Error = 8
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void ViiperLogCallback(ViiperLogLevel level, nint message);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void SteamControllerOutputCallback(nuint handle, nint data, uint length);

[StructLayout(LayoutKind.Sequential)]
internal struct USBServerConfig
{
    internal nint Addr;
    internal ulong ConnectionTimeoutMs;
    internal ulong DeviceHandlerConnectTimeoutMs;
    internal uint WriteBatchFlushIntervalMs;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SteamControllerDeviceState
{
    internal byte A;
    internal byte X;
    internal byte B;
    internal byte Y;
    internal byte L1;
    internal byte R1;
    internal byte L2;
    internal byte R2;
    internal byte Menu;
    internal byte Steam;
    internal byte Options;
    internal byte DPadDown;
    internal byte DPadLeft;
    internal byte DPadRight;
    internal byte DPadUp;
    internal byte L3;
    internal byte LGrip;
    internal byte RGrip;
    internal byte LPadTouch;
    internal byte RPadTouch;
    internal byte LPadPress;
    internal byte RPadPress;
    internal byte LPadAndStick;
    internal short LPadX;
    internal short LPadY;
    internal short RPadX;
    internal short RPadY;
    internal ushort LTrigger;
    internal ushort RTrigger;
    internal short LStickX;
    internal short LStickY;
    internal short AccelX;
    internal short AccelY;
    internal short AccelZ;
    internal short GyroX;
    internal short GyroY;
    internal short GyroZ;
    internal short GyroQuatW;
    internal short GyroQuatX;
    internal short GyroQuatY;
    internal short GyroQuatZ;
    internal ushort BatteryMilliVolts;
}
