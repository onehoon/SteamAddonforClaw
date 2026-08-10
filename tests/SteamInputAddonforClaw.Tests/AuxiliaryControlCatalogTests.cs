using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class AuxiliaryControlCatalogTests
{
    [Fact]
    public void MsiClawCatalog_UsesCanonicalRearSidesAndOrder()
    {
        Assert.Equal(MsiClawControls.M2, MsiClawControls.Catalog[0].Id);
        Assert.Equal(ControlSide.Left, MsiClawControls.Catalog[0].Side);
        Assert.Equal(MsiClawControls.M1, MsiClawControls.Catalog[1].Id);
        Assert.Equal(ControlSide.Right, MsiClawControls.Catalog[1].Side);
    }

    [Fact]
    public void MsiClawControls_MapToCanonicalSteamGrips()
    {
        Assert.Equal(SteamGripSide.Left, MsiClawSteamInputMapping.GetGrip(MsiClawControls.M2));
        Assert.Equal(SteamGripSide.Right, MsiClawSteamInputMapping.GetGrip(MsiClawControls.M1));
    }
    [Fact]
    public void DefaultDeviceAndControlIds_AreInvalidAndRejectedByDescriptors()
    {
        Assert.False(default(HandheldDeviceId).IsValid);
        Assert.False(default(AuxiliaryControlId).IsValid);
        Assert.Throws<ArgumentException>(() => new HandheldDeviceDescriptor(default, "Test", "Device", "Test Device"));
        Assert.Throws<ArgumentException>(() => new AuxiliaryControlDescriptor(default, "Control", ControlSurface.Unknown, ControlSide.Unknown));
    }

    [Fact]
    public void EmptyCatalog_IsValid()
    {
        var catalog = new AuxiliaryControlCatalog([]);

        Assert.Equal(0, catalog.Count);
    }

    [Fact]
    public void Controls_PreserveRegistrationOrderAndResolveBothDirections()
    {
        var first = Control("device.first", "First");
        var second = Control("device.second", "Second");
        var catalog = new AuxiliaryControlCatalog([first, second]);

        Assert.Equal(first, catalog[0]);
        Assert.Equal(second, catalog[1]);
        Assert.Equal(0, catalog.GetIndex(first.Id));
        Assert.Equal(1, catalog.GetIndex(second.Id));
    }

    [Fact]
    public void Catalog_SupportsSixOrMoreControls()
    {
        var controls = Enumerable.Range(1, 6).Select(index => Control($"device.control-{index}", $"Control {index}"));
        var catalog = new AuxiliaryControlCatalog(controls);

        Assert.Equal(6, catalog.Count);
        Assert.Equal("Control 6", catalog[5].DisplayName);
    }

    [Fact]
    public void DuplicateControlId_IsRejected()
    {
        var id = new AuxiliaryControlId("device.duplicate");

        Assert.Throws<ArgumentException>(() => new AuxiliaryControlCatalog([Control(id, "First"), Control(id, "Second")]));
    }

    [Fact]
    public void UnknownControlId_AndInvalidIndex_AreExplicitFailures()
    {
        var catalog = new AuxiliaryControlCatalog([Control("device.first", "First")]);

        Assert.Throws<KeyNotFoundException>(() => catalog.GetIndex(new AuxiliaryControlId("device.unknown")));
        Assert.Throws<IndexOutOfRangeException>(() => _ = catalog[-1]);
        Assert.Throws<IndexOutOfRangeException>(() => _ = catalog[1]);
    }

    private static AuxiliaryControlDescriptor Control(string id, string displayName) => Control(new AuxiliaryControlId(id), displayName);
    private static AuxiliaryControlDescriptor Control(AuxiliaryControlId id, string displayName) => new(id, displayName, ControlSurface.Unknown, ControlSide.Unknown);
}
