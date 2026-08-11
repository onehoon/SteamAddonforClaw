using System.Text.Json;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Recovery;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawPhysicalIsolationStageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ClawIsolationTests", Guid.NewGuid().ToString("N"));
    private string JournalPath => Path.Combine(_directory, "recovery.json");

    [Fact]
    public async Task AddsBaseAndChildInOrderAndRollsBackInReverse()
    {
        var hid = new FakeHidHide();
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(["AddApplication", "AddDevice:USB\\MSI_ROOT", "AddDevice:HID\\CHILD"], hid.Trace);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(["AddApplication", "AddDevice:USB\\MSI_ROOT", "AddDevice:HID\\CHILD", "RemoveDevice:HID\\CHILD", "RemoveDevice:USB\\MSI_ROOT", "RemoveApplication"], hid.Trace);
    }

    [Fact]
    public async Task CaseInsensitiveDuplicateIsOnlyMutatedOnce()
    {
        var hid = new FakeHidHide();
        var stage = Create(hid, physicalIdentity: "hid\\child");
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Single(hid.HiddenDevices);
    }

    [Fact]
    public async Task InactiveHidHideIsEnabledLastAndRestoredFirst()
    {
        var hid = new FakeHidHide { Active = false };
        var stage = Create(hid);

        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal("SetActive:True", hid.Trace[^1]);
        Assert.True(hid.Active);

        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal("SetActive:False", hid.Trace[4]);
        Assert.False(hid.Active);
    }

    [Fact]
    public async Task HidHideInverseDriftStopsBeforeAnyMutation()
    {
        var hid = new FakeHidHide { Active = false };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);

        hid.Inverse = true;

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal("HidHideStateDrift", result.Reason);
        Assert.DoesNotContain(hid.Trace, entry => entry.StartsWith("Add", StringComparison.Ordinal));
        Assert.DoesNotContain("SetActive:True", hid.Trace);
    }

    [Fact]
    public async Task HidHideActiveDriftStopsBeforeAnyMutation()
    {
        var hid = new FakeHidHide { Active = false };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);

        hid.Active = true;

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal("HidHideStateDrift", result.Reason);
        Assert.Empty(hid.Trace);
    }

    [Fact]
    public async Task PreExistingEntriesArePreserved()
    {
        var hid = new FakeHidHide { HiddenDevices = ["USB\\MSI_ROOT", "HID\\CHILD"], Applications = ["C:\\addon.exe"] };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Empty(hid.Trace);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(["USB\\MSI_ROOT", "HID\\CHILD"], hid.HiddenDevices);
        Assert.Contains("C:\\addon.exe", hid.Applications);
    }

    [Fact]
    public async Task AmbiguousDeviceMutationDoesNotRemoveWhitelist()
    {
        var hid = new FakeHidHide { FailInspectionAfterDeviceMutation = true };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.False((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal("AmbiguousMutationPending", (await stage.RollbackMutationAsync(CancellationToken.None)).Reason);
        Assert.Contains("C:\\addon.exe", hid.Applications);
    }

    [Fact]
    public async Task WhitelistVerifiedAbsentButJournalCompletionFailureKeepsRollbackPending()
    {
        var hid = new FakeHidHide { FailApplicationAddWithoutApplying = true };
        var stage = Create(hid, store: new FaultingStore(JournalPath, successfulReplacements: 1));
        var prepare = await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True(prepare.Succeeded, prepare.Reason);
        var execute = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.False(execute.Succeeded);
        Assert.Equal("WhitelistJournalCompletionFailed", execute.Reason);
        Assert.False((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task DeviceVerifiedAbsentButJournalCompletionFailureKeepsRollbackPending()
    {
        var hid = new FakeHidHide { FailDeviceAddWithoutApplying = true };
        var stage = Create(hid, store: new FaultingStore(JournalPath, successfulReplacements: 2));
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal("DeviceJournalCompletionFailed", (await stage.ExecuteMutationAsync(CancellationToken.None)).Reason);
        Assert.False((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task AddReportsFailureButPresentMutationIsRolledBack()
    {
        var hid = new FakeHidHide { ReportDeviceAddFailureAfterApplying = true };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal("DeviceAddReportedFailure", (await stage.ExecuteMutationAsync(CancellationToken.None)).Reason);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.DoesNotContain(hid.HiddenDevices, x => string.Equals(x, "USB\\MSI_ROOT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("RemoveDevice:USB\\MSI_ROOT", hid.Trace);
        Assert.DoesNotContain(hid.Applications, x => string.Equals(x, "C:\\addon.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData((int)HidHideInspectionStatus.InverseWhitelist)]
    [InlineData((int)HidHideInspectionStatus.ConfigurationUnavailable)]
    public async Task NonAvailableHidHideCannotStartMutation(int status)
    {
        var hid = new FakeHidHide { Status = (HidHideInspectionStatus)status };
        var stage = Create(hid);
        Assert.False((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Empty(hid.Trace);
    }

    private MsiClawPhysicalIsolationStage Create(FakeHidHide hid, string physicalIdentity = "USB\\MSI_ROOT", IRecoveryJournalStore? store = null)
    {
        Directory.CreateDirectory(_directory);
        var recovery = new RecoveryManager(store ?? new RecoveryJournalStore(JournalPath));
        recovery.BeginDeviceNativeStateMutation(new(NativeStateCaptureStatus.Success,
            new(new("test"), 1, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { Mode = "XInput" })), "captured"));
        var sessionId = recovery.LoadJournal().Journal!.RecoverySessionId;
        return new(new FakeInput(new(Guid.NewGuid(), "path", "HID\\CHILD", physicalIdentity)), new FakeSession(sessionId), recovery, hid, () => "C:\\addon.exe");
    }

    public void Dispose() { try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } catch { } }

    private sealed class FakeInput(MsiClawPhysicalInputIdentity identity) : IMsiClawPhysicalInputIdentityProvider { public MsiClawPhysicalInputIdentity? CurrentIdentity => identity; }
    private sealed class FakeSession(Guid id) : IRoutingRecoverySessionProvider { public Guid? CurrentRecoverySessionId => id; }
    private sealed class FakeHidHide : IHidHideClient
    {
        public HidHideInspectionStatus Status { get; set; } = HidHideInspectionStatus.Available;
        public List<string> Applications { get; set; } = [];
        public List<string> HiddenDevices { get; set; } = [];
        public List<string> Trace { get; } = [];
        public bool FailInspectionAfterDeviceMutation { get; set; }
        public bool FailDeviceAdd { get; set; }
        public bool ReportDeviceAddFailureAfterApplying { get; set; }
        public bool FailApplicationAddWithoutApplying { get; set; }
        public bool FailDeviceAddWithoutApplying { get; set; }
        public bool Active { get; set; } = true;
        public bool Inverse { get; set; }
        public HidHideInspection Inspect() => FailInspectionAfterDeviceMutation && Trace.Any(x => x.StartsWith("AddDevice"))
            ? new(HidHideInspectionStatus.ConfigurationUnavailable, new HashSet<string>(Applications), HiddenDevices, IsActive: Active, IsInverseWhitelist: Inverse)
            : new(Status, new HashSet<string>(Applications), HiddenDevices, IsActive: Active, IsInverseWhitelist: Inverse);
        public bool SetActive(bool active) { Trace.Add("SetActive:" + active); Active = active; return true; }
        public bool AddApplication(string path) { Trace.Add("AddApplication"); if (FailApplicationAddWithoutApplying) return false; Applications.Add(path); return true; }
        public bool RemoveApplication(string path) { Trace.Add("RemoveApplication"); Applications.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)); return true; }
        public bool AddHiddenDevice(string entry) { Trace.Add("AddDevice:" + entry); if (FailDeviceAddWithoutApplying) return false; HiddenDevices.Add(entry); return !FailDeviceAdd && !ReportDeviceAddFailureAfterApplying; }
        public bool RemoveHiddenDevice(string entry) { Trace.Add("RemoveDevice:" + entry); HiddenDevices.RemoveAll(x => string.Equals(x, entry, StringComparison.OrdinalIgnoreCase)); return true; }
    }

    private sealed class FaultingStore(string path, int successfulReplacements) : IRecoveryJournalStore
    {
        private readonly RecoveryJournalStore _inner = new(path);
        private int _replacements;
        public string JournalPath => _inner.JournalPath;
        public bool Exists() => _inner.Exists();
        public string ReadText() => _inner.ReadText();
        public void WriteNew(RecoveryJournal journal) => _inner.WriteNew(journal);
        public void ReplaceExisting(RecoveryJournal journal)
        { if (_replacements++ >= successfulReplacements) throw new IOException("Injected journal completion failure."); _inner.ReplaceExisting(journal); }
        public void Delete() => _inner.Delete();
    }
}
