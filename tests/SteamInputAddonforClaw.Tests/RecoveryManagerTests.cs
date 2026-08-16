using System.Text.Json;
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
    public void NoJournal_LoadReturnsNoRecoveryNeeded()
        => Assert.Equal(RecoveryStatus.NoRecoveryNeeded, Manager().LoadJournal().Status);

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
            session.RecoverySessionId, mutationId, "steamdeck", 0x28DE, 0x1205, []).Status);
        Assert.Equal(RecoveryStatus.Success, manager.ResolveAddonOwnedVirtualDeviceIdentity(
            session.RecoverySessionId, mutationId, ["USB\\VID_28DE&PID_1205\\owned"]).Status);

        var loaded = manager.LoadJournal().Journal!;
        var entry = Assert.Single(loaded.Mutations.AddonOwnedVirtualDeviceEntries!);
        Assert.Equal(RecoveryManager.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(mutationId, entry.MutationId);
        Assert.Equal("steamdeck", entry.DeviceType);
        Assert.Equal((ushort)0x28DE, entry.VendorId);
        Assert.Equal((ushort)0x1205, entry.ProductId);
        Assert.Equal("USB\\VID_28DE&PID_1205\\owned", Assert.Single(entry.ResolvedInstanceIds));
    }

    [Fact]
    public void SerializedJournal_DoesNotContainObsoleteMutationPropertyNames()
    {
        var mutationId = Guid.NewGuid();
        var journal = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, Snapshot(),
            new(DeviceNativeStateChanged: true, HidHideDeviceAdditions: ["HID\\Claw"], ExecutableWhitelistAdditions: ["C:\\addon.exe"],
                AddonOwnedVirtualDeviceEntries: [new(mutationId, "steamdeck", 0x28DE, 0x1205, [], ["USB\\VID_28DE&PID_1205\\owned"])]));

        var json = JsonSerializer.Serialize(journal);

        Assert.DoesNotContain("AddonOwnedVirtualDevices\"", json);
        Assert.DoesNotContain("TemporaryXbox360OutputCreated", json);
        Assert.Contains("AddonOwnedVirtualDeviceEntries", json);
    }

    [Fact]
    public void CurrentSchemaJournal_RoundTripsThroughStoreWithoutLegacyConversion()
    {
        var manager = Manager();
        var native = manager.BeginDeviceNativeStateMutation(Capture()).Journal!;
        manager.RecordHidHideWhitelistAddition(native.RecoverySessionId, "C:\\addon.exe");
        manager.RecordHidHideDeviceAddition(native.RecoverySessionId, "HID\\Claw");
        var mutationId = Guid.NewGuid();
        manager.RecordAddonOwnedVirtualDeviceIntent(native.RecoverySessionId, mutationId, "steamdeck", 0x28DE, 0x1205, []);
        manager.ResolveAddonOwnedVirtualDeviceIdentity(native.RecoverySessionId, mutationId, ["USB\\VID_28DE&PID_1205\\owned"]);

        var loaded = manager.LoadJournal();

        Assert.Equal(RecoveryStatus.Success, loaded.Status);
        var journal = loaded.Journal!;
        Assert.Equal(RecoveryManager.CurrentSchemaVersion, journal.SchemaVersion);
        Assert.Equal(native.RecoverySessionId, journal.RecoverySessionId);
        Assert.Equal(DeviceId, journal.OriginalDeviceState!.DeviceId);
        Assert.True(journal.Mutations.DeviceNativeStateChanged);
        Assert.Contains("C:\\addon.exe", journal.Mutations.ExecutableWhitelistAdditions!);
        Assert.Contains("HID\\Claw", journal.Mutations.HidHideDeviceAdditions!);
        var entry = Assert.Single(journal.Mutations.AddonOwnedVirtualDeviceEntries!);
        Assert.Equal(mutationId, entry.MutationId);
        Assert.Equal("USB\\VID_28DE&PID_1205\\owned", Assert.Single(entry.ResolvedInstanceIds));
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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void LoadJournal_UnsupportedSchema_FailsClosedWithoutMutatingJournal(int schema)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathName, "{\"SchemaVersion\":" + schema + ",\"RecoverySessionId\":\"" + Guid.NewGuid() + "\",\"CreatedAt\":\"2026-01-01T00:00:00+00:00\",\"OriginalControllerState\":null,\"Mutations\":{}}");
        var store = new SpyStore(new RecoveryJournalStore(PathName));
        var manager = new RecoveryManager(store);

        var result = manager.LoadJournal();

        Assert.Equal(RecoveryStatus.Failure, result.Status);
        Assert.Contains("Unsupported recovery schema", result.Reason);
        Assert.True(store.Exists());
        Assert.False(store.DeleteCalled);
        Assert.False(store.ReplaceExistingCalled);
    }

    [Fact]
    public void MalformedJournal_IsPreserved()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathName, "{bad");
        Assert.Equal(RecoveryStatus.Failure, Manager().LoadJournal().Status);
        Assert.True(File.Exists(PathName));
    }

    private RecoveryManager Manager() => new(new RecoveryJournalStore(PathName));
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed class SpyStore(IRecoveryJournalStore inner) : IRecoveryJournalStore
    {
        public bool DeleteCalled { get; private set; }
        public bool ReplaceExistingCalled { get; private set; }
        public string JournalPath => inner.JournalPath;
        public bool Exists() => inner.Exists();
        public string ReadText() => inner.ReadText();
        public void WriteNew(RecoveryJournal journal) => inner.WriteNew(journal);
        public void ReplaceExisting(RecoveryJournal journal) { ReplaceExistingCalled = true; inner.ReplaceExisting(journal); }
        public void Delete() { DeleteCalled = true; inner.Delete(); }
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
