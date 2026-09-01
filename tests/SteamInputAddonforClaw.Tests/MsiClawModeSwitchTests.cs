using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;
using Microsoft.Win32.SafeHandles;

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

    [Theory]
    [InlineData((int)MsiClawNativeMode.XInput)]
    [InlineData((int)MsiClawNativeMode.DirectInput)]
    public async Task Windows_writer_passes_exact_command_to_raw_transport(int modeValue)
    {
        var mode = (MsiClawNativeMode)modeValue;
        var container = Guid.NewGuid();
        var device = Device(container, "USB\\ROOT", "HID\\MSI", 0x1901, 0xFFA0, 0x0001);
        var expected = new MsiClawControlHidDevice(device, 0xFFA0, 0x0001, MsiClawPhysicalIdentity.From(device));
        var transport = new RecordingRawTransport();
        var writer = new WindowsMsiClawModeWriter(new FixedLookup(new("hid-path", device.InstanceId, container)), transport);

        Assert.True(await writer.WriteAsync(expected, mode, CancellationToken.None));
        Assert.Equal("hid-path", transport.DevicePath);
        Assert.Equal(MsiClawModeCommand.Build(mode), transport.Bytes);
    }

    [Fact]
    public async Task Windows_writer_propagates_transport_failure_and_rejects_empty_lookup()
    {
        var device = Device(Guid.NewGuid(), "USB\\ROOT", "HID\\MSI", 0x1901, 0xFFA0, 0x0001);
        var expected = new MsiClawControlHidDevice(device, 0xFFA0, 0x0001, MsiClawPhysicalIdentity.From(device));
        var transport = new RecordingRawTransport { Result = false };
        var writer = new WindowsMsiClawModeWriter(new FixedLookup(new("hid-path", device.InstanceId, device.ContainerId)), transport);
        Assert.False(await writer.WriteAsync(expected, MsiClawNativeMode.DirectInput, CancellationToken.None));
        Assert.Equal(1, transport.CallCount);

        var emptyTransport = new RecordingRawTransport();
        var emptyWriter = new WindowsMsiClawModeWriter(new FixedLookup(new("", device.InstanceId, device.ContainerId)), emptyTransport);
        Assert.False(await emptyWriter.WriteAsync(expected, MsiClawNativeMode.DirectInput, CancellationToken.None));
        Assert.Equal(1, emptyTransport.CallCount);
    }

    [Fact]
    public async Task Windows_writer_supports_sentinel_identity_at_raw_transport_boundary()
    {
        var sentinel = Guid.Parse("00000000-0000-0000-ffff-ffffffffffff");
        var device = Topology(sentinel, "USB\\VID_0DB0&PID_1901\\CLAW_A", 0x1901);
        var expected = new MsiClawControlHidDevice(device, 0xFFA0, 0x0001, MsiClawPhysicalIdentity.From(device));
        var transport = new RecordingRawTransport();
        var writer = new WindowsMsiClawModeWriter(new FixedLookup(new("hid-path", device.InstanceId, sentinel)), transport);

        Assert.True(await writer.WriteAsync(expected, MsiClawNativeMode.DirectInput, CancellationToken.None));
        Assert.Equal(1, transport.CallCount);
    }

    [Fact]
    public async Task Raw_transport_requires_exact_full_write_and_handles_cancellation()
    {
        var fake = new FakeNativeHidApi { WriteResult = true, BytesWritten = 63 };
        var transport = new WindowsMsiClawRawHidTransport(fake);
        Assert.False(await transport.WriteAsync("hid-path", new byte[64], CancellationToken.None));

        fake.BytesWritten = 64;
        Assert.True(await transport.WriteAsync("hid-path", new byte[64], CancellationToken.None));
        Assert.Equal(2, fake.OpenCallCount);
        Assert.Equal(2, fake.WriteCallCount);

        var writesBeforeCancellation = fake.WriteCallCount;
        var cancelled = new CancellationTokenSource();
        fake.OnOpen = () => cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => transport.WriteAsync("hid-path", new byte[64], cancelled.Token));
        Assert.Equal(writesBeforeCancellation, fake.WriteCallCount);
    }

    [Fact]
    public async Task Raw_transport_returns_false_when_open_fails()
    {
        var fake = new FakeNativeHidApi { OpenSucceeds = false };
        var transport = new WindowsMsiClawRawHidTransport(fake);

        Assert.False(await transport.WriteAsync("hid-path", new byte[64], CancellationToken.None));
        Assert.Equal(1, fake.OpenCallCount);
        Assert.Equal(0, fake.WriteCallCount);
    }

    [Fact]
    public async Task Raw_transport_returns_false_when_native_write_fails()
    {
        var fake = new FakeNativeHidApi { WriteResult = false, BytesWritten = 0 };
        var transport = new WindowsMsiClawRawHidTransport(fake);

        Assert.False(await transport.WriteAsync("hid-path", new byte[64], CancellationToken.None));
        Assert.Equal(1, fake.OpenCallCount);
        Assert.Equal(1, fake.WriteCallCount);
    }

    [Fact]
    public async Task Raw_transport_rejects_empty_path_before_native_io()
    {
        var fake = new FakeNativeHidApi();
        var transport = new WindowsMsiClawRawHidTransport(fake);

        Assert.False(await transport.WriteAsync("", new byte[64], CancellationToken.None));
        Assert.Equal(0, fake.OpenCallCount);
        Assert.Equal(0, fake.WriteCallCount);
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
        Assert.True(result.Succeeded); Assert.True(result.OldPidDisappeared); Assert.True(result.TargetPidAppeared); Assert.True(result.SourceIdentityVerified); Assert.True(result.TargetTopologyVerified); Assert.Equal(MsiClawNativeMode.DirectInput, writer.Mode);
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
        Assert.True(result.SourceIdentityVerified);
        Assert.True(result.TargetTopologyVerified);
        Assert.Equal("USB\\VID_0DB0\\CLAW_A", writer.Device?.VerifiedIdentity.PhysicalDeviceKey);
    }

    [Fact]
    public async Task Mode_controller_rejects_multiple_independent_target_roots()
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

        Assert.Equal(MsiClawModeTransitionStatus.AmbiguousDevice, result.Status);
        Assert.Equal("USB\\VID_0DB0\\CLAW_A", writer.Device?.VerifiedIdentity.PhysicalDeviceKey);
        Assert.Equal(clawA.InstanceId, writer.Device?.Device.InstanceId);
    }

    [Fact]
    public async Task XInput_to_DirectInput_succeeds_when_the_target_has_a_new_root_and_the_old_pid_is_gone()
    {
        // PR11 section 14: the supported MSI Claw legitimately exposes a DIFFERENT Windows
        // physical-root representation in PID1902 vs PID1901. That alone must not fail the transition.
        var source = Topology(Guid.NewGuid(), "USB\\VID_0DB0&PID_1901\\ROOT_A", 0x1901);
        var target = Topology(Guid.NewGuid(), "USB\\VID_0DB0&PID_1902\\ROOT_B", 0x1902);
        var writer = new RecordingWriter();
        var result = await new MsiClawModeController(new SequenceEnumerator([source], [target]), new MsiClawControlHidResolver(), writer, TimeSpan.FromSeconds(1), TimeSpan.Zero)
            .SwitchModeAsync(MsiClawNativeMode.DirectInput, MsiClawPhysicalIdentity.From(source), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.OldPidDisappeared);
        Assert.True(result.TargetPidAppeared);
        Assert.True(result.SourceIdentityVerified);
        Assert.True(result.TargetTopologyVerified);
    }

    [Fact]
    public async Task Transition_does_not_succeed_while_the_old_pid_is_still_present()
    {
        // PR11 section 5/14: one present target logical group is NOT enough -- the old native-mode
        // device must be gone. If it stays through the bounded window, fail closed.
        var source = Topology(Guid.NewGuid(), "USB\\VID_0DB0&PID_1901\\ROOT_A", 0x1901);
        var target = Topology(Guid.NewGuid(), "USB\\VID_0DB0&PID_1902\\ROOT_B", 0x1902);
        var result = await new MsiClawModeController(new SequenceEnumerator([source], [source, target]), new MsiClawControlHidResolver(), new RecordingWriter(), TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(5))
            .SwitchModeAsync(MsiClawNativeMode.DirectInput, MsiClawPhysicalIdentity.From(source), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(MsiClawModeTransitionStatus.OldDeviceDidNotDisappear, result.Status);
        Assert.False(result.OldPidDisappeared);
        Assert.True(result.TargetPidAppeared);
    }

    [Fact]
    public async Task Target_Other_does_not_invoke_writer()
    {
        var source = Topology(Guid.NewGuid(), "USB\\VID_0DB0&PID_1901\\ROOT_A", 0x1901);
        var writer = new RecordingWriter();
        var result = await new MsiClawModeController(new SequenceEnumerator([source]), new MsiClawControlHidResolver(), writer)
            .SwitchModeAsync(MsiClawNativeMode.Other, MsiClawPhysicalIdentity.From(source), CancellationToken.None);

        Assert.Equal(MsiClawModeTransitionStatus.UnsupportedDevice, result.Status);
        Assert.Equal(0, writer.CallCount);
        Assert.Null(result.TargetPid);
    }

    [Fact]
    public async Task Current_Other_does_not_invoke_writer()
    {
        var source = Device(Guid.NewGuid(), "USB\\ROOT", "HID\\MSI", 0x1903, 0, 0);
        var writer = new RecordingWriter();
        var result = await new MsiClawModeController(new SequenceEnumerator([source]), new MsiClawControlHidResolver(), writer)
            .SwitchModeAsync(MsiClawNativeMode.XInput, MsiClawPhysicalIdentity.From(source), CancellationToken.None);

        Assert.Equal(MsiClawModeTransitionStatus.UnsupportedDevice, result.Status);
        Assert.Equal(0, writer.CallCount);
    }

    [Fact]
    public async Task Ambiguous_target_topology_fails_closed()
    {
        var source = Topology(Guid.NewGuid(), "USB\\VID_0DB0&PID_1901\\ROOT_A", 0x1901);
        var first = Topology(Guid.NewGuid(), "USB\\VID_0DB0&PID_1902\\ROOT_B", 0x1902);
        var second = Topology(Guid.NewGuid(), "USB\\VID_0DB0&PID_1902\\ROOT_C", 0x1902);
        var result = await new MsiClawModeController(new SequenceEnumerator([source], [first, second]), new MsiClawControlHidResolver(), new RecordingWriter(), TimeSpan.FromSeconds(1), TimeSpan.Zero)
            .SwitchModeAsync(MsiClawNativeMode.DirectInput, MsiClawPhysicalIdentity.From(source), CancellationToken.None);

        Assert.Equal(MsiClawModeTransitionStatus.AmbiguousDevice, result.Status);
    }

    [Fact]
    public async Task Target_aliases_on_same_logical_device_are_not_ambiguous()
    {
        var source = Topology(Guid.NewGuid(), "USB\\VID_0DB0&PID_1901\\ROOT_A", 0x1901);
        var targetContainer = Guid.NewGuid();
        var target = Device(targetContainer, "USB\\TARGET_PARENT", "HID\\TARGET_A", 0x1902, 0xFFF0, 0x0040);
        var alias = Device(targetContainer, "USB\\TARGET_PARENT", "HID\\TARGET_B", 0x1902, 0xFFF0, 0x0040);
        var result = await new MsiClawModeController(new SequenceEnumerator([source], [target, alias]), new MsiClawControlHidResolver(), new RecordingWriter(), TimeSpan.FromSeconds(1), TimeSpan.Zero)
            .SwitchModeAsync(MsiClawNativeMode.DirectInput, MsiClawPhysicalIdentity.From(source), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.TargetTopologyVerified);
    }

    [Fact]
    public async Task Sentinel_target_aliases_under_same_physical_root_are_one_logical_target()
    {
        var source = Topology(Guid.NewGuid(), "USB\\VID_0DB0&PID_1901\\ROOT_A", 0x1901);
        var root = "USB\\VID_0DB0&PID_1902\\ROOT_B";
        var first = TargetAlias(root, "PARENT_MI_00", "HID\\TARGET_A");
        var second = TargetAlias(root, "PARENT_MI_01", "HID\\TARGET_B");
        var result = await new MsiClawModeController(new SequenceEnumerator([source], [first, second]), new MsiClawControlHidResolver(), new RecordingWriter(), TimeSpan.FromSeconds(1), TimeSpan.Zero)
            .SwitchModeAsync(MsiClawNativeMode.DirectInput, MsiClawPhysicalIdentity.From(source), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.TargetTopologyVerified);
    }

    [Fact]
    public async Task Transient_write_failure_re_resolves_and_retries_before_target_appears()
    {
        var source = Topology(Guid.NewGuid(), "USB\\VID_0DB0&PID_1901\\ROOT_A", 0x1901);
        var target = Topology(Guid.NewGuid(), "USB\\VID_0DB0&PID_1902\\ROOT_B", 0x1902);
        var writer = new RecordingWriter { FailuresRemaining = 1 };
        var result = await new MsiClawModeController(new SequenceEnumerator([source], [source], [target]), new MsiClawControlHidResolver(), writer, TimeSpan.FromSeconds(1), TimeSpan.Zero)
            .SwitchModeAsync(MsiClawNativeMode.DirectInput, MsiClawPhysicalIdentity.From(source), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, writer.CallCount);
    }

    [Fact]
    public async Task Wrong_target_usage_is_not_accepted()
    {
        var source = Topology(Guid.NewGuid(), "USB\\VID_0DB0&PID_1901\\ROOT_A", 0x1901);
        var wrongTarget = Device(Guid.NewGuid(), "USB\\ROOT_B", "HID\\TARGET", 0x1902, 0xFFF0, 0x0001);
        var result = await new MsiClawModeController(new SequenceEnumerator([source], [wrongTarget]), new MsiClawControlHidResolver(), new RecordingWriter(), TimeSpan.FromMilliseconds(10), TimeSpan.Zero)
            .SwitchModeAsync(MsiClawNativeMode.DirectInput, MsiClawPhysicalIdentity.From(source), CancellationToken.None);

        Assert.Equal(MsiClawModeTransitionStatus.TargetDeviceDidNotAppear, result.Status);
    }

    private static ControllerDeviceInfo Device(Guid? container, string? parent, string instance, ushort pid = 0x1901, ushort usagePage = 0, ushort usage = 0) => new(instance, container, parent, parent is null ? [] : [parent], "HID", [], [], "HIDClass", null, null, 0x0DB0, pid, true, UsagePage: usagePage, Usage: usage);

    private static ControllerDeviceInfo Topology(Guid container, string root, ushort pid)
    {
        var child = $"USB\\VID_0DB0&PID_{pid:X4}&MI_00\\{root[^1]}";
        return new(child, container, root, [root], "HID", [], [], "HIDClass", null, null, 0x0DB0, pid, true, UsagePage: pid == 0x1901 ? (ushort)0xFFA0 : (ushort)0xFFF0, Usage: pid == 0x1901 ? (ushort)1 : (ushort)0x40);
    }
    private static ControllerDeviceInfo TargetAlias(string root, string parent, string instance) =>
        new(instance, Guid.Parse("00000000-0000-0000-ffff-ffffffffffff"), parent, [root], "HID", [], [], "HIDClass", null, null, 0x0DB0, 0x1902, true, UsagePage: 0xFFF0, Usage: 0x0040);

    private sealed class SequenceEnumerator(params IReadOnlyList<ControllerDeviceInfo>[] states) : IControllerDeviceEnumerator
    { private int _index; public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => states[Math.Min(_index++, states.Length - 1)]; }
    private sealed class RecordingWriter : IMsiClawModeWriter
    {
        public int CallCount { get; private set; }
        public int FailuresRemaining { get; set; }
        public MsiClawNativeMode Mode { get; private set; }
        public MsiClawControlHidDevice? Device { get; private set; }
        public Task<bool> WriteAsync(MsiClawControlHidDevice device, MsiClawNativeMode mode, CancellationToken cancellationToken)
        {
            CallCount++;
            Device = device;
            Mode = mode;
            if (FailuresRemaining > 0) { FailuresRemaining--; return Task.FromResult(false); }
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
        public bool Result { get; set; } = true;
        public int CallCount { get; private set; }
        public Task<bool> WriteAsync(string devicePath, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            DevicePath = devicePath;
            Bytes = bytes.ToArray();
            CallCount++;
            return Task.FromResult(Result && !string.IsNullOrWhiteSpace(devicePath));
        }
    }

    private sealed class FakeNativeHidApi : IMsiClawNativeHidApi
    {
        public bool WriteResult { get; set; }
        public bool OpenSucceeds { get; set; } = true;
        public uint BytesWritten { get; set; }
        public int OpenCallCount { get; private set; }
        public int WriteCallCount { get; private set; }
        public Action? OnOpen { get; set; }
        public int LastError => 123;
        public SafeFileHandle Open(string devicePath, uint desiredAccess, uint shareMode, uint creationDisposition)
        {
            OpenCallCount++;
            OnOpen?.Invoke();
            return new SafeFileHandle(new IntPtr(OpenSucceeds ? 1 : -1), ownsHandle: false);
        }
        public bool Write(SafeFileHandle handle, byte[] buffer, out uint bytesWritten)
        {
            WriteCallCount++;
            bytesWritten = BytesWritten;
            return WriteResult;
        }
        public bool TryGetReportLengths(SafeFileHandle handle, out int inputReportLength, out int outputReportLength, out ushort usagePage, out ushort usage, out int hidStatus)
        {
            inputReportLength = 0;
            outputReportLength = 0;
            usagePage = 0;
            usage = 0;
            hidStatus = 0;
            return false;
        }
    }
}
