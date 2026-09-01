using System.Reflection;
using SteamInputAddonforClaw.HidHide;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Full PID1902 track: the persistent, deterministic Addon-owned HidHide baseline primitive.
/// PR10 addendum: while Addon Controller Mode owns the controller, the Addon owns the effective
/// HidHide isolation configuration and NORMALIZES foreign whitelist/hidden entries into one baseline
/// (official HidHideCLI + HidHideClient + Addon) rather than failing closed on them.</summary>
public sealed class AddonControllerHidHideBaselineTests
{
    private const string AddonExe = @"C:\Program Files\SteamInputAddonForClaw\SteamInputAddonforClaw.exe";
    private const string AddonExeMixedCase = @"c:\program files\steaminputaddonforclaw\steaminputaddonforclaw.exe";
    private const string ForeignExe = @"C:\Program Files\ClawTweaks\ClawTweaks.exe";
    // The resolver returns these two canonical paths; a compliant whitelist must contain EXACTLY
    // these, compared by resolved path (never by filename -- review [P1]).
    private const string OfficialCli = @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe";
    private const string OfficialClient = @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideClient.exe";
    private const string CliByName = OfficialCli;
    private const string ClientByName = OfficialClient;
    // A stale registration from an OLD HidHide install location -- same filename, different path.
    private const string StaleCli = @"C:\Old\HidHide\HidHideCLI.exe";
    private const string Pid1902Collection = @"HID\VID_0DB0&PID_1902&MI_00\7&ABCDEF&0&0000";
    private const string OtherHidden = @"HID\VID_0DB0&PID_1902&MI_03\7&ABCDEF&0&0003";

    private static readonly string[] Officials = [CliByName, ClientByName];

    private static AddonControllerHidHideBaseline Baseline(FakeHidHideClient client, bool resolveOfficials = true) =>
        new(client, AddonExe, () => resolveOfficials ? [OfficialCli, OfficialClient] : []);

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

    [Fact] // review [P1]: an unresolved raw entry is normalized (via ReplaceApplications), not a conflict
    public void Inspect_unresolved_raw_whitelist_entry_is_applicable_not_a_conflict()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName, AddonExe], HasUnresolvedWhitelistEntries = true };
        var result = Baseline(client).InspectDisabledModeBaseline([]);
        Assert.Equal(AddonHidHideBaselineOutcome.Applicable, result.Outcome);
        Assert.False(result.IsCompliant);
    }

    [Fact] // PR10 addendum: the real blocker -- CLI + Client + Addon must NOT read as a foreign conflict
    public void Inspect_official_cli_client_and_addon_is_already_compliant()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName, AddonExeMixedCase], Hidden = [Pid1902Collection], Active = true, Inverse = false };
        Assert.Equal(AddonHidHideBaselineOutcome.AlreadyCompliant, Baseline(client).InspectDisabledModeBaseline([Pid1902Collection]).Outcome);
    }

    [Fact]
    public void Inspect_foreign_whitelist_entry_is_applicable_not_a_conflict()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName, AddonExe, ForeignExe], Active = true };
        var result = Baseline(client).InspectDisabledModeBaseline([]);
        Assert.Equal(AddonHidHideBaselineOutcome.Applicable, result.Outcome);
        Assert.False(result.IsCompliant);
    }

    [Fact]
    public void Inspect_foreign_hidden_entry_is_applicable_not_a_conflict()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName, AddonExe], Hidden = [OtherHidden], Active = true };
        Assert.Equal(AddonHidHideBaselineOutcome.Applicable, Baseline(client).InspectDisabledModeBaseline([Pid1902Collection]).Outcome);
    }

    [Fact]
    public void Inspect_missing_official_registration_is_applicable_not_a_conflict()
    {
        var client = new FakeHidHideClient { Whitelist = [ClientByName, AddonExe], Hidden = [Pid1902Collection], Active = true };
        Assert.Equal(AddonHidHideBaselineOutcome.Applicable, Baseline(client).InspectDisabledModeBaseline([Pid1902Collection]).Outcome);
    }

    [Fact]
    public void Inspect_readable_but_not_yet_compliant_is_applicable_and_never_reports_compliant()
    {
        var client = new FakeHidHideClient { Whitelist = [], Hidden = [], Active = false };
        var result = Baseline(client).InspectDisabledModeBaseline([]);

        Assert.Equal(AddonHidHideBaselineOutcome.Applicable, result.Outcome);
        Assert.False(result.IsCompliant);
    }

    // ---- Apply with no known PID1902 target (first Center M Disable) ----

    [Fact]
    public void Apply_with_no_target_normalizes_to_cli_client_addon_and_activates()
    {
        var client = new FakeHidHideClient { Active = false, Inverse = false };
        var result = Baseline(client).ApplyDisabledModeBaseline([]);

        Assert.Equal(AddonHidHideBaselineOutcome.Success, result.Outcome);
        Assert.Equal(3, client.Whitelist.Count);
        Assert.Contains(client.Whitelist, e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(OfficialCli), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(client.Whitelist, e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(OfficialClient), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(client.Whitelist, e => string.Equals(Path.GetFullPath(e), AddonExe, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(client.Hidden);
        Assert.True(client.Active);
        Assert.False(client.Inverse);
    }

    [Fact] // PR10 addendum sections 5-6: foreign entries are removed, not treated as a conflict
    public void Apply_removes_foreign_whitelist_and_hidden_entries_and_repairs_missing_officials()
    {
        var client = new FakeHidHideClient
        {
            Whitelist = [ForeignExe, ClientByName], // foreign present, official CLI missing, Addon missing
            Hidden = [OtherHidden],
            Active = false,
        };
        var result = Baseline(client).ApplyDisabledModeBaseline([Pid1902Collection]);

        Assert.Equal(AddonHidHideBaselineOutcome.Success, result.Outcome);
        Assert.DoesNotContain(client.Whitelist, e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(ForeignExe), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(client.Whitelist, e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(OfficialCli), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(client.Whitelist, e => string.Equals(Path.GetFullPath(e), AddonExe, StringComparison.OrdinalIgnoreCase));
        Assert.Equal([Pid1902Collection], client.Hidden);
        Assert.True(client.Active);
    }

    [Fact] // review [P1]: a stale registration at an OLD install path is removed; the canonical one is added
    public void Apply_removes_a_stale_same_named_official_entry_and_adds_the_resolved_canonical_path()
    {
        var client = new FakeHidHideClient { Whitelist = [StaleCli, OfficialClient, AddonExe], Hidden = [Pid1902Collection], Active = true };
        var result = Baseline(client).ApplyDisabledModeBaseline([Pid1902Collection]);

        Assert.Equal(AddonHidHideBaselineOutcome.Success, result.Outcome);
        Assert.DoesNotContain(client.Whitelist, e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(StaleCli), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(client.Whitelist, e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(OfficialCli), StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // section 4.1: a missing official whose canonical path cannot be resolved is a prerequisite gap
    public void Apply_when_a_required_official_path_cannot_be_resolved_is_unavailable()
    {
        var client = new FakeHidHideClient { Whitelist = [ClientByName, AddonExe], Active = false };
        var result = Baseline(client, resolveOfficials: false).ApplyDisabledModeBaseline([]);

        Assert.Equal(AddonHidHideBaselineOutcome.Unavailable, result.Outcome);
        Assert.Contains("OfficialHidHidePathUnresolved", result.Reason);
    }

    // ---- Apply with an exact PID1902 target ----

    [Fact]
    public void Apply_with_exact_target_adds_it_once_with_no_wildcard()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName], Active = false };
        var result = Baseline(client).ApplyDisabledModeBaseline([Pid1902Collection, "  " + Pid1902Collection.ToLowerInvariant() + "  "]);

        Assert.Equal(AddonHidHideBaselineOutcome.Success, result.Outcome);
        Assert.Equal([Pid1902Collection], client.Hidden);
        Assert.True(client.Active);
        Assert.Equal(1, result.Snapshot.HiddenTargetCount);
    }

    // ---- Idempotence ----

    [Fact]
    public void Apply_twice_leaves_one_target_and_no_churn()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName], Active = false };
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
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName, AddonExe], Hidden = [Pid1902Collection], Active = true };
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
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName], Inverse = true, Active = false, SupportsInverse = true, InverseSettable = true };
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
        Assert.DoesNotContain("SetActive(True)", client.MutationCalls);
    }

    // ---- Mutation / verification failures ----

    [Fact]
    public void Apply_mutation_reporting_false_fails_closed()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName], Active = false, FailAddApplication = true };
        Assert.Equal(AddonHidHideBaselineOutcome.MutationFailed, Baseline(client).ApplyDisabledModeBaseline([]).Outcome);
    }

    [Fact]
    public void Apply_foreign_removal_reporting_false_fails_closed()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName, AddonExe, ForeignExe], Active = true, FailRemoveApplication = true };
        Assert.Equal(AddonHidHideBaselineOutcome.MutationFailed, Baseline(client).ApplyDisabledModeBaseline([]).Outcome);
    }

    [Fact]
    public void Apply_that_mutates_but_verifies_to_a_wrong_state_is_verification_failed()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName], Active = false, DropHiddenOnReinspect = true };
        Assert.Equal(AddonHidHideBaselineOutcome.VerificationFailed, Baseline(client).ApplyDisabledModeBaseline([Pid1902Collection]).Outcome);
    }

    [Fact]
    public void Apply_where_verification_inspect_becomes_unavailable_fails_closed()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName], Active = false, UnavailableAfterFirstInspect = true };
        Assert.Equal(AddonHidHideBaselineOutcome.Unavailable, Baseline(client).ApplyDisabledModeBaseline([]).Outcome);
    }

    [Fact] // review [P1]: an unresolved entry surviving verification is a read-back mismatch, not a conflict
    public void Apply_where_an_unresolved_entry_appears_during_verification_fails_closed()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName], Active = false, UnresolvedOnReinspect = true };
        Assert.Equal(AddonHidHideBaselineOutcome.VerificationFailed, Baseline(client).ApplyDisabledModeBaseline([]).Outcome);
    }

    [Fact] // review [P1]: an unresolved raw entry is normalized via the exact-replace path and succeeds
    public void Apply_normalizes_an_unresolved_raw_whitelist_entry_via_replace_applications()
    {
        var client = new FakeHidHideClient
        {
            Whitelist = ["\\Device\\HarddiskVolume3\\Old\\stale-uninstalled.exe"],
            HasUnresolvedWhitelistEntries = true,
            Active = false,
        };
        var result = Baseline(client).ApplyDisabledModeBaseline([Pid1902Collection]);

        Assert.Equal(AddonHidHideBaselineOutcome.Success, result.Outcome);
        Assert.Equal(1, client.ReplaceApplicationsCalls);
        Assert.Equal(0, client.MutationCalls.Count(call => call is "AddApplication" or "RemoveApplication"));
        Assert.Equal(3, client.Whitelist.Count);
        Assert.Contains(client.Whitelist, e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(OfficialCli), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(client.Whitelist, e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(OfficialClient), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(client.Whitelist, e => string.Equals(Path.GetFullPath(e), AddonExe, StringComparison.OrdinalIgnoreCase));
        Assert.Equal([Pid1902Collection], client.Hidden);
    }

    [Fact] // review [P1]: a failed exact-replace fails closed
    public void Apply_fails_closed_when_replace_applications_reports_failure()
    {
        var client = new FakeHidHideClient
        {
            Whitelist = ["\\Device\\HarddiskVolume3\\Old\\stale-uninstalled.exe"],
            HasUnresolvedWhitelistEntries = true,
            Active = false,
            FailReplaceApplications = true,
        };
        var result = Baseline(client).ApplyDisabledModeBaseline([]);

        Assert.Equal(AddonHidHideBaselineOutcome.MutationFailed, result.Outcome);
        Assert.Equal(1, client.ReplaceApplicationsCalls);
    }

    // ---- Clear (Enabled-mode / release) baseline ----

    [Fact]
    public void Clear_removes_addon_isolation_and_preserves_the_official_applications()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName, AddonExe], Hidden = [Pid1902Collection], Active = true };
        var result = Baseline(client).ApplyEnabledModeBaseline([Pid1902Collection]);

        Assert.Equal(AddonHidHideBaselineOutcome.Success, result.Outcome);
        Assert.DoesNotContain(client.Whitelist, e => string.Equals(Path.GetFullPath(e), AddonExe, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(client.Whitelist, e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(OfficialCli), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(client.Whitelist, e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(OfficialClient), StringComparison.OrdinalIgnoreCase));
        Assert.Empty(client.Hidden);
        Assert.False(client.Active);
        Assert.False(client.Inverse);
    }

    [Fact]
    public void Clear_twice_is_idempotent()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName, AddonExe], Hidden = [Pid1902Collection], Active = true };
        var baseline = Baseline(client);

        Assert.Equal(AddonHidHideBaselineOutcome.Success, baseline.ApplyEnabledModeBaseline([Pid1902Collection]).Outcome);
        client.MutationCalls.Clear();
        var second = baseline.ApplyEnabledModeBaseline([Pid1902Collection]);

        Assert.Equal(AddonHidHideBaselineOutcome.AlreadyCompliant, second.Outcome);
        Assert.Empty(client.MutationCalls);
    }

    [Fact] // section 12: a foreign entry must not make returning to Center M permanently impossible
    public void Clear_succeeds_even_when_a_foreign_entry_is_present()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName, AddonExe, ForeignExe], Hidden = [Pid1902Collection, OtherHidden], Active = true };
        var result = Baseline(client).ApplyEnabledModeBaseline([Pid1902Collection]);

        Assert.Equal(AddonHidHideBaselineOutcome.Success, result.Outcome);
        Assert.Contains(client.Whitelist, e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(ForeignExe), StringComparison.OrdinalIgnoreCase)); // left untouched
        Assert.Contains(OtherHidden, client.Hidden); // foreign hidden left untouched
        Assert.DoesNotContain(client.Hidden, e => string.Equals(e, Pid1902Collection, StringComparison.OrdinalIgnoreCase)); // Addon target removed
        Assert.False(client.Active);
    }

    [Fact]
    public void Clear_fails_closed_when_a_removal_cannot_be_verified()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName, AddonExe], Hidden = [Pid1902Collection], Active = true, KeepHiddenOnRemove = true };
        Assert.Equal(AddonHidHideBaselineOutcome.VerificationFailed, Baseline(client).ApplyEnabledModeBaseline([Pid1902Collection]).Outcome);
    }

    [Fact] // review [P1]: an unresolved third-party entry must not trap the user in Addon authority
    public void Clear_succeeds_with_an_unresolved_third_party_whitelist_entry_present()
    {
        var client = new FakeHidHideClient
        {
            Whitelist = [CliByName, ClientByName, AddonExe],
            Hidden = [Pid1902Collection],
            Active = true,
            HasUnresolvedWhitelistEntries = true, // a stale third-party entry that can no longer be converted
        };
        var result = Baseline(client).ApplyEnabledModeBaseline([Pid1902Collection]);

        Assert.Equal(AddonHidHideBaselineOutcome.Success, result.Outcome);
        Assert.DoesNotContain(client.Whitelist, e => string.Equals(Path.GetFullPath(e), AddonExe, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(client.Hidden, e => string.Equals(e, Pid1902Collection, StringComparison.OrdinalIgnoreCase));
        Assert.False(client.Active);
    }

    // ---- Persistence / independence from routing recovery ----

    [Fact]
    public void Apply_writes_persistent_config_with_no_auto_rollback_and_no_recovery_journal_dependency()
    {
        var parameters = typeof(AddonControllerHidHideBaseline)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single().GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(IHidHideClient), parameters[0].ParameterType);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
        Assert.Equal(typeof(Func<IReadOnlyList<string>>), parameters[2].ParameterType);
        Assert.True(parameters[2].IsOptional);

        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName], Active = false };
        Baseline(client).ApplyDisabledModeBaseline([Pid1902Collection]);

        Assert.True(client.Active);
        Assert.Equal([Pid1902Collection], client.Hidden);
        Assert.False(typeof(AddonControllerHidHideBaseline).GetInterfaces().Contains(typeof(IDisposable)));
    }

    [Fact]
    public void Constructor_rejects_a_non_qualified_executable_path()
        => Assert.Throws<ArgumentException>(() => new AddonControllerHidHideBaseline(new FakeHidHideClient(), "SteamInputAddonforClaw.exe"));

    // ---- startup admission accepting one existing Addon-owned exact target ----

    private const string PrimaryPid1902Collection = @"HID\VID_0DB0&PID_1902&MI_00&COL01\7&ABCDEF&0&0000";
    private const string OtherPrimaryPid1902Collection = @"HID\VID_0DB0&PID_1902&MI_00&COL01\9&FEDCBA&0&0000";

    private static bool IsPrimary(string target) =>
        SteamInputAddonforClaw.Devices.MSI.Claw.MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(target);

    [Fact]
    public void AllowingOwnedTarget_zero_target_first_boot_is_admitted()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName, AddonExe], Hidden = [], Active = true, Inverse = false };
        Assert.Equal(AddonHidHideBaselineOutcome.AlreadyCompliant,
            Baseline(client).InspectDisabledModeBaselineAllowingExistingOwnedTarget(IsPrimary).Outcome);
    }

    [Fact]
    public void AllowingOwnedTarget_one_exact_previously_owned_primary_target_is_admitted()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName, AddonExe], Hidden = [PrimaryPid1902Collection], Active = true, Inverse = false };
        var result = Baseline(client).InspectDisabledModeBaselineAllowingExistingOwnedTarget(IsPrimary);
        Assert.Equal(AddonHidHideBaselineOutcome.AlreadyCompliant, result.Outcome);
        Assert.Empty(client.MutationCalls);
    }

    [Fact]
    public void AllowingOwnedTarget_foreign_hidden_entry_is_applicable_not_compliant()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName, AddonExe], Hidden = [OtherHidden], Active = true };
        Assert.Equal(AddonHidHideBaselineOutcome.Applicable,
            Baseline(client).InspectDisabledModeBaselineAllowingExistingOwnedTarget(IsPrimary).Outcome);
    }

    [Fact]
    public void TryGetSingleExistingOwnedTarget_returns_the_one_compliant_owned_primary_target()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName, AddonExe], Hidden = [PrimaryPid1902Collection], Active = true, Inverse = false };
        Assert.Equal(PrimaryPid1902Collection, Baseline(client).TryGetSingleExistingOwnedTarget(IsPrimary));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TryGetSingleExistingOwnedTarget_returns_null_unless_exactly_one(int count)
    {
        var hidden = count switch
        {
            0 => new List<string>(),
            2 => [PrimaryPid1902Collection, OtherPrimaryPid1902Collection],
            _ => [PrimaryPid1902Collection],
        };
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName, AddonExe], Hidden = hidden, Active = true };
        Assert.Null(Baseline(client).TryGetSingleExistingOwnedTarget(IsPrimary));
    }

    [Fact]
    public void TryGetSingleExistingOwnedTarget_returns_null_for_a_foreign_or_non_primary_entry()
    {
        var foreign = new FakeHidHideClient { Whitelist = [CliByName, ClientByName, AddonExe], Hidden = [OtherHidden], Active = true };
        Assert.Null(Baseline(foreign).TryGetSingleExistingOwnedTarget(IsPrimary));
        var nonPrimary = new FakeHidHideClient { Whitelist = [CliByName, ClientByName, AddonExe], Hidden = [Pid1902Collection], Active = true };
        Assert.Null(Baseline(nonPrimary).TryGetSingleExistingOwnedTarget(IsPrimary));
    }

    [Fact]
    public void TryGetSingleExistingOwnedTarget_returns_null_when_the_baseline_is_not_compliant()
    {
        var client = new FakeHidHideClient { Whitelist = [CliByName, ClientByName, AddonExe], Hidden = [PrimaryPid1902Collection], Active = false };
        Assert.Null(Baseline(client).TryGetSingleExistingOwnedTarget(IsPrimary));
    }

    [Fact] // review [P1]: keep the one persisted owned target even when an unrelated hidden entry coexists
    public void NormalizingExistingOwnedTarget_retains_the_owned_target_and_wipes_unrelated_entries()
    {
        var client = new FakeHidHideClient
        {
            Whitelist = [CliByName, ClientByName, AddonExe],
            Hidden = [PrimaryPid1902Collection, OtherHidden], // owned primary + unrelated foreign
            Active = true,
        };
        var result = Baseline(client).ApplyDisabledModeBaselineNormalizingExistingOwnedTarget(IsPrimary);

        Assert.Equal(AddonHidHideBaselineOutcome.Success, result.Outcome);
        Assert.Equal([PrimaryPid1902Collection], client.Hidden);
    }

    [Fact] // two owned-primary candidates is ambiguous -> fall back to the zero-target baseline
    public void NormalizingExistingOwnedTarget_with_two_owned_candidates_wipes_to_zero_targets()
    {
        var client = new FakeHidHideClient
        {
            Whitelist = [CliByName, ClientByName, AddonExe],
            Hidden = [PrimaryPid1902Collection, OtherPrimaryPid1902Collection],
            Active = true,
        };
        var result = Baseline(client).ApplyDisabledModeBaselineNormalizingExistingOwnedTarget(IsPrimary);

        Assert.Equal(AddonHidHideBaselineOutcome.Success, result.Outcome);
        Assert.Empty(client.Hidden);
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
        public bool FailRemoveApplication { get; init; }
        public bool DropHiddenOnReinspect { get; init; }
        public bool KeepHiddenOnRemove { get; init; }
        public bool UnavailableAfterFirstInspect { get; init; }
        public bool UnresolvedOnReinspect { get; init; }
        public bool FailReplaceApplications { get; init; }
        public int ReplaceApplicationsCalls { get; private set; }
        public List<string> MutationCalls { get; } = [];

        private int _inspectCount;

        public HidHideInspection Inspect()
        {
            if (ThrowOnInspect) throw new InvalidOperationException("boom");
            _inspectCount++;
            if (UnavailableAfterFirstInspect && _inspectCount > 1)
                return new(HidHideInspectionStatus.ConfigurationUnavailable, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (_inspectCount > 1 && DropHiddenOnReinspect) Hidden.Clear();

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
                HasUnresolvedApplicationWhitelistEntries: HasUnresolvedWhitelistEntries || (UnresolvedOnReinspect && _inspectCount > 1));
        }

        public bool ReplaceApplications(IReadOnlyCollection<string> executablePaths)
        {
            ReplaceApplicationsCalls++;
            if (FailReplaceApplications) return false;
            Whitelist = executablePaths.ToList();
            HasUnresolvedWhitelistEntries = false; // the raw stale entry no longer exists after an exact replace
            return true;
        }

        public bool AddApplication(string executablePath)
        {
            MutationCalls.Add("AddApplication");
            if (FailAddApplication) return false;
            if (!Whitelist.Any(e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase)))
                Whitelist.Add(executablePath);
            return true;
        }

        public bool RemoveApplication(string executablePath)
        {
            MutationCalls.Add("RemoveApplication");
            if (FailRemoveApplication) return false;
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
