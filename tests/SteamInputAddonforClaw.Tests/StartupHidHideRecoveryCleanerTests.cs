using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Startup;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class StartupHidHideRecoveryCleanerTests
{
    [Fact]
    public void RequiresCleanup_NoEvidence_ReturnsFalse()
    {
        var journal = Journal();
        Assert.False(StartupHidHideRecoveryCleaner.RequiresCleanup(journal));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void RequiresCleanup_AnyEvidence_ReturnsTrue(bool withHidden, bool withWhitelist, bool withOriginalActive)
    {
        var journal = Journal(
            hidden: withHidden ? ["HID\\A"] : null,
            whitelist: withWhitelist ? ["C:\\addon.exe"] : null,
            originalActive: withOriginalActive ? false : null);
        Assert.True(StartupHidHideRecoveryCleaner.RequiresCleanup(journal));
    }

    [Fact]
    public void OriginalActiveStateNull_SetActiveNeverCalled_CleansOwnedEntries()
    {
        var client = new FakeHidHideClient(active: true, hidden: ["HID\\A"], whitelist: ["C:\\addon.exe"]);
        var journal = Journal(hidden: ["HID\\A"], whitelist: ["C:\\addon.exe"], originalActive: null);

        Assert.True(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));

        Assert.False(client.SetActiveCalled);
        Assert.Empty(client.CurrentHidden);
        Assert.Empty(client.CurrentWhitelist);
    }

    [Fact]
    public void OriginalActiveFalse_CurrentActiveTrue_OwnedEntriesOnly_RestoresAndCleans()
    {
        var client = new FakeHidHideClient(active: true, hidden: ["HID\\A", "HID\\B"], whitelist: []);
        var journal = Journal(hidden: ["HID\\A", "HID\\B"], originalActive: false);

        Assert.True(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out var reason));

        Assert.True(client.SetActiveCalled);
        Assert.False(client.IsActive);
        Assert.Empty(client.CurrentHidden);
        Assert.Equal("Startup stale HidHide recovery completed.", reason);
    }

    [Fact]
    public void OriginalActiveFalse_CurrentAlreadyDisabled_ForeignEntriesPreserved()
    {
        var client = new FakeHidHideClient(active: false, hidden: ["HID\\B", "HID\\Foreign"], whitelist: []);
        var journal = Journal(hidden: ["HID\\A", "HID\\B"], originalActive: false);

        Assert.True(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));

        Assert.False(client.SetActiveCalled);
        Assert.Equal(["HID\\Foreign"], client.CurrentHidden);
    }

    [Fact]
    public void OriginalActiveFalse_CurrentActiveTrue_ForeignHiddenEntryExists_FailsClosed()
    {
        var client = new FakeHidHideClient(active: true, hidden: ["HID\\B", "HID\\Foreign"], whitelist: []);
        var journal = Journal(hidden: ["HID\\A", "HID\\B"], originalActive: false);

        Assert.False(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out var reason));

        Assert.False(client.SetActiveCalled);
        Assert.NotEmpty(reason);
        // Nothing mutated.
        Assert.Equal(["HID\\B", "HID\\Foreign"], client.CurrentHidden);
    }

    [Fact]
    public void OriginalActiveStateTrue_FailsClosedWithoutMutation()
    {
        var client = new FakeHidHideClient(active: false, hidden: ["HID\\A"], whitelist: ["C:\\addon.exe"]);
        var journal = Journal(hidden: ["HID\\A"], whitelist: ["C:\\addon.exe"], originalActive: true);

        Assert.False(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));

        Assert.False(client.SetActiveCalled);
        Assert.Equal(["HID\\A"], client.CurrentHidden);
        Assert.Equal(["C:\\addon.exe"], client.CurrentWhitelist);
    }

    [Theory]
    [InlineData((int)HidHideInspectionStatus.NotInstalled)]
    [InlineData((int)HidHideInspectionStatus.ConfigurationUnavailable)]
    [InlineData((int)HidHideInspectionStatus.AccessDenied)]
    [InlineData((int)HidHideInspectionStatus.InverseWhitelist)]
    public void UnsafeInspectionStatus_FailsClosedWithoutMutation(int statusValue)
    {
        var status = (HidHideInspectionStatus)statusValue;
        var client = new FakeHidHideClient(active: true, hidden: ["HID\\A"], whitelist: []) { Status = status };
        var journal = Journal(hidden: ["HID\\A"], originalActive: null);

        Assert.False(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));

        Assert.Equal(0, client.RemoveHiddenDeviceCalls);
        Assert.False(client.SetActiveCalled);
    }

    [Fact]
    public void DisabledStatus_IsConfigurationReadable_NotRejected()
    {
        var client = new FakeHidHideClient(active: false, hidden: ["HID\\A"], whitelist: []) { Status = HidHideInspectionStatus.Disabled };
        var journal = Journal(hidden: ["HID\\A"], originalActive: null);

        Assert.True(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));
        Assert.Empty(client.CurrentHidden);
    }

    [Fact]
    public void IdempotentPartialResidue_AlreadyAbsentEntrySkipped()
    {
        var client = new FakeHidHideClient(active: false, hidden: ["HID\\B"], whitelist: []);
        var journal = Journal(hidden: ["HID\\A", "HID\\B"], originalActive: null);

        Assert.True(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));

        Assert.Equal(2, client.RemoveHiddenDeviceCalls);
        Assert.Empty(client.CurrentHidden);
    }

    [Fact]
    public void RemoveHiddenDeviceFails_CleanupFails()
    {
        var client = new FakeHidHideClient(active: false, hidden: ["HID\\A"], whitelist: []) { FailRemoveHiddenDevice = true };
        var journal = Journal(hidden: ["HID\\A"], originalActive: null);

        Assert.False(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));
    }

    [Fact]
    public void HiddenEntryRemainsAfterRemoval_CleanupFails()
    {
        var client = new FakeHidHideClient(active: false, hidden: ["HID\\A"], whitelist: []) { VerifyMismatchHidden = true };
        var journal = Journal(hidden: ["HID\\A"], originalActive: null);

        Assert.False(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));
    }

    [Fact]
    public void RemoveApplicationFails_CleanupFails()
    {
        var client = new FakeHidHideClient(active: false, hidden: [], whitelist: ["C:\\addon.exe"]) { FailRemoveApplication = true };
        var journal = Journal(whitelist: ["C:\\addon.exe"], originalActive: null);

        Assert.False(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));
    }

    [Fact]
    public void WhitelistEntryRemainsAfterRemoval_CleanupFails()
    {
        var client = new FakeHidHideClient(active: false, hidden: [], whitelist: ["C:\\addon.exe"]) { VerifyMismatchWhitelist = true };
        var journal = Journal(whitelist: ["C:\\addon.exe"], originalActive: null);

        Assert.False(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));
    }

    [Fact]
    public void SetActiveFails_CleanupFails()
    {
        var client = new FakeHidHideClient(active: true, hidden: ["HID\\A"], whitelist: []) { FailSetActive = true };
        var journal = Journal(hidden: ["HID\\A"], originalActive: false);

        Assert.False(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));
        Assert.True(client.SetActiveCalled);
    }

    [Fact]
    public void StateDriftsBeforeDisable_SecondInspectionUnsafe_SetActiveNeverCalledFailsClosed()
    {
        var client = new FakeHidHideClient(active: true, hidden: ["HID\\A"], whitelist: []);
        var journal = Journal(hidden: ["HID\\A"], originalActive: false);
        var firstInspection = true;
        client.OnInspect = () =>
        {
            if (firstInspection) { firstInspection = false; return; }
            client.CurrentHidden.Add("HID\\Foreign");
        };

        Assert.False(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));
        Assert.False(client.SetActiveCalled);
    }

    [Fact]
    public void InvalidEvidence_BlankHiddenEntry_FailsClosedWithoutAnyHidHideCall()
    {
        var client = new FakeHidHideClient(active: true, hidden: [], whitelist: []);
        var journal = Journal(hidden: [" "], originalActive: null);

        Assert.False(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));
        Assert.Equal(0, client.InspectCallCount);
    }

    [Fact]
    public void InvalidEvidence_BlankWhitelistPath_FailsClosedWithoutAnyHidHideCall()
    {
        var client = new FakeHidHideClient(active: true, hidden: [], whitelist: []);
        var journal = Journal(whitelist: [""], originalActive: null);

        Assert.False(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));
        Assert.Equal(0, client.InspectCallCount);
    }

    private static RecoveryJournal Journal(IReadOnlyList<string>? hidden = null, IReadOnlyList<string>? whitelist = null, bool? originalActive = null) =>
        new(RecoveryManager.CurrentSchemaVersion, Guid.NewGuid(), DateTimeOffset.UtcNow, null,
            new(HidHideDeviceAdditions: hidden, ExecutableWhitelistAdditions: whitelist, OriginalHidHideActiveState: originalActive));

    /// <summary>
    /// Models real HidHide semantics closely enough for the cleaner's contract: hidden/whitelist
    /// removals mutate underlying sets (so post-removal verification reflects reality) unless a
    /// "VerifyMismatch*" flag is set to simulate a driver that reports success but didn't take.
    /// </summary>
    private sealed class FakeHidHideClient(bool active, IEnumerable<string> hidden, IEnumerable<string> whitelist) : IHidHideClient
    {
        public HashSet<string> CurrentHidden { get; } = new(hidden, StringComparer.OrdinalIgnoreCase);
        public HashSet<string> CurrentWhitelist { get; } = new(whitelist, StringComparer.OrdinalIgnoreCase);
        public HidHideInspectionStatus Status { get; set; } = HidHideInspectionStatus.Available;
        public bool IsActive { get; set; } = active;
        public bool FailRemoveHiddenDevice { get; set; }
        public bool FailRemoveApplication { get; set; }
        public bool FailSetActive { get; set; }
        public bool VerifyMismatchHidden { get; set; }
        public bool VerifyMismatchWhitelist { get; set; }
        public bool SetActiveCalled { get; private set; }
        public int InspectCallCount { get; private set; }
        public int RemoveHiddenDeviceCalls { get; private set; }
        public Action? OnInspect { get; set; }

        public HidHideInspection Inspect()
        {
            InspectCallCount++;
            OnInspect?.Invoke();
            return new(Status, new HashSet<string>(CurrentWhitelist, StringComparer.OrdinalIgnoreCase), CurrentHidden.ToList(), null, IsActive, Status == HidHideInspectionStatus.InverseWhitelist);
        }

        public bool AddApplication(string executablePath) => true;

        public bool RemoveApplication(string executablePath)
        {
            if (FailRemoveApplication) return false;
            if (!VerifyMismatchWhitelist) CurrentWhitelist.RemoveWhere(entry => string.Equals(entry, executablePath, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        public bool AddHiddenDevice(string deviceEntry) => true;

        public bool RemoveHiddenDevice(string deviceEntry)
        {
            RemoveHiddenDeviceCalls++;
            if (FailRemoveHiddenDevice) return false;
            if (!VerifyMismatchHidden) CurrentHidden.RemoveWhere(entry => string.Equals(entry, deviceEntry, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        public bool SetActive(bool active)
        {
            SetActiveCalled = true;
            if (FailSetActive) return false;
            IsActive = active;
            return true;
        }
    }
}
