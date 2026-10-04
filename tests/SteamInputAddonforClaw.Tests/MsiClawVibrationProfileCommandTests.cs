using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawVibrationProfileCommandTests
{
    [Theory]
    [InlineData(50, 50, 0x32, 0x32)]
    [InlineData(0, 100, 0x00, 0x64)]
    [InlineData(100, 0, 0x64, 0x00)]
    public void Motor_pair_write_builds_the_exact_64_byte_profile_frame(
        int left,
        int right,
        byte expectedLeft,
        byte expectedRight)
    {
        var expected = new byte[64];
        expected[0] = 0x0F;
        expected[3] = 0x3C;
        expected[4] = 0x21;
        expected[5] = 0x01;
        expected[7] = 0x22;
        expected[8] = 0x02;
        expected[9] = expectedLeft;
        expected[10] = expectedRight;

        var actual = MsiClawVibrationProfileCommand.BuildMotorPairWrite(left, right);

        Assert.Equal(expected, actual);
        Assert.Equal(64, actual.Length);
        Assert.Equal(new byte[]
        {
            0x0F, 0x00, 0x00, 0x3C, 0x21, 0x01, 0x00, 0x22, 0x02,
            expectedLeft, expectedRight
        }, actual[..11]);
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(101, 50)]
    [InlineData(50, -1)]
    [InlineData(50, 101)]
    public void Motor_pair_write_rejects_values_outside_zero_to_hundred(int left, int right)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MsiClawVibrationProfileCommand.BuildMotorPairWrite(left, right));
    }
}
