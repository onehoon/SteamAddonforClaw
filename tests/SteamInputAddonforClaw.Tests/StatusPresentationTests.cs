using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class StatusPresentationTests
{
    [Theory]
    [InlineData("MICRO-STAR INTERNATIONAL")]
    [InlineData("MICRO-STAR INTERNATIONAL CO., LTD")]
    [InlineData("MICRO-STAR INTERNATIONAL CO., LTD.")]
    [InlineData("MICRO-STAR INTERNATIONAL CO.,LTD")]
    [InlineData("micro-star international co., ltd.")]
    public void FormatManufacturerForDisplay_KnownMsiAliases_ReturnsMsi(string rawManufacturer) =>
        Assert.Equal("MSI", StatusPresentation.FormatManufacturerForDisplay(rawManufacturer));

    [Fact]
    public void FormatManufacturerForDisplay_UnknownManufacturer_PreservesTrimmedValue() =>
        Assert.Equal("Acme Devices", StatusPresentation.FormatManufacturerForDisplay("  Acme Devices  "));

    [Fact]
    public void FormatSteamGame_Actual_WithAppId_ReportsRunning() =>
        Assert.Equal("Running", StatusPresentation.FormatSteamGame(new FrontendSteamSnapshot(true, 123, FrontendSteamSource.Actual)));

    [Fact]
    public void FormatSteamGame_Actual_WithoutAppId_ReportsNotRunning() =>
        Assert.Equal("Not Running", StatusPresentation.FormatSteamGame(new FrontendSteamSnapshot(false, 0, FrontendSteamSource.Actual)));

    [Fact]
    public void FormatSteamGame_BigPicture_ReportsBigPictureModeRegardlessOfAppId() =>
        Assert.Equal("Big Picture Mode", StatusPresentation.FormatSteamGame(new FrontendSteamSnapshot(false, 0, FrontendSteamSource.BigPicture)));

    [Fact]
    public void FormatSteamGame_DeveloperTest_ReportsNotRunningEvenWithAppId()
    {
        // Regression: DeveloperTest is a synthetic source used for diagnostics and must never be
        // presented as an actual running game, no matter what AppId accompanies it.
        var status = StatusPresentation.FormatSteamGame(new FrontendSteamSnapshot(true, uint.MaxValue, FrontendSteamSource.DeveloperTest));
        Assert.Equal("Not Running", status);
    }

    [Fact]
    public void FormatSteamGame_IndeterminateSource_ReportsNotRunning() =>
        Assert.Equal("Not Running", StatusPresentation.FormatSteamGame(new FrontendSteamSnapshot(true, 123, FrontendSteamSource.Indeterminate)));

    [Fact]
    public void FormatControllerSoftwareStatus_Running_ReportsRunningRegardlessOfInstallation() =>
        Assert.Equal("Running", StatusPresentation.FormatControllerSoftwareStatus(new("MsiCenterM", "MSI Center M", FrontendSoftwareInstallationStatus.Indeterminate, FrontendSoftwareRuntimeStatus.Running, "")));

    [Fact]
    public void FormatControllerSoftwareStatus_Starting_ReportsStarting() =>
        Assert.Equal("Starting", StatusPresentation.FormatControllerSoftwareStatus(new("MsiCenterM", "MSI Center M", FrontendSoftwareInstallationStatus.Installed, FrontendSoftwareRuntimeStatus.Starting, "")));

    [Fact]
    public void FormatControllerSoftwareStatus_IndeterminateRuntime_ReportsIndeterminate() =>
        Assert.Equal("Indeterminate", StatusPresentation.FormatControllerSoftwareStatus(new("MsiCenterM", "MSI Center M", FrontendSoftwareInstallationStatus.Installed, FrontendSoftwareRuntimeStatus.Indeterminate, "")));

    [Fact]
    public void FormatControllerSoftwareStatus_InstalledButNotRunning_ReportsInstalledSlashNotRunning() =>
        Assert.Equal("Installed / Not running", StatusPresentation.FormatControllerSoftwareStatus(new("MsiCenterM", "MSI Center M", FrontendSoftwareInstallationStatus.Installed, FrontendSoftwareRuntimeStatus.NotRunning, "")));

    [Fact]
    public void FormatControllerSoftwareStatus_NotInstalled_ReportsNotInstalled() =>
        Assert.Equal("Not installed", StatusPresentation.FormatControllerSoftwareStatus(new("MsiCenterM", "MSI Center M", FrontendSoftwareInstallationStatus.NotInstalled, FrontendSoftwareRuntimeStatus.NotRunning, "")));

    [Fact]
    public void IsWarning_RecoveryUnsafe_RemainsWarning() =>
        Assert.True(StatusPresentation.IsWarning(Snapshot(recoverySafe: false)));

    [Fact]
    public void IsWarning_UnsupportedHardware_HidesRecoveryWarning() =>
        Assert.False(StatusPresentation.IsWarning(Snapshot(
            recoverySafe: false,
            hardwareStatus: FrontendHardwareStatus.Unsupported)));

    [Fact]
    public void IsWarning_HardwareCompatibilityIndeterminate_RemainsVisible() =>
        Assert.True(StatusPresentation.IsWarning(Snapshot(hardwareStatus: FrontendHardwareStatus.Indeterminate)));

    [Fact]
    public void IsWarning_ControllerEnvironmentIndeterminate_RemainsVisible() =>
        Assert.True(StatusPresentation.IsWarning(Snapshot(environmentStatus: FrontendControllerEnvironmentStatus.Indeterminate)));

    [Fact]
    public void IsWarning_SetupRequired_IsWarning() =>
        Assert.True(StatusPresentation.IsWarning(Snapshot(addonStatus: FrontendAddonOperationalStatus.SetupRequired)));

    [Fact]
    public void IsWarning_IndeterminateAddonStatus_IsWarning() =>
        Assert.True(StatusPresentation.IsWarning(Snapshot(addonStatus: FrontendAddonOperationalStatus.Indeterminate)));

    [Fact]
    public void IsWarning_NormalReadyState_IsNotWarning() =>
        Assert.False(StatusPresentation.IsWarning(Snapshot()));

    private static FrontendStatusSnapshot Snapshot(
        bool recoverySafe = true,
        FrontendAddonOperationalStatus addonStatus = FrontendAddonOperationalStatus.Ready,
        FrontendHardwareStatus hardwareStatus = FrontendHardwareStatus.Supported,
        FrontendControllerEnvironmentStatus environmentStatus = FrontendControllerEnvironmentStatus.Supported) =>
        new(
            new("Test", "Test", "Test", []),
            new(hardwareStatus, "", "", "Test"),
            [],
            environmentStatus,
            "Test",
            new(FrontendPrerequisiteStatus.Ready, "", FrontendPrerequisiteStatus.Ready, "", FrontendPrerequisiteStatus.Ready, ""),
            new(false, 0, FrontendSteamSource.Actual),
            addonStatus, "Test", recoverySafe,
            FrontendSetupStatus.Indeterminate, "", false);
}
