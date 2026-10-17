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
    [InlineData(FrontendControllerBadgeState.Xbox360Active, "XBOX 360 · Active")]
    [InlineData(FrontendControllerBadgeState.SteamDeckActive, "Steam Deck · Active")]
    [InlineData(FrontendControllerBadgeState.DualSenseEdgeActive, "DualSense Edge · Active")]
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

    [Theory]
    [InlineData(FrontendHardwareStatus.Supported, FrontendControllerBadgeState.Xbox360Active, "Success")]
    [InlineData(FrontendHardwareStatus.Supported, FrontendControllerBadgeState.SteamDeckActive, "Success")]
    [InlineData(FrontendHardwareStatus.Supported, FrontendControllerBadgeState.DualSenseEdgeActive, "Success")]
    [InlineData(FrontendHardwareStatus.Supported, FrontendControllerBadgeState.MsiNative, "Success")]
    [InlineData(FrontendHardwareStatus.Supported, FrontendControllerBadgeState.Initializing, "Caution")]
    [InlineData(FrontendHardwareStatus.Supported, FrontendControllerBadgeState.Reconnecting, "Caution")]
    [InlineData(FrontendHardwareStatus.Supported, FrontendControllerBadgeState.NeedsAttention, "Critical")]
    [InlineData(FrontendHardwareStatus.Supported, FrontendControllerBadgeState.Unavailable, "Neutral")]
    [InlineData(FrontendHardwareStatus.Unsupported, FrontendControllerBadgeState.Xbox360Active, "Critical")]
    [InlineData(FrontendHardwareStatus.Indeterminate, FrontendControllerBadgeState.Xbox360Active, "Neutral")]
    public void ControllerIndicatorTone_MapsStatusAndPreservesHardwareOverrides(
        FrontendHardwareStatus hardwareStatus,
        FrontendControllerBadgeState badge,
        string expected) =>
        Assert.Equal(expected, DeviceSummaryPresentation.ControllerIndicatorTone(hardwareStatus, badge).ToString());

    [Theory]
    [InlineData(FrontendStartupTaskState.Verified, false, "Success")]
    [InlineData(FrontendStartupTaskState.NeedsAttention, false, "Critical")]
    [InlineData(FrontendStartupTaskState.Unknown, false, "Neutral")]
    [InlineData(FrontendStartupTaskState.NeedsAttention, true, "Caution")]
    [InlineData(FrontendStartupTaskState.Unknown, true, "Caution")]
    public void StartupTaskIndicatorTone_MapsStateAndRepairProgress(
        FrontendStartupTaskState state,
        bool repairing,
        string expected) =>
        Assert.Equal(expected, DeviceSummaryPresentation.StartupTaskIndicatorTone(
            new FrontendStartupTaskSnapshot(state, FrontendStartupTaskIssue.None), repairing).ToString());

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
            ControllerBadge = FrontendControllerBadgeState.SteamDeckActive,
            StartupTask = new(FrontendStartupTaskState.NeedsAttention, FrontendStartupTaskIssue.ReadFailed)
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
        Assert.Equal(FrontendStartupTaskSnapshot.Unknown, unavailable.StartupTask);
        Assert.Equal(DeviceStatusIndicatorTone.Neutral,
            DeviceSummaryPresentation.ControllerIndicatorTone(unavailable.Hardware.Status, unavailable.ControllerBadge));
        Assert.Equal(DeviceStatusIndicatorTone.Neutral,
            DeviceSummaryPresentation.StartupTaskIndicatorTone(unavailable.StartupTask, repairing: false));
    }

    [Theory]
    [InlineData(FrontendStartupTaskState.Unknown, false, "Device_AutoStart_Unknown")]
    [InlineData(FrontendStartupTaskState.Verified, false, "Device_AutoStart_Verified")]
    [InlineData(FrontendStartupTaskState.NeedsAttention, false, "Device_AutoStart_NeedsAttention")]
    [InlineData(FrontendStartupTaskState.NeedsAttention, true, "Device_AutoStart_Repairing")]
    public void StartupTaskStatusResourceKey_maps_the_typed_state(
        FrontendStartupTaskState state,
        bool repairing,
        string expected) =>
        Assert.Equal(expected, DeviceSummaryPresentation.StartupTaskStatusResourceKey(
            new FrontendStartupTaskSnapshot(state, FrontendStartupTaskIssue.None), repairing));

    [Theory]
    [InlineData(FrontendStartupTaskIssue.ReadFailed, "Device_AutoStart_Issue_ReadFailed")]
    [InlineData(FrontendStartupTaskIssue.StableExecutableMissing, "Device_AutoStart_Issue_StableExecutableMissing")]
    [InlineData(FrontendStartupTaskIssue.RegistrationFailed, "Device_AutoStart_Issue_RegistrationFailed")]
    [InlineData(FrontendStartupTaskIssue.ReadbackUnverified, "Device_AutoStart_Issue_ReadbackUnverified")]
    [InlineData(FrontendStartupTaskIssue.None, null)]
    public void StartupTaskIssueResourceKey_preserves_the_failure_cause(
        FrontendStartupTaskIssue issue,
        string? expected) =>
        Assert.Equal(expected, DeviceSummaryPresentation.StartupTaskIssueResourceKey(issue));

    [Theory]
    [InlineData(FrontendStartupTaskState.Unknown, true, false, true, true)]
    [InlineData(FrontendStartupTaskState.NeedsAttention, true, false, true, true)]
    [InlineData(FrontendStartupTaskState.Verified, true, false, false, false)]
    [InlineData(FrontendStartupTaskState.Unknown, false, false, false, false)]
    [InlineData(FrontendStartupTaskState.NeedsAttention, false, false, true, false)]
    [InlineData(FrontendStartupTaskState.NeedsAttention, true, true, true, false)]
    public void StartupTaskRepair_visibility_and_enabled_state_follow_runtime_availability(
        FrontendStartupTaskState state,
        bool runtimeResponsive,
        bool repairing,
        bool expectedVisible,
        bool expectedCanRepair)
    {
        var snapshot = new FrontendStartupTaskSnapshot(state, FrontendStartupTaskIssue.None);

        Assert.Equal(expectedVisible,
            DeviceSummaryPresentation.ShouldShowStartupTaskRepair(snapshot, runtimeResponsive));
        Assert.Equal(expectedCanRepair,
            DeviceSummaryPresentation.CanRepairStartupTask(snapshot, runtimeResponsive, repairing));
    }
}
