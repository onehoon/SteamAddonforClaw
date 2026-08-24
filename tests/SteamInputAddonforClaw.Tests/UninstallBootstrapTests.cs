using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Startup;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class UninstallBootstrapTests
{
    [Fact]
    public void Elevated_cleanup_argument_is_distinct_from_prerequisite_setup()
    {
        Assert.Equal("--elevated-uninstall-cleanup", UninstallBootstrap.ElevatedArgument);
        Assert.NotEqual("--elevated-prerequisite-setup", UninstallBootstrap.ElevatedArgument);
    }

    [Fact]
    public void Data_root_is_the_full_reset_root()
    {
        var root = AddonDataPaths.ResolveDataRoot("C:\\Users\\Test\\App");
        Assert.EndsWith("SteamInputAddonforClaw-Data", root, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Elevated_cleanup_request_is_invoked_once_and_preserves_cancellation_result()
    {
        var calls = 0;
        var result = await UninstallBootstrap.RequestElevatedCleanupAsync(() =>
        {
            calls++;
            return Task.FromResult(new ElevatedProcessResult(ElevatedProcessResultKind.CancelledBeforeStart));
        });

        Assert.Equal(1, calls);
        Assert.Equal(ElevatedProcessResultKind.CancelledBeforeStart, result.Kind);
    }

    [Fact]
    public void Failed_runtime_release_blocks_sensitive_cleanup()
    {
        Assert.False(UninstallRuntimeReleasePolicy.AllowsSensitiveCleanup(false));
        Assert.True(UninstallRuntimeReleasePolicy.AllowsSensitiveCleanup(true));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(0, false)]
    [InlineData(4, false)]
    public void Stale_recovery_requires_stock_center_m_environment(int modeValue, bool expected)
    {
        Assert.Equal(expected, UninstallSafetyEnvironmentPolicy.AllowsRecovery((ControllerEnvironmentMode)modeValue));
    }
}
