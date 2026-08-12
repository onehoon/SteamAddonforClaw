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
    public void Strong_identity_does_not_match_when_both_physical_keys_are_missing()
    {
        var sentinel = Guid.Parse("00000000-0000-0000-ffff-ffffffffffff");
        var first = new MsiClawPhysicalIdentity(sentinel, "USB\\ROOT", "HID\\A", MsiClawHardware.VendorId, MsiClawHardware.XInputProductId, MsiClawIdentityConfidence.Strong);
        var second = new MsiClawPhysicalIdentity(sentinel, "USB\\ROOT", "HID\\B", MsiClawHardware.VendorId, MsiClawHardware.DirectInputProductId, MsiClawIdentityConfidence.Strong);

        Assert.False(first.StronglyMatches(second));
    }

    [Fact]
    public void Windows_writer_selects_only_the_verified_hid_candidate()
    {
        var sentinel = Guid.Parse("00000000-0000-0000-ffff-ffffffffffff");
        var device = Topology(sentinel, "USB\\VID_0DB0&PID_1901\\CLAW_A", 0x1901);
        var expected = new MsiClawControlHidDevice(device, 0xFFA0, 0x0001, MsiClawPhysicalIdentity.From(device));

        var selected = WindowsMsiClawModeWriter.SelectDeviceInformation(expected,
        [
            new("unrelated", "HID\\OTHER", sentinel),
            new("matching", device.InstanceId, sentinel)
        ]);

        Assert.NotNull(selected);
        Assert.Equal("matching", selected.Id);
    }

    [Fact]
    public void Windows_writer_rejects_ambiguous_or_container_mismatched_candidates()
    {
        var container = Guid.NewGuid();
        var device = Device(container, "USB\\ROOT", "HID\\MSI", 0x1901, 0xFFA0, 0x0001);
        var expected = new MsiClawControlHidDevice(device, 0xFFA0, 0x0001, MsiClawPhysicalIdentity.From(device));

        Assert.Null(WindowsMsiClawModeWriter.SelectDeviceInformation(expected,
        [
            new("duplicate-a", device.InstanceId, container),
            new("duplicate-b", device.InstanceId, container)
        ]));
        Assert.Null(WindowsMsiClawModeWriter.SelectDeviceInformation(expected,
        [
            new("wrong-container", device.InstanceId, Guid.NewGuid())
        ]));
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
    public async Task Windows_writer_passes_exact_command_to_raw_transport()
    {
        var container = Guid.NewGuid();
        var device = Device(container, "USB\\ROOT", "HID\\MSI", 0x1901, 0xFFA0, 0x0001);
        var expected = new MsiClawControlHidDevice(device, 0xFFA0, 0x0001, MsiClawPhysicalIdentity.From(device));
        var transport = new RecordingRawTransport();
        var writer = new WindowsMsiClawModeWriter(new FixedLookup(new("hid-path", device.InstanceId, container)), transport);

        Assert.True(await writer.WriteAsync(expected, MsiClawNativeMode.DirectInput, CancellationToken.None));
        Assert.Equal("hid-path", transport.DevicePath);
        Assert.Equal(MsiClawModeCommand.Build(MsiClawNativeMode.DirectInput), transport.Bytes);
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

    [Fact]
    public async Task Mode_controller_verifies_sentinel_pid_transition_by_physical_root_key()
    {
        var sentinel = Guid.Parse("00000000-0000-0000-ffff-ffffffffffff");
        var oldDevice = Topology(sentinel, "USB\\VID_0DB0&PID_1901\\CLAW_A", 0x1901);
        var newDevice = Topology(sentinel, "USB\\VID_0DB0&PID_1902\\CLAW_A", 0x1902);
        var enumerator = new SequenceEnumerator([oldDevice], [newDevice]);
        var writer = new RecordingWriter();
        var controller = new MsiClawModeController(enumerator, new MsiClawControlHidResolver(), writer, TimeSpan.FromSeconds(1), TimeSpan.Zero);

        var result = await controller.SwitchModeAsync(MsiClawNativeMode.DirectInput, MsiClawPhysicalIdentity.From(oldDevice), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.IdentityVerified);
        Assert.Equal("USB\\VID_0DB0\\CLAW_A", writer.Device?.VerifiedIdentity.PhysicalDeviceKey);
    }

    [Fact]
    public async Task Mode_controller_ignores_unrelated_sentinel_physical_root()
    {
        var sentinel = Guid.Parse("00000000-0000-0000-ffff-ffffffffffff");
        var clawA = Topology(sentinel, "USB\\VID_0DB0&PID_1901\\CLAW_A", 0x1901);
        var clawB = Topology(sentinel, "USB\\VID_0DB0&PID_1901\\CLAW_B", 0x1901);
        var targetA = Topology(sentinel, "USB\\VID_0DB0&PID_1902\\CLAW_A", 0x1902);
        var targetB = Topology(sentinel, "USB\\VID_0DB0&PID_1902\\CLAW_B", 0x1902);
        var enumerator = new SequenceEnumerator([clawA, clawB], [targetA, targetB]);
        var writer = new RecordingWriter();
        var result = await new MsiClawModeController(enumerator, new MsiClawControlHidResolver(), writer, TimeSpan.FromSeconds(1), TimeSpan.Zero)
            .SwitchModeAsync(MsiClawNativeMode.DirectInput, MsiClawPhysicalIdentity.From(clawA), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("USB\\VID_0DB0\\CLAW_A", writer.Device?.VerifiedIdentity.PhysicalDeviceKey);
        Assert.Equal(clawA.InstanceId, writer.Device?.Device.InstanceId);
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
    {
        public MsiClawNativeMode Mode { get; private set; }
        public MsiClawControlHidDevice? Device { get; private set; }
        public Task<bool> WriteAsync(MsiClawControlHidDevice device, MsiClawNativeMode mode, CancellationToken cancellationToken)
        {
            Device = device;
            Mode = mode;
            return Task.FromResult(true);
        }
    }
    private sealed class EmptyLookup : IMsiClawHidDeviceInformationLookup
    { public Task<IReadOnlyList<MsiClawHidDeviceInformation>> FindAsync(string selector, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MsiClawHidDeviceInformation>>([]); }
    private sealed class FixedLookup(MsiClawHidDeviceInformation item) : IMsiClawHidDeviceInformationLookup
    { public Task<IReadOnlyList<MsiClawHidDeviceInformation>> FindAsync(string selector, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MsiClawHidDeviceInformation>>([item]); }
    private sealed class RecordingRawTransport : IMsiClawRawHidTransport
    {
        public string? DevicePath { get; private set; }
        public byte[] Bytes { get; private set; } = [];
        public Task<bool> WriteAsync(string devicePath, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            DevicePath = devicePath;
            Bytes = bytes.ToArray();
            return Task.FromResult(true);
        }
    }
}
