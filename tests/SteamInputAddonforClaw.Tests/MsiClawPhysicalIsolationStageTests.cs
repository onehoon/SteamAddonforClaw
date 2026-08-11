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

    [Theory]
    [InlineData((int)HidHideInspectionStatus.Disabled)]
    [InlineData((int)HidHideInspectionStatus.InverseWhitelist)]
    [InlineData((int)HidHideInspectionStatus.ConfigurationUnavailable)]
    public async Task NonAvailableHidHideCannotStartMutation(int status)
    {
        var hid = new FakeHidHide { Status = (HidHideInspectionStatus)status };
        var stage = Create(hid);
        Assert.False((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Empty(hid.Trace);
    }

    private MsiClawPhysicalIsolationStage Create(FakeHidHide hid, string physicalIdentity = "USB\\MSI_ROOT")
    {
        Directory.CreateDirectory(_directory);
        var recovery = new RecoveryManager(new RecoveryJournalStore(JournalPath));
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
        public HidHideInspection Inspect() => FailInspectionAfterDeviceMutation && Trace.Any(x => x.StartsWith("AddDevice"))
            ? new(HidHideInspectionStatus.ConfigurationUnavailable, new HashSet<string>(Applications), HiddenDevices)
            : new(Status, new HashSet<string>(Applications), HiddenDevices);
        public bool AddApplication(string path) { Trace.Add("AddApplication"); Applications.Add(path); return true; }
        public bool RemoveApplication(string path) { Trace.Add("RemoveApplication"); Applications.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)); return true; }
        public bool AddHiddenDevice(string entry) { Trace.Add("AddDevice:" + entry); HiddenDevices.Add(entry); return true; }
        public bool RemoveHiddenDevice(string entry) { Trace.Add("RemoveDevice:" + entry); HiddenDevices.RemoveAll(x => string.Equals(x, entry, StringComparison.OrdinalIgnoreCase)); return true; }
    }
}
