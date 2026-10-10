using SteamInputAddonforClaw.Hosting;
using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ControllerCadencePolicyTests
{
    [Theory]
    [InlineData(0, false, false, true, false)]
    [InlineData(123, false, false, true, true)]
    [InlineData(0, true, false, true, true)]
    [InlineData(0, false, true, true, true)]
    [InlineData(0, false, false, false, true)]
    [InlineData(0, true, false, false, true)]
    public void Existing_game_and_observer_facts_resolve_the_expected_controller_cadence(
        uint runningAppId,
        bool bigPictureActive,
        bool xboxGameActive,
        bool xboxCanReportNoActiveGame,
        bool expectedFast)
    {
        var actual = AddonProcessHost.RequiresFastControllerCadence(
            new SteamPresentationSnapshot(runningAppId, bigPictureActive),
            xboxGameActive,
            xboxCanReportNoActiveGame);

        Assert.Equal(expectedFast, actual);
    }
}
