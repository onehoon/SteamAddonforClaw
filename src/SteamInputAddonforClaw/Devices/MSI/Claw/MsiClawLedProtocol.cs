using SteamInputAddonforClaw.Contracts.ControllerLed;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal static class MsiClawLedProtocol
{
    private const int ReportLength = 64;
    private const int FrameLength = 27;

    internal static bool TryResolveRgbAddress(ushort firmwareVersion, out ushort address)
    {
        address = firmwareVersion switch
        {
            0x0163 or 0x0211 => 0x01FA,
            0x0166 or 0x0167 or 0x0217 or 0x0219 or 0x0308 or 0x0411 or 0x0414 => 0x024A,
            _ => 0
        };
        return address != 0;
    }

    internal static bool TryBuildStaticWrites(
        ushort firmwareVersion,
        ControllerLedSettings settings,
        out IReadOnlyList<byte[]> writes)
    {
        writes = [];
        if (ControllerLedSettingsValidation.Validate(settings) is not null
            || !TryResolveRgbAddress(firmwareVersion, out var address))
            return false;

        var frame = new byte[FrameLength];
        for (var zone = 0; zone < 9; zone++)
        {
            frame[zone * 3] = settings.Red;
            frame[zone * 3 + 1] = settings.Green;
            frame[zone * 3 + 2] = settings.Blue;
        }

        var header = new byte[ReportLength];
        header[0] = 0x0F;
        header[3] = 0x3C;
        header[4] = 0x21;
        header[5] = 0x01;
        header[6] = (byte)(address >> 8);
        header[7] = (byte)address;
        header[8] = 0x20;
        header[9] = 0x00;
        header[10] = 0x01;
        header[11] = 0x09;
        header[12] = 0x03;
        header[13] = settings.Enabled ? (byte)settings.Brightness : (byte)0;
        frame.CopyTo(header, 14);

        var packets = new List<byte[]>(4) { header };
        foreach (var offset in new[] { 32, 59, 86 })
        {
            var packet = new byte[ReportLength];
            packet[0] = 0x0F;
            packet[3] = 0x3C;
            packet[4] = 0x21;
            packet[5] = 0x01;
            var target = checked(address + offset);
            packet[6] = (byte)(target >> 8);
            packet[7] = (byte)target;
            packet[8] = FrameLength;
            frame.CopyTo(packet, 9);
            packets.Add(packet);
        }

        writes = packets;
        return true;
    }
}
