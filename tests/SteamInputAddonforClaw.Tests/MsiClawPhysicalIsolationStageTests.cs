using System.Text.Json;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Routing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawPhysicalIsolationStageTests : IDisposable
{
    private const string OfficialHidHideClient = "C:\\Program Files\\Nefarius Software Solutions\\HidHide\\x64\\HidHideClient.exe";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ClawIsolationTests", Guid.NewGuid().ToString("N"));
    private string JournalPath => Path.Combine(_directory, "recovery.json");

    [Fact]
    public async Task AddsOnlyAcquiredPrimaryChildAndRollsBack()
    {
        var hid = new FakeHidHide();
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(["AddApplication", "AddDevice:HID\\VID_0DB0&PID_1902&MI_00&COL01\\CHILD"], hid.Trace);
        Assert.DoesNotContain(hid.Trace, entry => entry.Contains("USB\\MSI_ROOT", StringComparison.OrdinalIgnoreCase));
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(["AddApplication", "AddDevice:HID\\VID_0DB0&PID_1902&MI_00&COL01\\CHILD", "RemoveDevice:HID\\VID_0DB0&PID_1902&MI_00&COL01\\CHILD", "RemoveApplication"], hid.Trace);
    }

    [Fact]
    public async Task Prepare_allows_addon_and_official_hidhide_client()
    {
        var hid = new FakeHidHide { Applications = ["C:\\addon.exe", OfficialHidHideClient] };
        var stage = Create(hid, trustedHidHideApplicationPaths: [OfficialHidHideClient]);

        var result = await stage.PrepareMutationAsync(CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
    }

    [Fact]
    public async Task Prepare_rejects_addon_and_foreign_application()
    {
        var hid = new FakeHidHide { Applications = ["C:\\addon.exe", "C:\\other.exe"] };
        var stage = Create(hid, trustedHidHideApplicationPaths: [OfficialHidHideClient]);

        var result = await stage.PrepareMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("ForeignConfiguration", result.Reason);
    }

    [Fact]
    public async Task Prepare_rejects_preexisting_hidden_device_with_official_client()
    {
        var hid = new FakeHidHide { Applications = ["C:\\addon.exe", OfficialHidHideClient], HiddenDevices = ["HID\\FOREIGN"] };
        var stage = Create(hid, trustedHidHideApplicationPaths: [OfficialHidHideClient]);

        var result = await stage.PrepareMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("ForeignConfiguration", result.Reason);
    }

    [Fact]
    public async Task PhysicalIdentityIsNeverHidden()
    {
        var hid = new FakeHidHide();
        var stage = Create(hid, physicalIdentity: "USB\\VID_0DB0&PID_1902\\ROOT");
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Single(hid.HiddenDevices);
        Assert.DoesNotContain(hid.HiddenDevices, value => value.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReconcileOwnedState_repairs_only_owned_hidhide_drift()
    {
        var hid = new FakeHidHide { Active = false, Status = HidHideInspectionStatus.Disabled };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        hid.Trace.Clear();
        hid.Applications.Clear();
        hid.HiddenDevices.Clear();
        hid.Active = false;

        var result = await stage.ReconcileOwnedStateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("Repaired", result.Reason);
        Assert.Equal(["AddApplication", "AddDevice:HID\\VID_0DB0&PID_1902&MI_00&COL01\\CHILD", "SetActive:True"], hid.Trace);
        Assert.Contains("C:\\addon.exe", hid.Applications);
        Assert.Contains("HID\\VID_0DB0&PID_1902&MI_00&COL01\\CHILD", hid.HiddenDevices);
        Assert.True(hid.Active);
    }

    [Fact]
    public async Task ReconcileOwnedState_preserves_foreign_hidhide_entries()
    {
        var hid = new FakeHidHide();
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        hid.Trace.Clear();
        hid.HiddenDevices.Add("HID\\FOREIGN");

        var result = await stage.ReconcileOwnedStateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("Healthy", result.Reason);
        Assert.Empty(hid.Trace);
        Assert.Contains("HID\\FOREIGN", hid.HiddenDevices);
    }

    [Fact]
    public async Task ReconcileOwnedState_does_not_reenable_owned_active_with_foreign_blocked_entry()
    {
        var hid = new FakeHidHide { Active = false, Status = HidHideInspectionStatus.Disabled };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        hid.Trace.Clear();
        hid.HiddenDevices.Add("HID\\FOREIGN");
        hid.Active = false;

        var result = await stage.ReconcileOwnedStateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("ActiveStateRepairUnsafeForeignBlockedEntries", result.Reason);
        Assert.DoesNotContain("SetActive:True", hid.Trace);
        Assert.Contains("HID\\FOREIGN", hid.HiddenDevices);
    }

    [Fact]
    public async Task ReconcileOwnedState_fails_when_preexisting_whitelist_disappears()
    {
        var hid = new FakeHidHide { Applications = ["C:\\addon.exe"] };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        hid.Applications.Clear(); hid.Trace.Clear();

        var result = await stage.ReconcileOwnedStateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("PreExistingWhitelistDrift", result.Reason);
        Assert.Empty(hid.Trace);
    }

    [Fact]
    public async Task ReconcileOwnedState_fails_when_preexisting_active_disappears()
    {
        var hid = new FakeHidHide { Applications = ["C:\\addon.exe"], Active = true };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        hid.Active = false; hid.Trace.Clear();

        var result = await stage.ReconcileOwnedStateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("PreExistingActiveStateDrift", result.Reason);
        Assert.Empty(hid.Trace);
    }

    [Fact]
    public async Task ReconcileOwnedState_owned_device_repair_must_be_verified()
    {
        var hid = new FakeHidHide { Active = false, Status = HidHideInspectionStatus.Disabled };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        hid.FailDeviceAddWithoutApplying = false;
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        hid.HiddenDevices.Clear(); hid.ReportDeviceAddSuccessWithoutApplying = true; hid.Trace.Clear();

        var result = await stage.ReconcileOwnedStateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("DeviceRepairUnverified", result.Reason);
    }

    [Fact]
    public async Task InactiveHidHideIsEnabledLastAndRestoredFirst()
    {
        var hid = new FakeHidHide { Active = false, Status = HidHideInspectionStatus.Disabled };
        var stage = Create(hid);

        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal("SetActive:True", hid.Trace[^1]);
        Assert.True(hid.Active);

        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal("SetActive:False", hid.Trace[3]);
        Assert.False(hid.Active);
    }

    [Fact]
    public async Task HidHideInverseDriftStopsBeforeAnyMutation()
    {
        var hid = new FakeHidHide { Active = false, Status = HidHideInspectionStatus.Disabled };
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
        var hid = new FakeHidHide { Active = false, Status = HidHideInspectionStatus.Disabled };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);

        hid.Active = true;

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal("HidHideStateDrift", result.Reason);
        Assert.Empty(hid.Trace);
    }

    [Fact]
    public async Task HidHideInverseDriftBeforeActivationDoesNotEnableOrOwnGlobalState()
    {
        var hid = new FakeHidHide { Active = false, Status = HidHideInspectionStatus.Disabled, DriftToInverseAfterDeviceAdd = true };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("HidHideStateDriftBeforeActivation", result.Reason);
        Assert.DoesNotContain("SetActive:True", hid.Trace);
        Assert.False(hid.Active);
        Assert.True(hid.Inverse);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.DoesNotContain("C:\\addon.exe", hid.Applications);
        Assert.Empty(hid.HiddenDevices);
        Assert.True(hid.Inverse);
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
        Assert.DoesNotContain(hid.HiddenDevices, x => string.Equals(x, "HID\\VID_0DB0&PID_1902&MI_00&COL01\\CHILD", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("RemoveDevice:HID\\VID_0DB0&PID_1902&MI_00&COL01\\CHILD", hid.Trace);
        Assert.DoesNotContain(hid.Applications, x => string.Equals(x, "C:\\addon.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("HID\\VID_0DB0&PID_1901&MI_00&COL01\\CHILD")]
    [InlineData("HID\\VID_0DB0&PID_1902&MI_01&COL01\\CHILD")]
    [InlineData("HID\\VID_0DB0&PID_1902&MI_00&COL02\\CHILD")]
    [InlineData("USB\\VID_0DB0&PID_1902\\ROOT")]
    [InlineData("HID\\VID_0DB0&PID_1902&MI_00&COL01\\")]
    [InlineData("")]
    public async Task InvalidIsolationTargetFailsBeforeAnyMutation(string pnpInstanceId)
    {
        var hid = new FakeHidHide();
        var stage = Create(hid, pnpInstanceId: pnpInstanceId);

        var result = await stage.PrepareMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("PhysicalIsolationTargetInvalid", result.Reason);
        Assert.Empty(hid.Trace);
    }

    [Fact]
    public async Task AnyExistingHiddenEntryIsRejectedBeforeAnyMutation()
    {
        var hid = new FakeHidHide { Active = false, Status = HidHideInspectionStatus.Disabled, HiddenDevices = ["HID\\FOREIGN"] };
        var stage = Create(hid);

        var result = await stage.PrepareMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("ForeignConfiguration", result.Reason);
        Assert.Empty(hid.Trace);
    }

    [Fact]
    public async Task ForeignEntryDuringTemporaryActiveLeasePreservesRecoveryEvidence()
    {
        var hid = new FakeHidHide { Active = false, Status = HidHideInspectionStatus.Disabled };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        hid.HiddenDevices.Add("HID\\FOREIGN");

        var rollback = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.False(rollback.Succeeded);
        Assert.Equal("ActiveStateRestoreUnsafeForeignBlockedEntries", rollback.Reason);
        Assert.DoesNotContain("SetActive:False", hid.Trace);
        Assert.Contains("C:\\addon.exe", hid.Applications);
        Assert.Contains("HID\\VID_0DB0&PID_1902&MI_00&COL01\\CHILD", hid.HiddenDevices);

        hid.HiddenDevices.Remove("HID\\FOREIGN");
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.False(hid.Active);
        Assert.Empty(hid.HiddenDevices);
        Assert.Empty(hid.Applications);
    }

    [Fact]
    public async Task ForeignEntryBeforeTemporaryActivationPreventsGlobalEnableAndRollsBackOwnedState()
    {
        var hid = new FakeHidHide { Active = false, Status = HidHideInspectionStatus.Disabled, ForeignEntryAppearsBeforeActivation = true };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);

        var execute = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(execute.Succeeded);
        Assert.Equal("ActiveStateEnableUnsafeForeignBlockedEntries", execute.Reason);
        Assert.False(hid.Active);
        Assert.DoesNotContain("SetActive:True", hid.Trace);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(["HID\\FOREIGN"], hid.HiddenDevices);
        Assert.Empty(hid.Applications);
    }

    [Fact]
    public async Task ForeignEntryImmediatelyAfterTemporaryActivationFailsClosedAndPreservesEvidence()
    {
        var hid = new FakeHidHide { Active = false, Status = HidHideInspectionStatus.Disabled, ForeignEntryAppearsAfterActivation = true };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);

        var execute = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(execute.Succeeded);
        Assert.Equal("ActiveStateEnableUnsafeForeignBlockedEntries", execute.Reason);
        Assert.True(hid.Active);
        Assert.Contains("HID\\FOREIGN", hid.HiddenDevices);
        Assert.Equal("ActiveStateRestoreUnsafeForeignBlockedEntries", (await stage.RollbackMutationAsync(CancellationToken.None)).Reason);
        Assert.Contains("C:\\addon.exe", hid.Applications);
    }

    [Fact]
    public async Task InverseWhitelistDriftDuringTemporaryActiveLeasePreventsGlobalDisable()
    {
        var hid = new FakeHidHide { Active = false, Status = HidHideInspectionStatus.Disabled };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        hid.Inverse = true;

        var rollback = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.False(rollback.Succeeded);
        Assert.Equal("ActiveStateRestoreUnsafeInverseWhitelistDrift", rollback.Reason);
        Assert.DoesNotContain("SetActive:False", hid.Trace);
        Assert.True(hid.Active);
    }

    [Fact]
    public async Task ActiveEnableReportedFailureButAppliedPreservesRollbackOwnership()
    {
        var hid = new FakeHidHide { Active = false, Status = HidHideInspectionStatus.Disabled, ReportActiveEnableFailureAfterApplying = true };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);

        var execute = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(execute.Succeeded);
        Assert.Equal("ActiveStateEnableReportedFailure", execute.Reason);
        Assert.True(hid.Active);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.False(hid.Active);
        Assert.Empty(hid.HiddenDevices);
    }

    [Theory]
    [InlineData((int)HidHideInspectionStatus.InverseWhitelist)]
    [InlineData((int)HidHideInspectionStatus.ConfigurationUnavailable)]
    [InlineData((int)HidHideInspectionStatus.NotInstalled)]
    [InlineData((int)HidHideInspectionStatus.AccessDenied)]
    public async Task NonAvailableHidHideCannotStartMutation(int status)
    {
        var hid = new FakeHidHide { Status = (HidHideInspectionStatus)status };
        var stage = Create(hid);
        Assert.False((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Empty(hid.Trace);
    }

    private MsiClawPhysicalIsolationStage Create(FakeHidHide hid, string physicalIdentity = "USB\\MSI_ROOT", IRecoveryJournalStore? store = null, string pnpInstanceId = "HID\\VID_0DB0&PID_1902&MI_00&COL01\\CHILD", IReadOnlyCollection<string>? trustedHidHideApplicationPaths = null)
    {
        Directory.CreateDirectory(_directory);
        var recovery = new RecoveryManager(store ?? new RecoveryJournalStore(JournalPath));
        recovery.BeginDeviceNativeStateMutation(new(NativeStateCaptureStatus.Success,
            new(new("test"), 1, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { Mode = "XInput" })), "captured"));
        var sessionId = recovery.LoadJournal().Journal!.RecoverySessionId;
        return new(new FakeInput(new(Guid.NewGuid(), "path", pnpInstanceId, physicalIdentity)), new FakeSession(sessionId), recovery, hid, () => "C:\\addon.exe", trustedHidHideApplicationPaths);
    }

    public void Dispose() { try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } catch { } }

    private sealed class FakeInput(MsiClawPhysicalInputIdentity identity) : IMsiClawPhysicalInputIdentityProvider { public MsiClawPhysicalInputIdentity? CurrentIdentity => identity; public long CurrentSessionGeneration => 1; }
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
        public bool ReportDeviceAddSuccessWithoutApplying { get; set; }
        public bool Active { get; set; } = true;
        public bool Inverse { get; set; }
        public bool DriftToInverseAfterDeviceAdd { get; set; }
        public bool ForeignEntryAppearsBeforeActivation { get; set; }
        public bool ForeignEntryAppearsAfterActivation { get; set; }
        private int _postDeviceAddInspectionCount;
        public HidHideInspection Inspect() => FailInspectionAfterDeviceMutation && Trace.Any(x => x.StartsWith("AddDevice"))
            ? new(HidHideInspectionStatus.ConfigurationUnavailable, new HashSet<string>(Applications), HiddenDevices, IsActive: Active, IsInverseWhitelist: Inverse)
            : Inspection();
        private HidHideInspection Inspection()
        {
            if (Trace.Any(x => x.StartsWith("AddDevice")) && ++_postDeviceAddInspectionCount == 3 && ForeignEntryAppearsBeforeActivation)
                HiddenDevices.Add("HID\\FOREIGN");
            return new(Status, new HashSet<string>(Applications), HiddenDevices, IsActive: Active, IsInverseWhitelist: Inverse);
        }
        public bool ReportActiveEnableFailureAfterApplying { get; set; }
        public bool SetActive(bool active)
        {
            Trace.Add("SetActive:" + active);
            Active = active;
            if (active && ForeignEntryAppearsAfterActivation) HiddenDevices.Add("HID\\FOREIGN");
            return active && ReportActiveEnableFailureAfterApplying ? false : true;
        }
        public bool AddApplication(string path) { Trace.Add("AddApplication"); if (FailApplicationAddWithoutApplying) return false; Applications.Add(path); return true; }
        public bool RemoveApplication(string path) { Trace.Add("RemoveApplication"); Applications.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)); return true; }
        public bool AddHiddenDevice(string entry) { Trace.Add("AddDevice:" + entry); if (FailDeviceAddWithoutApplying || ReportDeviceAddSuccessWithoutApplying) return !FailDeviceAddWithoutApplying; HiddenDevices.Add(entry); if (DriftToInverseAfterDeviceAdd) Inverse = true; return !FailDeviceAdd && !ReportDeviceAddFailureAfterApplying; }
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
