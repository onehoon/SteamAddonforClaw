using SteamInputAddonforClaw.Controllers;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Devices.Abstractions;
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
        var hidHide = new FakeHidHide(AddonPath) { Hidden = true };
        var input = new FakeInput();
        await using var coordinator = Create(input, hidHide, store);

        Assert.True(coordinator.Start().Started);
        await coordinator.StopAsync();

        Assert.Equal(0, store.WriteCount);
        Assert.Equal(0, hidHide.AddCount);
        Assert.Equal(0, hidHide.RemoveCount);
    }

    [Fact]
    public async Task HidHideNotInstalled_RemainsPassiveWithoutMutation()
    {
        var store = new MemoryStore();
        var hidHide = new FakeHidHide { Status = HidHideInspectionStatus.NotInstalled };
        var input = new FakeInput();
        await using var coordinator = Create(input, hidHide, store);

        Assert.False(coordinator.Start().Started);
        Assert.Equal(0, input.StartCount);
        Assert.Equal(0, store.WriteCount);
        Assert.Equal(0, hidHide.AddCount);
    }

    [Fact]
    public async Task HidHideDisabled_StartsDirectInputWithoutMutation()
    {
        var store = new MemoryStore();
        var hidHide = new FakeHidHide { Status = HidHideInspectionStatus.Disabled };
        var input = new FakeInput();
        await using var coordinator = Create(input, hidHide, store);

        Assert.True(coordinator.Start().Started);
        Assert.Equal(1, input.StartCount);
        Assert.Equal(0, store.WriteCount);
        Assert.Equal(0, hidHide.AddCount);
    }

    [Fact]
    public async Task ActiveHidHide_WhenMsiDeviceIsNotHidden_StartsDirectInputWithoutMutation()
    {
        var store = new MemoryStore();
        var hidHide = new FakeHidHide();
        var input = new FakeInput();
        await using var coordinator = Create(input, hidHide, store);

        Assert.True(coordinator.Start().Started);
        Assert.Equal(0, store.WriteCount);
        Assert.Equal(0, hidHide.AddCount);
    }

    [Fact]
    public async Task AbsentWhitelistEntry_PersistsThenAddsThenStartsAndCleansUp()
    {
        var events = new List<string>();
        var store = new MemoryStore(events);
        var hidHide = new FakeHidHide(events) { Hidden = true };
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
        var hidHide = new FakeHidHide { Hidden = true };
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
        var hidHide = new FakeHidHide { Hidden = true, AddSucceeds = false };
        var input = new FakeInput();
        await using var coordinator = Create(input, hidHide, store);

        Assert.False(coordinator.Start().Started);
        Assert.False(store.Exists());
        Assert.Equal(0, input.StartCount);
    }

    [Fact]
    public async Task AmbiguousAddFailure_WithPersistedEntryPreservesAndUsesTheLease()
    {
        var store = new MemoryStore();
        var hidHide = new FakeHidHide { Hidden = true, AddSucceeds = false, AddChangesBeforeFailure = true };
        var input = new FakeInput();
        await using var coordinator = Create(input, hidHide, store);

        Assert.True(coordinator.Start().Started);
        await coordinator.StopAsync();

        Assert.False(store.Exists());
        Assert.Equal(1, hidHide.RemoveCount);
    }

    [Fact]
    public async Task RemoveFailure_PreservesJournalForStartupRecovery()
    {
        var store = new MemoryStore();
        var hidHide = new FakeHidHide { Hidden = true, RemoveSucceeds = false };
        var input = new FakeInput();
        await using var coordinator = Create(input, hidHide, store);

        Assert.True(coordinator.Start().Started);
        await coordinator.StopAsync();

        Assert.True(store.Exists());
    }

    [Fact]
    public async Task Cleanup_WhenHidHideBecomesDisabledAfterLease_RemovesEntryAndDeletesJournal()
    {
        var store = new MemoryStore();
        var hidHide = new FakeHidHide { Hidden = true, Status = HidHideInspectionStatus.Disabled };
        hidHide.InspectionStatuses.Enqueue(HidHideInspectionStatus.Available);
        hidHide.InspectionStatuses.Enqueue(HidHideInspectionStatus.Available);
        var input = new FakeInput();
        await using var coordinator = Create(input, hidHide, store);

        Assert.True(coordinator.Start().Started);
        await coordinator.StopAsync();

        Assert.False(store.Exists());
        Assert.DoesNotContain(AddonPath, hidHide.Entries, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Verification_WhenHidHideBecomesInverseWhitelist_ReleasesLeaseAndDoesNotStartDirectInput()
    {
        var store = new MemoryStore();
        var hidHide = new FakeHidHide { Hidden = true, Status = HidHideInspectionStatus.InverseWhitelist };
        hidHide.InspectionStatuses.Enqueue(HidHideInspectionStatus.Available);
        hidHide.InspectionStatuses.Enqueue(HidHideInspectionStatus.InverseWhitelist);
        var input = new FakeInput();
        await using var coordinator = Create(input, hidHide, store);

        var result = coordinator.Start();

        Assert.False(result.Started);
        Assert.Equal(0, input.StartCount);
        Assert.Equal(1, hidHide.RemoveCount);
        Assert.DoesNotContain(AddonPath, hidHide.Entries, StringComparer.OrdinalIgnoreCase);
        Assert.False(store.Exists());
    }

    [Fact]
    public async Task Verification_WhenHidHideBecomesDisabledAndLeaseCleanupFails_DoesNotStartDirectInput()
    {
        var store = new MemoryStore();
        var hidHide = new FakeHidHide { Hidden = true, Status = HidHideInspectionStatus.Disabled, RemoveSucceeds = false };
        hidHide.InspectionStatuses.Enqueue(HidHideInspectionStatus.Available);
        hidHide.InspectionStatuses.Enqueue(HidHideInspectionStatus.Disabled);
        var input = new FakeInput();
        await using var coordinator = Create(input, hidHide, store);

        var result = coordinator.Start();

        Assert.False(result.Started);
        Assert.Equal(0, input.StartCount);
        Assert.Contains(AddonPath, hidHide.Entries, StringComparer.OrdinalIgnoreCase);
        Assert.True(store.Exists());
    }

    [Fact]
    public async Task VerificationFailure_RetryRecognizesJournalOwnedLeaseAndCleansItUp()
    {
        var store = new MemoryStore();
        var hidHide = new FakeHidHide { Hidden = true };
        hidHide.InspectionStatuses.Enqueue(HidHideInspectionStatus.Available);
        hidHide.InspectionStatuses.Enqueue(HidHideInspectionStatus.ConfigurationUnavailable);
        hidHide.InspectionStatuses.Enqueue(HidHideInspectionStatus.Available);
        var input = new FakeInput();
        await using var coordinator = Create(input, hidHide, store);

        Assert.False(coordinator.Start().Started);
        Assert.True(store.Exists());
        Assert.True(coordinator.Start().Started);
        await coordinator.StopAsync();

        Assert.False(store.Exists());
        Assert.Equal(1, hidHide.RemoveCount);
    }

    [Fact]
    public async Task DiagnosticDoesNotReclaimRoutingOwnedMixedWhitelistLease()
    {
        var store = new MemoryStore();
        var hidHide = new FakeHidHide { Hidden = true };
        hidHide.Entries.Add(AddonPath);
        store.WriteNew(new RecoveryJournal(
            RecoveryManager.CurrentSchemaVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            new(DeviceIdForTest(), 1, DateTimeOffset.UtcNow, System.Text.Json.JsonSerializer.SerializeToElement(new { Mode = "XInput" })),
            new(DeviceNativeStateChanged: true, ExecutableWhitelistAdditions: [AddonPath])));
        var input = new FakeInput();
        await using var coordinator = Create(input, hidHide, store);

        Assert.True(coordinator.Start().Started);
        await coordinator.StopAsync();

        Assert.Equal(0, hidHide.RemoveCount);
        Assert.Contains(AddonPath, hidHide.Entries, StringComparer.OrdinalIgnoreCase);
        Assert.True(store.Exists());
    }

    private static M1M2DiagnosticCoordinator Create(FakeInput input, FakeHidHide hidHide, MemoryStore store) =>
        new(input, hidHide, new RecoveryManager(store), AddonPath, () => ["HID\\VID_0DB0&PID_1902&MI_00&COL01\\TEST"]);

    private static HandheldDeviceId DeviceIdForTest() => new("test.device");

    private sealed class FakeInput(List<string>? events = null) : IMsiClawInputDiagnostic
    {
        public event EventHandler<ControllerState>? StateChanged = delegate { };
        public event EventHandler<MsiClawInputTestSummary>? TestCompleted;
        public SteamInputAddonforClaw.Input.ControllerState LatestState => default;
        public bool IsRunning { get; private set; }
        public int StartCount { get; private set; }
        public MsiClawInputStartResult Start() { StartCount++; IsRunning = true; events?.Add("Start"); return new(MsiClawInputStartStatus.Started, "Started"); }
        public MsiClawInputStartResult StartPrepared(DirectInputDeviceDescriptor descriptor) { IsRunning = true; events?.Add("StartPrepared"); return new(MsiClawInputStartStatus.Started, "Started"); }
        public Task<bool> WaitForFirstValidStateAsync(CancellationToken cancellationToken) => Task.FromResult(true);
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
        public bool AddChangesBeforeFailure { get; init; }
        public bool RemoveSucceeds { get; init; } = true;
        public bool Hidden { get; init; }
        public HidHideInspectionStatus Status { get; init; } = HidHideInspectionStatus.Available;
        public Queue<HidHideInspectionStatus> InspectionStatuses { get; } = new();
        public FakeHidHide(List<string> events) : this() => _events = events;
        public HidHideInspection Inspect() => new(InspectionStatuses.TryDequeue(out var status) ? status : Status, Entries, Hidden ? ["HID\\VID_0DB0&PID_1902&MI_00&COL01\\TEST"] : []);
        public bool AddApplication(string executablePath) { AddCount++; _events?.Add("Add"); if (!AddSucceeds) { if (AddChangesBeforeFailure) Entries.Add(executablePath); return false; } Entries.Add(executablePath); return true; }
        public bool RemoveApplication(string executablePath) { RemoveCount++; _events?.Add("Remove"); if (!RemoveSucceeds) return false; Entries.Remove(executablePath); return true; }
        public bool AddHiddenDevice(string deviceEntry) => true;
        public bool RemoveHiddenDevice(string deviceEntry) => true;
    }

    private sealed class MemoryStore(List<string>? events = null) : IRecoveryJournalStore
    {
        private RecoveryJournal? _journal;
        public bool ThrowOnWrite { get; init; }
        public int WriteCount { get; private set; }
        public string JournalPath => "memory";
        public bool Exists() => _journal is not null;
        public string ReadText() => System.Text.Json.JsonSerializer.Serialize(_journal!);
        public void WriteNew(RecoveryJournal journal) { WriteCount++; events?.Add("PersistJournal"); if (ThrowOnWrite) throw new IOException(); _journal = journal; }
        public void ReplaceExisting(RecoveryJournal journal) { WriteCount++; events?.Add("ReplaceJournal"); if (ThrowOnWrite) throw new IOException(); if (_journal is null) throw new IOException(); _journal = journal; }
        public void Delete() { events?.Add("DeleteJournal"); _journal = null; }
    }
}
