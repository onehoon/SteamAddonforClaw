using SteamInputAddonforClaw.Contracts.ControllerVibration;
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
    private readonly AppLogLevel _previousMinimumLevel;
    private readonly string? _previousDirectory;

    public MsiClawVibrationStrengthClientTests()
    {
        _previousMinimumLevel = AppLog.MinimumLevelOverride;
        _previousDirectory = AppLog.DirectoryOverride;
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        AppLog.DirectoryOverride = _logDirectory;
    }

    [Theory]
    [InlineData(50, 50, 0x32, 0x32)]
    [InlineData(0, 100, 0x00, 0x64)]
    [InlineData(100, 0, 0x64, 0x00)]
    public async Task Production_CG3EM_apply_sends_one_exact_pair_write_to_the_owned_PID1902_control_HID(
        int left,
        int right,
        byte expectedLeft,
        byte expectedRight)
    {
        var controlDevice = CreateControlDevice();
        var io = new FakeProfileIo();
        var (client, devices) = CreateClient(io, "msi.claw.cg3em", [controlDevice]);

        var succeeded = await client.ApplyAsync(
            new ControllerVibrationSettings(left, right),
            MsiClawPhysicalIdentity.From(controlDevice),
            default);

        Assert.True(succeeded);
        Assert.Equal(1, devices.EnumerationCount);
        Assert.Single(io.WriteFrames);
        Assert.Equal(MsiClawVibrationProfileCommand.BuildMotorPairWrite(left, right), io.WriteFrames[0]);
        Assert.Equal(new byte[] { 0x0F, 0, 0, 0x3C, 0x21, 0x01, 0, 0x22, 0x02, expectedLeft, expectedRight }, io.WriteFrames[0][..11]);
    }

    [Theory]
    [InlineData("msi.claw.a2vm.7")]
    [InlineData("msi.claw.a2vm.8")]
    [InlineData("unknown")]
    public async Task Production_apply_is_unavailable_for_unverified_models_before_HID_enumeration(string modelId)
    {
        var io = new FakeProfileIo();
        var (client, devices) = CreateClient(io, modelId);
        var identity = MsiClawPhysicalIdentity.From(CreateControlDevice());

        var succeeded = await client.ApplyAsync(ControllerVibrationSettings.Default, identity, default);

        Assert.False(succeeded);
        Assert.Equal(0, devices.EnumerationCount);
        Assert.Empty(io.WriteFrames);
    }

    [Fact]
    public async Task Production_apply_rejects_PID1901_before_HID_enumeration()
    {
        var io = new FakeProfileIo();
        var (client, devices) = CreateClient(io, "msi.claw.cg3em");
        var identity = MsiClawPhysicalIdentity.From(CreateControlDevice(productId: 0x1901, usagePage: 0xFFA0, usage: 0x0001));

        var succeeded = await client.ApplyAsync(ControllerVibrationSettings.Default, identity, default);

        Assert.False(succeeded);
        Assert.Equal(0, devices.EnumerationCount);
        Assert.Empty(io.WriteFrames);
    }

    [Fact]
    public async Task Production_apply_rejects_weak_identity_before_HID_enumeration()
    {
        var io = new FakeProfileIo();
        var (client, devices) = CreateClient(io, "msi.claw.cg3em");
        var identity = MsiClawPhysicalIdentity.From(CreateControlDevice(strongIdentity: false));

        var succeeded = await client.ApplyAsync(ControllerVibrationSettings.Default, identity, default);

        Assert.False(succeeded);
        Assert.Equal(0, devices.EnumerationCount);
        Assert.Empty(io.WriteFrames);
    }

    [Fact]
    public async Task Production_apply_rejects_a_control_HID_that_does_not_match_the_owned_identity()
    {
        var expected = CreateControlDevice(instanceSuffix: "OWNED");
        var other = CreateControlDevice(
            instanceSuffix: "OTHER",
            identityContainer: Guid.NewGuid(),
            identityRoot: "USB\\VID_0DB0&PID_1902\\OTHER");
        var io = new FakeProfileIo();
        var (client, _) = CreateClient(io, "msi.claw.cg3em", [other]);

        var succeeded = await client.ApplyAsync(
            ControllerVibrationSettings.Default,
            MsiClawPhysicalIdentity.From(expected),
            default);

        Assert.False(succeeded);
        Assert.Empty(io.WriteFrames);
    }

    [Fact]
    public async Task Production_apply_rejects_ambiguous_control_HIDs_without_writing()
    {
        var first = CreateControlDevice(instanceSuffix: "FIRST");
        var duplicate = first with { InstanceId = "HID\\VID_0DB0&PID_1902&MI_00&COL02\\DUPLICATE" };
        var io = new FakeProfileIo();
        var (client, _) = CreateClient(io, "msi.claw.cg3em", [first, duplicate]);

        var succeeded = await client.ApplyAsync(
            ControllerVibrationSettings.Default,
            MsiClawPhysicalIdentity.From(first),
            default);

        Assert.False(succeeded);
        Assert.Empty(io.WriteFrames);
    }

    [Fact]
    public async Task Production_write_failure_is_not_retried_or_read_back()
    {
        var device = CreateControlDevice();
        var io = new FakeProfileIo { WriteResult = false };
        var (client, _) = CreateClient(io, "msi.claw.cg3em", [device]);

        var succeeded = await client.ApplyAsync(
            new ControllerVibrationSettings(30, 70),
            MsiClawPhysicalIdentity.From(device),
            default);

        Assert.False(succeeded);
        Assert.Single(io.WriteFrames);
    }

    [Theory]
    [InlineData(FrontendProbe.ApplyZeroHundred, 0x00, 0x64)]
    [InlineData(FrontendProbe.RestoreFiftyFifty, 0x32, 0x32)]
    public async Task Developer_profile_probe_keeps_its_explicit_single_pair_write(
        FrontendProbe mode,
        byte expectedLeft,
        byte expectedRight)
    {
        var device = CreateControlDevice();
        var io = new FakeProfileIo();
        var (client, _) = CreateClient(io, "msi.claw.cg3em", [device]);
        var clientMode = mode == FrontendProbe.ApplyZeroHundred
            ? MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred
            : MsiClawVibrationProfileWriteProbeMode.RestoreFiftyFifty;

        var result = await client.RunDiagnosticMotorPairWriteAsync(clientMode, () => true, default);

        Assert.True(result.Succeeded);
        Assert.Single(io.WriteFrames);
        Assert.Equal(new byte[] { 0x0F, 0, 0, 0x3C, 0x21, 0x01, 0, 0x22, 0x02, expectedLeft, expectedRight }, io.WriteFrames[0][..11]);
        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("ControllerVibrationProfileWriteProbeStarted", log);
        Assert.Contains("SyncToRom=False", log);
        Assert.Contains("VerifiedForProduction=False", log);
    }

    [Fact]
    public async Task Developer_probe_requires_disabled_authority_and_CG3EM_before_enumeration()
    {
        var io = new FakeProfileIo();
        var (client, devices) = CreateClient(io, "msi.claw.cg3em");

        var authorityUnavailable = await client.RunDiagnosticMotorPairWriteAsync(
            MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred, () => false, default);

        Assert.Equal(MsiClawVibrationProfileWriteProbeOutcome.Unavailable, authorityUnavailable.Outcome);
        Assert.Equal(0, devices.EnumerationCount);
        Assert.Empty(io.WriteFrames);

        var (unsupportedClient, unsupportedDevices) = CreateClient(io, "msi.claw.a2vm.8");
        var unsupported = await unsupportedClient.RunDiagnosticMotorPairWriteAsync(
            MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred, () => true, default);

        Assert.Equal(MsiClawVibrationProfileWriteProbeOutcome.Unavailable, unsupported.Outcome);
        Assert.Equal(0, unsupportedDevices.EnumerationCount);
        Assert.Empty(io.WriteFrames);
    }

    private static (MsiClawVibrationStrengthClient Client, FakeEnumerator Devices) CreateClient(
        FakeProfileIo io,
        string modelId,
        IReadOnlyList<ControllerDeviceInfo>? configuredDevices = null)
    {
        var devices = new FakeEnumerator(configuredDevices ?? [CreateControlDevice()]);
        return (new MsiClawVibrationStrengthClient(
            new HandheldDeviceModelId(modelId), devices, new MsiClawControlHidResolver(), io), devices);
    }

    private static ControllerDeviceInfo CreateControlDevice(
        ushort productId = 0x1902,
        ushort usagePage = 0xFFF0,
        ushort usage = 0x0040,
        bool strongIdentity = true,
        string instanceSuffix = "CLAW",
        Guid? identityContainer = null,
        string? identityRoot = null)
    {
        Guid? container = strongIdentity ? identityContainer ?? new Guid("5d6f297b-0f1a-4d58-b737-0048baec0dd1") : null;
        string? root = strongIdentity ? identityRoot ?? "USB\\VID_0DB0&PID_1902\\CLAW" : null;
        IReadOnlyList<string> ancestors = root is null ? [] : [root];
        return new ControllerDeviceInfo(
            $"HID\\VID_0DB0&PID_{productId:X4}&MI_00&COL02\\{instanceSuffix}",
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

    public enum FrontendProbe { ApplyZeroHundred, RestoreFiftyFifty }

    private sealed class FakeEnumerator(IReadOnlyList<ControllerDeviceInfo> devices) : IControllerDeviceEnumerator
    {
        public int EnumerationCount { get; private set; }

        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
        {
            EnumerationCount++;
            return devices;
        }
    }

    private sealed class FakeProfileIo : IMsiClawVibrationProfileIo
    {
        public List<byte[]> WriteFrames { get; } = [];
        public bool WriteResult { get; init; } = true;

        public Task<bool> WriteAsync(
            MsiClawControlHidDevice device,
            ReadOnlyMemory<byte> report,
            CancellationToken cancellationToken)
        {
            WriteFrames.Add(report.ToArray());
            return Task.FromResult(WriteResult);
        }
    }

    public void Dispose()
    {
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = _previousDirectory;
        AppLog.MinimumLevelOverride = _previousMinimumLevel;
        if (Directory.Exists(_logDirectory))
            Directory.Delete(_logDirectory, recursive: true);
    }
}
