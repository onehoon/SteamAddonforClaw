using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawBackButtonProfileDiagnosticTests
{
    [Fact]
    public void Read_profile_builder_emits_only_the_two_bounded_slot_queries()
    {
        var requests = MsiClawBackButtonProfileProtocol.BuildSlotReadRequests();

        Assert.Equal(2, requests.Count);
        Assert.Collection(requests,
            request => AssertRequest(request, 0x00BA),
            request => AssertRequest(request, 0x0163));
    }

    [Theory]
    [InlineData(0x00BA, FrontendMsiBackButtonProfileClassification.HistoricalMarkerPresent)]
    [InlineData(0x0163, FrontendMsiBackButtonProfileClassification.OtherFormat)]
    public void Read_profile_parser_checks_ack_echo_and_retains_all_eight_original_bytes(
        ushort address,
        FrontendMsiBackButtonProfileClassification classification)
    {
        var payload = classification == FrontendMsiBackButtonProfileClassification.HistoricalMarkerPresent
            ? new byte[] { 0x01, 0x04, 0x0C, 0x9D, 0xAA, 0xBB, 0xCC, 0xDD }
            : new byte[] { 0x9D, 0x04, 0x0C, 0x11, 0x22, 0x33, 0x44, 0x55 };
        var response = ReadResponse(address, payload);

        var outcome = MsiClawBackButtonProfileProtocol.ParseReadResponse(response, address, out var parsed);

        Assert.Equal(MsiClawBackButtonProfileProtocol.ParseOutcome.ReadbackParsed, outcome);
        Assert.Equal(payload, parsed.Payload);
        Assert.Equal(classification, parsed.Classification);

        response[4] = 0x04;
        Assert.Equal(MsiClawBackButtonProfileProtocol.ParseOutcome.UnexpectedReport,
            MsiClawBackButtonProfileProtocol.ParseReadResponse(response, address, out _));
        response[4] = 0x05;
        response[5] = 0x00;
        Assert.Equal(MsiClawBackButtonProfileProtocol.ParseOutcome.WrongAddressOrIndex,
            MsiClawBackButtonProfileProtocol.ParseReadResponse(response, address, out _));
        response[5] = MsiClawBackButtonProfileProtocol.ReadIndex;
        response[6] = 0x02;
        response[7] = 0x4A;
        Assert.Equal(MsiClawBackButtonProfileProtocol.ParseOutcome.WrongAddressOrIndex,
            MsiClawBackButtonProfileProtocol.ParseReadResponse(response, address, out _));
        Assert.Equal(MsiClawBackButtonProfileProtocol.ParseOutcome.UnexpectedReport,
            MsiClawBackButtonProfileProtocol.ParseReadResponse(response.AsSpan(0, 17), address, out _));
    }

    [Theory]
    [InlineData("msi.claw.a2vm.8", 0x0230)]
    [InlineData("msi.claw.cg3em", 0x0414)]
    [InlineData("msi.claw.a2vm.7", 0x7777)]
    public async Task Supported_models_read_two_slots_without_firmware_mutation_or_version_guessing(string modelId, ushort firmware)
    {
        var fixture = new ReaderFixture(firmware);
        var reader = fixture.CreateReader();

        var result = await reader.ReadAsync(modelId, () => fixture.Identity, () => true, CancellationToken.None);

        Assert.Equal(firmware, result.FirmwareVersion);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.ReadbackParsed, result.M1.Outcome);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.ReadbackParsed, result.M2.Outcome);
        Assert.Equal(2, fixture.Transport.Requests.Count);
        Assert.Equal(0x00BA, (ushort)((fixture.Transport.Requests[0][6] << 8) | fixture.Transport.Requests[0][7]));
        Assert.Equal(0x0163, (ushort)((fixture.Transport.Requests[1][6] << 8) | fixture.Transport.Requests[1][7]));
        Assert.All(fixture.Transport.Requests, request => Assert.Equal(0x04, request[4]));
        Assert.Empty(fixture.Transport.Mutations);
        Assert.Equal(0x01, result.M1.ReadIndex);
        Assert.Equal(0x08, result.M1.RequestedLength);
        Assert.Equal(FrontendMsiBackButtonProfileClassification.HistoricalMarkerPresent, result.M1.Classification);
        Assert.Equal(FrontendMsiBackButtonProfileClassification.HistoricalMarkerPresent, result.M2.Classification);
    }

    [Fact]
    public async Task Unsupported_model_center_m_enabled_weak_owner_and_ambiguous_hid_fail_before_any_query()
    {
        var unsupported = new ReaderFixture(0x0230);
        var unsupportedResult = await unsupported.CreateReader().ReadAsync(
            "unknown-model", () => unsupported.Identity, () => true, CancellationToken.None);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.UnsupportedModel, unsupportedResult.M1.Outcome);
        Assert.Empty(unsupported.Transport.Requests);

        var centerMEnabled = new ReaderFixture(0x0230);
        var centerMResult = await centerMEnabled.CreateReader().ReadAsync(
            "msi.claw.a2vm.8", () => centerMEnabled.Identity, () => false, CancellationToken.None);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, centerMResult.M1.Outcome);
        Assert.Empty(centerMEnabled.Transport.Requests);

        var weakOwner = new ReaderFixture(0x0230);
        var weak = weakOwner.Identity with { Confidence = MsiClawIdentityConfidence.Indeterminate };
        var weakResult = await weakOwner.CreateReader().ReadAsync(
            "msi.claw.a2vm.8", () => weak, () => true, CancellationToken.None);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, weakResult.M1.Outcome);
        Assert.Empty(weakOwner.Transport.Requests);

        var ambiguous = new ReaderFixture(0x0230, duplicateControlHidPath: true);
        var ambiguousResult = await ambiguous.CreateReader().ReadAsync(
            "msi.claw.a2vm.8", () => ambiguous.Identity, () => true, CancellationToken.None);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, ambiguousResult.M1.Outcome);
        Assert.Empty(ambiguous.Transport.Requests);

        var noOwner = new ReaderFixture(0x0230);
        var noOwnerResult = await noOwner.CreateReader().ReadAsync(
            "msi.claw.a2vm.8", () => null, () => true, CancellationToken.None);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, noOwnerResult.M1.Outcome);
        Assert.Empty(noOwner.Transport.Requests);

        var wrongPid = new ReaderFixture(0x0230, wrongPid: true);
        var wrongPidResult = await wrongPid.CreateReader().ReadAsync(
            "msi.claw.a2vm.8", () => wrongPid.Identity, () => true, CancellationToken.None);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.OwnerUnavailable, wrongPidResult.M1.Outcome);
        Assert.Empty(wrongPid.Transport.Requests);

        var unavailableTransport = new ReaderFixture(0x0230);
        unavailableTransport.Transport.AttributesAvailable = false;
        var unavailableResult = await unavailableTransport.CreateReader().ReadAsync(
            "msi.claw.a2vm.8", () => unavailableTransport.Identity, () => true, CancellationToken.None);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.TransportUnavailable, unavailableResult.M1.Outcome);
        Assert.Empty(unavailableTransport.Transport.Requests);
    }

    [Fact]
    public async Task Profile_reader_classifies_no_reply_unexpected_ack_and_cancellation_without_extra_commands()
    {
        var timeout = new ReaderFixture(0x0230);
        timeout.Transport.Exchange = _ => new(true, []);
        var timeoutResult = await timeout.CreateReader().ReadAsync(
            "msi.claw.a2vm.8", () => timeout.Identity, () => true, CancellationToken.None);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.NoReplyOrTimeout, timeoutResult.M1.Outcome);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.NoReplyOrTimeout, timeoutResult.M2.Outcome);
        Assert.Equal(2, timeout.Transport.Requests.Count);

        var wrongAck = new ReaderFixture(0x0414);
        wrongAck.Transport.Exchange = request =>
        {
            var address = (ushort)((request[6] << 8) | request[7]);
            var report = ReadResponse(address, new byte[] { 0x01, 0x04, 0x0C, 0x9D, 1, 2, 3, 4 });
            report[4] = 0x21;
            return new(true, [report]);
        };
        var wrongAckResult = await wrongAck.CreateReader().ReadAsync(
            "msi.claw.cg3em", () => wrongAck.Identity, () => true, CancellationToken.None);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.UnexpectedReport, wrongAckResult.M1.Outcome);
        Assert.Equal(2, wrongAck.Transport.Requests.Count);

        var cancelled = new ReaderFixture(0x0230);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledResult = await cancelled.CreateReader().ReadAsync(
            "msi.claw.a2vm.8", () => cancelled.Identity, () => true, cancellation.Token);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.Cancelled, cancelledResult.M1.Outcome);
        Assert.Equal(FrontendMsiBackButtonProfileReadOutcome.Cancelled, cancelledResult.M2.Outcome);
        Assert.Empty(cancelled.Transport.Requests);
    }

    private static void AssertRequest(byte[] request, ushort address)
    {
        Assert.Equal(64, request.Length);
        Assert.Equal(0x0F, request[0]);
        Assert.Equal(0x3C, request[3]);
        Assert.Equal(0x04, request[4]);
        Assert.Equal(0x01, request[5]);
        Assert.Equal((byte)(address >> 8), request[6]);
        Assert.Equal((byte)address, request[7]);
        Assert.Equal(0x08, request[8]);
        Assert.All(request.Skip(9), value => Assert.Equal(0, value));
        Assert.DoesNotContain(request[4], new byte[] { 0x21, 0x22, 0x24 });
    }

    private static byte[] ReadResponse(ushort address, byte[] payload)
    {
        var response = new byte[64];
        response[0] = 0x10;
        response[3] = 0x3C;
        response[4] = 0x05;
        response[5] = MsiClawBackButtonProfileProtocol.ReadIndex;
        response[6] = (byte)(address >> 8);
        response[7] = (byte)address;
        response[8] = MsiClawBackButtonProfileProtocol.PayloadLength;
        response[9] = 0x00;
        payload.CopyTo(response, 10);
        return response;
    }

    private sealed class ReaderFixture
    {
        private const string DevicePath = "exact-control-path";
        private readonly ControllerDeviceInfo[] _devices;
        private readonly MsiClawHidDeviceInformation[] _hidInformation;

        internal ReaderFixture(ushort firmware, bool duplicateControlHidPath = false, bool wrongPid = false)
        {
            var containerId = Guid.NewGuid();
            const string instanceId = "HID\\VID_0DB0&PID_1902&MI_00&COL01\\CLAW_A";
            var control = new ControllerDeviceInfo(
                instanceId,
                containerId,
                "USB\\VID_0DB0&PID_1902\\CLAW_A",
                ["USB\\VID_0DB0&PID_1902\\CLAW_A"],
                "HID", ["HID\\VID_0DB0&PID_1902"], [], "HIDClass", null, "HidUsb",
                0x0DB0, wrongPid ? (ushort)0x1901 : (ushort)0x1902, true, "MSI Claw Control", 0xFFF0, 0x0040);
            _devices = [control];
            var info = new MsiClawHidDeviceInformation(DevicePath, instanceId, containerId);
            _hidInformation = duplicateControlHidPath ? [info, info] : [info];
            Identity = MsiClawPhysicalIdentity.From(control);
            Transport = new RecordingTransport(firmware);
        }

        internal MsiClawPhysicalIdentity Identity { get; }
        internal RecordingTransport Transport { get; }

        internal MsiClawBackButtonProfileReader CreateReader() => new(
            new FixedDeviceEnumerator(_devices),
            new MsiClawControlHidResolver(),
            new FixedHidLookup(_hidInformation),
            Transport);
    }

    private sealed class FixedDeviceEnumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => devices;
    }

    private sealed class FixedHidLookup(IReadOnlyList<MsiClawHidDeviceInformation> information) : IMsiClawHidDeviceInformationLookup
    {
        public Task<IReadOnlyList<MsiClawHidDeviceInformation>> FindAsync(string selector, CancellationToken cancellationToken) =>
            Task.FromResult(information);
    }

    private sealed class RecordingTransport(ushort firmware) : IMsiClawRawHidTransport
    {
        public List<byte[]> Requests { get; } = [];
        public List<byte[]> Mutations { get; } = [];
        public bool AttributesAvailable { get; set; } = true;
        public Func<byte[], MsiClawHidWriteAndReadResult>? Exchange { get; set; }

        public bool TryGetDeviceAttributes(string devicePath, out MsiClawHidDeviceAttributes attributes)
        {
            attributes = new(MsiClawHardware.VendorId, MsiClawHardware.DirectInputProductId, firmware);
            return AttributesAvailable && devicePath == "exact-control-path";
        }

        public Task<bool> WriteAsync(string devicePath, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            Mutations.Add(bytes.ToArray());
            return Task.FromResult(true);
        }

        public Task<MsiClawHidWriteAndReadResult> WriteAndReadAsync(
            string devicePath, ReadOnlyMemory<byte> bytes, int reportLength, int maxReports,
            TimeSpan timeout, CancellationToken cancellationToken, bool preserveShortReports = false)
        {
            var request = bytes.ToArray();
            Requests.Add(request);
            if (Exchange is not null) return Task.FromResult(Exchange(request));
            var address = (ushort)((request[6] << 8) | request[7]);
            var payload = address == 0x00BA
                ? new byte[] { 0x01, 0x04, 0x0C, 0x9D, 0x11, 0x22, 0x33, 0x44 }
                : new byte[] { 0x01, 0x04, 0x0C, 0x12, 0x55, 0x66, 0x77, 0x88 };
            return Task.FromResult(new MsiClawHidWriteAndReadResult(true, [ReadResponse(address, payload)]));
        }
    }
}
