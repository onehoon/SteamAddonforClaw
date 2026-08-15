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

// Distinct managed delegate type from SteamControllerOutputCallback even though the shape is
// identical -- Steam Deck and Steam Controller are separate typed devices in VIIPER with their own
// native callback typedefs (VIIPER main@0b3627317d2008065d8ec231f94bf31af7527bbd), and keeping the
// managed delegates separate avoids silently coupling the two typed callback lifecycles together.
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void SteamDeckOutputCallback(nuint handle, nint data, uint length);

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

// Mirrors the generated dist/libVIIPER/libVIIPER.h SteamDeckDeviceState struct from VIIPER
// main@ec64282c69e5587466b950332d7983fd53a7d778 (PR #16) field-for-field, in the exact declared
// order. This is the complete generic canonical Steam Deck state (76 bytes) -- including
// trackpad/IMU/rear-button fields the Addon does not yet populate -- not an MSI Claw-specific
// subset. Do not remove fields merely because SD2's mapper currently sends them neutral.
[StructLayout(LayoutKind.Sequential)]
internal struct SteamDeckDeviceState
{
    internal byte A;
    internal byte X;
    internal byte B;
    internal byte Y;
    internal byte L1;
    internal byte R1;
    internal byte L2Digital;
    internal byte R2Digital;
    internal byte L5;
    internal byte Menu;
    internal byte Steam;
    internal byte Options;
    internal byte DPadDown;
    internal byte DPadLeft;
    internal byte DPadRight;
    internal byte DPadUp;
    internal byte L3;
    internal byte RPadTouch;
    internal byte LPadTouch;
    internal byte RPadPress;
    internal byte LPadPress;
    internal byte R5;
    internal byte R3;
    internal byte RStickTouch;
    internal byte LStickTouch;
    internal byte R4;
    internal byte L4;
    internal byte QuickAccess;
    internal short LPadX;
    internal short LPadY;
    internal short RPadX;
    internal short RPadY;
    internal short AccelX;
    internal short AccelY;
    internal short AccelZ;
    internal short Pitch;
    internal short Yaw;
    internal short Roll;
    internal short GyroQuatW;
    internal short GyroQuatX;
    internal short GyroQuatY;
    internal short GyroQuatZ;
    internal ushort LTrigger;
    internal ushort RTrigger;
    internal short LStickX;
    internal short LStickY;
    internal short RStickX;
    internal short RStickY;
    internal ushort LPadForce;
    internal ushort RPadForce;
    internal ushort LStickForce;
    internal ushort RStickForce;
}
