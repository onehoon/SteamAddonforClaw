using SteamInputAddonforClaw.Lifecycle;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ApplicationLifecyclePolicyTests
{
    [Fact]
    public void ShouldShowMainWindow_WhenBackgroundLaunch_ReturnsFalse() => Assert.False(ApplicationLifecyclePolicy.ShouldShowMainWindow(["--background"]));

    [Fact]
    public void ShouldShowMainWindow_WhenManualLaunch_ReturnsTrue() => Assert.True(ApplicationLifecyclePolicy.ShouldShowMainWindow([]));

    [Fact]
    public void OnWindowClose_WhenNotExplicit_ReturnsHideWindow() => Assert.Equal(ApplicationCloseAction.HideWindow, ApplicationLifecyclePolicy.OnWindowClose(false));

    [Theory]
    [InlineData(true)]
    public void OnWindowClose_WhenExplicitExit_ReturnsExitApplication(bool explicitExit) => Assert.Equal(ApplicationCloseAction.ExitApplication, ApplicationLifecyclePolicy.OnWindowClose(explicitExit));
}
