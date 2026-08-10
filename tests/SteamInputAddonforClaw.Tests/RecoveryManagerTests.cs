using System.Text.Json;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Recovery;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RecoveryManagerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ClawRecoveryTests", Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(_directory, "recovery.json");
    private static readonly HandheldDeviceId DeviceId = new("test.device");
    private static DeviceNativeStateSnapshot Snapshot() => new(DeviceId, 1, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { Mode = "DirectInput" }));
    private static NativeStateCaptureResult Capture() => new(NativeStateCaptureStatus.Success, Snapshot(), "Captured");

    [Fact]
    public async Task NoJournal_ReturnsNoRecoveryNeeded()
        => Assert.Equal(RecoveryStatus.NoRecoveryNeeded, (await Manager().RecoverIncompleteSessionAsync(CancellationToken.None)).Status);

    [Fact]
    public void BeginDeviceNativeStateMutation_PersistsValidatedV2Journal()
    {
        var result = Manager().BeginDeviceNativeStateMutation(Capture());
        Assert.Equal(RecoveryStatus.Success, result.Status);
        Assert.True(result.Journal!.Mutations.DeviceNativeStateChanged);
        Assert.Equal(DeviceId, result.Journal.OriginalDeviceState!.DeviceId);
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp-*"));
    }

    [Theory]
    [InlineData(NativeStateCaptureStatus.DeviceNotFound)]
    [InlineData(NativeStateCaptureStatus.Indeterminate)]
    [InlineData(NativeStateCaptureStatus.Failed)]
    public void UnsafeCapture_DeniesMutation(NativeStateCaptureStatus status)
    {
        Assert.Equal(RecoveryStatus.Failure, Manager().BeginDeviceNativeStateMutation(new(status, null, "unsafe")).Status);
        Assert.False(File.Exists(PathName));
    }

    [Fact]
    public void HidHideLease_DoesNotCreateDeviceSnapshot()
    {
        var result = Manager().BeginHidHideWhitelistLease("C:\\addon.exe");
        Assert.Equal(RecoveryStatus.Success, result.Status);
        Assert.Null(result.Journal!.OriginalDeviceState);
    }

    [Fact]
    public void WriteFailure_DeniesNativeStateMutation()
    {
        var result = new RecoveryManager(new FaultStore(writeFails: true)).BeginDeviceNativeStateMutation(Capture());
        Assert.Equal(RecoveryStatus.Failure, result.Status);
    }

    [Fact]
    public void SecondBegin_DoesNotOverwriteCrashEvidence()
    {
        var manager = Manager();
        var first = manager.BeginDeviceNativeStateMutation(Capture());
        Assert.Equal(RecoveryStatus.Failure, manager.BeginDeviceNativeStateMutation(Capture()).Status);
        Assert.Equal(first.Journal!.RecoverySessionId, manager.LoadJournal().Journal!.RecoverySessionId);
    }

    [Fact]
    public async Task NativeRestoreSuccess_DeletesJournal()
    {
        var native = new FakeNativeStateManager(DeviceId, NativeStateRestoreStatus.Success);
        var manager = Manager(new HandheldDeviceRegistry([new FakeAdapter(DeviceId, native)]));
        manager.BeginDeviceNativeStateMutation(Capture());
        var result = await manager.RecoverIncompleteSessionAsync(CancellationToken.None);
        Assert.True(result.Status == RecoveryStatus.Success, result.Reason);
        Assert.False(File.Exists(PathName));
    }

    [Fact]
    public async Task UnknownDeviceOrRestoreFailure_PreservesJournal()
    {
        var manager = Manager(new HandheldDeviceRegistry([]));
        manager.BeginDeviceNativeStateMutation(Capture());
        Assert.Equal(RecoveryStatus.Failure, (await manager.RecoverIncompleteSessionAsync(CancellationToken.None)).Status);
        Assert.True(File.Exists(PathName));
    }

    [Fact]
    public async Task MissingNativeStateManager_PreservesJournal()
    {
        var manager = Manager(new HandheldDeviceRegistry([new FakeAdapter(DeviceId, null)]));
        manager.BeginDeviceNativeStateMutation(Capture());
        Assert.Equal(RecoveryStatus.Failure, (await manager.RecoverIncompleteSessionAsync(CancellationToken.None)).Status);
        Assert.True(File.Exists(PathName));
    }

    [Theory]
    [InlineData(NativeStateRestoreStatus.Failed)]
    [InlineData(NativeStateRestoreStatus.Unsupported)]
    public async Task NativeRestoreFailure_PreservesJournal(NativeStateRestoreStatus status)
    {
        var manager = Manager(new HandheldDeviceRegistry([new FakeAdapter(DeviceId, new FakeNativeStateManager(DeviceId, status))]));
        manager.BeginDeviceNativeStateMutation(Capture());
        Assert.Equal(RecoveryStatus.Failure, (await manager.RecoverIncompleteSessionAsync(CancellationToken.None)).Status);
        Assert.True(File.Exists(PathName));
    }

    [Fact]
    public async Task DeleteFailure_IsNotReportedAsSuccess()
    {
        var store = new FaultStore(journal: new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, null, new()), deleteFails: true);
        Assert.Equal(RecoveryStatus.Failure, (await new RecoveryManager(store).RecoverIncompleteSessionAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public async Task FutureSchema_IsPreserved()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathName, "{\"SchemaVersion\":99}");
        Assert.Equal(RecoveryStatus.Failure, (await Manager().RecoverIncompleteSessionAsync(CancellationToken.None)).Status);
        Assert.True(File.Exists(PathName));
    }

    [Fact]
    public async Task LegacyWhitelistOnlyJournal_IsRecoveredWithoutRewrite()
    {
        Directory.CreateDirectory(_directory);
        var legacy = "{\"SchemaVersion\":1,\"RecoverySessionId\":\"" + Guid.NewGuid() + "\",\"CreatedAt\":\"2026-01-01T00:00:00+00:00\",\"OriginalControllerState\":{},\"Mutations\":{\"ControllerModeChanged\":false,\"ExecutableWhitelistAdditions\":[\"C:\\\\addon.exe\"]}}";
        File.WriteAllText(PathName, legacy);
        var hidHide = new FakeHidHide();
        var manager = new RecoveryManager(new RecoveryJournalStore(PathName), hidHideClient: hidHide);
        Assert.Equal(RecoveryStatus.Success, (await manager.RecoverIncompleteSessionAsync(CancellationToken.None)).Status);
        Assert.False(File.Exists(PathName));
    }

    [Fact]
    public async Task LegacyNoMutationJournal_IsDeleted()
    {
        WriteLegacy("{\"ControllerModeChanged\":false}");
        Assert.Equal(RecoveryStatus.Success, (await Manager().RecoverIncompleteSessionAsync(CancellationToken.None)).Status);
        Assert.False(File.Exists(PathName));
    }

    [Fact]
    public async Task UnsafeLegacyJournal_IsPreservedExactly()
    {
        Directory.CreateDirectory(_directory);
        var legacy = "{\"SchemaVersion\":1,\"RecoverySessionId\":\"" + Guid.NewGuid() + "\",\"CreatedAt\":\"2026-01-01T00:00:00+00:00\",\"OriginalControllerState\":{},\"Mutations\":{\"ControllerModeChanged\":true}}";
        File.WriteAllText(PathName, legacy);
        Assert.Equal(RecoveryStatus.Failure, (await Manager().RecoverIncompleteSessionAsync(CancellationToken.None)).Status);
        Assert.Equal(legacy, File.ReadAllText(PathName));
    }

    [Theory]
    [InlineData("{\"ControllerModeChanged\":false,\"HidHideDeviceAdditions\":[\"HID\\\\test\"]}")]
    [InlineData("{\"ControllerModeChanged\":false,\"AddonOwnedVirtualDevices\":[\"virtual\"]}")]
    [InlineData("{\"ControllerModeChanged\":false,\"TemporaryXbox360OutputCreated\":true}")]
    public async Task UnsupportedLegacyMutation_IsPreserved(string mutations)
    {
        var legacy = WriteLegacy(mutations);
        Assert.Equal(RecoveryStatus.Failure, (await Manager().RecoverIncompleteSessionAsync(CancellationToken.None)).Status);
        Assert.Equal(legacy, File.ReadAllText(PathName));
    }

    [Fact]
    public async Task MalformedJournal_IsPreserved()
    {
        Directory.CreateDirectory(_directory); File.WriteAllText(PathName, "{bad");
        Assert.Equal(RecoveryStatus.Failure, (await Manager().RecoverIncompleteSessionAsync(CancellationToken.None)).Status);
        Assert.True(File.Exists(PathName));
    }

    private RecoveryManager Manager(HandheldDeviceRegistry? registry = null) => new(new RecoveryJournalStore(PathName), registry);
    private string WriteLegacy(string mutations)
    {
        Directory.CreateDirectory(_directory);
        var legacy = "{\"SchemaVersion\":1,\"RecoverySessionId\":\"" + Guid.NewGuid() + "\",\"CreatedAt\":\"2026-01-01T00:00:00+00:00\",\"OriginalControllerState\":{},\"Mutations\":" + mutations + "}";
        File.WriteAllText(PathName, legacy);
        return legacy;
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed class FakeAdapter(HandheldDeviceId id, INativeControllerStateManager? native) : IHandheldDeviceAdapter
    {
        public HandheldDeviceDescriptor Descriptor { get; } = new(id, "Test", "Test", "Test");
        public AuxiliaryControlCatalog AuxiliaryControls { get; } = new([]);
        public IInternalControllerMatcher InternalControllerMatcher { get; } = new NeverMatcher();
        public IHandheldDeviceModelResolver? ModelResolver => null;
        public INativeControllerStateManager? NativeState => native;
        public DeviceProbeResult Probe(DeviceProbeContext context) => new(DeviceProbeStatus.NoMatch, "test");
    }
    private sealed class NeverMatcher : IInternalControllerMatcher { public InternalControllerMatchResult Match(InternalControllerMatchContext context) => new(InternalControllerMatchStatus.NoMatch, "test"); }
    private sealed class FakeNativeStateManager(HandheldDeviceId deviceId, NativeStateRestoreStatus status) : INativeControllerStateManager
    {
        public HandheldDeviceId DeviceId => deviceId;
        public NativeStateCaptureResult CaptureSnapshot() => Capture();
        public Task<NativeStateRestoreResult> RestoreSnapshotAsync(DeviceNativeStateSnapshot snapshot, CancellationToken cancellationToken) => Task.FromResult(new NativeStateRestoreResult(status, "test"));
    }
    private sealed class FakeHidHide : SteamInputAddonforClaw.HidHide.IHidHideClient
    {
        public SteamInputAddonforClaw.HidHide.HidHideInspection Inspect() => new(SteamInputAddonforClaw.HidHide.HidHideInspectionStatus.Available, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        public bool AddApplication(string executablePath) => true;
        public bool RemoveApplication(string executablePath) => true;
    }
    private sealed class FaultStore(RecoveryJournal? journal = null, bool writeFails = false, bool deleteFails = false) : IRecoveryJournalStore
    {
        public string JournalPath => "fault";
        public bool Exists() => journal is not null;
        public string ReadText() => JsonSerializer.Serialize(journal!);
        public void WriteNew(RecoveryJournal value) { if (writeFails) throw new IOException("write failed"); journal = value; }
        public void Delete() { if (deleteFails) throw new IOException("delete failed"); journal = null; }
    }
}
