using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class MsiClawVibrationStrengthClientTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public MsiClawVibrationStrengthClientTests()
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        AppLog.DirectoryOverride = _logDirectory;
    }

    [Fact]
    public async Task Unverified_capture_reads_both_profile_indexes_and_returns_no_firmware_values()
    {
        var io = new FakeProfileIo(30, 70, index1Left: 50, index1Right: 50);
        var (client, _) = CreateClient(io, "msi.claw.cg3em");

        var result = await client.CaptureAsync(default);

        Assert.False(result.Succeeded);
        Assert.Null(result.Values);
        Assert.Equal("FirmwareAddressMappingUnverified", result.Reason);
        Assert.Equal(2, io.ReadFrames.Count);
        Assert.Equal(MsiClawVibrationProfileCommand.BuildDiagnosticReadProfile(0x00, 0x0022, 0x02), io.ReadFrames[0]);
        Assert.Equal(MsiClawVibrationProfileCommand.BuildDiagnosticReadProfile(0x01, 0x0022, 0x02), io.ReadFrames[1]);
        Assert.All(io.ReadFrames, frame =>
        {
            Assert.Equal(0x04, frame[4]);
            Assert.Equal(0x00, frame[6]);
            Assert.Equal(0x22, frame[7]);
            Assert.Equal(0x02, frame[8]);
            Assert.DoesNotContain(frame[4], new byte[] { 0x21, 0x22 });
        });
        Assert.Empty(io.WriteFrames);
        AppLog.DrainForTests();

        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("ControllerVibrationProfileIndexProbe", log);
        Assert.Contains("Model=msi.claw.cg3em", log);
        Assert.Contains("ProductId=0x1902", log);
        Assert.Contains("UsagePage=0xFFF0", log);
        Assert.Contains("Usage=0x0040", log);
        Assert.Contains("Address=0x0022", log);
        Assert.Contains("RequestIndex=0", log);
        Assert.Contains("RequestIndex=1", log);
        Assert.Contains("ResponsePrefix=10-00-00-3C-05-00-00-22-02-1E-46", log);
        Assert.Contains("ResponsePrefix=10-00-00-3C-05-01-00-22-02-32-32", log);
        Assert.Contains("ResponseIndex=0", log);
        Assert.Contains("ResponseIndex=1", log);
        Assert.Contains("IndexEchoMatched=True", log);
        Assert.Contains("ResponseAddress=0x0022", log);
        Assert.Contains("ResponseLength=2", log);
        Assert.Contains("ResponseReportLength=64", log);
        Assert.Contains("CandidateLeft=30", log);
        Assert.Contains("CandidateRight=70", log);
        Assert.Contains("CandidateLeft=50", log);
        Assert.Contains("CandidateRight=50", log);
        Assert.Contains("StructuralParseSucceeded=True", log);
        Assert.Contains("VerifiedForProduction=False", log);
    }

    [Fact]
    public async Task Unverified_diagnostic_logs_rejected_index_zero_response_and_still_reads_index_one()
    {
        var io = new FakeProfileIo(40, 70);
        io.InvalidResponseIndexes.Add(0x00);
        var (client, _) = CreateClient(io, "msi.claw.cg3em");

        var result = await client.CaptureAsync(default);

        Assert.False(result.Succeeded);
        Assert.Null(result.Values);
        Assert.Equal("FirmwareAddressMappingUnverified", result.Reason);
        Assert.Equal(2, io.ReadFrames.Count);
        Assert.Equal(0x00, io.ReadFrames[0][5]);
        Assert.Equal(0x01, io.ReadFrames[1][5]);
        Assert.Empty(io.WriteFrames);
        AppLog.DrainForTests();

        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("RequestIndex=0", log);
        Assert.Contains("RequestIndex=1", log);
        Assert.Contains("StructuralParseSucceeded=False", log);
        Assert.Contains("StructuralParseSucceeded=True", log);
    }

    [Fact]
    public async Task Unverified_diagnostic_logs_index_echo_mismatch_without_rejecting_raw_response()
    {
        var io = new FakeProfileIo(30, 70);
        io.ResponseIndexOverrides[0x00] = 0x01;
        var (client, _) = CreateClient(io, "msi.claw.cg3em");

        var result = await client.CaptureAsync(default);

        Assert.False(result.Succeeded);
        Assert.Null(result.Values);
        Assert.Equal(2, io.ReadFrames.Count);
        AppLog.DrainForTests();

        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("RequestIndex=0", log);
        Assert.Contains("ResponseIndex=1", log);
        Assert.Contains("IndexEchoMatched=False", log);
        Assert.Contains("CandidateLeft=30", log);
        Assert.Contains("CandidateRight=70", log);
        Assert.Contains("StructuralParseSucceeded=True", log);
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(70, 35)]
    public async Task Valid_mutation_on_unverified_model_is_rejected_before_any_hid_io(int left, int right)
    {
        var io = new FakeProfileIo(50, 50);
        var (client, devices) = CreateClient(io, "msi.claw.cg3em");

        var result = await client.SetAsync(left, right, () => true, default);

        Assert.Equal(MsiClawVibrationStrengthMutationOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Values);
        Assert.Equal("FirmwareAddressMappingUnverified", result.Reason);
        Assert.Equal(0, devices.EnumerationCount);
        Assert.Empty(io.ReadFrames);
        Assert.Empty(io.WriteFrames);
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(101, 50)]
    [InlineData(50, -1)]
    [InlineData(50, 101)]
    public async Task Set_rejects_out_of_range_values_before_device_enumeration(int left, int right)
    {
        var io = new FakeProfileIo(50, 50);
        var (client, devices) = CreateClient(io);

        var result = await client.SetAsync(left, right, () => true, default);

        Assert.False(result.Succeeded);
        Assert.Equal("InvalidPercent", result.Reason);
        Assert.Equal(0, devices.EnumerationCount);
        Assert.Empty(io.WriteFrames);
    }

    [Fact]
    public async Task Verified_centerM_authority_cannot_bypass_unverified_firmware_address_guard()
    {
        var io = new FakeProfileIo(50, 50);
        var (client, devices) = CreateClient(io, "msi.claw.cg3em");

        var result = await client.SetAsync(70, 50, () => true, default);

        Assert.Equal(MsiClawVibrationStrengthMutationOutcome.Unavailable, result.Outcome);
        Assert.Equal("FirmwareAddressMappingUnverified", result.Reason);
        Assert.Equal(0, devices.EnumerationCount);
        Assert.Empty(io.ReadFrames);
        Assert.Empty(io.WriteFrames);
    }

    [Theory]
    [InlineData(FrontendControllerVibrationProfileWriteProbeMode.ApplyZeroHundred, 0x00, 0x64)]
    [InlineData(FrontendControllerVibrationProfileWriteProbeMode.RestoreFiftyFifty, 0x32, 0x32)]
    public async Task Developer_pair_write_probe_sends_exactly_one_contiguous_pid1902_write_without_readback_or_sync(
        FrontendControllerVibrationProfileWriteProbeMode mode,
        byte left,
        byte right)
    {
        var io = new FakeProfileIo(50, 50);
        var (client, devices) = CreateClient(io);
        var clientMode = mode switch
        {
            FrontendControllerVibrationProfileWriteProbeMode.ApplyZeroHundred => MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred,
            _ => MsiClawVibrationProfileWriteProbeMode.RestoreFiftyFifty
        };

        var result = await client.RunDiagnosticMotorPairWriteAsync(clientMode, () => true, default);

        Assert.Equal(MsiClawVibrationProfileWriteProbeOutcome.Succeeded, result.Outcome);
        Assert.Equal(clientMode, result.Mode);
        Assert.Equal(1, devices.EnumerationCount);
        var report = Assert.Single(io.WriteFrames);
        Assert.Equal(64, report.Length);
        Assert.Equal(new byte[] { 0x0F, 0x00, 0x00, 0x3C, 0x21, 0x01, 0x00, 0x22, 0x02, left, right }, report[..11]);
        Assert.Empty(io.ReadFrames);
        AppLog.DrainForTests();

        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("ControllerVibrationProfileWriteProbeStarted", log);
        Assert.Contains("ControllerVibrationProfileWriteProbeCompleted", log);
        Assert.Contains("Model=msi.claw.cg3em", log);
        Assert.Contains("ProductId=0x1902", log);
        Assert.Contains("ProfileIndex=1", log);
        Assert.Contains("Address=0x0022", log);
        Assert.Contains("Length=2", log);
        Assert.Contains("SyncToRom=False", log);
        Assert.Contains("VerifiedForProduction=False", log);
        Assert.Contains("TransportSucceeded=True", log);
    }

    [Theory]
    [InlineData(FrontendControllerVibrationProfileWriteProbeMode.ApplyZeroHundred, 0, 100)]
    [InlineData(FrontendControllerVibrationProfileWriteProbeMode.RestoreFiftyFifty, 50, 50)]
    public async Task Developer_pair_write_probe_transport_failure_is_not_retried_or_read_back(
        FrontendControllerVibrationProfileWriteProbeMode mode,
        byte left,
        byte right)
    {
        var io = new FakeProfileIo(50, 50) { WriteResult = false };
        var (client, _) = CreateClient(io);
        var clientMode = mode == FrontendControllerVibrationProfileWriteProbeMode.ApplyZeroHundred
            ? MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred
            : MsiClawVibrationProfileWriteProbeMode.RestoreFiftyFifty;

        var result = await client.RunDiagnosticMotorPairWriteAsync(
            clientMode, () => true, default);

        Assert.Equal(MsiClawVibrationProfileWriteProbeOutcome.Failed, result.Outcome);
        Assert.Equal("TransportWriteFailed", result.Reason);
        var report = Assert.Single(io.WriteFrames);
        Assert.Equal(new byte[] { 0x0F, 0x00, 0x00, 0x3C, 0x21, 0x01, 0x00, 0x22, 0x02, left, right }, report[..11]);
        Assert.Empty(io.ReadFrames);
        AppLog.DrainForTests();

        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("ControllerVibrationProfileWriteProbeCompleted", log);
        Assert.Contains("TransportSucceeded=False", log);
        Assert.Contains("SyncToRom=False", log);
    }

    [Theory]
    [InlineData("msi.claw.cg3em", 0x1901, 0xFFA0, 0x0001, true)]
    [InlineData("msi.claw.cg3em", 0x1902, 0x0001, 0x0005, true)]
    [InlineData("msi.claw.cg3em", 0x1902, 0xFFF0, 0x0040, false)]
    public async Task Developer_pair_write_probe_rejects_wrong_pid_endpoint_or_weak_identity_before_write(
        string modelId,
        int productId,
        int usagePage,
        int usage,
        bool strongIdentity)
    {
        var io = new FakeProfileIo(50, 50);
        var device = CreateCommandDevice((ushort)productId, (ushort)usagePage, (ushort)usage, strongIdentity);
        var (client, _) = CreateClient(io, modelId, [device]);

        var result = await client.RunDiagnosticMotorPairWriteAsync(
            MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred, () => true, default);

        Assert.NotEqual(MsiClawVibrationProfileWriteProbeOutcome.Succeeded, result.Outcome);
        Assert.Empty(io.WriteFrames);
        Assert.Empty(io.ReadFrames);
    }

    [Fact]
    public async Task Developer_pair_write_probe_rejects_non_CG3EM_model_before_enumeration()
    {
        var io = new FakeProfileIo(50, 50);
        var (client, devices) = CreateClient(io, "msi.claw.a2vm.7");

        var result = await client.RunDiagnosticMotorPairWriteAsync(
            MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred, () => true, default);

        Assert.Equal(MsiClawVibrationProfileWriteProbeOutcome.Unavailable, result.Outcome);
        Assert.Equal("UnsupportedModel", result.Reason);
        Assert.Equal(0, devices.EnumerationCount);
        Assert.Empty(io.WriteFrames);
        Assert.Empty(io.ReadFrames);
    }

    [Fact]
    public async Task Developer_pair_write_probe_requires_exact_centerM_disabled_state_before_enumeration()
    {
        var io = new FakeProfileIo(50, 50);
        var (client, devices) = CreateClient(io);

        var enabled = await client.RunDiagnosticMotorPairWriteAsync(
            MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred, () => false, default);
        var unavailable = await client.RunDiagnosticMotorPairWriteAsync(
            MsiClawVibrationProfileWriteProbeMode.RestoreFiftyFifty, () => throw new InvalidOperationException(), default);

        Assert.Equal(MsiClawVibrationProfileWriteProbeOutcome.Unavailable, enabled.Outcome);
        Assert.Equal("CenterMIsNotExactlyDisabled", enabled.Reason);
        Assert.Equal(MsiClawVibrationProfileWriteProbeOutcome.Unavailable, unavailable.Outcome);
        Assert.Equal(0, devices.EnumerationCount);
        Assert.Empty(io.WriteFrames);
    }

    [Fact]
    public async Task Developer_pair_write_probe_rejects_ambiguous_pid1902_control_hids()
    {
        var io = new FakeProfileIo(50, 50);
        var first = CreateCommandDevice();
        var duplicate = first with { InstanceId = "HID\\VID_0DB0&PID_1902&MI_00&COL02\\SECOND" };
        var (client, _) = CreateClient(io, configuredDevices: [first, duplicate]);

        var result = await client.RunDiagnosticMotorPairWriteAsync(
            MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred, () => true, default);

        Assert.Equal(MsiClawVibrationProfileWriteProbeOutcome.Unavailable, result.Outcome);
        Assert.Equal("Pid1902ControlHidNotUniquelyResolved", result.Reason);
        Assert.Empty(io.WriteFrames);
    }

    [Fact]
    public async Task Each_operation_reenumerates_instead_of_reusing_a_removed_command_device()
    {
        var io = new FakeProfileIo(50, 50);
        var (client, devices) = CreateClient(io, "msi.claw.cg3em");

        var beforeRemoval = await client.CaptureAsync(default);
        Assert.Equal("FirmwareAddressMappingUnverified", beforeRemoval.Reason);
        Assert.Equal(2, io.ReadFrames.Count);
        devices.Devices = [];
        var afterRemoval = await client.CaptureAsync(default);

        Assert.False(afterRemoval.Succeeded);
        Assert.Null(afterRemoval.Values);
        Assert.Equal("FirmwareAddressMappingUnverified", afterRemoval.Reason);
        Assert.Equal(2, devices.EnumerationCount);
        Assert.Equal(2, io.ReadFrames.Count);
    }

    private static (MsiClawVibrationStrengthClient Client, FakeEnumerator Devices) CreateClient(
        FakeProfileIo io,
        string modelId = "msi.claw.cg3em",
        IReadOnlyList<ControllerDeviceInfo>? configuredDevices = null)
    {
        var devices = new FakeEnumerator(configuredDevices ?? [CreateCommandDevice()]);
        return (new MsiClawVibrationStrengthClient(new HandheldDeviceModelId(modelId), devices, new MsiClawControlHidResolver(), io), devices);
    }

    private static ControllerDeviceInfo CreateCommandDevice(
        ushort productId = 0x1902,
        ushort usagePage = 0xFFF0,
        ushort usage = 0x0040,
        bool strongIdentity = true)
    {
        Guid? container = strongIdentity ? Guid.NewGuid() : null;
        string? root = strongIdentity ? "USB\\VID_0DB0&PID_1902\\CLAW" : null;
        IReadOnlyList<string> ancestors = root is null ? [] : [root];
        return new ControllerDeviceInfo(
            "HID\\VID_0DB0&PID_1902&MI_00&COL02\\CLAW",
            container,
            root,
            ancestors,
            "HID",
            [],
            [],
            "HIDClass",
            null,
            null,
            0x0DB0,
            productId,
            true,
            "MSI Claw Control",
            usagePage,
            usage);
    }

    private sealed class FakeEnumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator
    {
        public IReadOnlyList<ControllerDeviceInfo> Devices { get; set; } = devices;
        public int EnumerationCount { get; private set; }

        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
        {
            EnumerationCount++;
            return Devices;
        }
    }

    private sealed class FakeProfileIo(
        int left,
        int right,
        int? index1Left = null,
        int? index1Right = null) : IMsiClawVibrationProfileIo
    {
        private readonly Dictionary<byte, (int Left, int Right)> _valuesByIndex = new()
        {
            [0x00] = (left, right),
            [0x01] = (index1Left ?? left, index1Right ?? right)
        };

        internal List<byte[]> WriteFrames { get; } = [];
        internal List<byte[]> ReadFrames { get; } = [];
        internal HashSet<byte> InvalidResponseIndexes { get; } = [];
        internal Dictionary<byte, byte> ResponseIndexOverrides { get; } = [];
        internal bool WriteResult { get; init; } = true;

        public Task<bool> WriteAsync(MsiClawControlHidDevice device, ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
        {
            WriteFrames.Add(report.ToArray());
            return Task.FromResult(WriteResult);
        }

        public Task<IReadOnlyList<byte[]>?> WriteAndReadAsync(
            MsiClawControlHidDevice device,
            ReadOnlyMemory<byte> report,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var request = report.ToArray();
            ReadFrames.Add(request);
            var profileIndex = request[5];
            var address = Address(request);
            var values = _valuesByIndex[profileIndex];
            var response = MsiClawVibrationProfileCommandTests.DiagnosticResponse(
                ResponseIndexOverrides.GetValueOrDefault(profileIndex, profileIndex),
                address,
                request[8],
                values.Left,
                values.Right);
            if (InvalidResponseIndexes.Contains(profileIndex))
                response[0] = 0xFF;
            return Task.FromResult<IReadOnlyList<byte[]>?>([response]);
        }

        private static ushort Address(ReadOnlySpan<byte> frame) => (ushort)((frame[6] << 8) | frame[7]);
    }

    public void Dispose()
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Off;
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_logDirectory))
            Directory.Delete(_logDirectory, recursive: true);
    }
}
