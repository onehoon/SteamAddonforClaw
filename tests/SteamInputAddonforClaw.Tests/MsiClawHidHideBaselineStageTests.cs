using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.HidHide;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawHidHideBaselineStageTests
{
    private const string Addon = "C:\\addon.exe";
    private const string Client = "C:\\Program Files\\Nefarius Software Solutions\\HidHide\\x64\\HidHideClient.exe";
    private const string Cli = "C:\\Program Files\\Nefarius Software Solutions\\HidHide\\x64\\HidHideCLI.exe";

    [Fact]
    public async Task Already_normalized_baseline_has_zero_mutations()
    {
        var hid = new FakeHidHide { Applications = [Addon, Client, Cli] };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.True(result.Succeeded, result.Reason);
        Assert.Empty(hid.Trace);
    }

    [Fact]
    public async Task Stale_and_foreign_entries_are_removed_and_not_restored()
    {
        var hid = new FakeHidHide { Applications = [Addon, Client, Cli, "C:\\foreign.exe"], HiddenDevices = ["HID\\VID_0DB0&PID_1902&MI_00&COL01\\OLD", "HID\\FOREIGN"] };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal([Addon, Client, Cli], hid.Applications);
        Assert.Empty(hid.HiddenDevices);
        Assert.Contains("RemoveApplication:C:\\foreign.exe", hid.Trace);
        Assert.Contains("RemoveHiddenDevice:HID\\VID_0DB0&PID_1902&MI_00&COL01\\OLD", hid.Trace);
    }

    [Fact]
    public async Task Missing_addon_is_added_and_active_state_is_unchanged()
    {
        var hid = new FakeHidHide { Applications = [Client], Active = false };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Contains(Addon, hid.Applications);
        Assert.False(hid.Active);
        Assert.Contains("AddApplication:C:\\addon.exe", hid.Trace);
    }

    [Fact]
    public async Task Missing_official_tools_are_added_to_the_required_baseline()
    {
        var hid = new FakeHidHide { Applications = [Addon] };
        var stage = Create(hid);

        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal([Addon, Client, Cli], hid.Applications);
        Assert.Contains("AddApplication:" + Client, hid.Trace);
        Assert.Contains("AddApplication:" + Cli, hid.Trace);
    }

    [Fact]
    public async Task Official_paths_are_resolved_at_activation_not_composition()
    {
        IReadOnlyCollection<string> resolvedPaths = [];
        var hid = new FakeHidHide { Applications = [Addon] };
        var stage = new MsiClawHidHideBaselineStage(hid, Addon, () => resolvedPaths);

        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        resolvedPaths = [Client, Cli];

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Contains(Client, hid.Applications);
        Assert.Contains(Cli, hid.Applications);
    }

    [Theory]
    [InlineData((int)HidHideInspectionStatus.InverseWhitelist)]
    [InlineData((int)HidHideInspectionStatus.ConfigurationUnavailable)]
    [InlineData((int)HidHideInspectionStatus.AccessDenied)]
    [InlineData((int)HidHideInspectionStatus.NotInstalled)]
    public async Task Unsafe_inspection_fails_before_mutation(int status)
    {
        var hid = new FakeHidHide { Status = (HidHideInspectionStatus)status, Applications = ["C:\\foreign.exe"], HiddenDevices = ["HID\\FOREIGN"] };
        var result = await Create(hid).PrepareMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Empty(hid.Trace);
    }

    [Fact]
    public async Task Unresolved_whitelist_fails_before_mutation()
    {
        var hid = new FakeHidHide { Unresolved = true, Applications = ["C:\\foreign.exe"] };
        var result = await Create(hid).PrepareMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Empty(hid.Trace);
    }

    [Fact]
    public async Task Remove_hidden_device_failure_fails_baseline()
    {
        var hid = new FakeHidHide { HiddenDevices = ["HID\\FOREIGN"], RemoveHiddenDeviceSucceeds = false };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Contains("HID\\FOREIGN", hid.HiddenDevices);
    }

    [Fact]
    public async Task Remove_application_failure_fails_baseline()
    {
        var hid = new FakeHidHide { Applications = ["C:\\foreign.exe"], RemoveApplicationSucceeds = false };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Contains("C:\\foreign.exe", hid.Applications);
    }

    [Fact]
    public async Task Add_application_failure_fails_baseline()
    {
        var hid = new FakeHidHide { AddApplicationSucceeds = false };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.DoesNotContain(Addon, hid.Applications);
    }

    [Fact]
    public async Task Successful_hidden_device_removal_is_verified_before_baseline_succeeds()
    {
        var hid = new FakeHidHide { HiddenDevices = ["HID\\FOREIGN"], RemoveHiddenDeviceApplies = false };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("HiddenDeviceVerificationFailed", result.Reason);
    }

    [Fact]
    public async Task Successful_addon_add_is_verified_before_baseline_succeeds()
    {
        var hid = new FakeHidHide { AddApplicationApplies = false };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("RequiredWhitelistVerificationFailed", result.Reason);
    }

    [Fact]
    public async Task Rollback_is_non_restoring_success()
    {
        var hid = new FakeHidHide { Applications = ["C:\\foreign.exe"], HiddenDevices = ["HID\\FOREIGN"] };
        var stage = Create(hid);
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        var result = await stage.RollbackMutationAsync(CancellationToken.None);
        Assert.True(result.Succeeded, result.Reason);
        Assert.Empty(hid.HiddenDevices);
        Assert.DoesNotContain("C:\\foreign.exe", hid.Applications);
    }

    private static MsiClawHidHideBaselineStage Create(FakeHidHide hid) => new(hid, Addon, () => [Client, Cli]);

    private sealed class FakeHidHide : IHidHideClient
    {
        public HidHideInspectionStatus Status { get; set; } = HidHideInspectionStatus.Available;
        public bool Unresolved { get; set; }
        public bool Active { get; set; } = true;
        public bool RemoveHiddenDeviceSucceeds { get; set; } = true;
        public bool RemoveApplicationSucceeds { get; set; } = true;
        public bool AddApplicationSucceeds { get; set; } = true;
        public bool AddApplicationApplies { get; set; } = true;
        public bool RemoveHiddenDeviceApplies { get; set; } = true;
        public List<string> Applications { get; set; } = [];
        public List<string> HiddenDevices { get; set; } = [];
        public List<string> Trace { get; } = [];
        public HidHideInspection Inspect() => new(Status, Applications.ToHashSet(StringComparer.OrdinalIgnoreCase), HiddenDevices, IsActive: Active, IsInverseWhitelist: Status == HidHideInspectionStatus.InverseWhitelist, HasUnresolvedApplicationWhitelistEntries: Unresolved);
        public bool AddApplication(string path) { Trace.Add("AddApplication:" + path); if (!AddApplicationSucceeds) return false; if (AddApplicationApplies) Applications.Add(path); return true; }
        public bool RemoveApplication(string path) { Trace.Add("RemoveApplication:" + path); if (!RemoveApplicationSucceeds) return false; Applications.RemoveAll(value => string.Equals(value, path, StringComparison.OrdinalIgnoreCase)); return true; }
        public bool AddHiddenDevice(string entry) { Trace.Add("AddHiddenDevice:" + entry); HiddenDevices.Add(entry); return true; }
        public bool RemoveHiddenDevice(string entry) { Trace.Add("RemoveHiddenDevice:" + entry); if (!RemoveHiddenDeviceSucceeds) return false; if (RemoveHiddenDeviceApplies) HiddenDevices.RemoveAll(value => string.Equals(value, entry, StringComparison.OrdinalIgnoreCase)); return true; }
    }
}
