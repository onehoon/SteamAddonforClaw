using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawModeSwitchTests
{
    [Theory]
    [InlineData(0, 0x01)]
    [InlineData(1, 0x02)]
    public void Mode_command_is_exact_64_byte_report(int modeValue, byte modeByte)
    {
        var mode = (MsiClawNativeMode)modeValue;
        var report = MsiClawModeCommand.Build(mode);
        Assert.Equal(64, report.Length);
        Assert.Equal(new byte[] { 0x0F, 0x00, 0x00, 0x3C, 0x24, modeByte, 0x00 }, report[..7]);
        Assert.All(report[7..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void Strong_identity_requires_container_and_parent()
    {
        var strong = MsiClawPhysicalIdentity.From(Device(Guid.NewGuid(), "USB\\ROOT", "HID\\MSI"));
        var weak = MsiClawPhysicalIdentity.From(Device(null, null, "HID\\MSI"));
        Assert.Equal(MsiClawIdentityConfidence.Strong, strong.Confidence);
        Assert.Equal(MsiClawIdentityConfidence.Indeterminate, weak.Confidence);
        Assert.False(strong.StronglyMatches(weak));
    }

    [Fact]
    public void Sentinel_identity_uses_pid_independent_msi_root_key()
    {
        var sentinel = Guid.Parse("00000000-0000-0000-ffff-ffffffffffff");
        var xinput = Topology(sentinel, "USB\\VID_0DB0&PID_1901\\CLAW_A", 0x1901);
        var directInput = Topology(sentinel, "USB\\VID_0DB0&PID_1902\\CLAW_A", 0x1902);
        var other = Topology(sentinel, "USB\\VID_0DB0&PID_1902\\CLAW_B", 0x1902);

        var first = MsiClawPhysicalIdentity.From(xinput);
        var second = MsiClawPhysicalIdentity.From(directInput);
        var unrelated = MsiClawPhysicalIdentity.From(other);

        Assert.Equal(MsiClawIdentityConfidence.Strong, first.Confidence);
        Assert.Equal("USB\\VID_0DB0\\CLAW_A", first.PhysicalDeviceKey);
        Assert.Equal(first.PhysicalDeviceKey, second.PhysicalDeviceKey);
        Assert.True(first.StronglyMatches(second));
        Assert.False(first.StronglyMatches(unrelated));
    }

    [Fact]
    public async Task Windows_writer_rejects_sentinel_without_verified_physical_key()
    {
        var sentinel = Guid.Parse("00000000-0000-0000-ffff-ffffffffffff");
        var device = Device(sentinel, "USB\\ROOT", "HID\\MSI", 0x1901, 0xFFA0, 0x0001);
        var unverified = new MsiClawControlHidDevice(device, 0xFFA0, 0x0001,
            new MsiClawPhysicalIdentity(sentinel, device.ParentInstanceId, device.InstanceId, device.VendorId, device.ProductId, MsiClawIdentityConfidence.Strong));
        var writer = new WindowsMsiClawModeWriter(new EmptyLookup());

        Assert.False(await writer.WriteAsync(unverified, MsiClawNativeMode.DirectInput, CancellationToken.None));
    }

    [Fact]
    public async Task Mode_controller_requires_observed_old_and_target_devices()
    {
        var identity = Guid.NewGuid();
        var oldDevice = Device(identity, "USB\\ROOT", "HID\\MSI", 0x1901, 0xFFA0, 0x0001);
        var newDevice = Device(identity, "USB\\ROOT", "HID\\MSI", 0x1902, 0xFFF0, 0x0040);
        var enumerator = new SequenceEnumerator([oldDevice], [newDevice]);
        var writer = new RecordingWriter();
        var controller = new MsiClawModeController(enumerator, new MsiClawControlHidResolver(), writer, TimeSpan.FromSeconds(1), TimeSpan.Zero);
        var result = await controller.SwitchModeAsync(MsiClawNativeMode.DirectInput, MsiClawPhysicalIdentity.From(oldDevice), CancellationToken.None);
        Assert.True(result.Succeeded); Assert.True(result.OldPidDisappeared); Assert.True(result.TargetPidAppeared); Assert.True(result.IdentityVerified); Assert.Equal(MsiClawNativeMode.DirectInput, writer.Mode);
    }

    private static ControllerDeviceInfo Device(Guid? container, string? parent, string instance, ushort pid = 0x1901, ushort usagePage = 0, ushort usage = 0) => new(instance, container, parent, parent is null ? [] : [parent], "HID", [], [], "HIDClass", null, null, 0x0DB0, pid, true, UsagePage: usagePage, Usage: usage);

    private static ControllerDeviceInfo Topology(Guid container, string root, ushort pid)
    {
        var child = $"USB\\VID_0DB0&PID_{pid:X4}&MI_00\\{root[^1]}";
        return new(child, container, root, [root], "HID", [], [], "HIDClass", null, null, 0x0DB0, pid, true, UsagePage: pid == 0x1901 ? (ushort)0xFFA0 : (ushort)0xFFF0, Usage: pid == 0x1901 ? (ushort)1 : (ushort)0x40);
    }

    private sealed class SequenceEnumerator(params IReadOnlyList<ControllerDeviceInfo>[] states) : IControllerDeviceEnumerator
    { private int _index; public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => states[Math.Min(_index++, states.Length - 1)]; }
    private sealed class RecordingWriter : IMsiClawModeWriter
    { public MsiClawNativeMode Mode { get; private set; } public Task<bool> WriteAsync(MsiClawControlHidDevice device, MsiClawNativeMode mode, CancellationToken cancellationToken) { Mode = mode; return Task.FromResult(true); } }
    private sealed class EmptyLookup : IMsiClawHidDeviceInformationLookup
    { public Task<IReadOnlyList<MsiClawHidDeviceInformation>> FindAsync(string selector, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MsiClawHidDeviceInformation>>([]); }
}
