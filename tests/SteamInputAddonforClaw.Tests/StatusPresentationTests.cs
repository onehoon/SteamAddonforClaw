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
    public void FormatControllerSoftwareStatus_PassiveWithoutVerifiedXInput_OmitsQualifier()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new(FrontendRoutingEligibilityReason.Eligible, FrontendRoutingOperationalState.Passive, Available: true, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: false);

        Assert.Equal("MSI Center M Native", status);
    }

    [Fact]
    public void FormatControllerStatus_PassiveWithVerifiedXInput_AppendsQualifier()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new(FrontendRoutingEligibilityReason.Eligible, FrontendRoutingOperationalState.Passive, Available: true, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: true);

        Assert.Equal("MSI Center M Native (XInput)", status);
    }

    [Fact]
    public void FormatControllerStatus_ActualStockRoutingActive_ReportsSteamController()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new(FrontendRoutingEligibilityReason.Eligible, FrontendRoutingOperationalState.OverrideActive, Available: true, SteamOutputActive: true, NativeDirectInputActive: true),
            nativeXInputVerified: false);

        Assert.Equal("Steam Controller (DInput)", status);
    }

    [Fact]
    public void FormatControllerStatus_EligibleButPipelineNotActuallyActive_DoesNotClaimSteamController()
    {
        // RoutingOperationalState.Passive is what "eligible but not yet entered" looks like --
        // eligibility alone (RoutingDecisionKind.Eligible / AddonOperationalStatus.Ready) is
        // never passed into this mapper, precisely so it cannot be mistaken for actual entry.
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new(FrontendRoutingEligibilityReason.Eligible, FrontendRoutingOperationalState.Passive, Available: true, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: false);

        Assert.NotEqual("Steam Controller (DInput)", status);
    }

    [Fact]
    public void FormatControllerStatus_OverrideActiveWithSteamOutputDisabled_DoesNotClaimSteamController()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new(FrontendRoutingEligibilityReason.Eligible, FrontendRoutingOperationalState.OverrideActive, Available: true, SteamOutputActive: false, NativeDirectInputActive: true),
            nativeXInputVerified: false);

        Assert.NotEqual("Steam Controller (DInput)", status);
    }

    [Fact]
    public void FormatControllerStatus_OverrideActiveWithoutNativeDirectInputConfirmed_DoesNotClaimSteamController()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new(FrontendRoutingEligibilityReason.Eligible, FrontendRoutingOperationalState.OverrideActive, Available: true, SteamOutputActive: true, NativeDirectInputActive: false),
            nativeXInputVerified: false);

        Assert.NotEqual("Steam Controller (DInput)", status);
    }

    [Fact]
    public void FormatControllerStatus_OverrideActiveWithIncompleteProof_ReportsUnavailableRatherThanNative()
    {
        // Regression: OverrideActive with incomplete Steam-output/DInput proof must fail
        // conservative to Unavailable, not silently fall through to "MSI Center M Native".
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new(FrontendRoutingEligibilityReason.Eligible, FrontendRoutingOperationalState.OverrideActive, Available: true, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: false);

        Assert.Equal("Unavailable", status);
    }

    [Fact]
    public void FormatControllerStatus_UntrustedState_ReportsUnavailableRegardlessOfRoutingState()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: false,
            routingStatus: new(FrontendRoutingEligibilityReason.Eligible, FrontendRoutingOperationalState.OverrideActive, Available: true, SteamOutputActive: true, NativeDirectInputActive: true),
            nativeXInputVerified: false);

        Assert.Equal("Unavailable", status);
    }

    [Fact]
    public void IsControllerStateTrusted_RecoveryUnsafe_IsFalse() =>
        Assert.False(StatusPresentation.IsControllerStateTrusted(Snapshot(recoverySafe: false)));

    [Fact]
    public void IsControllerStateTrusted_DeviceCompatibilityIndeterminate_IsFalse() =>
        Assert.False(StatusPresentation.IsControllerStateTrusted(Snapshot(routingReason: FrontendRoutingEligibilityReason.DeviceCompatibilityIndeterminate)));

    [Fact]
    public void IsControllerStateTrusted_ControllerEnvironmentIndeterminate_IsFalse() =>
        Assert.False(StatusPresentation.IsControllerStateTrusted(Snapshot(routingReason: FrontendRoutingEligibilityReason.ControllerEnvironmentIndeterminate)));

    [Fact]
    public void IsControllerStateTrusted_UnsupportedDevice_IsFalse() =>
        Assert.False(StatusPresentation.IsControllerStateTrusted(
            Snapshot(hardwareStatus: FrontendHardwareStatus.Unsupported, routingReason: FrontendRoutingEligibilityReason.UnsupportedDevice)));

    [Fact]
    public void IsControllerStateTrusted_UnsupportedControllerEnvironment_IsFalse() =>
        Assert.False(StatusPresentation.IsControllerStateTrusted(
            Snapshot(environmentStatus: FrontendControllerEnvironmentStatus.Unsupported, routingReason: FrontendRoutingEligibilityReason.ControllerEnvironmentUnsupported)));

    [Fact]
    public void FormatControllerStatus_UnsupportedDevice_ReportsUnavailable() =>
        Assert.Equal("Unavailable", StatusPresentation.FormatControllerStatus(
            StatusPresentation.IsControllerStateTrusted(
                Snapshot(hardwareStatus: FrontendHardwareStatus.Unsupported, routingReason: FrontendRoutingEligibilityReason.UnsupportedDevice)),
            new(FrontendRoutingEligibilityReason.Eligible, FrontendRoutingOperationalState.Passive, Available: true, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: false));

    [Fact]
    public void FormatControllerStatus_UnsupportedControllerEnvironment_ReportsUnavailable() =>
        Assert.Equal("Unavailable", StatusPresentation.FormatControllerStatus(
            StatusPresentation.IsControllerStateTrusted(
                Snapshot(environmentStatus: FrontendControllerEnvironmentStatus.Unsupported, routingReason: FrontendRoutingEligibilityReason.ControllerEnvironmentUnsupported)),
            new(FrontendRoutingEligibilityReason.Eligible, FrontendRoutingOperationalState.Passive, Available: true, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: false));

    [Fact]
    public void FormatControllerStatus_UnavailableRuntime_ReportsUnavailable() =>
        Assert.Equal("Unavailable", StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new(FrontendRoutingEligibilityReason.Eligible, FrontendRoutingOperationalState.Passive, Available: false, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: false));

    [Fact]
    public void IsControllerStateTrusted_NormalEligibleState_IsTrue() =>
        Assert.True(StatusPresentation.IsControllerStateTrusted(Snapshot()));

    [Fact]
    public void IsWarning_RecoveryUnsafe_RemainsWarning() =>
        Assert.True(StatusPresentation.IsWarning(Snapshot(recoverySafe: false)));

    [Fact]
    public void IsWarning_UnsupportedHardware_HidesRecoveryWarning() =>
        Assert.False(StatusPresentation.IsWarning(Snapshot(
            recoverySafe: false,
            hardwareStatus: FrontendHardwareStatus.Unsupported)));

    [Fact]
    public void IsWarning_CompatibilityIndeterminate_RemainsVisible() =>
        Assert.True(StatusPresentation.IsWarning(Snapshot(routingReason: FrontendRoutingEligibilityReason.DeviceCompatibilityIndeterminate)));

    [Fact]
    public void IsWarning_SetupRequired_IsWarning() =>
        Assert.True(StatusPresentation.IsWarning(Snapshot(addonStatus: FrontendAddonOperationalStatus.SetupRequired)));

    [Fact]
    public void IsWarning_NormalReadyState_IsNotWarning() =>
        Assert.False(StatusPresentation.IsWarning(Snapshot()));

    [Fact]
    public void IsControllerStateTrusted_IndeterminateEligibilityReason_IsFalse() =>
        Assert.False(StatusPresentation.IsControllerStateTrusted(Snapshot(routingReason: FrontendRoutingEligibilityReason.Indeterminate)));

    [Fact]
    public void FormatControllerStatus_IndeterminateOperationalState_ReportsUnavailable() =>
        Assert.Equal("Unavailable", StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new(FrontendRoutingEligibilityReason.Eligible, FrontendRoutingOperationalState.Indeterminate, Available: true, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: false));

    [Fact]
    public void FormatSteamGame_IndeterminateSource_ReportsNotRunning() =>
        Assert.Equal("Not Running", StatusPresentation.FormatSteamGame(new FrontendSteamSnapshot(true, 123, FrontendSteamSource.Indeterminate)));

    [Fact]
    public void IsWarning_IndeterminateAddonStatus_IsWarning() =>
        Assert.True(StatusPresentation.IsWarning(Snapshot(addonStatus: FrontendAddonOperationalStatus.Indeterminate)));

    private static FrontendStatusSnapshot Snapshot(
        bool recoverySafe = true,
        FrontendRoutingEligibilityReason routingReason = FrontendRoutingEligibilityReason.Eligible,
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
            new(routingReason, FrontendRoutingOperationalState.Passive, true, false, false),
            addonStatus, "Test", recoverySafe,
            FrontendSetupStatus.Indeterminate, "", false);
}
