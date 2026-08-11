using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Input.DirectInput;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawDirectInputDeviceSelectorTests
{
    [Fact]
    public void NoPid1902Candidate_IsNotFound()
    {
        var result = MsiClawDirectInputDeviceSelector.Select([Device(0x045E, 0x028E)]);
        Assert.Equal(MsiClawDirectInputSelectionStatus.NotFound, result.Status);
        Assert.Equal("Pid1902NotFound", result.Reason);
    }

    [Fact]
    public void VerifiedCandidate_IsSelected()
    {
        var result = MsiClawDirectInputDeviceSelector.Select([Device(0x0DB0, 0x1902)]);
        Assert.True(result.IsSelected);
        Assert.Equal("VerifiedMsiPhysicalRoot", result.Reason);
    }

    [Fact]
    public void VerifiedAliases_SelectDeterministically()
    {
        var first = Device(0x0DB0, 0x1902) with { InstanceGuid = Guid.Parse("00000000-0000-0000-0000-000000000002") };
        var second = Device(0x0DB0, 0x1902) with { InstanceGuid = Guid.Parse("00000000-0000-0000-0000-000000000001") };
        var result = MsiClawDirectInputDeviceSelector.Select([first, second]);
        var reversed = MsiClawDirectInputDeviceSelector.Select([second, first]);
        Assert.Equal(MsiClawDirectInputSelectionStatus.Selected, result.Status);
        Assert.Equal("VerifiedMsiPhysicalRootAlias", result.Reason);
        Assert.Equal(second.InstanceGuid, result.Descriptor!.InstanceGuid);
        Assert.Equal(result.Descriptor, reversed.Descriptor);
        Assert.Equal(2, result.CandidateCount);
    }

    [Fact]
    public void DifferentPhysicalRoots_FailClosed()
    {
        var result = MsiClawDirectInputDeviceSelector.Select([Device(0x0DB0, 0x1902), Device(0x0DB0, 0x1902) with { PhysicalIdentity = "USB\\OTHER" }]);
        Assert.Equal(MsiClawDirectInputSelectionStatus.Indeterminate, result.Status);
        Assert.Equal("MultiplePhysicalIdentities", result.Reason);
    }

    [Fact]
    public void DifferentPnpIdentities_FailClosed()
    {
        var result = MsiClawDirectInputDeviceSelector.Select([Device(0x0DB0, 0x1902), Device(0x0DB0, 0x1902) with { PnpInstanceId = "HID\\OTHER" }]);
        Assert.Equal(MsiClawDirectInputSelectionStatus.Indeterminate, result.Status);
        Assert.Equal("MultiplePnpInstanceIdentities", result.Reason);
    }

    [Fact]
    public void UnverifiedTopologyAndInsufficientButtons_FailClosed()
    {
        Assert.Equal(MsiClawDirectInputSelectionStatus.Indeterminate, MsiClawDirectInputDeviceSelector.Select([Device(0x0DB0, 0x1902) with { PhysicalIdentity = null }]).Status);
        var result = MsiClawDirectInputDeviceSelector.Select([Device(0x0DB0, 0x1902) with { ButtonCount = 16 }]);
        Assert.Equal("InsufficientButtonCount", result.Reason);
    }

    private static DirectInputDeviceDescriptor Device(ushort vendorId, ushort productId) => new(
        Guid.NewGuid(), Guid.NewGuid(), "test", vendorId, productId,
        "HID\\VID_0DB0&PID_1902&MI_00&COL01\\TEST", "HID\\INSTANCE", "USB\\MSI_ROOT", 0x0001, 0x0005, 17, 6, "Verified");
}
