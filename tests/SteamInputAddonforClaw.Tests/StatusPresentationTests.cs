using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class StatusPresentationTests
{
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
            routingStatus: new(RoutingOperationalState.Passive, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: false);

        Assert.Equal("MSI Center M Native", status);
    }

    [Fact]
    public void FormatControllerStatus_PassiveWithVerifiedXInput_AppendsQualifier()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new(RoutingOperationalState.Passive, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: true);

        Assert.Equal("MSI Center M Native (XInput)", status);
    }

    [Fact]
    public void FormatControllerStatus_ActualStockRoutingActive_ReportsSteamController()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new(RoutingOperationalState.OverrideActive, SteamOutputActive: true, NativeDirectInputActive: true),
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
            routingStatus: new(RoutingOperationalState.Passive, SteamOutputActive: false, NativeDirectInputActive: false),
            nativeXInputVerified: false);

        Assert.NotEqual("Steam Controller (DInput)", status);
    }

    [Fact]
    public void FormatControllerStatus_OverrideActiveWithSteamOutputDisabled_DoesNotClaimSteamController()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new(RoutingOperationalState.OverrideActive, SteamOutputActive: false, NativeDirectInputActive: true),
            nativeXInputVerified: false);

        Assert.NotEqual("Steam Controller (DInput)", status);
    }

    [Fact]
    public void FormatControllerStatus_OverrideActiveWithoutNativeDirectInputConfirmed_DoesNotClaimSteamController()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: true,
            routingStatus: new(RoutingOperationalState.OverrideActive, SteamOutputActive: true, NativeDirectInputActive: false),
            nativeXInputVerified: false);

        Assert.NotEqual("Steam Controller (DInput)", status);
    }

    [Fact]
    public void FormatControllerStatus_UntrustedState_ReportsUnavailableRegardlessOfRoutingState()
    {
        var status = StatusPresentation.FormatControllerStatus(
            stateTrusted: false,
            routingStatus: new(RoutingOperationalState.OverrideActive, SteamOutputActive: true, NativeDirectInputActive: true),
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
        AddonOperationalStatus addonStatus = AddonOperationalStatus.Ready) =>
        new(
            new DeviceStatusSnapshot("Test", "Test", "Test", []),
            new HardwareCompatibilityAssessment(HardwareCompatibilityStatus.Supported, null, null, "Test"),
            [],
            null!,
            null!,
            new SteamStatusSnapshot(false, 0),
            new RoutingDecision(RoutingDecisionKind.Eligible, routingReason),
            new AddonStatusSnapshot(addonStatus, "Test"),
            recoverySafe,
            addonOwnedOutputIdentityUncertain);
}
