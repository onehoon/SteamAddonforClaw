using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Status;
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
            routingStatus: new("Eligible", FrontendRoutingOperationalState.Passive, Available: true, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: false);

        Assert.Equal("MSI Center M Native", status);
    }

    [Fact]
    public void FormatControllerStatus_PassiveWithVerifiedXInput_AppendsQualifier()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new("Eligible", FrontendRoutingOperationalState.Passive, Available: true, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: true);

        Assert.Equal("MSI Center M Native (XInput)", status);
    }

    [Fact]
    public void FormatControllerStatus_ActualStockRoutingActive_ReportsSteamController()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new("Eligible", FrontendRoutingOperationalState.OverrideActive, Available: true, SteamOutputActive: true, NativeDirectInputActive: true),
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
            routingStatus: new("Eligible", FrontendRoutingOperationalState.Passive, Available: true, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: false);

        Assert.NotEqual("Steam Controller (DInput)", status);
    }

    [Fact]
    public void FormatControllerStatus_OverrideActiveWithSteamOutputDisabled_DoesNotClaimSteamController()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new("Eligible", FrontendRoutingOperationalState.OverrideActive, Available: true, SteamOutputActive: false, NativeDirectInputActive: true),
            nativeXInputVerified: false);

        Assert.NotEqual("Steam Controller (DInput)", status);
    }

    [Fact]
    public void FormatControllerStatus_OverrideActiveWithoutNativeDirectInputConfirmed_DoesNotClaimSteamController()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new("Eligible", FrontendRoutingOperationalState.OverrideActive, Available: true, SteamOutputActive: true, NativeDirectInputActive: false),
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
            routingStatus: new("Eligible", FrontendRoutingOperationalState.OverrideActive, Available: true, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: false);

        Assert.Equal("Unavailable", status);
    }

    [Fact]
    public void FormatControllerStatus_UntrustedState_ReportsUnavailableRegardlessOfRoutingState()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: false,
            routingStatus: new("Eligible", FrontendRoutingOperationalState.OverrideActive, Available: true, SteamOutputActive: true, NativeDirectInputActive: true),
            nativeXInputVerified: false);

        Assert.Equal("Unavailable", status);
    }

    [Fact]
    public void IsControllerStateTrusted_RecoveryUnsafe_IsFalse() =>
        Assert.False(StatusPresentation.IsControllerStateTrusted(Snapshot(recoverySafe: false)));

    [Fact]
    public void IsControllerStateTrusted_AddonOwnedOutputIdentityUncertain_IsFalse() =>
        Assert.False(StatusPresentation.IsControllerStateTrusted(Snapshot(addonOwnedOutputIdentityUncertain: true)));

    [Fact]
    public void IsControllerStateTrusted_DeviceCompatibilityIndeterminate_IsFalse() =>
        Assert.False(StatusPresentation.IsControllerStateTrusted(Snapshot(routingReason: RoutingDecisionReason.DeviceCompatibilityIndeterminate)));

    [Fact]
    public void IsControllerStateTrusted_ControllerEnvironmentIndeterminate_IsFalse() =>
        Assert.False(StatusPresentation.IsControllerStateTrusted(Snapshot(routingReason: RoutingDecisionReason.ControllerEnvironmentIndeterminate)));

    [Fact]
    public void IsControllerStateTrusted_UnsupportedDevice_IsFalse() =>
        Assert.False(StatusPresentation.IsControllerStateTrusted(
            Snapshot(hardwareStatus: HardwareCompatibilityStatus.Unsupported, routingReason: RoutingDecisionReason.UnsupportedDevice)));

    [Fact]
    public void IsControllerStateTrusted_UnsupportedControllerEnvironment_IsFalse() =>
        Assert.False(StatusPresentation.IsControllerStateTrusted(
            Snapshot(environmentStatus: ControllerEnvironmentCompatibilityStatus.Unsupported, routingReason: RoutingDecisionReason.ControllerEnvironmentUnsupported)));

    [Fact]
    public void FormatControllerStatus_UnsupportedDevice_ReportsUnavailable() =>
        Assert.Equal("Unavailable", StatusPresentation.FormatControllerStatus(
            StatusPresentation.IsControllerStateTrusted(
                Snapshot(hardwareStatus: HardwareCompatibilityStatus.Unsupported, routingReason: RoutingDecisionReason.UnsupportedDevice)),
            new("Eligible", FrontendRoutingOperationalState.Passive, Available: true, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: false));

    [Fact]
    public void FormatControllerStatus_UnsupportedControllerEnvironment_ReportsUnavailable() =>
        Assert.Equal("Unavailable", StatusPresentation.FormatControllerStatus(
            StatusPresentation.IsControllerStateTrusted(
                Snapshot(environmentStatus: ControllerEnvironmentCompatibilityStatus.Unsupported, routingReason: RoutingDecisionReason.ControllerEnvironmentUnsupported)),
            new("Eligible", FrontendRoutingOperationalState.Passive, Available: true, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: false));

    [Fact]
    public void FormatControllerStatus_UnavailableRuntime_ReportsUnavailable() =>
        Assert.Equal("Unavailable", StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new("Eligible", FrontendRoutingOperationalState.Passive, Available: false, SteamOutputActive: false, NativeDirectInputActive: false),
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
            hardwareStatus: HardwareCompatibilityStatus.Unsupported)));

    [Fact]
    public void IsWarning_UnsupportedHardware_WithUncertainAddonOutput_RemainsWarning() =>
        Assert.True(StatusPresentation.IsWarning(Snapshot(
            addonOwnedOutputIdentityUncertain: true,
            hardwareStatus: HardwareCompatibilityStatus.Unsupported)));

    [Fact]
    public void IsWarning_AddonOwnedOutputIdentityUncertain_RemainsWarning() =>
        Assert.True(StatusPresentation.IsWarning(Snapshot(addonOwnedOutputIdentityUncertain: true)));

    [Fact]
    public void IsWarning_CompatibilityIndeterminate_RemainsVisible() =>
        Assert.True(StatusPresentation.IsWarning(Snapshot(routingReason: RoutingDecisionReason.DeviceCompatibilityIndeterminate)));

    [Fact]
    public void IsWarning_SetupRequired_IsWarning() =>
        Assert.True(StatusPresentation.IsWarning(Snapshot(addonStatus: AddonOperationalStatus.SetupRequired)));

    [Fact]
    public void IsWarning_NormalReadyState_IsNotWarning() =>
        Assert.False(StatusPresentation.IsWarning(Snapshot()));

    private static FrontendStatusSnapshot Snapshot(
        bool recoverySafe = true,
        bool addonOwnedOutputIdentityUncertain = false,
        RoutingDecisionReason routingReason = RoutingDecisionReason.Eligible,
        AddonOperationalStatus addonStatus = AddonOperationalStatus.Ready,
        HardwareCompatibilityStatus hardwareStatus = HardwareCompatibilityStatus.Supported,
        ControllerEnvironmentCompatibilityStatus environmentStatus = ControllerEnvironmentCompatibilityStatus.Supported) =>
        new(
            new("Test", "Test", "Test", []),
            new(hardwareStatus == HardwareCompatibilityStatus.Supported ? FrontendHardwareStatus.Supported : hardwareStatus == HardwareCompatibilityStatus.Unsupported ? FrontendHardwareStatus.Unsupported : FrontendHardwareStatus.Indeterminate, "", "", "Test"),
            [],
            environmentStatus == ControllerEnvironmentCompatibilityStatus.Supported ? FrontendControllerEnvironmentStatus.Supported : environmentStatus == ControllerEnvironmentCompatibilityStatus.Unsupported ? FrontendControllerEnvironmentStatus.Unsupported : FrontendControllerEnvironmentStatus.Indeterminate,
            "Test",
            new(FrontendPrerequisiteStatus.Ready, "", FrontendPrerequisiteStatus.Ready, "", FrontendPrerequisiteStatus.Ready, ""),
            new(false, 0, FrontendSteamSource.Actual),
            new(routingReason.ToString(), FrontendRoutingOperationalState.Passive, true, false, false),
            addonStatus.ToString(), "Test", recoverySafe, addonOwnedOutputIdentityUncertain,
            FrontendSetupStatus.Indeterminate, "", false);
}
