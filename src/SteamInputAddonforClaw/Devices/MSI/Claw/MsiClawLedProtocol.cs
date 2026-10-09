using SteamInputAddonforClaw.Contracts.ControllerLed;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal static class MsiClawLedProtocol
{
    private const int ReportLength = 64;
    private const int FrameLength = 27;
    internal const ushort A2vm230CandidateRgbAddress = 0x024A;

    internal enum CandidateReadParseOutcome { CandidateReadbackParsed, UnexpectedReport, WrongAddressOrIndex }

    internal readonly record struct CandidateReadback(byte Effect, byte Speed, byte Brightness, byte[] RgbBytes);

    internal static byte[] BuildA2vm230CandidateProfileReadRequest()
    {
        var request = new byte[ReportLength];
        request[0] = 0x0F;
        request[3] = 0x3C;
        request[4] = 0x04;
        request[5] = 0x01;
        request[6] = (byte)(A2vm230CandidateRgbAddress >> 8);
        request[7] = (byte)(A2vm230CandidateRgbAddress & 0xFF);
        request[8] = 0x20;
        return request;
    }

    internal static CandidateReadParseOutcome ParseA2vm230CandidateProfileReadResponse(
        ReadOnlySpan<byte> response,
        out CandidateReadback readback)
    {
        readback = default;
        if (response.Length != ReportLength
            || response[0] != 0x10
            || response[3] != 0x3C
            || response[4] != 0x05
            || response[9] != 0x00
            || response[11] != 0x09)
            return CandidateReadParseOutcome.UnexpectedReport;

        var address = (ushort)((response[6] << 8) | response[7]);
        if (response[5] != 0x01
            || address != A2vm230CandidateRgbAddress
            || response[8] != 0x20)
            return CandidateReadParseOutcome.WrongAddressOrIndex;

        readback = new(response[10], response[12], response[13], response[14..41].ToArray());
        return CandidateReadParseOutcome.CandidateReadbackParsed;
    }

    internal static bool TryResolveRgbAddress(ushort firmwareVersion, out ushort address)
    {
        address = firmwareVersion switch
        {
            0x0163 or 0x0211 => 0x01FA,
            0x0166 or 0x0167 or 0x0217 or 0x0219 or 0x0230 or 0x0308 or 0x0411 or 0x0414 or 0x0419 => 0x024A,
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
