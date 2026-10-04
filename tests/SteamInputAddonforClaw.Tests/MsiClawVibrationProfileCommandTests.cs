using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawVibrationProfileCommandTests
{
    [Fact]
    public void Read_profile_builds_exact_left_and_right_64_byte_frames()
    {
        Assert.Equal(Expected(0x04, 0x01, 0x0022, 0x01), MsiClawVibrationProfileCommand.BuildReadProfile(0x0022));
        Assert.Equal(Expected(0x04, 0x01, 0x0023, 0x01), MsiClawVibrationProfileCommand.BuildReadProfile(0x0023));
    }

    [Fact]
    public void Diagnostic_reads_build_exact_dual_index_two_byte_frames()
    {
        var index0 = MsiClawVibrationProfileCommand.BuildDiagnosticReadProfile(0x00, 0x0022, 0x02);
        var index1 = MsiClawVibrationProfileCommand.BuildDiagnosticReadProfile(0x01, 0x0022, 0x02);

        Assert.Equal(Expected(0x04, 0x00, 0x0022, 0x02), index0);
        Assert.Equal(Expected(0x04, 0x01, 0x0022, 0x02), index1);
        Assert.Equal(new byte[] { 0x0F, 0x00, 0x00, 0x3C, 0x04, 0x00, 0x00, 0x22, 0x02 }, index0[..9]);
        Assert.Equal(new byte[] { 0x0F, 0x00, 0x00, 0x3C, 0x04, 0x01, 0x00, 0x22, 0x02 }, index1[..9]);
    }

    [Theory]
    [InlineData(0x0022)]
    [InlineData(0x0023)]
    public void Write_profile_builds_exact_frame(ushort address)
    {
        var expected = Expected(0x21, 0x01, address, 0x01);
        expected[9] = 70;

        Assert.Equal(expected, MsiClawVibrationProfileCommand.BuildWriteProfile(address, 70));
    }

    [Fact]
    public void Sync_to_rom_builds_exact_frame()
    {
        var expected = new byte[64];
        expected[0] = 0x0F;
        expected[3] = 0x3C;
        expected[4] = 0x22;

        Assert.Equal(expected, MsiClawVibrationProfileCommand.BuildSyncToRom());
    }

    [Theory]
    [InlineData(0x0022, 35)]
    [InlineData(0x0023, 70)]
    public void Parser_accepts_matching_profile_response(ushort address, int value)
    {
        Assert.True(MsiClawVibrationProfileCommand.TryParseReadProfileResponse(Response(address, value), address, out var parsed));
        Assert.Equal(value, parsed);
    }

    [Theory]
    [InlineData(0x00, 30, 70)]
    [InlineData(0x01, 40, 60)]
    public void Diagnostic_parser_accepts_two_byte_response_for_each_requested_index(
        byte profileIndex,
        int left,
        int right)
    {
        var report = DiagnosticResponse(profileIndex, 0x0022, 0x02, left, right);

        Assert.True(MsiClawVibrationProfileCommand.TryParseDiagnosticReadProfileResponse(
            report, profileIndex, 0x0022, 0x02, out var parsed));
        Assert.Equal(profileIndex, parsed.ResponseIndex);
        Assert.True(parsed.IndexEchoMatched);
        Assert.Equal(left, parsed.CandidateLeft);
        Assert.Equal(right, parsed.CandidateRight);
    }

    [Fact]
    public void Diagnostic_parser_keeps_non_echo_index_as_structurally_valid_evidence()
    {
        var report = DiagnosticResponse(0x01, 0x0022, 0x02, 30, 70);

        Assert.True(MsiClawVibrationProfileCommand.TryParseDiagnosticReadProfileResponse(
            report, 0x00, 0x0022, 0x02, out var parsed));
        Assert.Equal(0x01, parsed.ResponseIndex);
        Assert.False(parsed.IndexEchoMatched);
        Assert.Equal(30, parsed.CandidateLeft);
        Assert.Equal(70, parsed.CandidateRight);
    }

    [Fact]
    public void Diagnostic_parser_rejects_wrong_address_length_values_and_invalid_reports()
    {
        var valid = DiagnosticResponse(0x00, 0x0022, 0x02, 30, 70);

        var wrongAddress = (byte[])valid.Clone();
        wrongAddress[7] = 0x23;
        Assert.False(MsiClawVibrationProfileCommand.TryParseDiagnosticReadProfileResponse(
            wrongAddress, 0x00, 0x0022, 0x02, out _));

        var wrongLength = (byte[])valid.Clone();
        wrongLength[8] = 0x01;
        Assert.False(MsiClawVibrationProfileCommand.TryParseDiagnosticReadProfileResponse(
            wrongLength, 0x00, 0x0022, 0x02, out _));

        var outOfRange = (byte[])valid.Clone();
        outOfRange[9] = 101;
        Assert.False(MsiClawVibrationProfileCommand.TryParseDiagnosticReadProfileResponse(
            outOfRange, 0x00, 0x0022, 0x02, out _));

        Assert.False(MsiClawVibrationProfileCommand.TryParseDiagnosticReadProfileResponse(
            valid[..^1], 0x00, 0x0022, 0x02, out _));

        var invalidMarker = (byte[])valid.Clone();
        invalidMarker[4] = 0x04;
        Assert.False(MsiClawVibrationProfileCommand.TryParseDiagnosticReadProfileResponse(
            invalidMarker, 0x00, 0x0022, 0x02, out _));
    }

    [Fact]
    public void Strict_production_parser_still_rejects_index_zero_and_two_byte_responses()
    {
        Assert.False(MsiClawVibrationProfileCommand.TryParseReadProfileResponse(
            DiagnosticResponse(0x00, 0x0022, 0x02, 30, 70), 0x0022, out _));
    }

    [Fact]
    public void Parser_rejects_wrong_address_length_report_id_ack_marker_and_out_of_range_value()
    {
        var valid = Response(0x0022, 50);
        Assert.False(MsiClawVibrationProfileCommand.TryParseReadProfileResponse(valid, 0x0023, out _));
        Assert.False(MsiClawVibrationProfileCommand.TryParseReadProfileResponse(valid[..^1], 0x0022, out _));

        foreach (var index in new[] { 0, 4, 5, 3 })
        {
            var invalid = (byte[])valid.Clone();
            invalid[index] ^= 0x01;
            Assert.False(MsiClawVibrationProfileCommand.TryParseReadProfileResponse(invalid, 0x0022, out _));
        }

        var outOfRange = Response(0x0022, 101);
        Assert.False(MsiClawVibrationProfileCommand.TryParseReadProfileResponse(outOfRange, 0x0022, out _));
    }

    [Fact]
    public void Builder_rejects_unlisted_profile_addresses_and_invalid_percentages()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MsiClawVibrationProfileCommand.BuildReadProfile(0x0024));
        Assert.Throws<ArgumentOutOfRangeException>(() => MsiClawVibrationProfileCommand.BuildWriteProfile(0x0024, 50));
        Assert.Throws<ArgumentOutOfRangeException>(() => MsiClawVibrationProfileCommand.BuildWriteProfile(0x0022, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MsiClawVibrationProfileCommand.BuildWriteProfile(0x0023, 101));
    }

    [Fact]
    public void Diagnostic_builder_rejects_requests_outside_its_narrow_contract()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MsiClawVibrationProfileCommand.BuildDiagnosticReadProfile(0x02, 0x0022, 0x02));
        Assert.Throws<ArgumentOutOfRangeException>(() => MsiClawVibrationProfileCommand.BuildDiagnosticReadProfile(0x00, 0x0023, 0x02));
        Assert.Throws<ArgumentOutOfRangeException>(() => MsiClawVibrationProfileCommand.BuildDiagnosticReadProfile(0x01, 0x0022, 0x01));
    }

    internal static byte[] Response(ushort address, int value, byte profileIndex = 0x01)
    {
        var report = new byte[64];
        report[0] = 0x10;
        report[3] = 0x3C;
        report[4] = 0x05;
        report[5] = profileIndex;
        report[6] = (byte)(address >> 8);
        report[7] = (byte)address;
        report[8] = 0x01;
        report[9] = (byte)value;
        return report;
    }

    internal static byte[] DiagnosticResponse(
        byte profileIndex,
        ushort address,
        byte length,
        int left,
        int right)
    {
        var report = new byte[64];
        report[0] = 0x10;
        report[3] = 0x3C;
        report[4] = 0x05;
        report[5] = profileIndex;
        report[6] = (byte)(address >> 8);
        report[7] = (byte)address;
        report[8] = length;
        report[9] = (byte)left;
        report[10] = (byte)right;
        return report;
    }

    private static byte[] Expected(byte command, byte profileIndex, ushort address, byte length)
    {
        var report = new byte[64];
        report[0] = 0x0F;
        report[3] = 0x3C;
        report[4] = command;
        report[5] = profileIndex;
        report[6] = (byte)(address >> 8);
        report[7] = (byte)address;
        report[8] = length;
        return report;
    }
}
