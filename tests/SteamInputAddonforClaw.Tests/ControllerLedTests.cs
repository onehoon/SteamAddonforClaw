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
    [InlineData(0x0419, 0x024A)]
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

        var a2vm230Transport = new RecordingLedTransport(0x0230);
        var a2vm230 = CreateController([device], [new("control-path", device.InstanceId, device.ContainerId)], a2vm230Transport);
        Assert.False(await a2vm230.ApplyAsync(ControllerLedSettings.Default, identity, CancellationToken.None));
        Assert.Empty(a2vm230Transport.Writes);

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
        Assert.False(MsiClawLedProtocol.TryResolveRgbAddress(0x0230, out _));
    }

    [Fact]
    public void A2vm_candidate_read_request_is_one_exact_zero_filled_64_byte_ReadProfile_packet()
    {
        var request = MsiClawLedProtocol.BuildA2vm230CandidateProfileReadRequest();

        Assert.Equal(64, request.Length);
        Assert.Equal(new byte[] { 0x0F, 0, 0, 0x3C, 0x04, 0x01, 0x02, 0x4A, 0x20 }, request[..9]);
        Assert.All(request[9..], value => Assert.Equal(0, value));
        Assert.Equal(0x04, request[4]);
    }

    [Fact]
    public void A2vm_candidate_read_parser_requires_exact_report_header_and_echoed_request()
    {
        var valid = CandidateProfileReadResponse();

        Assert.Equal(MsiClawLedProtocol.CandidateReadParseOutcome.CandidateReadbackParsed,
            MsiClawLedProtocol.ParseA2vm230CandidateProfileReadResponse(valid, out var parsed));
        Assert.Equal((byte)0x01, parsed.Effect);
        Assert.Equal((byte)0x03, parsed.Speed);
        Assert.Equal((byte)0x64, parsed.Brightness);
        Assert.Equal(27, parsed.RgbBytes.Length);

        var wrongReportId = valid.ToArray();
        wrongReportId[0] = 0x11;
        Assert.Equal(MsiClawLedProtocol.CandidateReadParseOutcome.UnexpectedReport,
            MsiClawLedProtocol.ParseA2vm230CandidateProfileReadResponse(wrongReportId, out _));
        var wrongCommand = valid.ToArray();
        wrongCommand[4] = 0x21;
        Assert.Equal(MsiClawLedProtocol.CandidateReadParseOutcome.UnexpectedReport,
            MsiClawLedProtocol.ParseA2vm230CandidateProfileReadResponse(wrongCommand, out _));
        Assert.Equal(MsiClawLedProtocol.CandidateReadParseOutcome.UnexpectedReport,
            MsiClawLedProtocol.ParseA2vm230CandidateProfileReadResponse(valid[..63], out _));

        foreach (var (offset, value) in new[] { (5, (byte)0x00), (6, (byte)0x03), (7, (byte)0x49), (8, (byte)0x1F) })
        {
            var wrongEcho = valid.ToArray();
            wrongEcho[offset] = value;
            Assert.Equal(MsiClawLedProtocol.CandidateReadParseOutcome.WrongAddressOrIndex,
                MsiClawLedProtocol.ParseA2vm230CandidateProfileReadResponse(wrongEcho, out _));
        }
    }

    [Fact]
    public async Task A2vm_probe_reads_one_exact_owned_candidate_and_never_sends_a_profile_write()
    {
        var device = ControlDevice(Guid.NewGuid(), "HID\\VID_0DB0&PID_1902&MI_00&COL02\\CONTROL_A");
        var identity = MsiClawPhysicalIdentity.From(device);
        var transport = new RecordingLedTransport(0x0230)
        {
            WriteAndReadResult = new(true, [CandidateProfileReadResponse()])
        };
        var controller = CreateController([device], [new("exact-control-path", device.InstanceId, device.ContainerId)], transport);

        var result = await controller.ReadA2vm230CandidateProfileAsync(
            "msi.claw.a2vm.8", () => identity, () => true, CancellationToken.None);

        Assert.Equal(MsiClawLedProfileReadProbeOutcome.CandidateReadbackParsed, result.Outcome);
        Assert.Equal((ushort)0x0230, result.FirmwareVersion);
        Assert.True(result.ReadResponseValid);
        Assert.Equal("exact-control-path", transport.ReadRequestPath);
        Assert.Equal(MsiClawLedProtocol.BuildA2vm230CandidateProfileReadRequest(), transport.ReadRequest);
        Assert.Equal(1, transport.WriteAndReadCallCount);
        Assert.Equal(4, transport.MaxReportsRequested);
        Assert.Equal(TimeSpan.FromMilliseconds(750), transport.TimeoutRequested);
        Assert.True(transport.PreserveShortReportsRequested);
        Assert.Empty(transport.Writes);
    }

    [Fact]
    public async Task A2vm_probe_skips_unrelated_report_before_candidate_reply_with_one_outbound_request()
    {
        var device = ControlDevice(Guid.NewGuid(), "HID\\VID_0DB0&PID_1902&MI_00&COL02\\CONTROL_A");
        var identity = MsiClawPhysicalIdentity.From(device);
        var unrelated = new byte[64];
        unrelated[0] = 0x02;
        var transport = new RecordingLedTransport(0x0230)
        {
            WriteAndReadResult = new(true, [unrelated, CandidateProfileReadResponse()])
        };
        var controller = CreateController([device], [new("exact-control-path", device.InstanceId, device.ContainerId)], transport);

        var result = await controller.ReadA2vm230CandidateProfileAsync(
            "msi.claw.a2vm.8", () => identity, () => true, CancellationToken.None);

        Assert.Equal(MsiClawLedProfileReadProbeOutcome.CandidateReadbackParsed, result.Outcome);
        Assert.True(result.ReadResponseValid);
        Assert.Equal(1, transport.WriteAndReadCallCount);
        Assert.Equal(1, transport.ReadRequestCount);
        Assert.Equal(MsiClawLedProtocol.BuildA2vm230CandidateProfileReadRequest(), transport.ReadRequest);
        Assert.Equal(4, transport.MaxReportsRequested);
        Assert.Empty(transport.Writes);
    }

    [Fact]
    public async Task A2vm_probe_fails_closed_for_wrong_model_authority_owner_firmware_or_endpoint()
    {
        var device = ControlDevice(Guid.NewGuid(), "HID\\VID_0DB0&PID_1902&MI_00&COL02\\CONTROL_A");
        var identity = MsiClawPhysicalIdentity.From(device);

        var unsupportedModelTransport = new RecordingLedTransport(0x0230);
        var unsupportedModel = CreateController([device], [new("exact-control-path", device.InstanceId, device.ContainerId)], unsupportedModelTransport);
        Assert.Equal(MsiClawLedProfileReadProbeOutcome.Unavailable,
            (await unsupportedModel.ReadA2vm230CandidateProfileAsync("msi.claw.a2vm.7", () => identity, () => true, default)).Outcome);
        Assert.Equal(0, unsupportedModelTransport.WriteAndReadCallCount);

        var authorityTransport = new RecordingLedTransport(0x0230);
        var authority = CreateController([device], [new("exact-control-path", device.InstanceId, device.ContainerId)], authorityTransport);
        Assert.Equal("CenterMIsNotExactlyDisabled",
            (await authority.ReadA2vm230CandidateProfileAsync("msi.claw.a2vm.8", () => identity, () => false, default)).Reason);
        Assert.Equal(0, authorityTransport.WriteAndReadCallCount);

        var ownerTransport = new RecordingLedTransport(0x0230);
        var owner = CreateController([device], [new("exact-control-path", device.InstanceId, device.ContainerId)], ownerTransport);
        Assert.Equal("OwnedPhysicalSessionUnavailable",
            (await owner.ReadA2vm230CandidateProfileAsync("msi.claw.a2vm.8", () => null, () => true, default)).Reason);
        Assert.Equal(0, ownerTransport.WriteAndReadCallCount);

        var firmwareTransport = new RecordingLedTransport(0x0229);
        var firmware = CreateController([device], [new("exact-control-path", device.InstanceId, device.ContainerId)], firmwareTransport);
        Assert.Equal("UnsupportedFirmware",
            (await firmware.ReadA2vm230CandidateProfileAsync("msi.claw.a2vm.8", () => identity, () => true, default)).Reason);
        Assert.Equal(0, firmwareTransport.WriteAndReadCallCount);

        var duplicate = ControlDevice(device.ContainerId!.Value, "HID\\VID_0DB0&PID_1902&MI_00&COL02\\CONTROL_B");
        var ambiguousTransport = new RecordingLedTransport(0x0230);
        var ambiguous = CreateController([device, duplicate], [
            new("control-a", device.InstanceId, device.ContainerId),
            new("control-b", duplicate.InstanceId, duplicate.ContainerId)], ambiguousTransport);
        Assert.Equal("ExactPid1902ControlHidUnavailableOrAmbiguous",
            (await ambiguous.ReadA2vm230CandidateProfileAsync("msi.claw.a2vm.8", () => identity, () => true, default)).Reason);
        Assert.Equal(0, ambiguousTransport.WriteAndReadCallCount);

        var missingPathTransport = new RecordingLedTransport(0x0230);
        var missingPath = CreateController([device], [], missingPathTransport);
        Assert.Equal("ExactControlHidPathUnavailableOrAmbiguous",
            (await missingPath.ReadA2vm230CandidateProfileAsync("msi.claw.a2vm.8", () => identity, () => true, default)).Reason);
        Assert.Equal(0, missingPathTransport.WriteAndReadCallCount);

        foreach (var transport in new[] { unsupportedModelTransport, authorityTransport, ownerTransport, firmwareTransport, ambiguousTransport, missingPathTransport })
            Assert.Empty(transport.Writes);
    }

    [Fact]
    public async Task A2vm_probe_classifies_transport_and_reply_failures_without_fallback_or_profile_writes()
    {
        var device = ControlDevice(Guid.NewGuid(), "HID\\VID_0DB0&PID_1902&MI_00&COL02\\CONTROL_A");
        var identity = MsiClawPhysicalIdentity.From(device);

        async Task<MsiClawLedProfileReadProbeResult> Run(MsiClawHidWriteAndReadResult exchange)
        {
            var transport = new RecordingLedTransport(0x0230) { WriteAndReadResult = exchange };
            var controller = CreateController([device], [new("exact-control-path", device.InstanceId, device.ContainerId)], transport);
            var result = await controller.ReadA2vm230CandidateProfileAsync("msi.claw.a2vm.8", () => identity, () => true, default);
            Assert.Empty(transport.Writes);
            Assert.Equal(1, transport.WriteAndReadCallCount);
            return result;
        }

        Assert.Equal(MsiClawLedProfileReadProbeOutcome.TransportWriteFailed,
            (await Run(new(false, []))).Outcome);
        Assert.Equal(MsiClawLedProfileReadProbeOutcome.NoReplyOrTimeout,
            (await Run(new(true, []))).Outcome);
        Assert.Equal(MsiClawLedProfileReadProbeOutcome.UnexpectedReport,
            (await Run(new(true, [new byte[63]]))).Outcome);
        var wrongAddress = CandidateProfileReadResponse();
        wrongAddress[7] = 0x49;
        Assert.Equal(MsiClawLedProfileReadProbeOutcome.WrongAddressOrIndex,
            (await Run(new(true, [wrongAddress]))).Outcome);
    }

    [Fact]
    public async Task A2vm_probe_cancellation_before_io_sends_no_request_or_profile_write()
    {
        var device = ControlDevice(Guid.NewGuid(), "HID\\VID_0DB0&PID_1902&MI_00&COL02\\CONTROL_A");
        var identity = MsiClawPhysicalIdentity.From(device);
        var transport = new RecordingLedTransport(0x0230);
        var controller = CreateController([device], [new("exact-control-path", device.InstanceId, device.ContainerId)], transport);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.ReadA2vm230CandidateProfileAsync("msi.claw.a2vm.8", () => identity, () => true, cancellation.Token));

        Assert.Equal(0, transport.WriteAndReadCallCount);
        Assert.Empty(transport.Writes);
    }

    [Fact]
    public async Task A2vm_probe_rechecks_center_m_and_owned_identity_immediately_before_the_query()
    {
        var device = ControlDevice(Guid.NewGuid(), "HID\\VID_0DB0&PID_1902&MI_00&COL02\\CONTROL_A");
        var identity = MsiClawPhysicalIdentity.From(device);

        var authorityTransport = new RecordingLedTransport(0x0230);
        var authorityController = CreateController([device], [new("exact-control-path", device.InstanceId, device.ContainerId)], authorityTransport);
        var authorityReads = 0;
        var authorityResult = await authorityController.ReadA2vm230CandidateProfileAsync(
            "msi.claw.a2vm.8", () => identity, () => ++authorityReads == 1, default);
        Assert.Equal("CenterMIsNotExactlyDisabled", authorityResult.Reason);
        Assert.Equal(0, authorityTransport.WriteAndReadCallCount);

        var ownerTransport = new RecordingLedTransport(0x0230);
        var ownerController = CreateController([device], [new("exact-control-path", device.InstanceId, device.ContainerId)], ownerTransport);
        var identityReads = 0;
        var ownerResult = await ownerController.ReadA2vm230CandidateProfileAsync(
            "msi.claw.a2vm.8", () => ++identityReads == 1 ? identity : null, () => true, default);
        Assert.Equal("OwnedPhysicalSessionUnavailable", ownerResult.Reason);
        Assert.Equal(0, ownerTransport.WriteAndReadCallCount);
        Assert.Empty(authorityTransport.Writes);
        Assert.Empty(ownerTransport.Writes);
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

    [Fact]
    public async Task Existing_raw_hid_transport_preserves_a_short_reply_for_typed_probe_rejection()
    {
        var native = new FakeNativeHidApi { ReadReport = CandidateProfileReadResponse()[..63] };
        var transport = new WindowsMsiClawRawHidTransport(native);

        var exchange = await transport.WriteAndReadAsync(
            "exact-control-path",
            MsiClawLedProtocol.BuildA2vm230CandidateProfileReadRequest(),
            reportLength: 64,
            maxReports: 1,
            timeout: TimeSpan.FromMilliseconds(250),
            CancellationToken.None,
            preserveShortReports: true);

        Assert.True(exchange.WriteSucceeded);
        var report = Assert.Single(exchange.Reports);
        Assert.Equal(63, report.Length);
        Assert.Equal(MsiClawLedProtocol.CandidateReadParseOutcome.UnexpectedReport,
            MsiClawLedProtocol.ParseA2vm230CandidateProfileReadResponse(report, out _));

        var strictExchange = await transport.WriteAndReadAsync(
            "exact-control-path",
            MsiClawLedProtocol.BuildA2vm230CandidateProfileReadRequest(),
            reportLength: 64,
            maxReports: 1,
            timeout: TimeSpan.FromMilliseconds(250),
            CancellationToken.None);
        Assert.True(strictExchange.WriteSucceeded);
        Assert.Empty(strictExchange.Reports);
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

    private static byte[] CandidateProfileReadResponse()
    {
        var response = new byte[64];
        response[0] = 0x10;
        response[3] = 0x3C;
        response[4] = 0x05;
        response[5] = 0x01;
        response[6] = 0x02;
        response[7] = 0x4A;
        response[8] = 0x20;
        response[9] = 0x00;
        response[10] = 0x01;
        response[11] = 0x01;
        response[12] = 0x03;
        response[13] = 0x64;
        for (var index = 14; index < 41; index++) response[index] = (byte)(index - 14);
        return response;
    }

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
        public int WriteAndReadCallCount { get; private set; }
        public int ReadRequestCount { get; private set; }
        public int MaxReportsRequested { get; private set; }
        public TimeSpan TimeoutRequested { get; private set; }
        public bool PreserveShortReportsRequested { get; private set; }
        public string? ReadRequestPath { get; private set; }
        public byte[] ReadRequest { get; private set; } = [];
        public MsiClawHidWriteAndReadResult WriteAndReadResult { get; set; } = new(false, []);
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
        public Task<MsiClawHidWriteAndReadResult> WriteAndReadAsync(string devicePath, ReadOnlyMemory<byte> bytes, int reportLength, int maxReports, TimeSpan timeout, CancellationToken cancellationToken, bool preserveShortReports = false)
        {
            ReadRequestPath = devicePath;
            ReadRequest = bytes.ToArray();
            ReadRequestCount++;
            WriteAndReadCallCount++;
            MaxReportsRequested = maxReports;
            TimeoutRequested = timeout;
            PreserveShortReportsRequested = preserveShortReports;
            return Task.FromResult(WriteAndReadResult);
        }
    }

    private sealed class FakeNativeHidApi : IMsiClawNativeHidApi
    {
        public int LastError => 0;
        public bool AttributesAvailable { get; set; }
        public ushort VendorId { get; set; }
        public ushort ProductId { get; set; }
        public ushort VersionNumber { get; set; }
        public byte[]? ReadReport { get; init; }
        public int AttributeReadCount { get; private set; }
        public SafeFileHandle Open(string devicePath, uint desiredAccess, uint shareMode, uint creationDisposition) => new(new IntPtr(1), ownsHandle: false);
        public bool Write(SafeFileHandle handle, byte[] buffer, out uint bytesWritten) { bytesWritten = (uint)buffer.Length; return true; }
        public bool Read(SafeFileHandle handle, byte[] buffer, out uint bytesRead)
        {
            bytesRead = 0;
            if (ReadReport is null) return false;
            var length = Math.Min(buffer.Length, ReadReport.Length);
            ReadReport.AsSpan(0, length).CopyTo(buffer);
            bytesRead = (uint)length;
            return true;
        }
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
