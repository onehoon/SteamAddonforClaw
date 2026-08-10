namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal static class MsiClawModeCommand
{
    internal const byte SwitchMode = 0x24;
    internal static byte[] Build(MsiClawNativeMode mode)
    {
        var report = new byte[64]; report[0] = 0x0F; report[3] = 0x3C; report[4] = SwitchMode; report[5] = mode switch { MsiClawNativeMode.XInput => 0x01, MsiClawNativeMode.DirectInput => 0x02, _ => throw new ArgumentOutOfRangeException(nameof(mode)) }; return report;
    }
}
