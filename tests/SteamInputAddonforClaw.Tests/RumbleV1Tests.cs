using SteamInputAddonforClaw.Feedback;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Pure <see cref="SteamDeckRumbleDecoder"/> coverage. Full1902 Cleanup J removed the
/// routing-era <c>SteamDeckRumbleFeedbackBridge</c> / <c>FeedbackAuthority</c> and their behavioral
/// tests; the stateless packet decoder and <see cref="TwoMotorRumble"/> primitive are retained for a
/// future Full1902 feedback path.</summary>
public sealed class RumbleV1Tests
{
    [Fact]
    public void TwoMotorRumble_PreservesIndependentFullPrecisionChannels()
    {
        Assert.Equal(TwoMotorRumble.Stopped, new TwoMotorRumble(0, 0));
        Assert.Equal(new TwoMotorRumble(ushort.MaxValue, 1), new TwoMotorRumble(ushort.MaxValue, 1));
    }

    [Fact]
    public void Decoder_MapsValidatedDeckRumbleFieldsWithoutReduction()
    {
        var report = new byte[] { 0xEB, 9, 0x04, 0x78, 0x56, 0x34, 0x12, 0xCD, 0xAB, 0xFE, 0x7F };
        var result = SteamDeckRumbleDecoder.Decode(report);
        Assert.True(result.HasPhysicalTranslation);
        Assert.Equal((byte)0x04, result.RumbleType);
        Assert.Equal((ushort)0x5678, result.RumbleIntensity);
        Assert.Equal(new TwoMotorRumble(0x1234, 0xABCD), result.Rumble);
        Assert.Equal((sbyte)-2, result.RumbleLeftGain);
        Assert.Equal((sbyte)127, result.RumbleRightGain);
    }

    [Theory]
    [InlineData(0x80, -128)]
    [InlineData(0x7F, 127)]
    public void Decoder_PreservesSignedRumbleGainBoundaries(byte encodedGain, sbyte expectedGain)
    {
        var result = SteamDeckRumbleDecoder.Decode([0xEB, 9, 0, 0, 0, 0, 0, 0, 0, encodedGain, encodedGain]);

        Assert.Equal(expectedGain, result.RumbleLeftGain);
        Assert.Equal(expectedGain, result.RumbleRightGain);
    }

    [Fact]
    public void Decoder_RejectsMalformedAndClassifiesUnsupportedCommands()
    {
        Assert.Equal(SteamDeckFeedbackCommand.Malformed, SteamDeckRumbleDecoder.Decode([0xEB, 9]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Malformed, SteamDeckRumbleDecoder.Decode([0xEA]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Malformed, SteamDeckRumbleDecoder.Decode([0x8F]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Unsupported, SteamDeckRumbleDecoder.Decode([0xB6]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Unsupported, SteamDeckRumbleDecoder.Decode([0xB7]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Unsupported, SteamDeckRumbleDecoder.Decode([0xB8]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Unsupported, SteamDeckRumbleDecoder.Decode([0xB9]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Unknown, SteamDeckRumbleDecoder.Decode([0xCA]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Unknown, SteamDeckRumbleDecoder.Decode([0x99]).Command);
    }

    [Fact]
    public void Decoder_CoversNormalizedMinimumAndIndependentMotorBoundaries()
    {
        static byte[] Packet(ushort left, ushort right, byte size = 9)
            => [0xEB, size, 0, 0, 0, (byte)left, (byte)(left >> 8), (byte)right, (byte)(right >> 8), 2, 0];

        Assert.Equal(TwoMotorRumble.Stopped, SteamDeckRumbleDecoder.Decode(Packet(0, 0)).Rumble);
        Assert.Equal(new TwoMotorRumble(ushort.MaxValue, ushort.MaxValue), SteamDeckRumbleDecoder.Decode(Packet(ushort.MaxValue, ushort.MaxValue)).Rumble);
        Assert.Equal(new TwoMotorRumble(0x1234, 0), SteamDeckRumbleDecoder.Decode(Packet(0x1234, 0)).Rumble);
        Assert.Equal(new TwoMotorRumble(0, 0x5678), SteamDeckRumbleDecoder.Decode(Packet(0, 0x5678)).Rumble);
        Assert.Equal(SteamDeckFeedbackCommand.Rumble, SteamDeckRumbleDecoder.Decode(Packet(1, 2)).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Malformed, SteamDeckRumbleDecoder.Decode(Packet(1, 2)[..10]).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Malformed, SteamDeckRumbleDecoder.Decode(Packet(1, 2, 8)).Command);
        Assert.Equal(SteamDeckFeedbackCommand.Malformed, SteamDeckRumbleDecoder.Decode([]).Command);
    }

    [Fact]
    public void Decoder_PreservesModernSdlHapticMetadataAndExistingClawFallback()
    {
        var result = SteamDeckRumbleDecoder.Decode([0xEA, 0x13, 0x03, 0x07, 0x04, 0xFE, 0x34, 0x12, 0xFC, 0xFF, 0x78, 0x56, 0xBC, 0x9A, 0x64, 0x7F, 0x06, 0x22, 0x11, 0x44, 0x33]);

        Assert.Equal(SteamDeckFeedbackCommand.Haptic, result.Command);
        Assert.Equal(new SteamDeckHapticMetadata(19, 3, 7, 4, -2, true, 0x1234, -4, 0x5678, unchecked((ushort)0x9ABC), 100, 127, 6, 0x1122, 0x3344), result.Haptic);
        Assert.Equal((byte)4, result.Intensity);
        Assert.Equal(-2, result.Gain);
        Assert.Equal((byte)0, result.Strength8);
        Assert.Equal(TwoMotorRumble.Stopped, result.Rumble);
    }

    [Fact]
    public void Decoder_RejectsTruncatedModernSdlHaptic()
    {
        Assert.Equal(SteamDeckFeedbackCommand.Malformed, SteamDeckRumbleDecoder.Decode([0xEA, 19, 1, 2, 3, 4]).Command);
    }

    [Fact]
    public void Decoder_PreservesHistoricalHapticPrefixWithoutPretendingModernLayout()
    {
        var result = SteamDeckRumbleDecoder.Decode([0xEA, 0x0D, 2, 4, 100, 0]);

        Assert.Equal(SteamDeckFeedbackCommand.Haptic, result.Command);
        Assert.Equal((byte)0x0D, result.Haptic!.Value.DeclaredPayloadLength);
        Assert.Equal((byte)2, result.Haptic.Value.Side);
        Assert.Equal((byte)4, result.Haptic.Value.CommandType);
        Assert.False(result.Haptic.Value.IsModernSdlLayout);
        Assert.Null(result.Haptic.Value.Frequency);
        Assert.Equal(new TwoMotorRumble(100 * 257, 100 * 257), result.Rumble);
    }

    [Theory]
    [InlineData(0x80, -128)]
    [InlineData(0x7F, 127)]
    public void Decoder_PreservesModernHapticSignedGainBoundaries(byte encodedGain, sbyte expectedGain)
    {
        var report = new byte[21];
        report[0] = 0xEA;
        report[1] = 19;
        report[5] = encodedGain;
        var result = SteamDeckRumbleDecoder.Decode(report);

        Assert.Equal(expectedGain, result.Haptic!.Value.DbGain);
    }

    [Fact]
    public void Decoder_HapticMetadataDoesNotChangeExistingClawFallback()
    {
        var first = SteamDeckRumbleDecoder.Decode([0xEA, 19, 1, 1, 100, 0, 0x34, 0x12, 0x02, 0, 0x78, 0x56, 0xBC, 0x9A, 100, 1, 2, 0x22, 0x11, 0x44, 0x33]);
        var second = SteamDeckRumbleDecoder.Decode([0xEA, 19, 3, 5, 100, 0, 0xFF, 0xFF, 0xFC, 0xFF, 0xAA, 0xBB, 0xCC, 0xDD, 1, 127, 6, 0x01, 0, 0x02, 0]);

        Assert.Equal(new TwoMotorRumble(100 * 257, 100 * 257), first.Rumble);
        Assert.Equal(first.Rumble, second.Rumble);
    }

    [Fact]
    public void Decoder_PreservesLinuxHapticPulseMetadataWithoutPhysicalTranslation()
    {
        var result = SteamDeckRumbleDecoder.Decode([0x8F, 8, 2, 0x34, 0x12, 0x78, 0x56, 3, 0, 0xA0]);

        Assert.Equal(SteamDeckFeedbackCommand.HapticPulse, result.Command);
        Assert.Equal(new SteamDeckHapticPulseMetadata(8, 2, 0x1234, 0x5678, 3, 0xA0, true), result.HapticPulse);
        Assert.Equal(TwoMotorRumble.Stopped, result.Rumble);
        Assert.False(result.HasPhysicalTranslation);
    }

    // ---- Full1902 production Xbox360 motor mapping (work order section 8.1 / 19.2) ----

    [Fact]
    public void Xbox360Mapping_LeftIsLargeMotorAndRightIsSmallMotor()
    {
        Assert.Equal(new TwoMotorRumble(65535, 0),
            new TwoMotorRumble(Xbox360RumbleFeedbackBridge.Expand(255), Xbox360RumbleFeedbackBridge.Expand(0)));
        Assert.Equal(new TwoMotorRumble(0, 65535),
            new TwoMotorRumble(Xbox360RumbleFeedbackBridge.Expand(0), Xbox360RumbleFeedbackBridge.Expand(255)));
    }

    [Theory]
    [InlineData((byte)0, (ushort)0)]
    [InlineData((byte)1, (ushort)257)]
    [InlineData((byte)127, (ushort)32639)]
    [InlineData((byte)255, (ushort)65535)]
    public void Xbox360Mapping_ExpandRoundTripsExactly8BitMagnitudeThroughThePhysicalConversion(byte value, ushort expanded)
    {
        Assert.Equal(expanded, Xbox360RumbleFeedbackBridge.Expand(value));
        Assert.Equal(value, SteamInputAddonforClaw.Devices.MSI.Claw.MsiClawRumblePacketBuilder.ToPhysicalByte(expanded));
    }
}
