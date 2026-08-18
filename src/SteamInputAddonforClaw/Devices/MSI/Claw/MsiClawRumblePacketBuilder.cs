using SteamInputAddonforClaw.Feedback;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal static class MsiClawRumblePacketBuilder
{
    internal static byte[] Build(TwoMotorRumble rumble)
        => [0x05, 0x01, 0x00, 0x00, ToPhysicalByte(rumble.SmallMotor), ToPhysicalByte(rumble.LargeMotor), 0x00, 0x00, 0x00, 0x00, 0x00];

    internal static byte ToPhysicalByte(ushort motor) => (byte)(motor >> 8);
}
