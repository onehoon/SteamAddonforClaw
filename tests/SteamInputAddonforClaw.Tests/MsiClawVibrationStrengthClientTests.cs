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
    [InlineData("msi.claw.cg3em", 50, 50, 0x32, 0x32)]
    [InlineData("msi.claw.cg3em", 0, 100, 0x00, 0x64)]
    [InlineData("msi.claw.cg3em", 100, 0, 0x64, 0x00)]
    [InlineData("msi.claw.a2vm.8", 50, 50, 0x32, 0x32)]
    [InlineData("msi.claw.a2vm.8", 37, 63, 0x25, 0x3F)]
    public async Task Production_apply_sends_one_exact_pair_write_to_the_owned_PID1902_control_HID(
        string modelId,
        int left,
        int right,
        byte expectedLeft,
        byte expectedRight)
    {
        var controlDevice = CreateControlDevice();
        var io = new FakeProfileIo();
        var (client, devices) = CreateClient(io, modelId, [controlDevice]);

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

    [Theory]
    [InlineData("msi.claw.cg3em")]
    [InlineData("msi.claw.a2vm.8")]
    public async Task Production_apply_rejects_weak_identity_before_HID_enumeration(string modelId)
    {
        var io = new FakeProfileIo();
        var (client, devices) = CreateClient(io, modelId);
        var identity = MsiClawPhysicalIdentity.From(CreateControlDevice(strongIdentity: false));

        var succeeded = await client.ApplyAsync(ControllerVibrationSettings.Default, identity, default);

        Assert.False(succeeded);
        Assert.Equal(0, devices.EnumerationCount);
        Assert.Empty(io.WriteFrames);
    }

    [Theory]
    [InlineData("msi.claw.cg3em")]
    [InlineData("msi.claw.a2vm.8")]
    public async Task Production_apply_rejects_a_control_HID_that_does_not_match_the_owned_identity(string modelId)
    {
        var expected = CreateControlDevice(instanceSuffix: "OWNED");
        var other = CreateControlDevice(
            instanceSuffix: "OTHER",
            identityContainer: Guid.NewGuid(),
            identityRoot: "USB\\VID_0DB0&PID_1902\\OTHER");
        var io = new FakeProfileIo();
        var (client, _) = CreateClient(io, modelId, [other]);

        var succeeded = await client.ApplyAsync(
            ControllerVibrationSettings.Default,
            MsiClawPhysicalIdentity.From(expected),
            default);

        Assert.False(succeeded);
        Assert.Empty(io.WriteFrames);
    }

    [Theory]
    [InlineData("msi.claw.cg3em")]
    [InlineData("msi.claw.a2vm.8")]
    public async Task Production_apply_rejects_ambiguous_control_HIDs_without_writing(string modelId)
    {
        var first = CreateControlDevice(instanceSuffix: "FIRST");
        var duplicate = first with { InstanceId = "HID\\VID_0DB0&PID_1902&MI_00&COL02\\DUPLICATE" };
        var io = new FakeProfileIo();
        var (client, _) = CreateClient(io, modelId, [first, duplicate]);

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
    [InlineData("msi.claw.cg3em", FrontendProbe.ApplyZeroHundred, 0x00, 0x64)]
    [InlineData("msi.claw.cg3em", FrontendProbe.RestoreFiftyFifty, 0x32, 0x32)]
    [InlineData("msi.claw.a2vm.8", FrontendProbe.ApplyZeroHundred, 0x00, 0x64)]
    [InlineData("msi.claw.a2vm.8", FrontendProbe.RestoreFiftyFifty, 0x32, 0x32)]
    public async Task Developer_profile_probe_keeps_its_explicit_single_pair_write(
        string modelId,
        FrontendProbe mode,
        byte expectedLeft,
        byte expectedRight)
    {
        var device = CreateControlDevice();
        var io = new FakeProfileIo();
        var (client, _) = CreateClient(io, modelId, [device]);
        var identity = MsiClawPhysicalIdentity.From(device);
        var clientMode = mode == FrontendProbe.ApplyZeroHundred
            ? MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred
            : MsiClawVibrationProfileWriteProbeMode.RestoreFiftyFifty;

        var result = await client.RunDiagnosticMotorPairWriteAsync(clientMode, () => identity, () => true, default);

        Assert.True(result.Succeeded);
        Assert.Single(io.WriteFrames);
        Assert.Equal(64, io.WriteFrames[0].Length);
        Assert.Equal(MsiClawVibrationProfileCommand.BuildMotorPairWrite(expectedLeft, expectedRight), io.WriteFrames[0]);
        Assert.Equal(new byte[] { 0x0F, 0, 0, 0x3C, 0x21, 0x01, 0, 0x22, 0x02, expectedLeft, expectedRight }, io.WriteFrames[0][..11]);
        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("ControllerVibrationProfileWriteProbeStarted", log);
        Assert.Contains("ControllerVibrationProfileWriteProbeCompleted", log);
        Assert.Contains("SyncToRom=False", log);
        Assert.Contains("PhysicalEffectVerifiedByThisProbe=False", log);
        Assert.Contains("ThisWriteIsProductionApply=False", log);
        Assert.DoesNotContain("VerifiedForProduction=", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Developer_probe_requires_disabled_authority_and_supported_model_before_enumeration()
    {
        var io = new FakeProfileIo();
        var (client, devices) = CreateClient(io, "msi.claw.cg3em");
        var identity = MsiClawPhysicalIdentity.From(CreateControlDevice());

        var authorityUnavailable = await client.RunDiagnosticMotorPairWriteAsync(
            MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred, () => identity, () => false, default);

        Assert.Equal(MsiClawVibrationProfileWriteProbeOutcome.Unavailable, authorityUnavailable.Outcome);
        Assert.Equal(0, devices.EnumerationCount);
        Assert.Empty(io.WriteFrames);

        var (unsupportedClient, unsupportedDevices) = CreateClient(io, "msi.claw.a2vm.7");
        var unsupported = await unsupportedClient.RunDiagnosticMotorPairWriteAsync(
            MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred, () => identity, () => true, default);

        Assert.Equal(MsiClawVibrationProfileWriteProbeOutcome.Unavailable, unsupported.Outcome);
        Assert.Equal(0, unsupportedDevices.EnumerationCount);
        Assert.Empty(io.WriteFrames);

        var (unknownClient, unknownDevices) = CreateClient(io, "unknown");
        var unknown = await unknownClient.RunDiagnosticMotorPairWriteAsync(
            MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred, () => identity, () => true, default);
        Assert.Equal(MsiClawVibrationProfileWriteProbeOutcome.Unavailable, unknown.Outcome);
        Assert.Equal(0, unknownDevices.EnumerationCount);
        Assert.Empty(io.WriteFrames);
    }

    [Fact]
    public async Task Developer_probe_requires_a_healthy_owned_physical_session_before_enumeration()
    {
        var io = new FakeProfileIo();
        var (client, devices) = CreateClient(io, "msi.claw.a2vm.8");

        var unavailable = await client.RunDiagnosticMotorPairWriteAsync(
            MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred, () => null, () => true, default);

        Assert.Equal(MsiClawVibrationProfileWriteProbeOutcome.Unavailable, unavailable.Outcome);
        Assert.Equal("OwnedPhysicalSessionUnavailable", unavailable.Reason);
        Assert.Equal(0, devices.EnumerationCount);
        Assert.Empty(io.WriteFrames);

        var weak = MsiClawPhysicalIdentity.From(CreateControlDevice(strongIdentity: false));
        var weakResult = await client.RunDiagnosticMotorPairWriteAsync(
            MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred, () => weak, () => true, default);
        Assert.Equal(MsiClawVibrationProfileWriteProbeOutcome.Unavailable, weakResult.Outcome);
        Assert.Equal("OwnedPhysicalSessionUnavailable", weakResult.Reason);
        Assert.Equal(0, devices.EnumerationCount);
        Assert.Empty(io.WriteFrames);
    }

    [Fact]
    public async Task Developer_probe_rejects_a_control_hid_that_does_not_match_the_live_owned_identity()
    {
        var owned = CreateControlDevice(instanceSuffix: "OWNED");
        var other = CreateControlDevice(instanceSuffix: "OTHER", identityContainer: Guid.NewGuid(), identityRoot: "USB\\VID_0DB0&PID_1902\\OTHER");
        var io = new FakeProfileIo();
        var (client, _) = CreateClient(io, "msi.claw.a2vm.8", [other]);

        var result = await client.RunDiagnosticMotorPairWriteAsync(
            MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred,
            () => MsiClawPhysicalIdentity.From(owned),
            () => true,
            default);

        Assert.Equal(MsiClawVibrationProfileWriteProbeOutcome.Unavailable, result.Outcome);
        Assert.Equal("Pid1902ControlHidNotUniquelyResolved", result.Reason);
        Assert.Empty(io.WriteFrames);
    }

    [Fact]
    public async Task Developer_probe_rejects_ambiguous_control_hids_without_writing()
    {
        var first = CreateControlDevice(instanceSuffix: "FIRST");
        var second = first with { InstanceId = "HID\\VID_0DB0&PID_1902&MI_00&COL02\\SECOND" };
        var io = new FakeProfileIo();
        var (client, _) = CreateClient(io, "msi.claw.a2vm.8", [first, second]);

        var result = await client.RunDiagnosticMotorPairWriteAsync(
            MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred,
            () => MsiClawPhysicalIdentity.From(first),
            () => true,
            default);

        Assert.Equal(MsiClawVibrationProfileWriteProbeOutcome.Unavailable, result.Outcome);
        Assert.Equal("Pid1902ControlHidNotUniquelyResolved", result.Reason);
        Assert.Empty(io.WriteFrames);
    }

    [Fact]
    public async Task Developer_probe_cancellation_before_resolution_sends_no_write()
    {
        var io = new FakeProfileIo();
        var (client, devices) = CreateClient(io, "msi.claw.a2vm.8");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.RunDiagnosticMotorPairWriteAsync(
            MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred,
            () => MsiClawPhysicalIdentity.From(CreateControlDevice()),
            () => true,
            cancellation.Token));

        Assert.Equal(0, devices.EnumerationCount);
        Assert.Empty(io.WriteFrames);
    }

    [Fact]
    public async Task Developer_probe_rechecks_center_m_and_owned_identity_immediately_before_the_single_write()
    {
        var device = CreateControlDevice();
        var identity = MsiClawPhysicalIdentity.From(device);
        var io = new FakeProfileIo();
        var (centerMClient, _) = CreateClient(io, "msi.claw.a2vm.8", [device]);
        var authorityReads = 0;
        var authorityResult = await centerMClient.RunDiagnosticMotorPairWriteAsync(
            MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred,
            () => identity,
            () => ++authorityReads == 1,
            default);

        Assert.Equal(MsiClawVibrationProfileWriteProbeOutcome.Unavailable, authorityResult.Outcome);
        Assert.Equal("CenterMIsNotExactlyDisabled", authorityResult.Reason);
        Assert.Empty(io.WriteFrames);

        var (identityClient, _) = CreateClient(io, "msi.claw.a2vm.8", [device]);
        var identityReads = 0;
        var identityResult = await identityClient.RunDiagnosticMotorPairWriteAsync(
            MsiClawVibrationProfileWriteProbeMode.ApplyZeroHundred,
            () => ++identityReads == 1 ? identity : null,
            () => true,
            default);

        Assert.Equal(MsiClawVibrationProfileWriteProbeOutcome.Unavailable, identityResult.Outcome);
        Assert.Equal("OwnedPhysicalSessionUnavailable", identityResult.Reason);
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
