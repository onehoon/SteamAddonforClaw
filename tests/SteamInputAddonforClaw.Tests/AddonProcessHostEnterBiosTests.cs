using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AddonProcessHostEnterBiosTests
{
    [Fact]
    public void Enter_bios_has_an_enabled_mode_unowned_path_that_verifies_mode5_before_restart()
    {
        var source = ReadSource("src", "SteamInputAddonforClaw", "Hosting", "AddonProcessHost.cs");

        Assert.Contains("if (_physicalOwnership is { } owner)", source, StringComparison.Ordinal);
        Assert.Contains("if (!firmwareRestart)", source, StringComparison.Ordinal);
        Assert.Contains("PrepareUnownedPhysicalControllerForFirmwareBiosAsync(startupComposition, token)", source, StringComparison.Ordinal);

        var helperStart = source.IndexOf(
            "private async Task<SteamInputAddonforClaw.Devices.MSI.Claw.PhysicalOwnershipReleaseResult> PrepareUnownedPhysicalControllerForFirmwareBiosAsync",
            StringComparison.Ordinal);
        Assert.True(helperStart >= 0);
        var helper = source[helperStart..];
        Assert.Contains("CaptureStableCurrentSnapshotAsync", helper, StringComparison.Ordinal);
        Assert.Contains("allowTransientDeviceNotFound: false", helper, StringComparison.Ordinal);
        Assert.Contains("MsiClawAddonPhysicalOwnership.TryReadIdentity", helper, StringComparison.Ordinal);
        Assert.Contains("MsiClawGamepadMode.Bios", helper, StringComparison.Ordinal);
        Assert.Contains("SwitchAndVerifyAsync", helper, StringComparison.Ordinal);
        Assert.Contains("if (!prepared.Succeeded)", helper, StringComparison.Ordinal);

        var transition = ReadSource("src", "SteamInputAddonforClaw", "CenterMStartup", "CenterMRebootAuthorityTransition.cs");
        var prepare = transition.IndexOf("var prepare = await _preparePhysicalOwnershipForFirmwareBios", StringComparison.Ordinal);
        var restart = transition.IndexOf("var restart = await firmwareSession.RequestRestartAsync", StringComparison.Ordinal);
        Assert.True(prepare >= 0 && restart > prepare,
            "the firmware restart request must follow the verified GamepadMode preparation");
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine([directory!.FullName, .. parts]));
    }
}
