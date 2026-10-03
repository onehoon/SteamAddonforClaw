using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawVibrationStrengthClientTests
{
    [Theory]
    [InlineData(50, 50)]
    [InlineData(35, 70)]
    public async Task Capture_returns_actual_pair_without_mutating_firmware(int left, int right)
    {
        var io = new FakeProfileIo(left, right);
        var (client, _) = CreateClient(io);

        var result = await client.CaptureAsync(default);

        Assert.True(result.Succeeded);
        Assert.Equal(new MsiClawVibrationStrengthValues(left, right), result.Values);
        Assert.Empty(io.WriteFrames);
    }

    [Theory]
    [InlineData(50, 50, 0, 0)]
    [InlineData(70, 50, 0x0022, 0)]
    [InlineData(50, 70, 0, 0x0023)]
    [InlineData(70, 80, 0x0022, 0x0023)]
    public async Task Set_commits_only_changed_channels_in_order_and_returns_final_pair(
        int requestedLeft, int requestedRight, int expectedLeftAddress, int expectedRightAddress)
    {
        var io = new FakeProfileIo(50, 50);
        var (client, _) = CreateClient(io);

        var result = await client.SetAsync(requestedLeft, requestedRight, () => true, default);

        Assert.True(result.Succeeded);
        Assert.Equal(new MsiClawVibrationStrengthValues(requestedLeft, requestedRight), result.Values);
        var profileAddresses = io.WriteFrames.Where(frame => frame[4] == 0x21)
            .Select(frame => (frame[6] << 8) | frame[7]).ToArray();
        var expected = new[] { expectedLeftAddress, expectedRightAddress }.Where(address => address != 0).ToArray();
        Assert.Equal(expected, profileAddresses);
        var expectedCommands = Enumerable.Range(0, expected.Length)
            .SelectMany(_ => new byte[] { 0x21, 0x22 }).ToArray();
        Assert.Equal(expectedCommands, io.WriteFrames.Select(frame => frame[4]));
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
    public async Task CenterM_must_be_exactly_disabled_before_profile_mutation()
    {
        var io = new FakeProfileIo(50, 50);
        var (client, _) = CreateClient(io);

        var result = await client.SetAsync(70, 50, () => false, default);

        Assert.Equal(MsiClawVibrationStrengthMutationOutcome.Unavailable, result.Outcome);
        Assert.Equal(new MsiClawVibrationStrengthValues(50, 50), result.Values);
        Assert.Empty(io.WriteFrames);
    }

    [Fact]
    public async Task Profile_write_failure_returns_observed_values_and_never_reports_success()
    {
        var io = new FakeProfileIo(50, 50) { FailProfileWriteAtOrdinal = 1 };
        var (client, _) = CreateClient(io);

        var result = await client.SetAsync(70, 50, () => true, default);

        Assert.False(result.Succeeded);
        Assert.Equal(new MsiClawVibrationStrengthValues(50, 50), result.Values);
        Assert.Equal(new byte[] { 0x21 }, io.WriteFrames.Select(frame => frame[4]));
    }

    [Fact]
    public async Task Sync_failure_does_not_report_success_or_fabricate_requested_value()
    {
        var io = new FakeProfileIo(50, 50) { FailSyncAtOrdinal = 1 };
        var (client, _) = CreateClient(io);

        var result = await client.SetAsync(70, 50, () => true, default);

        Assert.False(result.Succeeded);
        Assert.Equal(new MsiClawVibrationStrengthValues(50, 50), result.Values);
        Assert.Equal(new byte[] { 0x21, 0x22 }, io.WriteFrames.Select(frame => frame[4]));
    }

    [Fact]
    public async Task Readback_mismatch_fails_and_returns_the_final_observed_pair()
    {
        var io = new FakeProfileIo(50, 50);
        io.OverrideRead(0x0022, readNumber: 2, value: 50);
        var (client, _) = CreateClient(io);

        var result = await client.SetAsync(70, 50, () => true, default);

        Assert.False(result.Succeeded);
        Assert.Equal(new MsiClawVibrationStrengthValues(70, 50), result.Values);
        Assert.Equal("LeftMotorCommitFailed", result.Reason);
    }

    [Fact]
    public async Task Partial_pair_failure_keeps_the_first_commit_and_reports_actual_pair_without_rollback()
    {
        var io = new FakeProfileIo(50, 50) { FailProfileWriteAtOrdinal = 2 };
        var (client, _) = CreateClient(io);

        var result = await client.SetAsync(70, 80, () => true, default);

        Assert.False(result.Succeeded);
        Assert.Equal(new MsiClawVibrationStrengthValues(70, 50), result.Values);
        Assert.Equal("RightMotorCommitFailed", result.Reason);
    }

    [Fact]
    public async Task Each_operation_reenumerates_instead_of_reusing_a_removed_command_device()
    {
        var io = new FakeProfileIo(50, 50);
        var (client, devices) = CreateClient(io);

        Assert.True((await client.CaptureAsync(default)).Succeeded);
        devices.Devices = [];
        var afterRemoval = await client.CaptureAsync(default);

        Assert.False(afterRemoval.Succeeded);
        Assert.Null(afterRemoval.Values);
        Assert.Equal(2, devices.EnumerationCount);
    }

    private static (MsiClawVibrationStrengthClient Client, FakeEnumerator Devices) CreateClient(FakeProfileIo io)
    {
        var devices = new FakeEnumerator([CreateCommandDevice()]);
        return (new MsiClawVibrationStrengthClient(devices, new MsiClawControlHidResolver(), io), devices);
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
        private readonly Dictionary<ushort, int> _readCounts = [];
        private readonly Dictionary<(ushort Address, int ReadNumber), int> _readOverrides = [];
        private readonly Dictionary<ushort, int> _staged = [];
        private int _profileWriteCount;
        private int _syncCount;

        internal List<byte[]> WriteFrames { get; } = [];
        internal int FailProfileWriteAtOrdinal { get; init; }
        internal int FailSyncAtOrdinal { get; init; }

        internal void OverrideRead(ushort address, int readNumber, int value) =>
            _readOverrides[(address, readNumber)] = value;

        public Task<bool> WriteAsync(MsiClawControlHidDevice device, ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
        {
            var frame = report.ToArray();
            WriteFrames.Add(frame);
            if (frame[4] == 0x21)
            {
                _profileWriteCount++;
                if (_profileWriteCount == FailProfileWriteAtOrdinal)
                    return Task.FromResult(false);
                _staged[Address(frame)] = frame[9];
            }
            else if (frame[4] == 0x22)
            {
                _syncCount++;
                if (_syncCount == FailSyncAtOrdinal)
                    return Task.FromResult(false);
                foreach (var staged in _staged)
                    _values[staged.Key] = staged.Value;
                _staged.Clear();
            }

            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<byte[]>?> WriteAndReadAsync(
            MsiClawControlHidDevice device,
            ReadOnlyMemory<byte> report,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var address = Address(report.Span);
            var readNumber = _readCounts.GetValueOrDefault(address) + 1;
            _readCounts[address] = readNumber;
            var value = _readOverrides.TryGetValue((address, readNumber), out var overridden)
                ? overridden
                : _values[address];
            return Task.FromResult<IReadOnlyList<byte[]>?>([MsiClawVibrationProfileCommandTests.Response(address, value)]);
        }

        private static ushort Address(ReadOnlySpan<byte> frame) => (ushort)((frame[6] << 8) | frame[7]);
    }
}
