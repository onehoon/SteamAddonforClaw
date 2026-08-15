namespace SteamInputAddonforClaw.Diagnostics.GordonDPad;

/// <summary>Why a raw Windows HID report could not be interpreted as a Gordon controller input report.</summary>
internal enum GordonHidReportRejectionReason
{
    TooShort,
    UnexpectedReportId,
}

/// <summary>
/// The result of parsing one raw Windows HID input report from the Addon-owned Gordon device: either the
/// three relevant payload bytes (matching VIIPER's <c>buildReport</c> byte layout -- <c>Byte8</c> is the
/// A/X/B/Y/L1/R1/L2/R2 bitmask, <c>Byte9</c> carries D-pad + Menu/Steam/Options/LGrip, <c>Byte10</c> carries
/// the remaining buttons), or why the report was rejected.
/// </summary>
internal readonly record struct GordonHidReportParseResult(bool Accepted, byte ReportId, byte Byte8, byte Byte9, byte Byte10, GordonHidReportRejectionReason? RejectionReason)
{
    internal byte DPadMask => (byte)(Byte9 & 0x0F);

    internal static GordonHidReportParseResult Rejected(GordonHidReportRejectionReason reason, byte reportId = 0) => new(false, reportId, 0, 0, 0, reason);
    internal static GordonHidReportParseResult Ok(byte reportId, byte byte8, byte byte9, byte byte10) => new(true, reportId, byte8, byte9, byte10, null);
}

/// <summary>
/// Pure parsing of the Addon-owned Gordon's 64-byte Windows HID input report -- the same byte layout
/// VIIPER's Go <c>buildReport</c> produces (<c>report[0]</c> is Gordon's own internal report-id marker,
/// not a Windows HID protocol report ID; the descriptor declares a single unnumbered report, so
/// <c>ReadFile</c> returns exactly <see cref="ExpectedLength"/> bytes with no separate leading byte
/// injected by Windows). No I/O, no state -- safe to call from any thread, any number of times.
/// </summary>
internal static class GordonHidReportParser
{
    internal const int ExpectedLength = 64;
    internal const byte ExpectedReportId = 0x01;

    internal static GordonHidReportParseResult Parse(ReadOnlySpan<byte> report)
    {
        if (report.Length < ExpectedLength) return GordonHidReportParseResult.Rejected(GordonHidReportRejectionReason.TooShort);
        if (report[0] != ExpectedReportId) return GordonHidReportParseResult.Rejected(GordonHidReportRejectionReason.UnexpectedReportId, report[0]);
        return GordonHidReportParseResult.Ok(report[0], report[8], report[9], report[10]);
    }
}
