using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class FrontendContractTests
{
    [Fact]
    public void Wire_contract_round_trips_through_SystemTextJson()
    {
        var value = new FrontendStatusSnapshot(
            new("MSI", "Claw", "BOARD", ["GPU"]),
            new("Supported", "Claw", "EX", "Matched"),
            [new("MsiCenterM", "MSI Center M", "Installed", "Running", "Ready")],
            "Supported", "StockCenterMOnlySupported",
            new("Ready", "", "Ready", "", "Ready", ""),
            new(true, 480, "BigPicture"),
            new("Eligible", "OverrideActive", true, true),
            "Ready", "Eligible", true, false,
            FrontendSetupStatus.Complete, "Complete", false);

        var json = JsonSerializer.Serialize(value);
        var restored = JsonSerializer.Deserialize<FrontendStatusSnapshot>(json);

        Assert.NotNull(restored);
        Assert.Equal(value.Hardware, restored!.Hardware);
        Assert.Equal(value.Steam, restored.Steam);
        Assert.Equal(value.Routing, restored.Routing);
        Assert.Equal(value.RecoverySafe, restored.RecoverySafe);
        Assert.Equal(value.AddonOwnedOutputIdentityUncertain, restored.AddonOwnedOutputIdentityUncertain);
        Assert.Equal(value.ControllerSoftware, restored.ControllerSoftware);
    }

    [Fact]
    public void Mapper_preserves_fail_closed_safety_values()
    {
        var runtime = new SystemStatusSnapshot(
            new("MSI", "Claw", "BOARD", []),
            new(HardwareCompatibilityStatus.Indeterminate, null, null, "ProbeFailed"),
            [new(ControllerSoftwareKind.MsiCenterM, "MSI Center M", SoftwareInstallationStatus.Indeterminate, SoftwareRuntimeStatus.Indeterminate, "Unknown")],
            new(ControllerEnvironmentCompatibilityStatus.Indeterminate, ControllerEnvironmentCompatibilityReason.ControllerSoftwareStateIndeterminate),
            new(new(PrerequisiteKind.HidHide, PrerequisiteStatus.Indeterminate, "Unknown"), new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Indeterminate, "Unknown"), new(PrerequisiteKind.Viiper, PrerequisiteStatus.Indeterminate, "Unknown")),
            new(false, 0, SteamSessionSource.Actual),
            new(RoutingDecisionKind.Indeterminate, RoutingDecisionReason.DeviceCompatibilityIndeterminate),
            new(AddonOperationalStatus.Indeterminate, "Unknown"),
            false,
            true);

        var mapped = FrontendSnapshotMapper.Map(runtime, RoutingRuntimeStatusSnapshot.Unavailable);

        Assert.False(mapped.RecoverySafe);
        Assert.True(mapped.AddonOwnedOutputIdentityUncertain);
        Assert.Equal("Indeterminate", mapped.Hardware.Status);
        Assert.Equal("Passive", mapped.Routing.OperationalState);
        Assert.False(mapped.CanInstallRequiredComponents);
    }
}
