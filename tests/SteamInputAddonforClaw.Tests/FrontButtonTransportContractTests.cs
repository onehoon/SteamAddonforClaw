using System.Text.Json;
using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>App UI PR-C section 22.8: the split OEM1/WING frontend contract is replaced by the one
/// atomic front-button mapping contract, and the protocol is bumped exactly once (24 -> 25).</summary>
public sealed class FrontButtonTransportContractTests
{
    [Fact]
    public void Protocol_is_current()
        // PR-C bumped 24 -> 25; Shared Frontend V2 SF-V2-01 subsequently bumped 25 -> 26
        // (CaptureDeviceQuickSettings aggregate).
        => Assert.Equal(26, FrontendTransportProtocol.CurrentVersion);

    [Fact]
    public void The_split_oem1_wing_rpcs_and_snapshot_members_are_gone()
    {
        var methods = Enum.GetNames<FrontendRpcMethod>();
        Assert.DoesNotContain("SetOem1Mapping", methods);
        Assert.DoesNotContain("SetWingMapping", methods);
        Assert.Contains("SetFrontButtonMapping", methods);

        Assert.Null(typeof(FrontendSettingsSnapshot).GetProperty("Oem1Mapping"));
        Assert.Null(typeof(FrontendSettingsSnapshot).GetProperty("WingMapping"));
        Assert.NotNull(typeof(FrontendSettingsSnapshot).GetProperty("FrontButtonMapping"));

        Assert.Null(typeof(IAddonFrontendControl).GetMethod("SetOem1MappingAsync"));
        Assert.Null(typeof(IAddonFrontendControl).GetMethod("SetWingMappingAsync"));
        Assert.NotNull(typeof(IAddonFrontendControl).GetMethod("SetFrontButtonMappingAsync"));
    }

    [Fact]
    public void Settings_snapshot_carries_the_front_button_mapping_and_round_trips()
    {
        var value = new FrontendSettingsSnapshot(FrontendLogLevel.Info, false, FrontButtonMappingSettings.Default.With(
            FrontButtonKind.Gamebar, FrontButtonDomain.Steam, FrontButtonBinding.Of(FrontButtonAction.QuickSettingsOverlay)));
        var restored = JsonSerializer.Deserialize<FrontendSettingsSnapshot>(JsonSerializer.Serialize(value));
        Assert.Equal(value, restored);
    }

    [Fact]
    public void Bootstrap_snapshot_carries_one_front_button_availability_fact()
    {
        var value = new FrontendBootstrapSnapshot(
            new(FrontendLogLevel.Info, false, FrontButtonMappingSettings.Default), new(false), @"C:\Logs", FrontButtonMappingAvailable: true);
        var restored = JsonSerializer.Deserialize<FrontendBootstrapSnapshot>(JsonSerializer.Serialize(value));
        Assert.Equal(value, restored);
        Assert.True(restored!.FrontButtonMappingAvailable);
    }
}
