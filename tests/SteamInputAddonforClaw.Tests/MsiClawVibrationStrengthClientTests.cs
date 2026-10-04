using SteamInputAddonforClaw.Controllers.Detection;
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
        string modelId = "msi.claw.cg3em")
    {
        var devices = new FakeEnumerator([CreateCommandDevice()]);
        return (new MsiClawVibrationStrengthClient(new HandheldDeviceModelId(modelId), devices, new MsiClawControlHidResolver(), io), devices);
    }

    private static ControllerDeviceInfo CreateCommandDevice()
    {
        var container = Guid.NewGuid();
        const string root = "USB\\VID_0DB0&PID_1902\\CLAW";
        return new ControllerDeviceInfo(
            "HID\\VID_0DB0&PID_1902&MI_00&COL02\\CLAW",
            container,
            root,
            [root],
            "HID",
            [],
            [],
            "HIDClass",
            null,
            null,
            0x0DB0,
            0x1902,
            true,
            "MSI Claw Control",
            0xFFF0,
            0x0040);
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

        public Task<bool> WriteAsync(MsiClawControlHidDevice device, ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
        {
            WriteFrames.Add(report.ToArray());
            return Task.FromResult(true);
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
