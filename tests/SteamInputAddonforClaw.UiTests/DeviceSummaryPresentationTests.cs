using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class DeviceSummaryPresentationTests
{
    [Theory]
    [InlineData("MICRO-STAR INTERNATIONAL")]
    [InlineData("MICRO-STAR INTERNATIONAL CO., LTD")]
    [InlineData("MICRO-STAR INTERNATIONAL CO., LTD.")]
    [InlineData("MICRO-STAR INTERNATIONAL CO.,LTD")]
    [InlineData("micro-star international co., ltd.")]
    public void FormatManufacturerForDisplay_KnownMsiAliases_ReturnsMsi(string rawManufacturer) =>
        Assert.Equal("MSI", DeviceSummaryPresentation.FormatManufacturerForDisplay(rawManufacturer));

    [Fact]
    public void FormatManufacturerForDisplay_UnknownManufacturer_PreservesTrimmedValue() =>
        Assert.Equal("Acme Devices", DeviceSummaryPresentation.FormatManufacturerForDisplay("  Acme Devices  "));

    [Theory]
    [InlineData(FrontendControllerBadgeState.Unavailable, "Unavailable")]
    [InlineData(FrontendControllerBadgeState.MsiNative, "MSI Native")]
    [InlineData(FrontendControllerBadgeState.Xbox360Active, "Xbox 360 · Active")]
    [InlineData(FrontendControllerBadgeState.SteamDeckActive, "Steam Deck · Active")]
    [InlineData(FrontendControllerBadgeState.Initializing, "Initializing…")]
    [InlineData(FrontendControllerBadgeState.Reconnecting, "Reconnecting…")]
    [InlineData(FrontendControllerBadgeState.NeedsAttention, "Needs attention")]
    public void FormatControllerBadge_MapsEveryTypedState(FrontendControllerBadgeState badge, string expected) =>
        Assert.Equal(expected, DeviceSummaryPresentation.FormatControllerBadge(FrontendHardwareStatus.Supported, badge));

    [Theory]
    [InlineData(FrontendHardwareStatus.Unsupported, FrontendControllerBadgeState.SteamDeckActive, "Unsupported")]
    [InlineData(FrontendHardwareStatus.Indeterminate, FrontendControllerBadgeState.Xbox360Active, "Compatibility unknown")]
    public void FormatControllerBadge_PreservesHardwareCompatibilityWarnings(
        FrontendHardwareStatus hardwareStatus,
        FrontendControllerBadgeState badge,
        string expected) =>
        Assert.Equal(expected, DeviceSummaryPresentation.FormatControllerBadge(hardwareStatus, badge));

    [Fact]
    public void WithUnavailableControllerBadge_PreservesOtherCapturedStatus()
    {
        var snapshot = new FrontendStatusSnapshot(
            new("MSI", "Claw", "BOARD", ["GPU"]),
            new(FrontendHardwareStatus.Supported, "msi.claw", "model", "matched"),
            new(FrontendPrerequisiteStatus.Ready, "", FrontendPrerequisiteStatus.Ready, "", FrontendPrerequisiteStatus.Ready, ""),
            new(true, 480, FrontendSteamSource.Actual),
            FrontendAddonOperationalStatus.Ready, "Ready", true,
            FrontendSetupStatus.Complete, "Complete", false)
        {
            ControllerBadge = FrontendControllerBadgeState.SteamDeckActive
        };

        var unavailable = DeviceSummaryPresentation.WithUnavailableControllerBadge(snapshot);

        Assert.Equal(FrontendControllerBadgeState.Unavailable, unavailable.ControllerBadge);
        Assert.Equal(snapshot.Device, unavailable.Device);
        Assert.Equal(snapshot.Hardware, unavailable.Hardware);
        Assert.Equal(snapshot.Prerequisites, unavailable.Prerequisites);
        Assert.Equal(snapshot.Steam, unavailable.Steam);
        Assert.Equal(snapshot.AddonStatus, unavailable.AddonStatus);
        Assert.Equal(snapshot.SetupStatus, unavailable.SetupStatus);
        Assert.Equal(snapshot.CanInstallRequiredComponents, unavailable.CanInstallRequiredComponents);
    }
}
