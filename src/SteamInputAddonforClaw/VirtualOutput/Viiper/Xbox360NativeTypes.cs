using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

// Mirrors the generated dist/libVIIPER/libVIIPER.h Xbox360DeviceState struct from VIIPER
// main@e10b5f02945b1322f33c33468e583546600ba000 field-for-field, in the exact declared order.
// Preparation only: no Xbox360 native API delegates, exports, or P/Invoke calls exist yet -- see
// Xbox360DeviceStateMapper. This type is not consumed by CanonicalViiperNativeApi.
[StructLayout(LayoutKind.Sequential)]
internal struct Xbox360DeviceState
{
    internal uint Buttons;
    internal byte LT;
    internal byte RT;
    internal short LX;
    internal short LY;
    internal short RX;
    internal short RY;

    // Six inline value fields rather than a byte[]: a reference-type array field would make this
    // struct non-blittable and force a fresh managed allocation on every Map() call -- a real cost
    // once a future publisher calls this at Steam Deck-like report rates. Inline fields keep the
    // struct fully blittable and allocation-free, and give it a genuinely zero-valued default (no
    // explicit initialization needed for a neutral state).
    internal byte Reserved0;
    internal byte Reserved1;
    internal byte Reserved2;
    internal byte Reserved3;
    internal byte Reserved4;
    internal byte Reserved5;
}

// Bit values from the generated header's XBOX360_BUTTON_* defines. The lower 16 bits are used;
// higher bits are reserved by the native ABI. Guide (0x0400) is intentionally never set by the
// mapper -- the Addon does not expose a Guide-equivalent control.
internal static class Xbox360ButtonBits
{
    internal const uint DPadUp = 0x0001u;
    internal const uint DPadDown = 0x0002u;
    internal const uint DPadLeft = 0x0004u;
    internal const uint DPadRight = 0x0008u;
    internal const uint Start = 0x0010u;
    internal const uint Back = 0x0020u;
    internal const uint LeftThumb = 0x0040u;
    internal const uint RightThumb = 0x0080u;
    internal const uint LeftShoulder = 0x0100u;
    internal const uint RightShoulder = 0x0200u;
    internal const uint Guide = 0x0400u;
    internal const uint A = 0x1000u;
    internal const uint B = 0x2000u;
    internal const uint X = 0x4000u;
    internal const uint Y = 0x8000u;
}
