using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Contracts.ControllerLed;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ControllerLedTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.ControllerLed.{Guid.NewGuid():N}");
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void Defaults_are_off_with_full_brightness_and_white_color()
    {
        Assert.Equal(new ControllerLedSettings(false, 100, 255, 255, 255), ControllerLedSettings.Default);
        Assert.Null(ControllerLedSettingsValidation.Validate(ControllerLedSettings.Default));
        Assert.Equal("BrightnessOutOfRange", ControllerLedSettingsValidation.Validate(ControllerLedSettings.Default with { Brightness = -1 }));
        Assert.Equal("BrightnessOutOfRange", ControllerLedSettingsValidation.Validate(ControllerLedSettings.Default with { Brightness = 101 }));
    }

    [Fact]
    public void Older_settings_without_controller_led_use_the_default()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{\"LogLevel\":\"Debug\"}");

        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Equal(ControllerLedSettings.Default, settings.ControllerLed);
    }

    [Fact]
    public void Invalid_brightness_is_rejected_and_off_retains_color_and_brightness()
    {
        var saved = new ControllerLedSettings(true, 47, 17, 91, 203);
        var store = new SettingsStore(SettingsPath);
        store.Save(new AppSettings { ControllerLed = saved });
        var coordinator = new StartupSettingsCoordinator(new AppSettings { ControllerLed = saved }, store, new NoOpStartupManager());

        Assert.False(coordinator.ChangeControllerLedSettings(saved with { Brightness = 101 }));
        Assert.True(coordinator.ChangeControllerLedSettings(saved with { Enabled = false }));

        var expected = saved with { Enabled = false };
        Assert.Equal(expected, coordinator.ControllerLed);
        Assert.Equal(expected, store.Load().ControllerLed);
    }

    [Theory]
    [InlineData(0x0163, 0x01FA)]
    [InlineData(0x0166, 0x024A)]
    [InlineData(0x0167, 0x024A)]
    [InlineData(0x0211, 0x01FA)]
    [InlineData(0x0217, 0x024A)]
    [InlineData(0x0219, 0x024A)]
    [InlineData(0x0308, 0x024A)]
    [InlineData(0x0411, 0x024A)]
    [InlineData(0x0414, 0x024A)]
    public void Exact_known_firmware_builds_the_four_static_frame_writes(int firmwareVersion, int expectedBase)
    {
        var settings = new ControllerLedSettings(true, 63, 0x12, 0x34, 0x56);

        Assert.True(MsiClawLedProtocol.TryBuildStaticWrites((ushort)firmwareVersion, settings, out var writes));

        Assert.Equal(4, writes.Count);
        Assert.All(writes, packet => Assert.Equal(64, packet.Length));
        Assert.Equal(new byte[] { 0x0F, 0x00, 0x00, 0x3C, 0x21, 0x01 }, writes[0][..6]);
        Assert.Equal((byte)(expectedBase >> 8), writes[0][6]);
        Assert.Equal((byte)expectedBase, writes[0][7]);
        Assert.Equal(0x20, writes[0][8]);
        Assert.Equal(0x00, writes[0][9]);
        Assert.Equal(0x01, writes[0][10]);
        Assert.Equal(0x09, writes[0][11]);
        Assert.Equal(0x03, writes[0][12]);
        Assert.Equal(63, writes[0][13]);

        var frame = Enumerable.Range(0, 9).SelectMany(_ => new byte[] { 0x12, 0x34, 0x56 }).ToArray();
        Assert.Equal(frame, writes[0][14..41]);
        Assert.Equal(new[] { expectedBase + 32, expectedBase + 59, expectedBase + 86 }, writes.Skip(1).Select(packet => packet[6] * 256 + packet[7]));
        foreach (var packet in writes.Skip(1))
        {
            Assert.Equal(new byte[] { 0x0F, 0x00, 0x00, 0x3C, 0x21, 0x01 }, packet[..6]);
            Assert.Equal(27, packet[8]);
            Assert.Equal(frame, packet[9..36]);
            Assert.DoesNotContain((byte)0x22, packet);
        }
    }

    [Fact]
    public void Off_uses_zero_hardware_brightness_without_erasing_saved_values()
    {
        var settings = new ControllerLedSettings(false, 71, 4, 5, 6);

        Assert.True(MsiClawLedProtocol.TryBuildStaticWrites(0x0411, settings, out var writes));

        Assert.Equal(0, writes[0][13]);
        Assert.Equal(71, settings.Brightness);
        Assert.Equal((4, 5, 6), (settings.Red, settings.Green, settings.Blue));
    }

    [Fact]
    public async Task Unknown_firmware_and_ambiguous_or_non_control_hids_issue_no_profile_writes()
    {
        var device = ControlDevice(Guid.NewGuid(), "HID\\VID_0DB0&PID_1902&MI_00&COL02\\CONTROL_A");
        var identity = MsiClawPhysicalIdentity.From(device);

        var unknownTransport = new RecordingLedTransport(0x9999);
        var unknown = CreateController([device], [new("control-path", device.InstanceId, device.ContainerId)], unknownTransport);
        Assert.False(await unknown.ApplyAsync(ControllerLedSettings.Default, identity, CancellationToken.None));
        Assert.Empty(unknownTransport.Writes);

        var duplicate = ControlDevice(device.ContainerId!.Value, "HID\\VID_0DB0&PID_1902&MI_00&COL02\\CONTROL_B");
        var ambiguousTransport = new RecordingLedTransport(0x0411);
        var ambiguous = CreateController([device, duplicate], [
            new("control-a", device.InstanceId, device.ContainerId),
            new("control-b", duplicate.InstanceId, duplicate.ContainerId)], ambiguousTransport);
        Assert.False(await ambiguous.ApplyAsync(ControllerLedSettings.Default, identity, CancellationToken.None));
        Assert.Empty(ambiguousTransport.Writes);

        var gamepad = device with { InstanceId = "HID\\VID_0DB0&PID_1902&MI_00&COL01\\GAMEPAD", UsagePage = 0x0001, Usage = 0x0005 };
        var rumble = device with { InstanceId = "HID\\VID_0DB0&PID_1902&MI_02&COL01\\RUMBLE", UsagePage = 0xFF00, Usage = 0x0001 };
        var wrongCollectionsTransport = new RecordingLedTransport(0x0411);
        var wrongCollections = CreateController([gamepad, rumble], [], wrongCollectionsTransport);
        Assert.False(await wrongCollections.ApplyAsync(ControllerLedSettings.Default, identity, CancellationToken.None));
        Assert.Empty(wrongCollectionsTransport.Writes);
    }

    [Fact]
    public async Task Exact_owned_control_hid_gets_four_ordered_writes_and_stops_on_failure()
    {
        var device = ControlDevice(Guid.NewGuid(), "HID\\VID_0DB0&PID_1902&MI_00&COL02\\CONTROL_A");
        var identity = MsiClawPhysicalIdentity.From(device);
        var transport = new RecordingLedTransport(0x0411);
        var controller = CreateController([device], [new("exact-control-path", device.InstanceId, device.ContainerId)], transport);

        Assert.True(await controller.ApplyAsync(new(true, 50, 1, 2, 3), identity, CancellationToken.None));
        Assert.Equal(4, transport.Writes.Count);
        Assert.All(transport.Writes, write => Assert.Equal("exact-control-path", write.Path));
        Assert.Equal(new[] { 0x024A, 0x026A, 0x0285, 0x02A0 }, transport.Writes.Select(write => write.Bytes[6] * 256 + write.Bytes[7]));

        var failing = new RecordingLedTransport(0x0411) { FailAtWrite = 2 };
        var failureController = CreateController([device], [new("exact-control-path", device.InstanceId, device.ContainerId)], failing);
        Assert.False(await failureController.ApplyAsync(ControllerLedSettings.Default, identity, CancellationToken.None));
        Assert.Equal(2, failing.Writes.Count);
    }

    [Fact]
    public void Existing_raw_hid_transport_reads_the_version_from_hid_attributes()
    {
        var native = new FakeNativeHidApi { AttributesAvailable = true, VendorId = 0x0DB0, ProductId = 0x1902, VersionNumber = 0x0411 };
        var transport = new WindowsMsiClawRawHidTransport(native);

        Assert.True(transport.TryGetDeviceAttributes("exact-control-path", out var attributes));
        Assert.Equal(new MsiClawHidDeviceAttributes(0x0DB0, 0x1902, 0x0411), attributes);
        Assert.Equal(1, native.AttributeReadCount);
        native.AttributesAvailable = false;
        Assert.False(transport.TryGetDeviceAttributes("exact-control-path", out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static MsiClawLedController CreateController(
        IReadOnlyList<ControllerDeviceInfo> devices,
        IReadOnlyList<MsiClawHidDeviceInformation> hidPaths,
        RecordingLedTransport transport) => new(new FixedEnumerator(devices), new MsiClawControlHidResolver(), new FixedLookup(hidPaths), transport);

    private static ControllerDeviceInfo ControlDevice(Guid containerId, string instanceId) => new(
        instanceId,
        containerId,
        "USB\\VID_0DB0&PID_1902\\CLAW_A",
        ["USB\\VID_0DB0&PID_1902\\CLAW_A"],
        "HID",
        ["HID\\VID_0DB0&PID_1902"],
        [],
        "HIDClass",
        null,
        "HidUsb",
        0x0DB0,
        0x1902,
        true,
        "MSI Claw Control",
        0xFFF0,
        0x0040);

    private sealed class FixedEnumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => devices;
    }

    private sealed class FixedLookup(IReadOnlyList<MsiClawHidDeviceInformation> devices) : IMsiClawHidDeviceInformationLookup
    {
        public Task<IReadOnlyList<MsiClawHidDeviceInformation>> FindAsync(string selector, CancellationToken cancellationToken) => Task.FromResult(devices);
    }

    private sealed class RecordingLedTransport(ushort version) : IMsiClawRawHidTransport
    {
        public List<(string Path, byte[] Bytes)> Writes { get; } = [];
        public int FailAtWrite { get; init; }
        public bool TryGetDeviceAttributes(string devicePath, out MsiClawHidDeviceAttributes attributes)
        {
            attributes = new(MsiClawHardware.VendorId, MsiClawHardware.DirectInputProductId, version);
            return devicePath == "exact-control-path";
        }
        public Task<bool> WriteAsync(string devicePath, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            Writes.Add((devicePath, bytes.ToArray()));
            return Task.FromResult(FailAtWrite == 0 || Writes.Count != FailAtWrite);
        }
    }

    private sealed class FakeNativeHidApi : IMsiClawNativeHidApi
    {
        public int LastError => 0;
        public bool AttributesAvailable { get; set; }
        public ushort VendorId { get; set; }
        public ushort ProductId { get; set; }
        public ushort VersionNumber { get; set; }
        public int AttributeReadCount { get; private set; }
        public SafeFileHandle Open(string devicePath, uint desiredAccess, uint shareMode, uint creationDisposition) => new(new IntPtr(1), ownsHandle: false);
        public bool Write(SafeFileHandle handle, byte[] buffer, out uint bytesWritten) { bytesWritten = (uint)buffer.Length; return true; }
        public bool TryGetAttributes(SafeFileHandle handle, out ushort vendorId, out ushort productId, out ushort versionNumber)
        {
            AttributeReadCount++;
            vendorId = VendorId;
            productId = ProductId;
            versionNumber = VersionNumber;
            return AttributesAvailable;
        }
        public bool TryGetReportLengths(SafeFileHandle handle, out int inputReportLength, out int outputReportLength, out ushort usagePage, out ushort usage, out int hidStatus)
        {
            inputReportLength = outputReportLength = hidStatus = 0;
            usagePage = usage = 0;
            return false;
        }
    }

    private sealed class NoOpStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }
}
