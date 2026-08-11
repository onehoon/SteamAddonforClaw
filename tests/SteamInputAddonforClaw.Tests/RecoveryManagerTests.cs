using System.Text.Json;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.HidHide;
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
    public void NativeThenWhitelistRecordUsesSameSession()
    {
        var manager = Manager();
        var native = manager.BeginDeviceNativeStateMutation(Capture());
        var recorded = manager.RecordHidHideWhitelistAddition(native.Journal!.RecoverySessionId, "C:\\addon.exe");
        var journal = manager.LoadJournal().Journal!;

        Assert.Equal(RecoveryStatus.Success, recorded.Status);
        Assert.Equal(native.Journal.RecoverySessionId, journal.RecoverySessionId);
        Assert.True(journal.Mutations.DeviceNativeStateChanged);
        Assert.Contains("C:\\addon.exe", journal.Mutations.ExecutableWhitelistAdditions!);
        Assert.NotNull(journal.OriginalDeviceState);
    }

    [Fact]
    public void RecordDeviceAdditionPreservesNativeAndWhitelist()
    {
        var manager = Manager();
        var native = manager.BeginDeviceNativeStateMutation(Capture()).Journal!;
        manager.RecordHidHideWhitelistAddition(native.RecoverySessionId, "C:\\addon.exe");
        Assert.Equal(RecoveryStatus.Success, manager.RecordHidHideDeviceAddition(native.RecoverySessionId, "HID\\Claw").Status);
        var journal = manager.LoadJournal().Journal!;

        Assert.True(journal.Mutations.DeviceNativeStateChanged);
        Assert.Contains("C:\\addon.exe", journal.Mutations.ExecutableWhitelistAdditions!);
        Assert.Contains("HID\\Claw", journal.Mutations.HidHideDeviceAdditions!);
    }

    [Fact]
    public void VirtualDeviceMutationRoundTripsStructuredIdentity()
    {
        var manager = Manager();
        var session = manager.BeginDeviceNativeStateMutation(Capture()).Journal!;
        var mutationId = Guid.NewGuid();

        Assert.Equal(RecoveryStatus.Success, manager.RecordAddonOwnedVirtualDeviceIntent(
            session.RecoverySessionId, mutationId, "steamcontroller", 0x28DE, 0x1102, []).Status);
        Assert.Equal(RecoveryStatus.Success, manager.ResolveAddonOwnedVirtualDeviceIdentity(
            session.RecoverySessionId, mutationId, ["USB\\VID_28DE&PID_1102\\owned"]).Status);

        var loaded = manager.LoadJournal().Journal!;
        var entry = Assert.Single(loaded.Mutations.AddonOwnedVirtualDeviceEntries!);
        Assert.Equal(3, loaded.SchemaVersion);
        Assert.Equal(mutationId, entry.MutationId);
        Assert.Equal("steamcontroller", entry.DeviceType);
        Assert.Equal((ushort)0x28DE, entry.VendorId);
        Assert.Equal((ushort)0x1102, entry.ProductId);
        Assert.Equal("USB\\VID_28DE&PID_1102\\owned", Assert.Single(entry.ResolvedInstanceIds));
    }

    [Fact]
    public void WrongSessionCannotRecordDeviceAddition()
    {
        var manager = Manager();
        var native = manager.BeginDeviceNativeStateMutation(Capture()).Journal!;
        Assert.Equal(RecoveryStatus.Failure, manager.RecordHidHideDeviceAddition(Guid.NewGuid(), "HID\\Claw").Status);
        Assert.Null(manager.LoadJournal().Journal!.Mutations.HidHideDeviceAdditions);
        Assert.Equal(native.RecoverySessionId, manager.LoadJournal().Journal!.RecoverySessionId);
    }

    [Fact]
    public void CompleteDeviceAdditionPreservesOtherMutations()
    {
        var manager = Manager();
        var native = manager.BeginDeviceNativeStateMutation(Capture()).Journal!;
        manager.RecordHidHideWhitelistAddition(native.RecoverySessionId, "C:\\addon.exe");
        manager.RecordHidHideDeviceAddition(native.RecoverySessionId, "HID\\Claw");
        Assert.Equal(RecoveryStatus.Success, manager.CompleteHidHideDeviceAddition(native.RecoverySessionId, "hid\\claw").Status);
        var journal = manager.LoadJournal().Journal!;
        Assert.True(journal.Mutations.DeviceNativeStateChanged);
        Assert.Contains("C:\\addon.exe", journal.Mutations.ExecutableWhitelistAdditions!);
        Assert.Empty(journal.Mutations.HidHideDeviceAdditions!);
    }

    [Fact]
    public void WrongSessionCannotModifyJournal()
    {
        var manager = Manager();
        var native = manager.BeginDeviceNativeStateMutation(Capture());
        var before = manager.LoadJournal().Journal!;
        var result = manager.RecordHidHideWhitelistAddition(Guid.NewGuid(), "C:\\addon.exe");
        var after = manager.LoadJournal().Journal!;

        Assert.Equal(RecoveryStatus.Failure, result.Status);
        Assert.Equal(before.RecoverySessionId, after.RecoverySessionId);
        Assert.Equal(before.Mutations, after.Mutations);
        Assert.Equal(before.OriginalDeviceState!.DeviceId, after.OriginalDeviceState!.DeviceId);
    }

    [Fact]
    public void StandaloneWhitelistBeginDoesNotJoinExistingNativeJournal()
    {
        var manager = Manager();
        var native = manager.BeginDeviceNativeStateMutation(Capture()).Journal!;
        var result = manager.BeginHidHideWhitelistLease("C:\\addon.exe");

        Assert.Equal(RecoveryStatus.Failure, result.Status);
        Assert.Equal(native.RecoverySessionId, manager.LoadJournal().Journal!.RecoverySessionId);
    }

    [Fact]
    public void MutationCompletionPreservesOtherMutationAndDeletesLastMutation()
    {
        var manager = Manager();
        var native = manager.BeginDeviceNativeStateMutation(Capture()).Journal!;
        Assert.Equal(RecoveryStatus.Success, manager.RecordHidHideWhitelistAddition(native.RecoverySessionId, "C:\\addon.exe").Status);

        Assert.Equal(RecoveryStatus.Success, manager.CompleteHidHideWhitelistAddition(native.RecoverySessionId, "C:\\addon.exe").Status);
        var afterWhitelist = manager.LoadJournal().Journal!;
        Assert.True(afterWhitelist.Mutations.DeviceNativeStateChanged);
        Assert.Empty(afterWhitelist.Mutations.ExecutableWhitelistAdditions!);

        Assert.Equal(RecoveryStatus.Success, manager.CompleteDeviceNativeStateMutation(native.RecoverySessionId).Status);
        Assert.Equal(RecoveryStatus.NoRecoveryNeeded, manager.LoadJournal().Status);
    }

    [Fact]
    public void MutationCompletionWriteFailurePreservesJournalEvidence()
    {
        var journal = new RecoveryJournal(
            RecoveryManager.CurrentSchemaVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Snapshot(),
            new(DeviceNativeStateChanged: true, ExecutableWhitelistAdditions: ["C:\\addon.exe"]));
        var store = new FaultStore(journal: journal, writeFails: true);
        var manager = new RecoveryManager(store);

        var result = manager.CompleteHidHideWhitelistAddition(journal.RecoverySessionId, "C:\\addon.exe");

        Assert.Equal(RecoveryStatus.Failure, result.Status);
        Assert.True(store.Exists());
        Assert.True(manager.LoadJournal().Journal!.Mutations.DeviceNativeStateChanged);
        Assert.Contains("C:\\addon.exe", manager.LoadJournal().Journal!.Mutations.ExecutableWhitelistAdditions!);
    }

    [Fact]
    public async Task MixedNativeAndWhitelistRecoverInReverseWhitelistThenNativeOrder()
    {
        var trace = new List<string>();
        var nativeState = new FakeNativeStateManager(DeviceId, NativeStateRestoreStatus.Success) { Events = trace };
        var hidHide = new FakeHidHide { Events = trace };
        var manager = new RecoveryManager(new RecoveryJournalStore(PathName), new HandheldDeviceRegistry([new FakeAdapter(DeviceId, nativeState)]), hidHide);
        var native = manager.BeginDeviceNativeStateMutation(Capture()).Journal!;
        manager.RecordHidHideWhitelistAddition(native.RecoverySessionId, "C:\\addon-a.exe");
        manager.RecordHidHideWhitelistAddition(native.RecoverySessionId, "C:\\addon-b.exe");
        hidHide.Entries.UnionWith(["C:\\addon-a.exe", "C:\\addon-b.exe"]);

        var result = await manager.RecoverIncompleteSessionAsync(CancellationToken.None);

        Assert.Equal(RecoveryStatus.Success, result.Status);
        Assert.Empty(hidHide.Entries);
        Assert.Equal(["C:\\addon-b.exe", "C:\\addon-a.exe", "RestoreNative"], trace);
        Assert.False(File.Exists(PathName));
    }

    [Fact]
    public async Task DeviceThenWhitelistThenNativeRecoveryOrder()
    {
        var trace = new List<string>();
        var nativeState = new FakeNativeStateManager(DeviceId, NativeStateRestoreStatus.Success) { Events = trace };
        var hidHide = new FakeHidHide { Events = trace };
        var manager = new RecoveryManager(new RecoveryJournalStore(PathName), new HandheldDeviceRegistry([new FakeAdapter(DeviceId, nativeState)]), hidHide);
        var native = manager.BeginDeviceNativeStateMutation(Capture()).Journal!;
        manager.RecordHidHideDeviceAddition(native.RecoverySessionId, "HID\\A");
        manager.RecordHidHideDeviceAddition(native.RecoverySessionId, "HID\\B");
        manager.RecordHidHideWhitelistAddition(native.RecoverySessionId, "C:\\addon-a.exe");
        manager.RecordHidHideWhitelistAddition(native.RecoverySessionId, "C:\\addon-b.exe");
        hidHide.HiddenEntries.UnionWith(["HID\\A", "HID\\B"]);
        hidHide.Entries.UnionWith(["C:\\addon-a.exe", "C:\\addon-b.exe"]);

        Assert.Equal(RecoveryStatus.Success, (await manager.RecoverIncompleteSessionAsync(CancellationToken.None)).Status);
        Assert.Equal(["HID\\B", "HID\\A", "C:\\addon-b.exe", "C:\\addon-a.exe", "RestoreNative"], trace);
    }

    [Theory]
    [InlineData((int)HidHideInspectionStatus.Disabled)]
    [InlineData((int)HidHideInspectionStatus.InverseWhitelist)]
    public async Task RecoveryDeviceAddition_WhenConfigurationIsReadable_RestoresEntry(int statusValue)
    {
        var status = (HidHideInspectionStatus)statusValue;
        var hidHide = new FakeHidHide { Status = status };
        var nativeState = new FakeNativeStateManager(DeviceId, NativeStateRestoreStatus.Success);
        var manager = new RecoveryManager(new RecoveryJournalStore(PathName), new HandheldDeviceRegistry([new FakeAdapter(DeviceId, nativeState)]), hidHide);
        var session = manager.BeginDeviceNativeStateMutation(Capture()).Journal!;
        manager.RecordHidHideDeviceAddition(session.RecoverySessionId, "HID\\Claw");
        hidHide.HiddenEntries.Add("HID\\Claw");

        var result = await manager.RecoverIncompleteSessionAsync(CancellationToken.None);

        Assert.Equal(RecoveryStatus.Success, result.Status);
        Assert.Empty(hidHide.HiddenEntries);
        Assert.Equal(RecoveryStatus.NoRecoveryNeeded, manager.LoadJournal().Status);
    }

    [Fact]
    public async Task WhitelistFailureStopsBeforeNativeRecovery()
    {
        var nativeState = new FakeNativeStateManager(DeviceId, NativeStateRestoreStatus.Success);
        var hidHide = new FakeHidHide { RemoveSucceeds = false };
        var manager = new RecoveryManager(new RecoveryJournalStore(PathName), new HandheldDeviceRegistry([new FakeAdapter(DeviceId, nativeState)]), hidHide);
        var native = manager.BeginDeviceNativeStateMutation(Capture()).Journal!;
        manager.RecordHidHideWhitelistAddition(native.RecoverySessionId, "C:\\addon.exe");
        hidHide.Entries.Add("C:\\addon.exe");

        Assert.Equal(RecoveryStatus.Failure, (await manager.RecoverIncompleteSessionAsync(CancellationToken.None)).Status);
        Assert.Equal(0, nativeState.RestoreCount);
        Assert.True(File.Exists(PathName));
    }

    [Fact]
    public async Task WhitelistCheckpointRemainsWhenNativeRecoveryFails()
    {
        var nativeState = new FakeNativeStateManager(DeviceId, NativeStateRestoreStatus.Failed);
        var hidHide = new FakeHidHide();
        var manager = new RecoveryManager(new RecoveryJournalStore(PathName), new HandheldDeviceRegistry([new FakeAdapter(DeviceId, nativeState)]), hidHide);
        var native = manager.BeginDeviceNativeStateMutation(Capture()).Journal!;
        manager.RecordHidHideWhitelistAddition(native.RecoverySessionId, "C:\\addon.exe");
        hidHide.Entries.Add("C:\\addon.exe");

        Assert.Equal(RecoveryStatus.Failure, (await manager.RecoverIncompleteSessionAsync(CancellationToken.None)).Status);
        var checkpoint = manager.LoadJournal().Journal!;
        Assert.True(checkpoint.Mutations.DeviceNativeStateChanged);
        Assert.Empty(checkpoint.Mutations.ExecutableWhitelistAdditions!);
    }

    [Fact]
    public async Task UnsupportedVirtualMutationFailsBeforePartialRecovery()
    {
        var hidHide = new FakeHidHide();
        var native = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, Snapshot(),
            new(true, null, ["C:\\addon.exe"], ["virtual"], false));
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathName, JsonSerializer.Serialize(native));
        var manager = new RecoveryManager(new RecoveryJournalStore(PathName), new HandheldDeviceRegistry([new FakeAdapter(DeviceId, new FakeNativeStateManager(DeviceId, NativeStateRestoreStatus.Success))]), hidHide);

        Assert.Equal(RecoveryStatus.Failure, (await manager.RecoverIncompleteSessionAsync(CancellationToken.None)).Status);
        Assert.Equal(0, hidHide.RemoveCount);
        Assert.True(File.Exists(PathName));
    }

    [Fact]
    public async Task MixedRecoveryUnknownDeviceFailsBeforeWhitelistMutation()
    {
        var hidHide = new FakeHidHide();
        var journal = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, Snapshot(),
            new(true, null, ["C:\\addon.exe"], null, false));
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathName, JsonSerializer.Serialize(journal));
        var manager = new RecoveryManager(new RecoveryJournalStore(PathName), new HandheldDeviceRegistry([]), hidHide);

        Assert.Equal(RecoveryStatus.Failure, (await manager.RecoverIncompleteSessionAsync(CancellationToken.None)).Status);
        Assert.Equal(0, hidHide.RemoveCount);
        Assert.True(File.Exists(PathName));
    }

    [Fact]
    public async Task MixedRecoveryMissingNativeStateFailsBeforeWhitelistMutation()
    {
        var hidHide = new FakeHidHide();
        var journal = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, Snapshot(),
            new(true, null, ["C:\\addon.exe"], null, false));
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathName, JsonSerializer.Serialize(journal));
        var manager = new RecoveryManager(new RecoveryJournalStore(PathName), new HandheldDeviceRegistry([new FakeAdapter(DeviceId, null)]), hidHide);

        Assert.Equal(RecoveryStatus.Failure, (await manager.RecoverIncompleteSessionAsync(CancellationToken.None)).Status);
        Assert.Equal(0, hidHide.RemoveCount);
        Assert.True(File.Exists(PathName));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task UnsupportedV2MutationFailsBeforeAnyRecovery(int unsupportedKind)
    {
        var hidHide = new FakeHidHide();
        var mutations = unsupportedKind switch
        {
            1 => new RecoveryMutationState(AddonOwnedVirtualDevices: ["virtual"]),
            _ => new RecoveryMutationState(TemporaryXbox360OutputCreated: true)
        };
        var journal = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, null, mutations);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathName, JsonSerializer.Serialize(journal));
        var manager = new RecoveryManager(new RecoveryJournalStore(PathName), hidHideClient: hidHide);

        Assert.Equal(RecoveryStatus.Failure, (await manager.RecoverIncompleteSessionAsync(CancellationToken.None)).Status);
        Assert.Equal(0, hidHide.RemoveCount);
        Assert.True(File.Exists(PathName));
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
        public int RestoreCount { get; private set; }
        public List<string>? Events { get; init; }
        public HandheldDeviceId DeviceId => deviceId;
        public NativeStateCaptureResult CaptureSnapshot() => Capture();
        public Task<NativeStateRestoreResult> RestoreSnapshotAsync(DeviceNativeStateSnapshot snapshot, CancellationToken cancellationToken) { RestoreCount++; Events?.Add("RestoreNative"); return Task.FromResult(new NativeStateRestoreResult(status, "test")); }
    }
    private sealed class FakeHidHide : SteamInputAddonforClaw.HidHide.IHidHideClient
    {
        public HashSet<string> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> HiddenEntries { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int RemoveCount { get; private set; }
        public List<string>? Events { get; init; }
        public bool RemoveSucceeds { get; init; } = true;
        public SteamInputAddonforClaw.HidHide.HidHideInspectionStatus Status { get; init; } = SteamInputAddonforClaw.HidHide.HidHideInspectionStatus.Available;
        public SteamInputAddonforClaw.HidHide.HidHideInspection Inspect() => new(Status, Entries, HiddenEntries.ToArray());
        public bool AddApplication(string executablePath) => true;
        public bool RemoveApplication(string executablePath) { RemoveCount++; Events?.Add(executablePath); if (!RemoveSucceeds) return false; return Entries.Remove(executablePath); }
        public bool AddHiddenDevice(string deviceEntry) => HiddenEntries.Add(deviceEntry);
        public bool RemoveHiddenDevice(string deviceEntry) { RemoveCount++; Events?.Add(deviceEntry); if (!RemoveSucceeds) return false; return HiddenEntries.Remove(deviceEntry); }
    }
    private sealed class FaultStore(RecoveryJournal? journal = null, bool writeFails = false, bool deleteFails = false) : IRecoveryJournalStore
    {
        public string JournalPath => "fault";
        public bool Exists() => journal is not null;
        public string ReadText() => JsonSerializer.Serialize(journal!);
        public void WriteNew(RecoveryJournal value) { if (writeFails) throw new IOException("write failed"); journal = value; }
        public void ReplaceExisting(RecoveryJournal value) { if (writeFails) throw new IOException("replace failed"); if (journal is null) throw new IOException("missing journal"); journal = value; }
        public void Delete() { if (deleteFails) throw new IOException("delete failed"); journal = null; }
    }
}
