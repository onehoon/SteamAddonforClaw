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
    [InlineData(FrontendHardwareStatus.Supported, "Supported")]
    [InlineData(FrontendHardwareStatus.Unsupported, "Unsupported")]
    [InlineData(FrontendHardwareStatus.Indeterminate, "Compatibility unknown")]
    public void FormatDeviceCompatibility_MapsHardwareStatus(FrontendHardwareStatus status, string expected) =>
        Assert.Equal(expected, DeviceSummaryPresentation.FormatDeviceCompatibility(status));
}
