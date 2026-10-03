using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawVibrationProfileCommandTests
{
    [Fact]
    public void Read_profile_builds_exact_left_and_right_64_byte_frames()
    {
        Assert.Equal(Expected(0x04, 0x0022, 0x01), MsiClawVibrationProfileCommand.BuildReadProfile(0x0022));
        Assert.Equal(Expected(0x04, 0x0023, 0x01), MsiClawVibrationProfileCommand.BuildReadProfile(0x0023));
    }

    [Theory]
    [InlineData(0x0022)]
    [InlineData(0x0023)]
    public void Write_profile_builds_exact_frame(ushort address)
    {
        var expected = Expected(0x21, address, 0x01);
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

    internal static byte[] Response(ushort address, int value)
    {
        var report = new byte[64];
        report[0] = 0x10;
        report[3] = 0x3C;
        report[4] = 0x05;
        report[5] = 0x01;
        report[6] = (byte)(address >> 8);
        report[7] = (byte)address;
        report[8] = 0x01;
        report[9] = (byte)value;
        return report;
    }

    private static byte[] Expected(byte command, ushort address, byte length)
    {
        var report = new byte[64];
        report[0] = 0x0F;
        report[3] = 0x3C;
        report[4] = command;
        report[5] = length;
        report[6] = (byte)(address >> 8);
        report[7] = (byte)address;
        report[8] = 0x01;
        return report;
    }
}
