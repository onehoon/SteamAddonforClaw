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

        // "HID\A" was already absent: no unnecessary RemoveHiddenDevice call for it, only "HID\B".
        Assert.Equal(1, client.RemoveHiddenDeviceCalls);
        Assert.Empty(client.CurrentHidden);
    }

    [Fact]
    public void IdempotentWhitelistResidue_AlreadyAbsentEntrySkipped()
    {
        var client = new FakeHidHideClient(active: false, hidden: [], whitelist: ["C:\\already-gone.exe"]);
        var journal = Journal(whitelist: ["C:\\addon.exe", "C:\\already-gone.exe"], originalActive: null);

        Assert.True(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));

        // "C:\addon.exe" was already absent: no unnecessary RemoveApplication call for it.
        Assert.Equal(1, client.RemoveApplicationCalls);
        Assert.Empty(client.CurrentWhitelist);
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
    public void SetActiveReportsSuccessButVerificationStillActive_CleanupFails()
    {
        var client = new FakeHidHideClient(active: true, hidden: ["HID\\A"], whitelist: []) { SetActiveVerifiedStillActive = true };
        var journal = Journal(hidden: ["HID\\A"], originalActive: false);

        Assert.False(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));

        Assert.True(client.SetActiveCalled);
        // The global-state mutation was never trusted at face value; nothing else was cleaned.
        Assert.Equal(0, client.RemoveHiddenDeviceCalls);
        Assert.Equal(["HID\\A"], client.CurrentHidden);
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

    [Fact]
    public void InvalidEvidence_HiddenEntryWithSurroundingWhitespace_FailsClosedWithoutAnyHidHideCall()
    {
        var client = new FakeHidHideClient(active: true, hidden: [], whitelist: []);
        var journal = Journal(hidden: [" HID\\A "], originalActive: null);

        Assert.False(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));
        Assert.Equal(0, client.InspectCallCount);
    }

    [Theory]
    [InlineData("addon.exe")] // relative
    [InlineData(@"\addon.exe")] // root-relative
    [InlineData("C:addon.exe")] // drive-relative
    [InlineData(@"C:\foo\..\addon.exe")] // non-canonical
    [InlineData(@"\\server\share\addon.exe")] // UNC
    [InlineData(@" C:\addon.exe")] // surrounding whitespace
    public void InvalidEvidence_NonCanonicalWhitelistPath_FailsClosedWithoutAnyHidHideCall(string path)
    {
        var client = new FakeHidHideClient(active: true, hidden: [], whitelist: []);
        var journal = Journal(whitelist: [path], originalActive: null);

        Assert.False(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));
        Assert.Equal(0, client.InspectCallCount);
    }

    [Fact]
    public void ValidCanonicalWhitelistPath_IsAccepted()
    {
        var client = new FakeHidHideClient(active: false, hidden: [], whitelist: [@"C:\addon.exe"]);
        var journal = Journal(whitelist: [@"C:\addon.exe"], originalActive: null);

        Assert.True(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out _));
        Assert.Empty(client.CurrentWhitelist);
    }

    [Fact]
    public void JournalWhitelistPresent_UnnormalizableRawWhitelistEntry_DoesNotTreatOwnedEntryAsAbsent()
    {
        // HidHideDriverClient.Inspect() drops a raw whitelist entry it cannot convert to a DOS
        // path from the normalized ApplicationWhitelist view, but keeps it in
        // RawApplicationWhitelist. If the journal-owned entry happened to be *that* dropped raw
        // entry, the normalized view alone would make it look already-absent -- which would let
        // the cleaner report success (and the journal get deleted) while the addon-owned
        // whitelist residue is still physically present. Must fail closed instead.
        var client = new FakeHidHideClient(active: false, hidden: [], whitelist: []);
        client.UnnormalizableRawWhitelistEntries.Add(@"\Device\HarddiskVolumeUnknown\addon.exe");
        var journal = Journal(whitelist: [@"C:\addon.exe"], originalActive: null);

        Assert.False(new StartupHidHideRecoveryCleaner(client).TryClean(journal, out var reason));

        Assert.Equal(0, client.RemoveApplicationCalls);
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void InverseWhitelistDriftsAfterGlobalRestore_HiddenCleanupFailsClosed()
    {
        // The global active-state restore itself succeeds and verifies fine, but before hidden-
        // device cleanup runs, another process flips HidHide into inverse-whitelist mode. Inverse
        // whitelist is still "configuration readable", so a check that only looks at
        // IsConfigurationReadable would incorrectly let cleanup continue.
        var client = new FakeHidHideClient(active: true, hidden: ["HID\\A"], whitelist: []);
        var journal = Journal(hidden: ["HID\\A"], originalActive: false);
        var setActiveHappened = false;
        client.OnInspect = () =>
        {
            if (setActiveHappened) client.Status = HidHideInspectionStatus.InverseWhitelist;
        };

        var cleaner = new StartupHidHideRecoveryCleaner(new DriftingHidHideClient(client, () => setActiveHappened = true));
        Assert.False(cleaner.TryClean(journal, out _));
        Assert.Equal(0, client.RemoveHiddenDeviceCalls);
    }

    /// <summary>
    /// Thin decorator that flags when SetActive has run, letting the wrapped FakeHidHideClient's
    /// OnInspect hook change behavior on subsequent inspections only.
    /// </summary>
    private sealed class DriftingHidHideClient(FakeHidHideClient inner, Action onSetActive) : IHidHideClient
    {
        public HidHideInspection Inspect() => inner.Inspect();
        public bool AddApplication(string executablePath) => inner.AddApplication(executablePath);
        public bool RemoveApplication(string executablePath) => inner.RemoveApplication(executablePath);
        public bool AddHiddenDevice(string deviceEntry) => inner.AddHiddenDevice(deviceEntry);
        public bool RemoveHiddenDevice(string deviceEntry) => inner.RemoveHiddenDevice(deviceEntry);
        public bool SetActive(bool active)
        {
            var result = inner.SetActive(active);
            onSetActive();
            return result;
        }
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
        public bool SetActiveVerifiedStillActive { get; set; }
        public int InspectCallCount { get; private set; }
        public int RemoveHiddenDeviceCalls { get; private set; }
        public int RemoveApplicationCalls { get; private set; }
        public Action? OnInspect { get; set; }

        /// <summary>
        /// Extra raw whitelist entries that never appear in the normalized <c>ApplicationWhitelist</c>,
        /// modeling HidHideDriverClient.Inspect() silently dropping an entry it could not
        /// convert to a DOS path (see HidHideDriverClient.Inspect()'s try/catch around ToDosPath).
        /// </summary>
        public List<string> UnnormalizableRawWhitelistEntries { get; } = [];

        public HidHideInspection Inspect()
        {
            InspectCallCount++;
            OnInspect?.Invoke();
            var raw = CurrentWhitelist.Concat(UnnormalizableRawWhitelistEntries).ToList();
            return new(Status, new HashSet<string>(CurrentWhitelist, StringComparer.OrdinalIgnoreCase), CurrentHidden.ToList(), raw, IsActive, Status == HidHideInspectionStatus.InverseWhitelist);
        }

        public bool AddApplication(string executablePath) => true;

        public bool RemoveApplication(string executablePath)
        {
            RemoveApplicationCalls++;
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
            if (!SetActiveVerifiedStillActive) IsActive = active;
            return true;
        }
    }
}
