using System.Reflection;
using SteamInputAddonforClaw.HidHide;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Full PID1902 track / PR2: the persistent, deterministic Addon-owned HidHide baseline
/// primitive. These tests prove it independently of Steam routing sessions, PID switching,
/// DirectInput, VIIPER, Center M, and reboot -- none of which PR2 touches.</summary>
public sealed class AddonControllerHidHideBaselineTests
{
    private const string AddonExe = @"C:\Program Files\SteamInputAddonForClaw\SteamInputAddonforClaw.exe";
    private const string AddonExeMixedCase = @"c:\program files\steaminputaddonforclaw\steaminputaddonforclaw.exe";
    private const string ForeignExe = @"C:\Program Files\ClawTweaks\ClawTweaks.exe";
    private const string Pid1902Collection = @"HID\VID_0DB0&PID_1902&MI_00\7&ABCDEF&0&0000";
    private const string OtherHidden = @"HID\VID_0DB0&PID_1902&MI_03\7&ABCDEF&0&0003";

    private static AddonControllerHidHideBaseline Baseline(FakeHidHideClient client) => new(client, AddonExe);

    // ---- Inspection / admission ----

    [Fact]
    public void Inspect_not_installed_is_unavailable()
    {
        var result = Baseline(new FakeHidHideClient { Status = HidHideInspectionStatus.NotInstalled }).InspectDisabledModeBaseline([]);
        Assert.Equal(AddonHidHideBaselineOutcome.Unavailable, result.Outcome);
        Assert.False(result.IsCompliant);
    }

    [Theory]
    [InlineData("ConfigurationUnavailable")]
    [InlineData("AccessDenied")]
    public void Inspect_unreadable_configuration_is_unavailable(string status)
    {
        var client = new FakeHidHideClient { Status = Enum.Parse<HidHideInspectionStatus>(status) };
        Assert.Equal(AddonHidHideBaselineOutcome.Unavailable, Baseline(client).InspectDisabledModeBaseline([]).Outcome);
    }

    [Fact]
    public void Inspect_client_throw_is_unavailable_not_a_crash()
    {
        var result = Baseline(new FakeHidHideClient { ThrowOnInspect = true }).InspectDisabledModeBaseline([]);
        Assert.Equal(AddonHidHideBaselineOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public void Inspect_unresolved_raw_whitelist_entry_is_conflict()
    {
        var client = new FakeHidHideClient { Whitelist = [AddonExe], HasUnresolvedWhitelistEntries = true };
        var result = Baseline(client).InspectDisabledModeBaseline([]);
        Assert.Equal(AddonHidHideBaselineOutcome.Conflict, result.Outcome);
    }

    [Fact]
    public void Inspect_foreign_whitelist_entry_is_conflict()
    {
        var client = new FakeHidHideClient { Whitelist = [AddonExe, ForeignExe], Active = true };
        Assert.Equal(AddonHidHideBaselineOutcome.Conflict, Baseline(client).InspectDisabledModeBaseline([]).Outcome);
    }

    [Fact]
    public void Inspect_foreign_hidden_entry_is_conflict()
    {
        var client = new FakeHidHideClient { Whitelist = [AddonExe], Hidden = [OtherHidden], Active = true };
        Assert.Equal(AddonHidHideBaselineOutcome.Conflict, Baseline(client).InspectDisabledModeBaseline([Pid1902Collection]).Outcome);
    }

    [Fact]
    public void Inspect_exact_disabled_mode_state_is_already_compliant()
    {
        var client = new FakeHidHideClient { Whitelist = [AddonExeMixedCase], Hidden = [Pid1902Collection], Active = true, Inverse = false };
        Assert.Equal(AddonHidHideBaselineOutcome.AlreadyCompliant, Baseline(client).InspectDisabledModeBaseline([Pid1902Collection]).Outcome);
    }

    [Fact]
    public void Inspect_readable_but_not_yet_compliant_is_applicable_and_never_reports_compliant()
    {
        // Regression (PR review): a Disabled-boot admission path must not read "can be applied" as
        // "physical isolation is already proven safe".
        var client = new FakeHidHideClient { Whitelist = [], Hidden = [], Active = false };
        var result = Baseline(client).InspectDisabledModeBaseline([]);

        Assert.Equal(AddonHidHideBaselineOutcome.Applicable, result.Outcome);
        Assert.False(result.IsCompliant);
    }

    [Fact]
    public void Inspect_exact_state_reports_compliant_true()
    {
        var client = new FakeHidHideClient { Whitelist = [AddonExe], Hidden = [Pid1902Collection], Active = true };
        var result = Baseline(client).InspectDisabledModeBaseline([Pid1902Collection]);

        Assert.Equal(AddonHidHideBaselineOutcome.AlreadyCompliant, result.Outcome);
        Assert.True(result.IsCompliant);
    }

    // ---- Apply with no known PID1902 target (first Center M Disable) ----

    [Fact]
    public void Apply_with_no_target_whitelists_addon_activates_and_never_fabricates_a_target()
    {
        var client = new FakeHidHideClient { Active = false, Inverse = false };
        var result = Baseline(client).ApplyDisabledModeBaseline([]);

        Assert.Equal(AddonHidHideBaselineOutcome.Success, result.Outcome);
        Assert.Single(client.Whitelist);
        Assert.Contains(client.Whitelist, e => string.Equals(Path.GetFullPath(e), AddonExe, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(client.Hidden);
        Assert.True(client.Active);
        Assert.False(client.Inverse);
        Assert.Equal(0, result.Snapshot.HiddenTargetCount);
        Assert.Equal(0, result.Snapshot.RequestedTargetCount);
    }

    // ---- Apply with an exact PID1902 target ----

    [Fact]
    public void Apply_with_exact_target_adds_it_once_with_no_wildcard()
    {
        var client = new FakeHidHideClient { Active = false };
        var result = Baseline(client).ApplyDisabledModeBaseline([Pid1902Collection, "  " + Pid1902Collection.ToLowerInvariant() + "  "]);

        Assert.Equal(AddonHidHideBaselineOutcome.Success, result.Outcome);
        Assert.Equal([Pid1902Collection], client.Hidden);
        Assert.DoesNotContain(client.Hidden, e => e.Contains("&PID_1902\\") || e.EndsWith("PID_1902"));
        Assert.True(client.Active);
        Assert.Equal(1, result.Snapshot.HiddenTargetCount);
    }

    // ---- Idempotence ----

    [Fact]
    public void Apply_twice_leaves_one_target_and_no_churn()
    {
        var client = new FakeHidHideClient { Active = false };
        var baseline = Baseline(client);

        Assert.Equal(AddonHidHideBaselineOutcome.Success, baseline.ApplyDisabledModeBaseline([Pid1902Collection]).Outcome);
        client.MutationCalls.Clear();
        var second = baseline.ApplyDisabledModeBaseline([Pid1902Collection]);

        Assert.Equal(AddonHidHideBaselineOutcome.AlreadyCompliant, second.Outcome);
        Assert.Empty(client.MutationCalls);
        Assert.Equal([Pid1902Collection], client.Hidden);
    }

    [Fact]
    public void Apply_when_already_compliant_does_no_mutation()
    {
        var client = new FakeHidHideClient { Whitelist = [AddonExe], Hidden = [Pid1902Collection], Active = true };
        var result = Baseline(client).ApplyDisabledModeBaseline([Pid1902Collection]);

        Assert.Equal(AddonHidHideBaselineOutcome.AlreadyCompliant, result.Outcome);
        Assert.Empty(client.MutationCalls);
    }

    // ---- Inverse-mode normalization ----

    [Fact]
    public void Inspect_unsupported_inverse_mode_is_conflict()
    {
        var client = new FakeHidHideClient { Inverse = true, SupportsInverse = false };
        Assert.Equal(AddonHidHideBaselineOutcome.Conflict, Baseline(client).InspectDisabledModeBaseline([]).Outcome);
    }

    [Fact]
    public void Apply_unsupported_inverse_mode_is_conflict_and_mutates_nothing()
    {
        var client = new FakeHidHideClient { Inverse = true, Active = false, SupportsInverse = false };
        var result = Baseline(client).ApplyDisabledModeBaseline([]);

        Assert.Equal(AddonHidHideBaselineOutcome.Conflict, result.Outcome);
        Assert.Empty(client.MutationCalls);
        Assert.True(client.Inverse);
    }

    [Fact]
    public void Apply_normalizes_inverse_mode_through_the_verified_control_path()
    {
        var client = new FakeHidHideClient { Inverse = true, Active = false, SupportsInverse = true, InverseSettable = true };
        var result = Baseline(client).ApplyDisabledModeBaseline([]);

        Assert.Equal(AddonHidHideBaselineOutcome.Success, result.Outcome);
        Assert.False(client.Inverse);
        Assert.Contains("SetInverseWhitelist(false)", client.MutationCalls);
    }

    [Fact]
    public void Apply_fails_closed_when_a_supported_inverse_mutation_still_cannot_be_confirmed()
    {
        var client = new FakeHidHideClient { Inverse = true, Active = false, SupportsInverse = true, InverseSettable = false };
        var result = Baseline(client).ApplyDisabledModeBaseline([]);

        Assert.Equal(AddonHidHideBaselineOutcome.MutationFailed, result.Outcome);
        Assert.True(client.Inverse);
        Assert.DoesNotContain("SetActive(True)", client.MutationCalls); // stopped before activating
    }

    // ---- Mutation / verification failures ----

    [Fact]
    public void Apply_mutation_reporting_false_fails_closed()
    {
        var client = new FakeHidHideClient { Active = false, FailAddApplication = true };
        Assert.Equal(AddonHidHideBaselineOutcome.MutationFailed, Baseline(client).ApplyDisabledModeBaseline([]).Outcome);
    }

    [Fact]
    public void Apply_that_mutates_but_verifies_to_a_wrong_state_is_verification_failed()
    {
        var client = new FakeHidHideClient { Active = false, DropHiddenOnReinspect = true };
        var result = Baseline(client).ApplyDisabledModeBaseline([Pid1902Collection]);
        Assert.Equal(AddonHidHideBaselineOutcome.VerificationFailed, result.Outcome);
    }

    [Fact]
    public void Apply_where_verification_inspect_becomes_unavailable_fails_closed()
    {
        var client = new FakeHidHideClient { Active = false, UnavailableAfterFirstInspect = true };
        Assert.Equal(AddonHidHideBaselineOutcome.Unavailable, Baseline(client).ApplyDisabledModeBaseline([]).Outcome);
    }

    [Fact]
    public void Apply_where_a_foreign_entry_appears_during_verification_is_conflict()
    {
        var client = new FakeHidHideClient { Active = false, ForeignWhitelistOnReinspect = ForeignExe };
        Assert.Equal(AddonHidHideBaselineOutcome.Conflict, Baseline(client).ApplyDisabledModeBaseline([]).Outcome);
    }

    // ---- Clear (Enabled-mode / stock) baseline ----

    [Fact]
    public void Clear_removes_addon_isolation_and_returns_to_the_clean_stock_baseline()
    {
        var client = new FakeHidHideClient { Whitelist = [AddonExe], Hidden = [Pid1902Collection], Active = true };
        var result = Baseline(client).ApplyEnabledModeBaseline([Pid1902Collection]);

        Assert.Equal(AddonHidHideBaselineOutcome.Success, result.Outcome);
        Assert.Empty(client.Whitelist);
        Assert.Empty(client.Hidden);
        Assert.False(client.Active);
        Assert.False(client.Inverse);
    }

    [Fact]
    public void Clear_twice_is_idempotent()
    {
        var client = new FakeHidHideClient { Whitelist = [AddonExe], Hidden = [Pid1902Collection], Active = true };
        var baseline = Baseline(client);

        Assert.Equal(AddonHidHideBaselineOutcome.Success, baseline.ApplyEnabledModeBaseline([Pid1902Collection]).Outcome);
        client.MutationCalls.Clear();
        var second = baseline.ApplyEnabledModeBaseline([Pid1902Collection]);

        Assert.Equal(AddonHidHideBaselineOutcome.AlreadyCompliant, second.Outcome);
        Assert.Empty(client.MutationCalls);
    }

    [Fact]
    public void Clear_fails_closed_on_a_foreign_hidden_entry()
    {
        var client = new FakeHidHideClient { Whitelist = [AddonExe], Hidden = [Pid1902Collection, OtherHidden], Active = true };
        Assert.Equal(AddonHidHideBaselineOutcome.Conflict, Baseline(client).ApplyEnabledModeBaseline([Pid1902Collection]).Outcome);
    }

    [Fact]
    public void Clear_fails_closed_when_a_removal_cannot_be_verified()
    {
        var client = new FakeHidHideClient { Whitelist = [AddonExe], Hidden = [Pid1902Collection], Active = true, KeepHiddenOnRemove = true };
        Assert.Equal(AddonHidHideBaselineOutcome.VerificationFailed, Baseline(client).ApplyEnabledModeBaseline([Pid1902Collection]).Outcome);
    }

    // ---- Persistence / independence from routing recovery ----

    [Fact]
    public void Apply_writes_persistent_config_with_no_auto_rollback_and_no_recovery_journal_dependency()
    {
        // The primitive must not depend on RecoveryManager / RoutingRecoverySessionId: its only
        // constructor parameters are the HidHide client and the Addon executable path.
        var parameters = typeof(AddonControllerHidHideBaseline)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single().GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(IHidHideClient), parameters[0].ParameterType);
        Assert.Equal(typeof(string), parameters[1].ParameterType);

        var client = new FakeHidHideClient { Active = false };
        var baseline = Baseline(client);
        baseline.ApplyDisabledModeBaseline([Pid1902Collection]);

        // No Dispose()/rollback path exists; the state simply stays.
        Assert.True(client.Active);
        Assert.Equal([Pid1902Collection], client.Hidden);
        Assert.False(typeof(AddonControllerHidHideBaseline).GetInterfaces().Contains(typeof(IDisposable)));
    }

    [Fact]
    public void Constructor_rejects_a_non_qualified_executable_path()
        => Assert.Throws<ArgumentException>(() => new AddonControllerHidHideBaseline(new FakeHidHideClient(), "SteamInputAddonforClaw.exe"));

    // ---- PR5 section 14/28: startup admission accepting one existing Addon-owned exact target ----

    private const string PrimaryPid1902Collection = @"HID\VID_0DB0&PID_1902&MI_00&COL01\7&ABCDEF&0&0000";
    private const string OtherPrimaryPid1902Collection = @"HID\VID_0DB0&PID_1902&MI_00&COL01\9&FEDCBA&0&0000";

    private static bool IsPrimary(string target) =>
        SteamInputAddonforClaw.Devices.MSI.Claw.MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(target);

    [Fact]
    public void AllowingOwnedTarget_zero_target_first_boot_is_admitted()
    {
        var client = new FakeHidHideClient { Whitelist = [AddonExe], Hidden = [], Active = true, Inverse = false };
        Assert.Equal(AddonHidHideBaselineOutcome.AlreadyCompliant,
            Baseline(client).InspectDisabledModeBaselineAllowingExistingOwnedTarget(IsPrimary).Outcome);
    }

    [Fact]
    public void AllowingOwnedTarget_one_exact_previously_owned_primary_target_is_admitted()
    {
        var client = new FakeHidHideClient { Whitelist = [AddonExe], Hidden = [PrimaryPid1902Collection], Active = true, Inverse = false };
        var result = Baseline(client).InspectDisabledModeBaselineAllowingExistingOwnedTarget(IsPrimary);
        Assert.Equal(AddonHidHideBaselineOutcome.AlreadyCompliant, result.Outcome);
        Assert.Empty(client.MutationCalls);
    }

    [Fact]
    public void AllowingOwnedTarget_foreign_hidden_entry_still_blocks()
    {
        var client = new FakeHidHideClient { Whitelist = [AddonExe], Hidden = [OtherHidden], Active = true };
        Assert.Equal(AddonHidHideBaselineOutcome.Conflict,
            Baseline(client).InspectDisabledModeBaselineAllowingExistingOwnedTarget(IsPrimary).Outcome);
    }

    [Fact]
    public void AllowingOwnedTarget_more_than_one_hidden_target_blocks()
    {
        var client = new FakeHidHideClient { Whitelist = [AddonExe], Hidden = [PrimaryPid1902Collection, OtherPrimaryPid1902Collection], Active = true };
        Assert.Equal(AddonHidHideBaselineOutcome.Conflict,
            Baseline(client).InspectDisabledModeBaselineAllowingExistingOwnedTarget(IsPrimary).Outcome);
    }

    [Fact]
    public void AllowingOwnedTarget_non_primary_pid1902_hidden_target_blocks()
    {
        var client = new FakeHidHideClient { Whitelist = [AddonExe], Hidden = [Pid1902Collection], Active = true };
        Assert.Equal(AddonHidHideBaselineOutcome.Conflict,
            Baseline(client).InspectDisabledModeBaselineAllowingExistingOwnedTarget(IsPrimary).Outcome);
    }

    [Fact]
    public void AllowingOwnedTarget_foreign_whitelist_entry_still_blocks()
    {
        var client = new FakeHidHideClient { Whitelist = [AddonExe, ForeignExe], Hidden = [PrimaryPid1902Collection], Active = true };
        Assert.Equal(AddonHidHideBaselineOutcome.Conflict,
            Baseline(client).InspectDisabledModeBaselineAllowingExistingOwnedTarget(IsPrimary).Outcome);
    }

    private sealed class FakeHidHideClient : IHidHideClient
    {
        public HidHideInspectionStatus? Status { get; set; }
        public List<string> Whitelist { get; set; } = [];
        public List<string> Hidden { get; set; } = [];
        public bool Active { get; set; }
        public bool Inverse { get; set; }
        public bool HasUnresolvedWhitelistEntries { get; set; }
        public bool ThrowOnInspect { get; init; }
        public bool SupportsInverse { get; init; } = true;
        public bool InverseSettable { get; init; }
        public bool FailAddApplication { get; init; }
        public bool DropHiddenOnReinspect { get; init; }
        public bool KeepHiddenOnRemove { get; init; }
        public bool UnavailableAfterFirstInspect { get; init; }
        public string? ForeignWhitelistOnReinspect { get; init; }
        public List<string> MutationCalls { get; } = [];

        private int _inspectCount;

        public HidHideInspection Inspect()
        {
            if (ThrowOnInspect) throw new InvalidOperationException("boom");
            _inspectCount++;
            if (UnavailableAfterFirstInspect && _inspectCount > 1)
                return new(HidHideInspectionStatus.ConfigurationUnavailable, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (_inspectCount > 1 && DropHiddenOnReinspect) Hidden.Clear();
            if (_inspectCount > 1 && ForeignWhitelistOnReinspect is { } foreign && !Whitelist.Contains(foreign)) Whitelist.Add(foreign);

            var status = Status ?? (Inverse
                ? HidHideInspectionStatus.InverseWhitelist
                : Active ? HidHideInspectionStatus.Available : HidHideInspectionStatus.Disabled);
            return new(
                status,
                new HashSet<string>(Whitelist, StringComparer.OrdinalIgnoreCase),
                Hidden.ToList(),
                Whitelist.ToList(),
                Active,
                Inverse,
                HasUnresolvedApplicationWhitelistEntries: HasUnresolvedWhitelistEntries);
        }

        public bool AddApplication(string executablePath)
        {
            MutationCalls.Add($"AddApplication");
            if (FailAddApplication) return false;
            if (!Whitelist.Any(e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase)))
                Whitelist.Add(executablePath);
            return true;
        }

        public bool RemoveApplication(string executablePath)
        {
            MutationCalls.Add("RemoveApplication");
            Whitelist.RemoveAll(e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase));
            return true;
        }

        public bool AddHiddenDevice(string deviceEntry)
        {
            MutationCalls.Add("AddHiddenDevice");
            if (!Hidden.Contains(deviceEntry, StringComparer.OrdinalIgnoreCase)) Hidden.Add(deviceEntry);
            return true;
        }

        public bool RemoveHiddenDevice(string deviceEntry)
        {
            MutationCalls.Add("RemoveHiddenDevice");
            if (!KeepHiddenOnRemove) Hidden.RemoveAll(e => string.Equals(e, deviceEntry, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        public bool SetActive(bool active) { MutationCalls.Add($"SetActive({active})"); Active = active; return true; }

        public bool SupportsInverseWhitelistMutation => SupportsInverse;

        public bool SetInverseWhitelist(bool inverse)
        {
            MutationCalls.Add($"SetInverseWhitelist({inverse.ToString().ToLowerInvariant()})");
            if (!InverseSettable) return false;
            Inverse = inverse;
            return true;
        }
    }
}
