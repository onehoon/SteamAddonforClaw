using SteamInputAddonforClaw.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ApplicationLifecyclePolicyTests
{
    [Theory]
    [InlineData(true, "")]
    [InlineData(true, "--restart")]
    [InlineData(false, "--background")]
    [InlineData(false, "--background --restart")]
    [InlineData(false, "--BaCkGrOuNd")]
    public void ShouldLaunchFrontend_FollowsRuntimeLaunchIntent(bool expected, string argumentText) =>
        Assert.Equal(expected, ApplicationLifecyclePolicy.ShouldLaunchFrontend(argumentText.Length == 0 ? [] : argumentText.Split(' ')));
}
