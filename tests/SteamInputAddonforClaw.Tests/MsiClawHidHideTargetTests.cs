using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawHidHideTargetTests
{
    private const string Root = @"USB\VID_0DB0&PID_1902\SERIAL123";
    private const string OtherRoot = @"USB\VID_0DB0&PID_1902\SERIAL999";
    private const string Primary = @"HID\VID_0DB0&PID_1902&MI_00&COL01\7&ABC&0&0000";
    private const string Control = @"HID\VID_0DB0&PID_1902&MI_00&COL02\7&ABC&0&0001";
    private const string Consumer = @"HID\VID_0DB0&PID_1902&MI_01&COL03\7&DEF&0&0002";

    [Fact]
    public void Resolver_returns_primary_control_and_consumer_in_stable_order_and_excludes_keyboard_mouse()
    {
        var primary = Device(Primary, Root, 0x0001, 0x0005, "MI_00");
        var devices = new[]
        {
            Device(Consumer, Root, 0x000C, 0x0001, "MI_01"),
            Device(@"HID\VID_0DB0&PID_1902&MI_01&COL01\7&DEF&0&0000", Root, 0x0001, 0x0006, "MI_01"),
            Device(Control, Root, 0xFFF0, 0x0040, "MI_00"),
            Device(@"HID\VID_0DB0&PID_1902&MI_01&COL02\7&DEF&0&0001", Root, 0x0001, 0x0002, "MI_01"),
            primary,
        };

        var result = MsiClawHardware.ResolveOwnedPid1902HidHideTargets(primary, devices);

        Assert.Equal(new[] { Primary, Control, Consumer }, result.Targets);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Resolver_excludes_auxiliary_candidates_from_a_different_physical_root()
    {
        var primary = Device(Primary, Root, 0x0001, 0x0005, "MI_00");
        var result = MsiClawHardware.ResolveOwnedPid1902HidHideTargets(primary,
        [
            primary,
            Device(Control, OtherRoot, 0xFFF0, 0x0040, "MI_00"),
            Device(Consumer, OtherRoot, 0x000C, 0x0001, "MI_01"),
        ]);

        Assert.Equal([Primary], result.Targets);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Resolver_allows_missing_auxiliary_collections()
    {
        var primary = Device(Primary, Root, 0x0001, 0x0005, "MI_00");

        Assert.Equal([Primary], MsiClawHardware.ResolveOwnedPid1902HidHideTargets(primary, [primary]).Targets);
        Assert.Equal([Primary, Control], MsiClawHardware.ResolveOwnedPid1902HidHideTargets(primary,
            [primary, Device(Control, Root, 0xFFF0, 0x0040, "MI_00")]).Targets);
    }

    [Fact]
    public void Resolver_omits_ambiguous_auxiliary_kind_but_keeps_primary()
    {
        var primary = Device(Primary, Root, 0x0001, 0x0005, "MI_00");
        var result = MsiClawHardware.ResolveOwnedPid1902HidHideTargets(primary,
        [
            primary,
            Device(Control, Root, 0xFFF0, 0x0040, "MI_00"),
            Device(@"HID\VID_0DB0&PID_1902&MI_00&COL02\7&ABC&0&0009", Root, 0xFFF0, 0x0040, "MI_00"),
            Device(Consumer, Root, 0x000C, 0x0001, "MI_01"),
        ]);

        Assert.Equal([Primary, Consumer], result.Targets);
        Assert.Contains("ControlAmbiguous:2", result.Diagnostics);
    }

    [Fact]
    public void Persisted_selector_migrates_primary_only_and_preserves_unambiguous_auxiliary_set()
    {
        Assert.Equal([Primary], MsiClawHardware.SelectPersistedOwnedPid1902HidHideTargets([Primary]));
        Assert.Equal([Primary, Control, Consumer],
            MsiClawHardware.SelectPersistedOwnedPid1902HidHideTargets(["foreign", Primary, Consumer, Control]));
        Assert.Equal([Primary, Control], MsiClawHardware.SelectPersistedOwnedPid1902HidHideTargets([Primary, Control]));
    }

    [Fact]
    public void Persisted_selector_discards_invalid_or_ambiguous_primary_sets_without_guessing()
    {
        Assert.Empty(MsiClawHardware.SelectPersistedOwnedPid1902HidHideTargets([Control, Consumer]));
        Assert.Empty(MsiClawHardware.SelectPersistedOwnedPid1902HidHideTargets([Primary, @"HID\VID_0DB0&PID_1902&MI_00&COL01\other"]));

        Assert.Equal([Primary], MsiClawHardware.SelectPersistedOwnedPid1902HidHideTargets([
            Primary,
            Control,
            @"HID\VID_0DB0&PID_1902&MI_00&COL02\7&ABC&0&0009"]));
    }

    private static ControllerDeviceInfo Device(string instanceId, string root, ushort usagePage, ushort usage, string interfaceId) =>
        new(
            InstanceId: instanceId,
            ContainerId: null,
            ParentInstanceId: $@"USB\VID_0DB0&PID_1902&{interfaceId}\PARENT",
            AncestorInstanceIds: [root],
            EnumeratorName: "HID",
            HardwareIds: [],
            CompatibleIds: [],
            ClassName: "HIDClass",
            ClassGuid: null,
            Service: "HidUsb",
            VendorId: MsiClawHardware.VendorId,
            ProductId: MsiClawHardware.DirectInputProductId,
            Present: true,
            UsagePage: usagePage,
            Usage: usage);
}
