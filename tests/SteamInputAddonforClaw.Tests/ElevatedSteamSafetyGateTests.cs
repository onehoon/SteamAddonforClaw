using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ElevatedSteamSafetyGateTests
{
    [Fact]
    public void BigPictureEnabledAndActive_BlocksMutation()
    {
        var result = Evaluate(0, true, new(true, true, "Active"));
        Assert.False(result.Allowed);
        Assert.Equal("SteamSessionActive", result.Reason);
    }

    [Fact]
    public void BigPictureDisabled_DoesNotProbeOrBlock()
    {
        var result = Evaluate(0, false, new(true, true, "Active"));
        Assert.True(result.Allowed);
    }

    [Fact]
    public void UnreliableSettings_BlocksMutation()
    {
        var result = ElevatedSteamSafetyGate.Evaluate(() => 0, () => new(new AppSettings(), false, "SettingsUnreliable"), () => new(false, true, "Inactive"));
        Assert.False(result.Allowed);
        Assert.Equal("SettingsUnreliable", result.Reason);
    }

    [Fact]
    public void MissingSettingsAreReliableDefaults()
    {
        var result = Evaluate(0, false, new(false, true, "Inactive"));
        Assert.True(result.Allowed);
    }

    [Fact]
    public void RunningAppIdTakesPriorityAndBlocks()
    {
        var result = Evaluate(123, false, new(false, true, "Inactive"));
        Assert.False(result.Allowed);
        Assert.Equal("SteamSessionActive", result.Reason);
    }

    private static (bool Allowed, string Reason) Evaluate(uint appId, bool enabled, SteamBigPictureProbeResult probe) =>
        ElevatedSteamSafetyGate.Evaluate(
            () => appId,
            () => new(new AppSettings(RouteInSteamBigPicture: enabled), true, "Loaded"),
            () => probe);
}
