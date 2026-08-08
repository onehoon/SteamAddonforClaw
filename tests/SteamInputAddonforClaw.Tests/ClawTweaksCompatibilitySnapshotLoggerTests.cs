using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ClawTweaksCompatibilitySnapshotLoggerTests
{
    [Theory]
    [InlineData("ClawTweaksHelper", true)]
    [InlineData("XBoxGameBar", true)]
    [InlineData("unrelated-process", false)]
    public void MatchesAny_UsesCaseInsensitiveCompatibilityTokens(string value, bool expected)
    {
        Assert.Equal(expected, ClawTweaksCompatibilitySnapshotLogger.MatchesAny(value, ["CLAW", "GAMEBAR"]));
    }

    [Theory]
    [InlineData("HID\\VID_045E&PID_028E&IG_00", true)]
    [InlineData("ROOT\\USB\\0000", false)]
    [InlineData("ROOT\\USB\\0000", true, "usbip2_ude")]
    [InlineData("USB\\VID_0DB0&PID_1902", true)]
    public void IsCompatibilityRelevant_SelectsControllerAndUsbIpEvidence(string instanceId, bool expected, string? service = null)
    {
        var device = new ControllerDeviceInfo(instanceId, null, null, [], null, [], [], null, null, service, null, null, true);

        Assert.Equal(expected, ClawTweaksCompatibilitySnapshotLogger.IsCompatibilityRelevant(device));
    }
}
