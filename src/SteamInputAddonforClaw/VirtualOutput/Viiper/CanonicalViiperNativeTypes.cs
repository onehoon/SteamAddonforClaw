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

// Mirrors VIIPER's native Steam Deck output callback typedef (VIIPER
// main@0b3627317d2008065d8ec231f94bf31af7527bbd): invoked by libVIIPER with the owning device
// handle, a pointer to the output report payload, and its length.
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

// Mirrors the generated dist/libVIIPER/libVIIPER.h SteamDeckDeviceState struct from VIIPER
// main@522d573f67a693500ef96174aef318f62e8caeef field-for-field, in the exact declared order.
// This is the complete generic canonical Steam Deck state (72 bytes) -- including
// trackpad/IMU/rear-button fields the Addon does not yet populate -- not an MSI Claw-specific
// subset. Do not remove fields merely because SD2's mapper currently sends them neutral.
//
// The canonical ABI ends at LPadForce (offset 68) / RPadForce (offset 70). VIIPER 522d573
// removed the non-canonical LStickForce/RStickForce tail fields present in earlier revisions --
// they had no corresponding field in the declared Valve/SDL/Linux Steam Deck payload. Do not
// reintroduce them; CanonicalViiperNativeAbiTests pins both the 72-byte size and the corrected
// tail offsets.
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
}
