using SteamInputAddonforClaw.Profiles;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class GameProfileEnrollmentTests
{
    [Theory]
    [InlineData(480)]
    [InlineData(228980)]
    [InlineData(993090)]
    public void Active_steam_target_does_not_implicitly_create_profiles_for_hidden_system_apps(uint appId)
    {
        Assert.False(GameProfileEnrollment.CanCreateForActiveSteamTarget(appId));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(570)]
    [InlineData(993091)]
    public void Active_steam_target_can_create_profiles_for_regular_app_ids(uint appId)
    {
        Assert.True(GameProfileEnrollment.CanCreateForActiveSteamTarget(appId));
    }

    [Fact]
    public void Active_steam_target_does_not_create_a_profile_for_app_id_zero()
    {
        Assert.False(GameProfileEnrollment.CanCreateForActiveSteamTarget(0));
    }
}
