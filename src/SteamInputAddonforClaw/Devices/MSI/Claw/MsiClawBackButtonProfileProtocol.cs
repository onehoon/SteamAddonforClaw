using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal static class MsiClawBackButtonProfileProtocol
{
    internal const int ReportLength = 64;
    internal const byte ReadIndex = 0x01;
    internal const byte PayloadLength = 0x08;
    internal const ushort M1Address = 0x00BA;
    internal const ushort M2Address = 0x0163;
    // The observed PID1902 response echoes index/address/length at bytes 5..8 and begins
    // the requested profile block at byte 9. The first profile byte is not a status byte.
    private const int PayloadOffset = 9;

    internal enum ParseOutcome { ReadbackParsed, UnexpectedReport, WrongAddressOrIndex }

    internal readonly record struct ParsedReadback(
        byte[] Payload,
        FrontendMsiBackButtonProfileClassification Classification);

    internal static IReadOnlyList<byte[]> BuildSlotReadRequests()
    {
        return [BuildReadRequest(M1Address), BuildReadRequest(M2Address)];
    }

    internal static byte[] BuildReadRequest(ushort address)
    {
        if (address is not (M1Address or M2Address))
            throw new ArgumentOutOfRangeException(nameof(address));

        var request = new byte[ReportLength];
        request[0] = 0x0F;
        request[3] = 0x3C;
        request[4] = 0x04;
        request[5] = ReadIndex;
        request[6] = (byte)(address >> 8);
        request[7] = (byte)address;
        request[8] = PayloadLength;
        return request;
    }

    internal static ParseOutcome ParseReadResponse(
        ReadOnlySpan<byte> response,
        ushort requestedAddress,
        out ParsedReadback parsed)
    {
        parsed = default;
        if (response.Length != ReportLength
            || response[0] != 0x10
            || response[3] != 0x3C
            || response[4] != 0x05
            || response.Length < PayloadOffset + PayloadLength)
            return ParseOutcome.UnexpectedReport;

        var address = (ushort)((response[6] << 8) | response[7]);
        if (response[5] != ReadIndex
            || address != requestedAddress
            || response[8] != PayloadLength)
            return ParseOutcome.WrongAddressOrIndex;

        var payload = response.Slice(PayloadOffset, PayloadLength).ToArray();
        var classification = ClassifyPayload(payload, requestedAddress);
        parsed = new(payload, classification);
        return ParseOutcome.ReadbackParsed;
    }

    internal static FrontendMsiBackButtonProfileClassification ClassifyPayload(ReadOnlySpan<byte> payload, ushort address)
    {
        if (payload.Length != PayloadLength)
            return FrontendMsiBackButtonProfileClassification.OtherFormat;

        if (address == M1Address
            && payload[0] == 0x01 && payload[1] == 0x04 && payload[2] == 0x00 && payload[3] == 0x59
            && payload[4] == 0xFF && payload[5] == 0xFF && payload[6] == 0xFF && payload[7] == 0xFF)
            return FrontendMsiBackButtonProfileClassification.ObservedLinuxRightBraceCandidate;
        if (address == M2Address
            && payload[0] == 0x01 && payload[1] == 0x04 && payload[2] == 0x00 && payload[3] == 0x58
            && payload[4] == 0xFF && payload[5] == 0xFF && payload[6] == 0xFF && payload[7] == 0xFF)
            return FrontendMsiBackButtonProfileClassification.ObservedLinuxLeftBraceCandidate;
        if (payload[0] == 0x01 && payload[1] == 0x04 && payload[2] == 0x0C)
            return FrontendMsiBackButtonProfileClassification.HistoricalMarkerPresent;
        if (payload[0] == 0x01 && payload[1] == 0x04)
            return FrontendMsiBackButtonProfileClassification.HistoricalMarkerAbsent;
        return FrontendMsiBackButtonProfileClassification.OtherFormat;
    }

    internal static byte[] BuildDirectInputRestoreWriteRequest(ushort address)
    {
        if (address is not (M1Address or M2Address))
            throw new ArgumentOutOfRangeException(nameof(address));

        var request = new byte[ReportLength];
        request[0] = 0x0F;
        request[3] = 0x3C;
        request[4] = 0x21;
        request[5] = ReadIndex;
        request[6] = (byte)(address >> 8);
        request[7] = (byte)address;
        request[8] = 0x02;
        request[9] = 0x01;
        request[10] = 0x00;
        return request;
    }

    internal static byte[] BuildSyncToRomRequest()
    {
        var request = new byte[ReportLength];
        request[0] = 0x0F;
        request[3] = 0x3C;
        request[4] = 0x22;
        return request;
    }
}
