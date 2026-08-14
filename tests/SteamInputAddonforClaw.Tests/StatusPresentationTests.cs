using SteamInputAddonforClaw.Devices;
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

    [Theory]
    [InlineData(false, "Not Running")]
    [InlineData(true, "Running")]
    public void FormatSteamGame_ReflectsSteamIsActive(bool isActive, string expected) =>
        Assert.Equal(expected, StatusPresentation.FormatSteamGame(isActive));

    [Fact]
    public void FormatControllerStatus_PassiveWithoutVerifiedXInput_OmitsQualifier()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new(Available: true, OperationalState: RoutingOperationalState.Passive, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: false);

        Assert.Equal("MSI Center M Native", status);
    }

    [Fact]
    public void FormatControllerStatus_PassiveWithVerifiedXInput_AppendsQualifier()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new(Available: true, OperationalState: RoutingOperationalState.Passive, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: true);

        Assert.Equal("MSI Center M Native (XInput)", status);
    }

    [Fact]
    public void FormatControllerStatus_ActualStockRoutingActive_ReportsSteamController()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new(Available: true, OperationalState: RoutingOperationalState.OverrideActive, SteamOutputActive: true, NativeDirectInputActive: true),
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
            routingStatus: new(Available: true, OperationalState: RoutingOperationalState.Passive, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: false);

        Assert.NotEqual("Steam Controller (DInput)", status);
    }

    [Fact]
    public void FormatControllerStatus_OverrideActiveWithSteamOutputDisabled_DoesNotClaimSteamController()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new(Available: true, OperationalState: RoutingOperationalState.OverrideActive, SteamOutputActive: false, NativeDirectInputActive: true),
            nativeXInputVerified: false);

        Assert.NotEqual("Steam Controller (DInput)", status);
    }

    [Fact]
    public void FormatControllerStatus_OverrideActiveWithoutNativeDirectInputConfirmed_DoesNotClaimSteamController()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new(Available: true, OperationalState: RoutingOperationalState.OverrideActive, SteamOutputActive: true, NativeDirectInputActive: false),
            nativeXInputVerified: false);

        Assert.NotEqual("Steam Controller (DInput)", status);
    }

    [Fact]
    public void FormatControllerStatus_UntrustedState_ReportsUnavailableRegardlessOfRoutingState()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: false,
            routingStatus: new(Available: true, OperationalState: RoutingOperationalState.OverrideActive, SteamOutputActive: true, NativeDirectInputActive: true),
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
            new(Available: true, OperationalState: RoutingOperationalState.Passive, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: false));

    [Fact]
    public void FormatControllerStatus_UnsupportedControllerEnvironment_ReportsUnavailable() =>
        Assert.Equal("Unavailable", StatusPresentation.FormatControllerStatus(
            StatusPresentation.IsControllerStateTrusted(
                Snapshot(environmentStatus: ControllerEnvironmentCompatibilityStatus.Unsupported, routingReason: RoutingDecisionReason.ControllerEnvironmentUnsupported)),
            new(Available: true, OperationalState: RoutingOperationalState.Passive, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: false));

    [Fact]
    public void FormatControllerStatus_UnavailableRuntime_ReportsUnavailable() =>
        Assert.Equal("Unavailable", StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: RoutingRuntimeStatusSnapshot.Unavailable,
            nativeXInputVerified: false));

    [Fact]
    public void IsControllerStateTrusted_NormalEligibleState_IsTrue() =>
        Assert.True(StatusPresentation.IsControllerStateTrusted(Snapshot()));

    [Fact]
    public void IsWarning_RecoveryUnsafe_RemainsWarning() =>
        Assert.True(StatusPresentation.IsWarning(Snapshot(recoverySafe: false)));

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

    private static SystemStatusSnapshot Snapshot(
        bool recoverySafe = true,
        bool addonOwnedOutputIdentityUncertain = false,
        RoutingDecisionReason routingReason = RoutingDecisionReason.Eligible,
        AddonOperationalStatus addonStatus = AddonOperationalStatus.Ready,
        HardwareCompatibilityStatus hardwareStatus = HardwareCompatibilityStatus.Supported,
        ControllerEnvironmentCompatibilityStatus environmentStatus = ControllerEnvironmentCompatibilityStatus.Supported) =>
        new(
            new DeviceStatusSnapshot("Test", "Test", "Test", []),
            new HardwareCompatibilityAssessment(hardwareStatus, null, null, "Test"),
            [],
            new ControllerEnvironmentCompatibilityAssessment(environmentStatus, ControllerEnvironmentCompatibilityReason.StockCenterMOnlySupported),
            null!,
            new SteamStatusSnapshot(false, 0),
            new RoutingDecision(RoutingDecisionKind.Eligible, routingReason),
            new AddonStatusSnapshot(addonStatus, "Test"),
            recoverySafe,
            addonOwnedOutputIdentityUncertain);
}
