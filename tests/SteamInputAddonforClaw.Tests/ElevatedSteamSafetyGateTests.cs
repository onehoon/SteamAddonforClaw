using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ElevatedSteamSafetyGateTests
{
    [Fact]
    public void RunningAppIdActive_BlocksMutation()
    {
        var result = Evaluate(123, new(false, true, "Inactive"));
        Assert.False(result.Allowed);
        Assert.Equal("SteamSessionActive", result.Reason);
    }

    [Fact]
    public void RunningAppIdReadFailure_BlocksMutation()
    {
        var result = ElevatedSteamSafetyGate.Evaluate(
            () => throw new InvalidOperationException("read failed"),
            () => new(false, true, "Inactive"));
        Assert.False(result.Allowed);
        Assert.Equal("RunningAppIdUnavailable", result.Reason);
    }

    [Fact]
    public void BigPictureActive_BlocksMutation()
    {
        var result = Evaluate(0, new(true, true, "Active"));
        Assert.False(result.Allowed);
        Assert.Equal("SteamSessionActive", result.Reason);
    }

    [Fact]
    public void BigPictureProbeUnreliable_BlocksMutation()
    {
        var result = Evaluate(0, new(false, false, "Unreliable"));
        Assert.False(result.Allowed);
        Assert.Equal("BigPictureProbeFailed", result.Reason);
    }

    [Fact]
    public void BigPictureProbeThrows_BlocksMutation()
    {
        var result = ElevatedSteamSafetyGate.Evaluate(() => 0, () => throw new InvalidOperationException("probe failed"));
        Assert.False(result.Allowed);
        Assert.Equal("BigPictureProbeFailed", result.Reason);
    }

    [Fact]
    public void NoGameAndBigPictureInactive_AllowsMutation()
    {
        var result = Evaluate(0, new(false, true, "Inactive"));
        Assert.True(result.Allowed);
        Assert.Equal("Allowed", result.Reason);
    }

    private static (bool Allowed, string Reason) Evaluate(uint appId, SteamBigPictureProbeResult probe) =>
        ElevatedSteamSafetyGate.Evaluate(() => appId, () => probe);
}
