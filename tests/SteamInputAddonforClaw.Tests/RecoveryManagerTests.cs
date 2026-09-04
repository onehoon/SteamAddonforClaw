using System.Text.Json;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Recovery;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

// Full1902 Cleanup G: RecoveryManager is read-only. Current production never creates or updates
// recovery.json. These tests cover legacy schema-v5 read/validation and fail-close behavior for a
// pre-existing old development-build file, built as direct fixtures rather than through any
// production writer (which no longer exists).
public sealed class RecoveryManagerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ClawRecoveryTests", Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(_directory, "recovery.json");
    private static readonly HandheldDeviceId DeviceId = new("test.device");
    private static DeviceNativeStateSnapshot Snapshot() => new(DeviceId, 1, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { Mode = "DirectInput" }));

    private void WriteFixture(RecoveryJournal journal)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathName, JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void WriteRaw(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathName, json);
    }

    private RecoveryManager Manager() => new(new RecoveryJournalStore(PathName));

    [Fact]
    public void NoJournal_LoadReturnsNoRecoveryNeeded()
        => Assert.Equal(RecoveryStatus.NoRecoveryNeeded, Manager().LoadJournal().Status);

    [Fact]
    public void HasIncompleteRecovery_ReflectsFilePresence()
    {
        Assert.False(Manager().HasIncompleteRecovery);
        WriteRaw("{}");
        Assert.True(Manager().HasIncompleteRecovery);
    }

    [Fact]
    public void HasIncompleteRecovery_ExistenceCheckFailure_FailsClosed()
        => Assert.True(new RecoveryManager(new ThrowingExistsStore()).HasIncompleteRecovery);

    [Fact]
    public void ValidSchemaV5NativeAndHidHideJournal_LoadsSuccessfully()
    {
        var sessionId = Guid.NewGuid();
        WriteFixture(new RecoveryJournal(
            RecoveryManager.CurrentSchemaVersion, sessionId, DateTimeOffset.UtcNow, Snapshot(),
            new(DeviceNativeStateChanged: true, HidHideDeviceAdditions: ["HID\\Claw"], ExecutableWhitelistAdditions: ["C:\\addon.exe"])));

        var loaded = Manager().LoadJournal();

        Assert.Equal(RecoveryStatus.Success, loaded.Status);
        Assert.Equal(sessionId, loaded.Journal!.RecoverySessionId);
        Assert.Equal(DeviceId, loaded.Journal.OriginalDeviceState!.DeviceId);
        Assert.True(loaded.Journal.Mutations.DeviceNativeStateChanged);
        Assert.Contains("C:\\addon.exe", loaded.Journal.Mutations.ExecutableWhitelistAdditions!);
        Assert.Contains("HID\\Claw", loaded.Journal.Mutations.HidHideDeviceAdditions!);
    }

    [Fact]
    public void LoadJournal_StillValidatesLegacySchemaV5VirtualDeviceEntries()
    {
        // Full1902 Cleanup F: current production never writes AddonOwnedVirtualDeviceEntries, but an
        // old development-build recovery.json may still contain them. LoadJournal must keep reading
        // and validating that schema-v5 shape until the dedicated RecoveryJournal cleanup decides
        // whether old files are dropped or retired.
        var mutationId = Guid.NewGuid();
        WriteFixture(new RecoveryJournal(
            RecoveryManager.CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, Snapshot(),
            new(DeviceNativeStateChanged: true,
                HidHideDeviceAdditions: ["HID\\Claw"],
                AddonOwnedVirtualDeviceEntries:
                [
                    new AddonOwnedVirtualDeviceRecoveryEntry(mutationId, "steamdeck", 0x28DE, 0x1205, [], ["USB\\VID_28DE&PID_1205\\legacy"])
                ])));

        var loaded = Manager().LoadJournal();

        Assert.Equal(RecoveryStatus.Success, loaded.Status);
        var entry = Assert.Single(loaded.Journal!.Mutations.AddonOwnedVirtualDeviceEntries!);
        Assert.Equal(mutationId, entry.MutationId);
        Assert.Equal("steamdeck", entry.DeviceType);
        Assert.Equal("USB\\VID_28DE&PID_1205\\legacy", Assert.Single(entry.ResolvedInstanceIds));
    }

    [Fact]
    public void LoadJournal_NativeStateChangedWithoutSnapshot_FailsClosed()
    {
        WriteFixture(new RecoveryJournal(
            RecoveryManager.CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, null,
            new(DeviceNativeStateChanged: true)));

        var loaded = Manager().LoadJournal();

        Assert.Equal(RecoveryStatus.Failure, loaded.Status);
        Assert.Contains("missing required state", loaded.Reason);
        Assert.True(File.Exists(PathName));
    }

    [Fact]
    public void LoadJournal_InvalidVirtualEntry_FailsClosed()
    {
        WriteFixture(new RecoveryJournal(
            RecoveryManager.CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, null,
            new(AddonOwnedVirtualDeviceEntries:
                [new AddonOwnedVirtualDeviceRecoveryEntry(Guid.Empty, "", 0, 0, [], [])])));

        Assert.Equal(RecoveryStatus.Failure, Manager().LoadJournal().Status);
        Assert.True(File.Exists(PathName));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void LoadJournal_UnsupportedSchema_FailsClosedAndPreservesFile(int schema)
    {
        WriteRaw("{\"SchemaVersion\":" + schema + ",\"RecoverySessionId\":\"" + Guid.NewGuid() + "\",\"CreatedAt\":\"2026-01-01T00:00:00+00:00\",\"OriginalDeviceState\":null,\"Mutations\":{}}");

        var result = Manager().LoadJournal();

        Assert.Equal(RecoveryStatus.Failure, result.Status);
        Assert.Contains("Unsupported recovery schema", result.Reason);
        Assert.True(File.Exists(PathName));
    }

    [Fact]
    public void LoadJournal_MissingSchema_FailsClosed()
    {
        WriteRaw("{\"RecoverySessionId\":\"" + Guid.NewGuid() + "\",\"Mutations\":{}}");
        Assert.Equal(RecoveryStatus.Failure, Manager().LoadJournal().Status);
    }

    [Fact]
    public void MalformedJournal_IsPreservedAndFailsClosed()
    {
        WriteRaw("{bad");
        Assert.Equal(RecoveryStatus.Failure, Manager().LoadJournal().Status);
        Assert.True(File.Exists(PathName));
    }

    [Fact]
    public void SerializedSchemaV5Journal_UsesTheExpectedMutationPropertyNames()
    {
        var journal = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, Snapshot(),
            new(DeviceNativeStateChanged: true, HidHideDeviceAdditions: ["HID\\Claw"], ExecutableWhitelistAdditions: ["C:\\addon.exe"],
                AddonOwnedVirtualDeviceEntries: [new(Guid.NewGuid(), "steamdeck", 0x28DE, 0x1205, [], ["USB\\VID_28DE&PID_1205\\owned"])]));

        var json = JsonSerializer.Serialize(journal);

        Assert.DoesNotContain("AddonOwnedVirtualDevices\"", json);
        Assert.DoesNotContain("TemporaryXbox360OutputCreated", json);
        Assert.Contains("AddonOwnedVirtualDeviceEntries", json);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed class ThrowingExistsStore : IRecoveryJournalStore
    {
        public string JournalPath => "throwing";
        public bool Exists() => throw new IOException("existence check failed");
        public string ReadText() => throw new NotSupportedException();
        public void Delete() => throw new NotSupportedException();
    }
}
