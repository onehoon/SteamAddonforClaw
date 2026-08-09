using SteamInputAddonforClaw.Controllers;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Recovery;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class M1M2DiagnosticCoordinatorTests
{
    private const string AddonPath = @"C:\Apps\SteamInputAddonforClaw.exe";

    [Fact]
    public async Task ExistingWhitelistEntry_DoesNotWriteOrRemoveAnything()
    {
        var store = new MemoryStore();
        var hidHide = new FakeHidHide(AddonPath);
        var input = new FakeInput();
        await using var coordinator = Create(input, hidHide, store);

        Assert.True(coordinator.Start().Started);
        await coordinator.StopAsync();

        Assert.Equal(0, store.WriteCount);
        Assert.Equal(0, hidHide.AddCount);
        Assert.Equal(0, hidHide.RemoveCount);
    }

    [Fact]
    public async Task AbsentWhitelistEntry_PersistsThenAddsThenStartsAndCleansUp()
    {
        var events = new List<string>();
        var store = new MemoryStore(events);
        var hidHide = new FakeHidHide(events);
        var input = new FakeInput(events);
        await using var coordinator = Create(input, hidHide, store);

        Assert.True(coordinator.Start().Started);
        await coordinator.StopAsync();

        Assert.Equal(["PersistJournal", "Add", "Start", "Remove", "DeleteJournal"], events);
        Assert.False(store.Exists());
    }

    [Fact]
    public async Task JournalWriteFailure_DoesNotAddOrAcquire()
    {
        var store = new MemoryStore { ThrowOnWrite = true };
        var hidHide = new FakeHidHide();
        var input = new FakeInput();
        await using var coordinator = Create(input, hidHide, store);

        Assert.False(coordinator.Start().Started);
        Assert.Equal(0, hidHide.AddCount);
        Assert.Equal(0, input.StartCount);
    }

    [Fact]
    public async Task AddFailure_ClearsPendingJournalAndDoesNotStartDiagnostic()
    {
        var store = new MemoryStore();
        var hidHide = new FakeHidHide { AddSucceeds = false };
        var input = new FakeInput();
        await using var coordinator = Create(input, hidHide, store);

        Assert.False(coordinator.Start().Started);
        Assert.False(store.Exists());
        Assert.Equal(0, input.StartCount);
    }

    [Fact]
    public async Task RemoveFailure_PreservesJournalForStartupRecovery()
    {
        var store = new MemoryStore();
        var hidHide = new FakeHidHide { RemoveSucceeds = false };
        var input = new FakeInput();
        await using var coordinator = Create(input, hidHide, store);

        Assert.True(coordinator.Start().Started);
        await coordinator.StopAsync();

        Assert.True(store.Exists());
    }

    [Fact]
    public void CrashRecovery_RemovesOnlyRecordedAddonEntryAndPreservesOthers()
    {
        var store = new MemoryStore();
        var hidHide = new FakeHidHide(@"C:\Apps\ClawTweaks.exe", @"C:\Apps\Other.exe");
        var manager = new RecoveryManager(store, hidHide);
        Assert.Equal(RecoveryStatus.Success, manager.BeginHidHideWhitelistLease(AddonPath).Status);
        Assert.True(hidHide.AddApplication(AddonPath));

        Assert.Equal(RecoveryStatus.Success, manager.RecoverIncompleteSession().Status);

        Assert.DoesNotContain(AddonPath, hidHide.Entries, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(@"C:\Apps\ClawTweaks.exe", hidHide.Entries, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(@"C:\Apps\Other.exe", hidHide.Entries, StringComparer.OrdinalIgnoreCase);
        Assert.False(store.Exists());
    }

    [Fact]
    public void CrashRecovery_WhenEntryAlreadyAbsent_ClearsOnlyAfterReadableInspection()
    {
        var store = new MemoryStore();
        var hidHide = new FakeHidHide();
        var manager = new RecoveryManager(store, hidHide);
        Assert.Equal(RecoveryStatus.Success, manager.BeginHidHideWhitelistLease(AddonPath).Status);

        Assert.Equal(RecoveryStatus.Success, manager.RecoverIncompleteSession().Status);
        Assert.False(store.Exists());
    }

    private static M1M2DiagnosticCoordinator Create(FakeInput input, FakeHidHide hidHide, MemoryStore store) =>
        new(input, hidHide, new RecoveryManager(store, hidHide), AddonPath);

    private sealed class FakeInput(List<string>? events = null) : IMsiClawInputDiagnostic
    {
        public event EventHandler<MsiClawInputTestSummary>? TestCompleted;
        public bool IsRunning { get; private set; }
        public int StartCount { get; private set; }
        public MsiClawInputStartResult Start() { StartCount++; IsRunning = true; events?.Add("Start"); return new(MsiClawInputStartStatus.Started, "Started"); }
        public Task StopAsync() { IsRunning = false; TestCompleted?.Invoke(this, new(1, 0, false, false, false, 0, true, MsiClawInputStopReason.Stopped)); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeHidHide(params string[] entries) : IHidHideClient
    {
        private readonly List<string>? _events;
        public HashSet<string> Entries { get; } = new(entries, StringComparer.OrdinalIgnoreCase);
        public int AddCount { get; private set; }
        public int RemoveCount { get; private set; }
        public bool AddSucceeds { get; init; } = true;
        public bool RemoveSucceeds { get; init; } = true;
        public FakeHidHide(List<string> events) : this() => _events = events;
        public HidHideInspection Inspect() => new(HidHideInspectionStatus.Available, Entries);
        public bool AddApplication(string executablePath) { AddCount++; _events?.Add("Add"); if (!AddSucceeds) return false; Entries.Add(executablePath); return true; }
        public bool RemoveApplication(string executablePath) { RemoveCount++; _events?.Add("Remove"); if (!RemoveSucceeds) return false; Entries.Remove(executablePath); return true; }
    }

    private sealed class MemoryStore(List<string>? events = null) : IRecoveryJournalStore
    {
        private RecoveryJournal? _journal;
        public bool ThrowOnWrite { get; init; }
        public int WriteCount { get; private set; }
        public string JournalPath => "memory";
        public bool Exists() => _journal is not null;
        public RecoveryJournal Read() => _journal!;
        public void WriteNew(RecoveryJournal journal) { WriteCount++; events?.Add("PersistJournal"); if (ThrowOnWrite) throw new IOException(); _journal = journal; }
        public void Delete() { events?.Add("DeleteJournal"); _journal = null; }
    }
}
