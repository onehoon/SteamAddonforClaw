using SteamInputAddonforClaw.Controllers;
using SteamInputAddonforClaw.Recovery;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class RecoveryManagerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ClawRecoveryTests", Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(_directory, "recovery.json");
    private static MsiControllerSnapshot Snapshot() => new(MsiControllerNativeMode.DirectInput, "instance", "parent", Guid.NewGuid(), 0x1902, DateTimeOffset.UtcNow);
    private static MsiControllerSnapshotResult SnapshotResult() => new(MsiControllerSnapshotStatus.Success, Snapshot(), "Snapshot captured.");

    [Fact]
    public void NoJournal_ReturnsNoRecoveryNeeded()
    {
        var manager = Manager();
        Assert.False(manager.HasIncompleteRecovery);
        Assert.Equal(RecoveryStatus.NoRecoveryNeeded, manager.RecoverIncompleteSession().Status);
    }

    [Fact]
    public void BeginRecoverySession_PersistsValidJournalAtomically()
    {
        var manager = Manager();
        var result = manager.BeginRecoverySession(SnapshotResult());
        Assert.Equal(RecoveryStatus.Success, result.Status);
        Assert.True(File.Exists(PathName));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp-*"));
        Assert.Equal(result.Journal!.RecoverySessionId, manager.LoadJournal().Journal!.RecoverySessionId);
    }

    [Fact]
    public void BeginRecoverySession_WriteFailure_DeniesMutation()
    {
        var result = new RecoveryManager(new FaultStore(write: true)).BeginRecoverySession(SnapshotResult());
        Assert.Equal(RecoveryStatus.Failure, result.Status);
    }

    [Fact]
    public void Recover_NoMutationJournal_DeletesJournal()
    {
        var manager = Manager();
        manager.BeginRecoverySession(SnapshotResult());
        Assert.Equal(RecoveryStatus.Success, manager.RecoverIncompleteSession().Status);
        Assert.False(File.Exists(PathName));
    }

    [Fact]
    public void MalformedJournal_FailsAndPreservesEvidence()
    {
        Directory.CreateDirectory(_directory); File.WriteAllText(PathName, "{bad json");
        Assert.Equal(RecoveryStatus.Failure, Manager().RecoverIncompleteSession().Status);
        Assert.True(File.Exists(PathName));
    }

    [Fact]
    public void UnsupportedSchema_FailsAndPreservesEvidence()
    {
        var manager = Manager(); manager.BeginRecoverySession(SnapshotResult());
        File.WriteAllText(PathName, File.ReadAllText(PathName).Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 99"));
        Assert.Equal(RecoveryStatus.Failure, manager.RecoverIncompleteSession().Status);
        Assert.True(File.Exists(PathName));
    }

    [Fact]
    public void RecordedUnsupportedMutation_FailsAndPreservesEvidence()
    {
        var journal = new RecoveryJournal(1, Guid.NewGuid(), DateTimeOffset.UtcNow, Snapshot(), new(ControllerModeChanged: true));
        var store = new MemoryStore(journal);
        Assert.Equal(RecoveryStatus.Failure, new RecoveryManager(store).RecoverIncompleteSession().Status);
        Assert.True(store.Exists());
    }

    [Fact]
    public void DeleteFailure_IsNotReportedAsSuccess()
    {
        var result = new RecoveryManager(new FaultStore(journal: Journal(), delete: true)).RecoverIncompleteSession();
        Assert.Equal(RecoveryStatus.Failure, result.Status);
    }

    [Fact]
    public void CompleteRecoverySession_RemovesJournal()
    {
        var manager = Manager(); manager.BeginRecoverySession(SnapshotResult());
        Assert.Equal(RecoveryStatus.Success, manager.CompleteRecoverySession().Status);
        Assert.False(File.Exists(PathName));
    }

    [Fact]
    public void MultipleBeginCalls_DoNotOverwriteCrashEvidence()
    {
        var manager = Manager(); var first = manager.BeginRecoverySession(SnapshotResult());
        Assert.Equal(RecoveryStatus.Failure, manager.BeginRecoverySession(SnapshotResult()).Status);
        Assert.Equal(first.Journal!.RecoverySessionId, manager.LoadJournal().Journal!.RecoverySessionId);
    }

    [Theory]
    [InlineData((int)MsiControllerSnapshotStatus.DeviceNotFound)]
    [InlineData((int)MsiControllerSnapshotStatus.Indeterminate)]
    [InlineData((int)MsiControllerSnapshotStatus.EnumerationFailed)]
    public void BeginRecoverySession_UnsafeSnapshotResult_DeniesMutation(int status)
    {
        var result = Manager().BeginRecoverySession(new((MsiControllerSnapshotStatus)status, null, "Unsafe snapshot result."));

        Assert.Equal(RecoveryStatus.Failure, result.Status);
        Assert.False(File.Exists(PathName));
    }

    private RecoveryManager Manager() => new(new RecoveryJournalStore(PathName));
    private static RecoveryJournal Journal() => new(1, Guid.NewGuid(), DateTimeOffset.UtcNow, Snapshot(), new());
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed class MemoryStore(RecoveryJournal journal) : IRecoveryJournalStore
    {
        private RecoveryJournal? _journal = journal;
        public string JournalPath => "memory";
        public bool Exists() => _journal is not null;
        public RecoveryJournal Read() => _journal!;
        public void WriteNew(RecoveryJournal value) { if (_journal is not null) throw new IOException(); _journal = value; }
        public void Delete() => _journal = null;
    }
    private sealed class FaultStore(RecoveryJournal? journal = null, bool write = false, bool delete = false) : IRecoveryJournalStore
    {
        public string JournalPath => "fault";
        public bool Exists() => journal is not null;
        public RecoveryJournal Read() => journal!;
        public void WriteNew(RecoveryJournal value) { if (write) throw new IOException("write failed"); }
        public void Delete() { if (delete) throw new IOException("delete failed"); }
    }
}
