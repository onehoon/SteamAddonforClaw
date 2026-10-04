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
    public async Task Unverified_capture_reads_both_addresses_for_diagnostics_but_returns_no_firmware_values()
    {
        var io = new FakeProfileIo(40, 70);
        var (client, _) = CreateClient(io, "msi.claw.cg3em");

        var result = await client.CaptureAsync(default);

        Assert.False(result.Succeeded);
        Assert.Null(result.Values);
        Assert.Equal("FirmwareAddressMappingUnverified", result.Reason);
        Assert.Equal(new ushort[] { 0x0022, 0x0023 }, io.ReadAddresses);
        Assert.Empty(io.WriteFrames);
        AppLog.DrainForTests();

        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("ControllerVibrationProfileProbe", log);
        Assert.Contains("Model=msi.claw.cg3em", log);
        Assert.Contains("ProductId=0x1902", log);
        Assert.Contains("UsagePage=0xFFF0", log);
        Assert.Contains("Usage=0x0040", log);
        Assert.Contains("Address=0x0022", log);
        Assert.Contains("Address=0x0023", log);
        Assert.Contains("ResponsePrefix=10-00-00-3C-05-01-00-22-01-28", log);
        Assert.Contains("ResponsePrefix=10-00-00-3C-05-01-00-23-01-46", log);
        Assert.Contains("ParsedValue=40", log);
        Assert.Contains("ParsedValue=70", log);
        Assert.Contains("ParseSucceeded=True", log);
        Assert.Contains("VerifiedForProduction=False", log);
    }

    [Fact]
    public async Task Unverified_diagnostic_logs_rejected_response_and_still_probes_the_other_address()
    {
        var io = new FakeProfileIo(40, 70);
        io.InvalidResponseAddresses.Add(0x0022);
        var (client, _) = CreateClient(io, "msi.claw.cg3em");

        var result = await client.CaptureAsync(default);

        Assert.False(result.Succeeded);
        Assert.Null(result.Values);
        Assert.Equal("FirmwareAddressMappingUnverified", result.Reason);
        Assert.Equal(new ushort[] { 0x0022, 0x0023 }, io.ReadAddresses);
        Assert.Empty(io.WriteFrames);
        AppLog.DrainForTests();

        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("Address=0x0022", log);
        Assert.Contains("ParseSucceeded=False", log);
        Assert.Contains("Address=0x0023", log);
        Assert.Contains("ParseSucceeded=True", log);
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
        Assert.Empty(io.ReadAddresses);
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
        Assert.Empty(io.ReadAddresses);
        Assert.Empty(io.WriteFrames);
    }

    [Fact]
    public async Task Each_operation_reenumerates_instead_of_reusing_a_removed_command_device()
    {
        var io = new FakeProfileIo(50, 50);
        var (client, devices) = CreateClient(io, "msi.claw.cg3em");

        var beforeRemoval = await client.CaptureAsync(default);
        Assert.Equal("FirmwareAddressMappingUnverified", beforeRemoval.Reason);
        devices.Devices = [];
        var afterRemoval = await client.CaptureAsync(default);

        Assert.False(afterRemoval.Succeeded);
        Assert.Null(afterRemoval.Values);
        Assert.Equal("FirmwareAddressMappingUnverified", afterRemoval.Reason);
        Assert.Equal(2, devices.EnumerationCount);
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

    private sealed class FakeProfileIo(int left, int right) : IMsiClawVibrationProfileIo
    {
        private readonly Dictionary<ushort, int> _values = new() { [0x0022] = left, [0x0023] = right };

        internal List<byte[]> WriteFrames { get; } = [];
        internal List<ushort> ReadAddresses { get; } = [];
        internal HashSet<ushort> InvalidResponseAddresses { get; } = [];

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
            var address = Address(report.Span);
            ReadAddresses.Add(address);
            var response = MsiClawVibrationProfileCommandTests.Response(address, _values[address]);
            if (InvalidResponseAddresses.Contains(address))
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
