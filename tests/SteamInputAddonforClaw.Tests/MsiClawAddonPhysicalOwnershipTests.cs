using System.Reflection;
using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Input.DirectInput;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Work order PR5: the first real physical ownership operation. Reconciles the same physical
/// MSI Claw to PID1902, acquires verified DirectInput, and persistently hides the exact primary
/// gamepad collection -- attaching no virtual controller and never rolling PID1902 back to PID1901.</summary>
[Collection("AppLog")]
public sealed class MsiClawAddonPhysicalOwnershipTests
{
    private const string PhysKey = @"USB\VID_0DB0\SERIAL123";
    private const string OtherPhysKey = @"USB\VID_0DB0\SERIAL999";
    private const string PrimaryPnp = @"HID\VID_0DB0&PID_1902&MI_00&COL01\7&abcdef&0&0000";
    private const string OtherPrimaryPnp = @"HID\VID_0DB0&PID_1902&MI_00&COL01\7&999999&0&0000";
    private const string NonPrimaryPnp = @"HID\VID_0DB0&PID_1902&MI_03\7&abcdef&0&0003";

    // ---- 25.1 already PID1902 ----

    [Fact]
    public async Task Already_pid1902_acquires_without_a_mode_write()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.False(result.ModeWriteIssued);
        Assert.Equal(0, h.SwitchCalls);
        Assert.Equal(PrimaryPnp, result.HiddenTarget);
        Assert.Equal(new[] { PrimaryPnp }, h.HidHideApplied);
    }

    // ---- 25.2 PID1901 switches exactly once ----

    [Fact]
    public async Task Pid1901_switches_to_pid1902_exactly_once()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.XInput };
        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.True(result.ModeWriteIssued);
        Assert.Equal(1, h.SwitchCalls);
        Assert.Equal(MsiClawNativeMode.DirectInput, h.LastSwitchTarget);
    }

    // ---- 25.3 fail closed before mutation ----

    [Fact]
    public async Task Authority_no_longer_disabled_fails_before_any_mutation()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.XInput, Authority = FrontendCenterMStartupState.Enabled };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Equal(0, h.SwitchCalls);
        Assert.Empty(h.HidHideApplied);
        Assert.False(h.InputSource.StartCalled);
    }

    [Theory]
    [InlineData("DeviceNotFound")]
    [InlineData("Indeterminate")]
    public async Task Missing_or_ambiguous_native_state_fails_before_mutation(string status)
    {
        var h = new Harness { InitialCaptureStatus = Enum.Parse<NativeStateCaptureStatus>(status) };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Equal(0, h.SwitchCalls);
        Assert.Empty(h.HidHideApplied);
    }

    [Fact]
    public async Task Weak_native_identity_fails_before_mutation()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.XInput, InitialConfidence = MsiClawIdentityConfidence.Indeterminate };
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await h.Build().AcquireAsync(default)).Outcome);
        Assert.Equal(0, h.SwitchCalls);
    }

    [Fact]
    public async Task Unsupported_native_mode_fails_before_mutation()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.Other };
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await h.Build().AcquireAsync(default)).Outcome);
        Assert.Equal(0, h.SwitchCalls);
    }

    // ---- 25.4 mode transition failure ----

    [Fact]
    public async Task Mode_transition_failure_blocks_directinput_and_hidhide()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.XInput, SwitchSucceeds = false };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.True(result.ModeWriteIssued);
        Assert.False(h.InputSource.StartCalled);
        Assert.Empty(h.HidHideApplied);
    }

    // ---- 25.5 final cross-mode identity mismatch (MANDATORY) ----

    [Fact]
    public async Task Final_cross_mode_identity_mismatch_fails_with_no_directinput_no_hide_no_rollback()
    {
        var h = new Harness
        {
            InitialMode = MsiClawNativeMode.XInput,
            FinalPhysKey = OtherPhysKey, // mode write "succeeded" but a different physical MSI Claw
        };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Contains("CrossModeIdentityMismatch", result.Reason);
        Assert.Equal(1, h.SwitchCalls);
        Assert.DoesNotContain(h.SwitchTargets, t => t == MsiClawNativeMode.XInput); // no PID1901 rollback
        Assert.False(h.InputSource.StartCalled);
        Assert.Empty(h.HidHideApplied);
    }

    // ---- 26.1 DirectInput appears after a short delay ----

    [Fact]
    public async Task Directinput_appearing_after_a_bounded_delay_still_acquires()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, DirectInputMissingAttempts = 3 };
        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.True(h.DirectInputEnumerateCalls > 1);
    }

    // ---- 26.2 DirectInput never appears ----

    [Fact]
    public async Task Directinput_never_appearing_fails_with_no_hidhide()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, DirectInputMissingAttempts = int.MaxValue };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Empty(h.HidHideApplied);
        Assert.False(h.InputSource.StartCalled);
    }

    // ---- 26.3 ambiguous DirectInput fails immediately; transient unresolved identity is retried ----

    [Fact]
    public async Task Ambiguous_directinput_fails_immediately_without_retrying()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, DirectInputAmbiguous = true };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Equal(1, h.DirectInputEnumerateCalls); // MultiplePhysicalIdentities is proven-invalid
        Assert.Empty(h.HidHideApplied);
    }

    [Fact]
    public async Task Transiently_unverified_directinput_identity_is_retried_then_acquired()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, DirectInputUnverifiedAttempts = 3 };
        var result = await h.Build().AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.True(h.DirectInputEnumerateCalls > 3);
    }

    // ---- 26.4 descriptor is a different physical MSI Claw ----

    [Fact]
    public async Task Descriptor_of_a_different_physical_claw_is_not_acquired_or_hidden()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, DirectInputPnpPhysKey = OtherPhysKey };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Contains("DirectInputPhysicalIdentityMismatch", result.Reason);
        Assert.False(h.InputSource.StartCalled);
        Assert.Empty(h.HidHideApplied);
    }

    [Fact]
    public async Task Non_primary_directinput_collection_is_rejected()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, DirectInputPnp = NonPrimaryPnp };
        var result = await h.Build().AcquireAsync(default);
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Empty(h.HidHideApplied);
    }

    // ---- 26.5 / 26.6 acquire + first valid state ----

    [Fact]
    public async Task Directinput_start_failure_blocks_hidhide()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        h.InputSource.StartResult = MsiClawInputStartStatus.AcquireFailed;
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Empty(h.HidHideApplied);
    }

    [Fact]
    public async Task First_valid_state_never_arriving_stops_the_source_and_blocks_hidhide()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        h.InputSource.FirstValidState = false;
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.True(h.InputSource.StopCalled);
        Assert.False(h.InputSource.IsRunning);
        Assert.Empty(h.HidHideApplied);
    }

    // ---- 27.2 / 27.3 HidHide reconcile ----

    [Theory]
    [InlineData("Conflict")]
    [InlineData("Unavailable")]
    [InlineData("MutationFailed")]
    [InlineData("VerificationFailed")]
    public async Task HidHide_reconcile_failure_releases_directinput_without_pid_rollback(string outcome)
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.XInput, HidHideOutcome = Enum.Parse<AddonHidHideBaselineOutcome>(outcome) };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.True(h.InputSource.StopCalled);
        Assert.Equal(1, h.SwitchCalls); // one PID1901 -> PID1902, never a reverse
        Assert.DoesNotContain(h.SwitchTargets, t => t == MsiClawNativeMode.XInput);
    }

    // ---- 27.7 / owned source stays alive ----

    [Fact]
    public async Task Successful_ownership_keeps_the_input_source_alive_and_exposed()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        var owner = h.Build();
        var result = await owner.AcquireAsync(default);

        Assert.True(result.IsOwned);
        Assert.True(h.InputSource.IsRunning);
        Assert.Same(h.InputSource, owner.LiveInputSource);
    }

    // ---- 29 teardown ----

    [Fact]
    public async Task Controlled_teardown_releases_directinput_without_authority_mutation()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        var owner = h.Build();
        await owner.AcquireAsync(default);

        await owner.DisposeAsync();
        await owner.DisposeAsync(); // idempotent

        Assert.True(h.InputSource.StopCalled);
        Assert.True(h.InputSource.DisposeCalled);
        Assert.Equal(0, h.SwitchCalls); // was already 1902 -> no writes at all, none on teardown
        Assert.Empty(h.EnabledBaselineCalls);
        Assert.Null(owner.LiveInputSource);
    }

    // ---- fresh authority read at the actual mutation boundary ----

    [Fact]
    public async Task Authority_flips_before_the_mode_write_fails_closed()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.XInput, Authority = FrontendCenterMStartupState.Enabled };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Contains("AuthorityChangedBeforeModeWrite", result.Reason);
        Assert.Equal(0, h.SwitchCalls);
        Assert.Equal(1, h.AuthorityReads); // exactly one fresh read, at the boundary
    }

    [Fact]
    public async Task Authority_flips_before_directinput_acquire_on_an_already_1902_boot_fails_closed()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, Authority = FrontendCenterMStartupState.Partial };
        var result = await h.Build().AcquireAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, result.Outcome);
        Assert.Contains("AuthorityChangedBeforeDirectInputAcquire", result.Reason);
        Assert.False(h.InputSource.StartCalled);
        Assert.Empty(h.HidHideApplied);
    }

    // ---- PR5 section 16: Enable-and-Restart release seam ----

    [Fact]
    public async Task Release_for_center_m_enable_stops_directinput_then_restores_pid1901_and_returns_the_target()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        var owner = h.Build();
        var acquired = await owner.AcquireAsync(default);
        Assert.True(acquired.IsOwned);

        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(release.Succeeded);
        Assert.Equal(PrimaryPnp, release.HiddenTarget);
        Assert.True(h.InputSource.StopCalled);
        Assert.Equal(new[] { MsiClawNativeMode.XInput }, h.SwitchTargets); // PID1902 -> PID1901, once
        Assert.Null(owner.LiveInputSource);

        // A subsequent acquisition is refused -- ownership was released for the official enable path.
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.AcquireAsync(default)).Outcome);
    }

    [Fact]
    public async Task Release_when_pid1901_restore_cannot_be_verified_reports_failure()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, SwitchFailsForRelease = true };
        var owner = h.Build();
        await owner.AcquireAsync(default);

        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.False(release.Succeeded);
        Assert.Contains("Pid1901Restore", release.Reason);
        Assert.Equal(PrimaryPnp, release.HiddenTarget); // still surfaced so the caller does not lose it
        Assert.True(h.InputSource.StopCalled);
    }

    [Fact]
    public async Task Release_remembers_the_verified_target_even_when_the_persistent_apply_fails()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, HidHideOutcome = AddonHidHideBaselineOutcome.VerificationFailed };
        var owner = h.Build();
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.AcquireAsync(default)).Outcome);

        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(release.Succeeded);
        Assert.Equal(PrimaryPnp, release.HiddenTarget); // held from the verified descriptor, not the failed apply
    }

    [Fact]
    public async Task Release_recovers_a_previous_boot_target_when_this_acquisition_never_owned()
    {
        const string prior = @"HID\VID_0DB0&PID_1902&MI_00&COL01\prev";
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, DirectInputMissingAttempts = int.MaxValue, ExistingOwnedTarget = prior };
        var owner = h.Build();
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.AcquireAsync(default)).Outcome);

        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(release.Succeeded);
        Assert.Equal(prior, release.HiddenTarget); // recovered from persistent HidHide
    }

    [Fact]
    public async Task Blocked_boot_release_recovers_the_persisted_target_without_any_acquisition()
    {
        const string prior = @"HID\VID_0DB0&PID_1902&MI_00&COL01\prev";
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, ExistingOwnedTarget = prior };
        var owner = h.Build(); // AcquireAsync is never called on a Blocked boot

        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(release.Succeeded);
        Assert.Equal(prior, release.HiddenTarget);
        Assert.Equal(new[] { MsiClawNativeMode.XInput }, h.SwitchTargets); // PID1902 -> PID1901
    }

    [Fact]
    public async Task Release_fails_closed_on_an_unsupported_native_mode()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.Other, ExistingOwnedTarget = PrimaryPnp };
        var owner = h.Build();

        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.False(release.Succeeded);
        Assert.Contains("UnsupportedReleaseMode", release.Reason);
        Assert.Equal(0, h.SwitchCalls); // no PID1901 write against an unknown mode
    }

    [Fact]
    public async Task Release_without_a_prior_acquisition_and_already_stock_pid_is_a_noop_success()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.XInput };
        var owner = h.Build();

        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(release.Succeeded);
        Assert.Null(release.HiddenTarget);
        Assert.Equal(0, h.SwitchCalls);
        Assert.False(h.InputSource.StopCalled);
    }

    // ================= PR8: owned DirectInput session recovery (work order section 21) =================

    private static async Task<(MsiClawAddonPhysicalOwnership Owner, Harness Harness)> AcquiredThenLost(Harness h)
    {
        var owner = h.Build();
        Assert.True((await owner.AcquireAsync(default)).IsOwned);
        h.InputSource.SimulateSessionLoss();
        h.Recovering = true;
        h.Events.Clear();
        return (owner, h);
    }

    [Fact] // 21.1
    public async Task Recovery_of_the_same_pid1902_reacquires_the_same_source_with_no_mode_write()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.True(recovery.IsOwned);
        Assert.Equal("OwnedPhysicalInputRecovered", recovery.Reason);
        Assert.Equal(PrimaryPnp, recovery.HiddenTarget);
        Assert.Same(h.InputSource, owner.LiveInputSource);
        Assert.True(h.InputSource.IsRunning);
        Assert.Equal(2, h.InputSource.StartCallCount); // the SAME source, started again
        Assert.Equal(0, h.SwitchCalls);
    }

    [Fact] // 21.2 -- mandatory ordering
    public async Task Recovery_verifies_hidhide_before_restarting_directinput()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });

        await owner.RecoverLostInputAsync(default);

        Assert.Equal(
            new[] { "NativeCapture", "DescriptorResolve", "HidHideApply", "InputStart", "FirstValidState" },
            h.Events);
    }

    [Fact] // 21.3
    public async Task Recovery_onto_a_different_strong_identity_fails_with_no_hidhide_no_start_no_mode_write()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryPhysKey = OtherPhysKey;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("PhysicalIdentityMismatch", recovery.Reason);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.DoesNotContain("InputStart", h.Events);
        Assert.Equal(0, h.SwitchCalls);
    }

    // ---------- PR9: owned PID1901 drift reclaim (work order section 21) ----------

    /// <summary>Owned, then session lost, then the same device drifted to PID1901.</summary>
    private static async Task<(MsiClawAddonPhysicalOwnership Owner, Harness Harness)> AcquiredThenLostAsPid1901(Harness h)
    {
        var owner = h.Build();
        Assert.True((await owner.AcquireAsync(default)).IsOwned);
        h.InputSource.SimulateSessionLoss();
        h.Recovering = true;
        h.RecoveryMode = MsiClawNativeMode.XInput; // current observed mode is PID1901
        h.RecoveryModeAfterReclaim = MsiClawNativeMode.DirectInput; // a successful reclaim lands PID1902
        h.Events.Clear();
        return (owner, h);
    }

    [Fact] // 21.1
    public async Task Same_owned_pid1901_reclaims_pid1902_then_continues_the_shared_recovery_tail()
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.True(recovery.IsOwned);
        Assert.Equal("OwnedPhysicalStateDriftReclaimed", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.Equal(PrimaryPnp, recovery.HiddenTarget);
        Assert.Equal(1, h.RecoverySwitchCalls);
        Assert.Equal(MsiClawNativeMode.DirectInput, h.LastSwitchTarget);
        Assert.DoesNotContain(MsiClawNativeMode.XInput, h.SwitchTargets); // never a reverse write
        Assert.Same(h.InputSource, owner.LiveInputSource);
        Assert.True(h.InputSource.IsRunning);
        // identity proven before the write; HidHide proven before the restart
        Assert.Equal(
            new[] { "NativeCapture", "NativeCapture", "DescriptorResolve", "HidHideApply", "InputStart", "FirstValidState" },
            h.Events);
    }

    [Fact] // 21.2 -- mandatory: a different strong PID1901 identity is never switched
    public async Task Pid1901_on_a_different_strong_identity_never_receives_a_mode_write()
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryPhysKey = OtherPhysKey;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("PhysicalIdentityMismatch", recovery.Reason);
        Assert.Equal(0, h.SwitchCalls);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.DoesNotContain("InputStart", h.Events);
    }

    [Theory] // 21.3
    [InlineData("Enabled")]
    [InlineData("Partial")]
    [InlineData("Unavailable")]
    public async Task Pid1901_reclaim_is_blocked_when_center_m_is_not_exactly_disabled(string authority)
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryAuthority = Enum.Parse<FrontendCenterMStartupState>(authority);

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("AuthorityNotDisabled", recovery.Reason);
        Assert.Equal(0, h.SwitchCalls);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.DoesNotContain("InputStart", h.Events);
    }

    [Fact] // 21.4
    public async Task Pid1902_reclaim_transition_failure_fails_closed_with_no_reverse_write()
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoverySwitchSucceeds = false;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("OwnedPhysicalStateDriftReclaimFailed", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.Equal(1, h.RecoverySwitchCalls); // exactly one attempt
        Assert.DoesNotContain(MsiClawNativeMode.XInput, h.SwitchTargets);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.DoesNotContain("InputStart", h.Events);
    }

    [Theory] // 21.5 / 21.6 -- post-write verification
    [InlineData("XInput", null)]        // final mode still PID1901
    [InlineData("Other", null)]         // final mode ambiguous / unsupported
    [InlineData("DirectInput", "diff")] // final PID1902 but a different strong identity
    public async Task Pid1902_reclaim_post_write_verification_failure_fails_closed(string finalMode, string? identity)
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryModeAfterReclaim = Enum.Parse<MsiClawNativeMode>(finalMode);
        if (identity is not null) h.RecoveryPhysKeyAfterReclaim = OtherPhysKey;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.True(recovery.ModeWriteIssued);
        Assert.Equal(1, h.RecoverySwitchCalls);
        Assert.DoesNotContain(MsiClawNativeMode.XInput, h.SwitchTargets); // no rollback
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.DoesNotContain("InputStart", h.Events);
    }

    [Fact] // 21.7
    public async Task Pid1901_reclaim_then_changed_exact_target_fails_closed_without_migration()
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryPnp = OtherPrimaryPnp;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("RecoveredTargetChanged", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.Equal(new[] { PrimaryPnp }, h.HidHideApplied); // only the original acquisition
    }

    [Theory] // 21.8
    [InlineData("Conflict")]
    [InlineData("MutationFailed")]
    [InlineData("VerificationFailed")]
    public async Task Pid1901_reclaim_then_hidhide_failure_blocks_restart_without_rollback(string outcome)
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryHidHideOutcome = Enum.Parse<AddonHidHideBaselineOutcome>(outcome);

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("HidHideReconcileFailed", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.Equal(1, h.RecoverySwitchCalls);
        Assert.DoesNotContain(MsiClawNativeMode.XInput, h.SwitchTargets);
        Assert.DoesNotContain("InputStart", h.Events);
        Assert.Null(owner.LiveInputSource);
    }

    [Fact] // 21.9
    public async Task Pid1901_reclaim_then_directinput_start_failure_fails_closed_without_rollback()
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.InputSource.StartResult = MsiClawInputStartStatus.AcquireFailed;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("DirectInputStartFailed", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.DoesNotContain(MsiClawNativeMode.XInput, h.SwitchTargets);
        Assert.Null(owner.LiveInputSource);
    }

    [Fact] // 21.10
    public async Task Pid1901_reclaim_then_first_valid_state_failure_stops_the_partial_session()
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.InputSource.FirstValidState = false;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("FirstValidStateNotObserved", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.True(h.InputSource.StopCalled);
        Assert.Null(owner.LiveInputSource);
    }

    [Fact] // 21.11 -- the PR8 same-PID1902 path is unchanged
    public async Task Same_pid1902_recovery_still_issues_no_mode_write()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.True(recovery.IsOwned);
        Assert.Equal("OwnedPhysicalInputRecovered", recovery.Reason);
        Assert.False(recovery.ModeWriteIssued);
        Assert.Equal(0, h.SwitchCalls);
        Assert.Equal(
            new[] { "NativeCapture", "DescriptorResolve", "HidHideApply", "InputStart", "FirstValidState" },
            h.Events);
    }

    [Fact] // 21.12 -- explicit release still works after a post-write reclaim failure
    public async Task Explicit_release_still_works_after_a_failed_pid1901_reclaim()
    {
        var (owner, h) = await AcquiredThenLostAsPid1901(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryHidHideOutcome = AddonHidHideBaselineOutcome.Conflict; // fails after the mode write
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.RecoverLostInputAsync(default)).Outcome);

        h.Recovering = false;
        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(release.Succeeded);
        Assert.Equal(PrimaryPnp, release.HiddenTarget); // owned target evidence retained through the failure
    }

    // ---------- PR10: physical device loss / PnP return recovery (work order section 20) ----------

    [Fact] // 20.1 -- mandatory: proves _ownsInputSource no longer means "was never committed"
    public async Task Recovery_re_enters_after_an_earlier_device_not_found_failure()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryCaptureStatus = NativeStateCaptureStatus.DeviceNotFound;

        var first = await owner.RecoverLostInputAsync(default);
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, first.Outcome);
        Assert.Contains("PhysicalDeviceMissing", first.Reason);
        Assert.Null(owner.LiveInputSource);

        h.RecoveryCaptureStatus = null; // the same physical MSI Claw safely returned
        h.Events.Clear();
        var second = await owner.RecoverLostInputAsync(default);

        Assert.True(second.IsOwned);
        Assert.Equal("OwnedPhysicalInputRecovered", second.Reason);
        Assert.False(second.ModeWriteIssued);
        Assert.Same(h.InputSource, owner.LiveInputSource);
        Assert.Equal(
            new[] { "NativeCapture", "DescriptorResolve", "HidHideApply", "InputStart", "FirstValidState" },
            h.Events);
    }

    [Fact] // 20.6 -- return as PID1901 after an earlier absence still reuses the PR9 one-shot reclaim
    public async Task Recovery_re_enters_as_pid1901_after_device_not_found_and_reclaims_once()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryCaptureStatus = NativeStateCaptureStatus.DeviceNotFound;
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.RecoverLostInputAsync(default)).Outcome);

        h.RecoveryCaptureStatus = null;
        h.RecoveryMode = MsiClawNativeMode.XInput;                 // returned as PID1901
        h.RecoveryModeAfterReclaim = MsiClawNativeMode.DirectInput;
        h.Events.Clear();
        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.True(recovery.IsOwned);
        Assert.Equal("OwnedPhysicalStateDriftReclaimed", recovery.Reason);
        Assert.True(recovery.ModeWriteIssued);
        Assert.Equal(1, h.RecoverySwitchCalls);
    }

    [Fact] // 20.2 -- re-entry still refuses when ownership was never committed
    public async Task Repeated_recovery_still_refuses_when_ownership_was_never_committed()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, DirectInputMissingAttempts = int.MaxValue };
        var owner = h.Build();
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.AcquireAsync(default)).Outcome);
        h.Recovering = true;

        Assert.Equal("OwnerNotCommitted", (await owner.RecoverLostInputAsync(default)).Reason);
        Assert.Equal("OwnerNotCommitted", (await owner.RecoverLostInputAsync(default)).Reason); // still, on retry
        Assert.Equal(0, h.SwitchCalls);
        Assert.Empty(h.HidHideApplied);
    }

    [Fact] // 20.8 -- a changed exact target on return stays fail-closed, no migration
    public async Task Recovery_after_device_not_found_with_a_changed_exact_target_fails_closed()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryCaptureStatus = NativeStateCaptureStatus.DeviceNotFound;
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.RecoverLostInputAsync(default)).Outcome);

        h.RecoveryCaptureStatus = null;
        h.RecoveryPnp = OtherPrimaryPnp; // same strong identity, different exact primary collection
        h.Events.Clear();
        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("RecoveredTargetChanged", recovery.Reason);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.Equal(new[] { PrimaryPnp }, h.HidHideApplied);
    }

    [Theory] // 21.5
    [InlineData("DeviceNotFound")]
    [InlineData("Indeterminate")]
    public async Task Recovery_with_missing_or_ambiguous_native_state_makes_no_mutation(string status)
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryCaptureStatus = Enum.Parse<NativeStateCaptureStatus>(status);

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("PhysicalDeviceMissing", recovery.Reason);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.DoesNotContain("InputStart", h.Events);
        Assert.Equal(0, h.SwitchCalls);
    }

    [Fact] // 21.6
    public async Task Recovery_when_the_exact_target_changed_does_not_migrate_or_restart()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryPnp = OtherPrimaryPnp;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("RecoveredTargetChanged", recovery.Reason);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.DoesNotContain("InputStart", h.Events);
        Assert.Equal(new[] { PrimaryPnp }, h.HidHideApplied); // only the original acquisition
    }

    [Theory] // 21.7
    [InlineData("Conflict")]
    [InlineData("Unavailable")]
    [InlineData("MutationFailed")]
    [InlineData("VerificationFailed")]
    public async Task Recovery_blocked_by_hidhide_does_not_restart_directinput(string outcome)
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryHidHideOutcome = Enum.Parse<AddonHidHideBaselineOutcome>(outcome);

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("HidHideReconcileFailed", recovery.Reason);
        Assert.DoesNotContain("InputStart", h.Events);
        Assert.Null(owner.LiveInputSource);
    }

    [Fact] // 21.8
    public async Task Recovery_directinput_start_failure_leaves_output_neutral_without_pid_or_hidhide_teardown()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.InputSource.StartResult = MsiClawInputStartStatus.AcquireFailed;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("DirectInputStartFailed", recovery.Reason);
        Assert.Null(owner.LiveInputSource);
        Assert.Equal(0, h.SwitchCalls);
    }

    [Fact] // 21.9
    public async Task Recovery_first_valid_state_failure_stops_the_partial_session_and_stays_not_live()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.InputSource.FirstValidState = false;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("FirstValidStateNotObserved", recovery.Reason);
        Assert.True(h.InputSource.StopCalled);
        Assert.Null(owner.LiveInputSource);
    }

    [Fact] // 21.10
    public async Task Recovery_is_a_noop_when_the_source_is_still_running()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        var owner = h.Build();
        Assert.True((await owner.AcquireAsync(default)).IsOwned);
        h.Recovering = true;
        h.Events.Clear();

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.True(recovery.IsOwned);
        Assert.Equal("RecoveryNotNeeded", recovery.Reason);
        Assert.Empty(h.Events);
        Assert.Same(h.InputSource, owner.LiveInputSource);
    }

    [Fact] // 21.12 -- explicit release still works after a pre-write recovery failure
    public async Task Explicit_release_still_works_after_a_failed_recovery()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryPhysKey = OtherPhysKey; // identity mismatch -> fails before any mode write
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.RecoverLostInputAsync(default)).Outcome);

        h.Recovering = false;
        var release = await owner.ReleaseForCenterMEnableAsync(default);

        Assert.True(release.Succeeded);
        Assert.Equal(PrimaryPnp, release.HiddenTarget); // owned target evidence retained through the failure
    }

    [Fact] // section 10.8
    public async Task Recovery_fails_closed_when_center_m_is_no_longer_exactly_disabled()
    {
        var (owner, h) = await AcquiredThenLost(new Harness { InitialMode = MsiClawNativeMode.DirectInput });
        h.RecoveryAuthority = FrontendCenterMStartupState.Enabled;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("AuthorityNotDisabled", recovery.Reason);
        Assert.DoesNotContain("HidHideApply", h.Events);
        Assert.DoesNotContain("InputStart", h.Events);
    }

    [Fact] // section 9
    public async Task Recovery_is_refused_after_release_for_center_m_enable()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput };
        var owner = h.Build();
        await owner.AcquireAsync(default);
        await owner.ReleaseForCenterMEnableAsync(default);
        h.InputSource.SimulateSessionLoss();
        h.Recovering = true;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Contains("ReleasedForCenterMEnable", recovery.Reason);
    }

    [Fact] // section 9 -- recovery before ownership was ever committed
    public async Task Recovery_without_a_committed_acquisition_is_refused()
    {
        var h = new Harness { InitialMode = MsiClawNativeMode.DirectInput, DirectInputMissingAttempts = int.MaxValue };
        var owner = h.Build();
        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, (await owner.AcquireAsync(default)).Outcome);
        h.Recovering = true;

        var recovery = await owner.RecoverLostInputAsync(default);

        Assert.Equal(MsiClawPhysicalOwnershipOutcome.Failed, recovery.Outcome);
        Assert.Equal("OwnerNotCommitted", recovery.Reason);
    }

    [Fact] // work order sections 7 / 11 / 15 / 22 -- host wiring for the owned-input completion callback
    public void Host_wires_the_owned_input_completion_callback_and_drains_recovery_first_on_shutdown()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var host = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));

        // The one completion signal is subscribed at the existing construction seam.
        Assert.Contains("directInputInputSource.TestCompleted += OnOwnedControllerPhysicalInputCompleted;", host, StringComparison.Ordinal);
        // Expected stop and pre-commit startup completions do not schedule recovery.
        Assert.Contains("summary.StopReason == Devices.MSI.Claw.MsiClawInputStopReason.Stopped", host, StringComparison.Ordinal);
        Assert.Contains("!summary.CleanupSucceeded", host, StringComparison.Ordinal);
        // Successful recovery requests the existing PR7 reconcile -- no duplicated Steam/BPM policy.
        Assert.Contains("RequestControllerPresentationReconcile(\"PhysicalInputRecovered\")", host, StringComparison.Ordinal);
        // Shutdown drains the recovery BEFORE the presentation reconcile it may itself request.
        Assert.True(
            host.IndexOf("await _ownedControllerRecovery.ConfigureAwait(false);", StringComparison.Ordinal)
            < host.IndexOf("await _presentationReconcile.ConfigureAwait(false);", StringComparison.Ordinal),
            "owned physical recovery must be drained before the presentation reconcile");
        // No polling / timer / recovery-manager framework.
        foreach (var forbidden in new[] { "ControllerRecoveryManager", "PhysicalRecoveryManager", "RecoveryTimer", "PeriodicTimer" })
            Assert.DoesNotContain(forbidden, host, StringComparison.Ordinal);
    }

    [Fact] // PR10 sections 6-8 / 15 -- host wiring for the Device Arrival PnP-return trigger
    public void Host_wires_the_device_arrival_watcher_and_shares_one_recovery_seam()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var host = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));

        // One Runtime-owned watcher, started only once a physical owner has committed.
        Assert.Contains("new Controllers.Detection.WindowsDeviceArrivalWatcher()", host, StringComparison.Ordinal);
        Assert.Contains("watcher.DeviceArrived += OnControllerDeviceArrived;", host, StringComparison.Ordinal);
        // Both triggers funnel through one scheduling seam.
        Assert.Contains("RequestOwnedControllerRecovery(physical, \"UnexpectedDirectInputCompletion\")", host, StringComparison.Ordinal);
        Assert.Contains("RequestOwnedControllerRecovery(physical, \"DeviceArrival\")", host, StringComparison.Ordinal);
        // A live owned source ignores unrelated arrivals with no native/HidHide/DI work.
        Assert.Contains("physical.LiveInputSource is { IsRunning: true }", host, StringComparison.Ordinal);
        // review [P1]: an arrival that lands while an attempt is in flight is retained as a single
        // pending bit and consumed for exactly one follow-up once the attempt finishes.
        Assert.Contains("Interlocked.Exchange(ref _pendingOwnedControllerArrival, 1)", host, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref _pendingOwnedControllerArrival, 0) != 0", host, StringComparison.Ordinal);
        Assert.Contains("\"DeferredDeviceArrival\"", host, StringComparison.Ordinal);
        // Shutdown stops the watcher before recovery drains.
        Assert.True(
            host.IndexOf("_deviceArrivalWatcher?.Dispose();", StringComparison.Ordinal)
            < host.IndexOf("await _ownedControllerRecovery.ConfigureAwait(false);", StringComparison.Ordinal),
            "the Device Arrival watcher must be disposed before the recovery drain");
        // No PnP polling / manager.
        foreach (var forbidden in new[] { "PnPRecoveryManager", "PnpRecoveryManager", "PeriodicTimer" })
            Assert.DoesNotContain(forbidden, host, StringComparison.Ordinal);
    }

    [Fact] // work order sections 13 / 23 -- no legacy authority / recovery-framework surface
    public void Recovery_code_introduces_no_legacy_takeover_or_recovery_framework_symbols()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs"));

        Assert.Contains("RecoverLostInputAsync", source, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "ExternalNativeTakeover", "ConfirmExternalNativeTakeover", "retryCurrentSessionAfterSafeCleanup",
            "ApplyEnabledModeBaseline", "ControllerRecoveryManager", "PhysicalRecoveryManager",
            "Timer", "epoch", "generation", "RecoveryBarrier",
        })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
    }

    // ---- 30 architecture guard ----

    [Fact]
    public void Physical_owner_takes_no_route_scoped_or_virtual_dependency()
    {
        var parameterTypes = typeof(MsiClawAddonPhysicalOwnership)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single().GetParameters().Select(p => p.ParameterType.FullName ?? "");
        Assert.All(parameterTypes, name =>
        {
            Assert.DoesNotContain("MsiClawNativeModeSessionCoordinator", name);
            Assert.DoesNotContain("MsiClawPhysicalIsolationStage", name);
            Assert.DoesNotContain("RecoveryManager", name);
            Assert.DoesNotContain("Viiper", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Xbox360", name);
        });

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawAddonPhysicalOwnership.cs"));
        foreach (var forbidden in new[]
        {
            "MsiClawNativeModeSessionCoordinator", "MsiClawPhysicalIsolationStage", "RoutingPipelineSessionCoordinator",
            "BeginDeviceNativeStateMutation", "RecordHidHideDeviceAddition", "AttachXbox360", "AttachSteamDeck", "EnterXbox360PresentationAsync",
        })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);

        // The production gate: acquisition starts only for exact Disabled + admission Ready, but the
        // release seam is constructed on ANY exact Disabled boot (including a Blocked one).
        var host = File.ReadAllText(Path.Combine(dir.FullName, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));
        Assert.Contains("startupResult.CenterMStartupState != FrontendCenterMStartupState.Disabled", host, StringComparison.Ordinal);
        Assert.Contains("var owner = CreatePhysicalOwnership(startupComposition);", host, StringComparison.Ordinal);
        Assert.Contains("startupResult.DisabledBootAdmission?.IsReady != true", host, StringComparison.Ordinal);
        Assert.True(
            host.IndexOf("_physicalOwnership = owner;", StringComparison.Ordinal)
            < host.IndexOf("startupResult.DisabledBootAdmission?.IsReady != true", StringComparison.Ordinal),
            "the release seam must be assigned before the acquisition admission gate");
    }

    // ---- harness ----

    private sealed class Harness
    {
        public FrontendCenterMStartupState Authority { get; set; } = FrontendCenterMStartupState.Disabled;
        public MsiClawNativeMode InitialMode { get; set; } = MsiClawNativeMode.DirectInput;
        public NativeStateCaptureStatus InitialCaptureStatus { get; set; } = NativeStateCaptureStatus.Success;
        public MsiClawIdentityConfidence InitialConfidence { get; set; } = MsiClawIdentityConfidence.Strong;
        public string FinalPhysKey { get; set; } = PhysKey;
        public bool SwitchSucceeds { get; set; } = true;
        public int DirectInputMissingAttempts { get; set; }
        public int DirectInputUnverifiedAttempts { get; set; }
        public bool DirectInputAmbiguous { get; set; }
        public string? ExistingOwnedTarget { get; set; }
        public string DirectInputPnp { get; set; } = PrimaryPnp;
        public string DirectInputPnpPhysKey { get; set; } = PhysKey;
        public AddonHidHideBaselineOutcome HidHideOutcome { get; set; } = AddonHidHideBaselineOutcome.Success;

        // ---- PR8 recovery knobs (only consulted once Recovering is set) ----
        public bool Recovering { get; set; }
        public MsiClawNativeMode? RecoveryMode { get; set; }
        public NativeStateCaptureStatus? RecoveryCaptureStatus { get; set; }
        public string? RecoveryPhysKey { get; set; }
        public string? RecoveryPnp { get; set; }
        public AddonHidHideBaselineOutcome? RecoveryHidHideOutcome { get; set; }
        public FrontendCenterMStartupState? RecoveryAuthority { get; set; }
        // ---- PR9 PID1901 drift reclaim knobs: the "after the reclaim mode write" observed state ----
        public MsiClawNativeMode? RecoveryModeAfterReclaim { get; set; }
        public string? RecoveryPhysKeyAfterReclaim { get; set; }
        public NativeStateCaptureStatus? RecoveryCaptureStatusAfterReclaim { get; set; }
        public bool RecoverySwitchSucceeds { get; set; } = true;
        public int RecoverySwitchCalls { get; private set; }
        public List<string> Events { get; } = [];

        private MsiClawNativeMode RecoveryCurrentMode => RecoverySwitchCalls > 0
            ? RecoveryModeAfterReclaim ?? MsiClawNativeMode.DirectInput
            : RecoveryMode ?? MsiClawNativeMode.DirectInput;
        private string RecoveryCurrentPhysKey => RecoverySwitchCalls > 0
            ? RecoveryPhysKeyAfterReclaim ?? RecoveryPhysKey ?? FinalPhysKey
            : RecoveryPhysKey ?? FinalPhysKey;
        private NativeStateCaptureStatus RecoveryCurrentStatus => RecoverySwitchCalls > 0
            ? RecoveryCaptureStatusAfterReclaim ?? NativeStateCaptureStatus.Success
            : RecoveryCaptureStatus ?? NativeStateCaptureStatus.Success;

        public int SwitchCalls { get; private set; }
        public MsiClawNativeMode LastSwitchTarget { get; private set; }
        public List<MsiClawNativeMode> SwitchTargets { get; } = [];
        public int DirectInputEnumerateCalls { get; private set; }
        public List<string> HidHideApplied { get; } = [];
        public List<string> EnabledBaselineCalls { get; } = [];
        public FakeInputSource InputSource { get; } = new();

        public int AuthorityReads { get; private set; }
        public bool SwitchFailsForRelease { get; set; }

        private string EffectivePnp => Recovering && RecoveryPnp is not null ? RecoveryPnp : DirectInputPnp;

        public MsiClawAddonPhysicalOwnership Build()
        {
            InputSource.Events = Events;
            return new(
            () =>
            {
                AuthorityReads++;
                return Recovering && RecoveryAuthority is { } authority ? authority : Authority;
            },
            _ =>
            {
                if (Recovering)
                {
                    Events.Add("NativeCapture");
                    if (RecoveryCurrentStatus != NativeStateCaptureStatus.Success)
                        return Task.FromResult(new NativeStateCaptureResult(RecoveryCurrentStatus, null, RecoveryCurrentStatus.ToString()));
                    return Task.FromResult(Capture(RecoveryCurrentMode, MsiClawIdentityConfidence.Strong, RecoveryCurrentPhysKey));
                }
                return Task.FromResult(Capture(
                    SwitchCalls == 0 ? InitialMode : LastSwitchTarget,
                    SwitchCalls == 0 ? InitialConfidence : MsiClawIdentityConfidence.Strong,
                    SwitchCalls == 0 ? PhysKey : FinalPhysKey));
            },
            (target, _, _) =>
            {
                SwitchCalls++;
                if (Recovering) RecoverySwitchCalls++;
                LastSwitchTarget = target;
                SwitchTargets.Add(target);
                var ok = target == MsiClawNativeMode.XInput
                    ? !SwitchFailsForRelease
                    : Recovering ? RecoverySwitchSucceeds : SwitchSucceeds;
                return Task.FromResult(new MsiClawModeTransitionResult(
                    ok ? MsiClawModeTransitionStatus.Succeeded : MsiClawModeTransitionStatus.WriteFailed,
                    MsiClawNativeMode.XInput, target, 0x1901, 0x1902, ok, ok, ok, ok, ok, 0,
                    ok ? "ok" : "WriteFailed"));
            },
            () =>
            {
                DirectInputEnumerateCalls++;
                if (Recovering) Events.Add("DescriptorResolve");
                if (DirectInputAmbiguous)
                    return [Descriptor(Guid.NewGuid()), Descriptor(Guid.NewGuid(), physId: "OTHER")];
                if (DirectInputEnumerateCalls <= DirectInputMissingAttempts)
                    return [];
                if (DirectInputEnumerateCalls <= DirectInputMissingAttempts + DirectInputUnverifiedAttempts)
                    return [Descriptor(Guid.NewGuid(), unverified: true)]; // PID1902 present, identity not yet resolved
                return [Descriptor(Guid.NewGuid())];
            },
            instanceId => instanceId == EffectivePnp
                ? PnpDevice(EffectivePnp, Recovering ? RecoveryPhysKey ?? DirectInputPnpPhysKey : DirectInputPnpPhysKey)
                : null,
            InputSource,
            target =>
            {
                HidHideApplied.Add(target);
                if (Recovering) Events.Add("HidHideApply");
                var outcome = Recovering && RecoveryHidHideOutcome is { } recoveryOutcome ? recoveryOutcome : HidHideOutcome;
                return new AddonHidHideBaselineResult(outcome, outcome.ToString(), AddonHidHideBaselineSnapshot.Unknown);
            },
            () => ExistingOwnedTarget,
            delay: (_, _) => Task.CompletedTask,
            directInputSettleWindow: TimeSpan.FromMilliseconds(200),
            directInputSettleInterval: TimeSpan.FromMilliseconds(1));
        }

        private NativeStateCaptureResult Capture(MsiClawNativeMode mode, MsiClawIdentityConfidence confidence, string physKey)
        {
            if (!Recovering && InitialCaptureStatus != NativeStateCaptureStatus.Success)
                return new(InitialCaptureStatus, null, InitialCaptureStatus.ToString());
            if (Recovering && RecoveryCaptureStatus is { } status && status != NativeStateCaptureStatus.Success)
                return new(status, null, status.ToString());
            var payload = new MsiClawNativeStatePayload(mode, "inst", "USB\\parent", null, 0x1902, confidence, physKey);
            var snapshot = new DeviceNativeStateSnapshot(new HandheldDeviceId("msi.claw"), 1, DateTimeOffset.UtcNow,
                JsonSerializer.SerializeToElement(payload));
            return new(NativeStateCaptureStatus.Success, snapshot, "ok");
        }

        private DirectInputDeviceDescriptor Descriptor(Guid instanceGuid, string? physId = null, bool unverified = false) => new(
            instanceGuid, Guid.NewGuid(), "MSI Claw Controller", 0x0DB0, 0x1902,
            DevicePath: unverified ? null : @"\\?\hid#vid_0db0&pid_1902",
            PnpInstanceId: unverified ? null : EffectivePnp,
            PhysicalIdentity: unverified ? null : physId ?? "msi-claw-phys",
            UsagePage: 0x0001, Usage: 0x0005, ButtonCount: 17, AxisCount: 6);

        private static ControllerDeviceInfo PnpDevice(string instanceId, string physKey)
        {
            var serial = physKey[(physKey.LastIndexOf('\\') + 1)..];
            return new ControllerDeviceInfo(
                InstanceId: instanceId,
                ContainerId: null,
                ParentInstanceId: $@"USB\VID_0DB0&PID_1902&MI_00\6&xyz&0&0000",
                AncestorInstanceIds: [$@"USB\VID_0DB0&PID_1902\{serial}"],
                EnumeratorName: "HID", HardwareIds: [], CompatibleIds: [], ClassName: "HIDClass", ClassGuid: null, Service: "HidUsb",
                VendorId: 0x0DB0, ProductId: 0x1902, Present: true, FriendlyName: "MSI Claw",
                UsagePage: 0x0001, Usage: 0x0005);
        }
    }

    private sealed class FakeInputSource : IMsiClawPreparedInputSource
    {
        public SteamInputAddonforClaw.Input.ControllerState LatestState => default;
        public MsiClawInputStartStatus StartResult { get; set; } = MsiClawInputStartStatus.Started;
        public bool FirstValidState { get; set; } = true;
        public bool StartCalled { get; private set; }
        public int StartCallCount { get; private set; }
        public bool StopCalled { get; private set; }
        public bool DisposeCalled { get; private set; }
        public bool IsRunning { get; private set; }
        public List<string>? Events { get; set; }

        public event EventHandler<SteamInputAddonforClaw.Input.ControllerState>? StateChanged { add { } remove { } }

        /// <summary>Simulate an unexpected owned-session termination: the poll loop has already
        /// neutralized state and cleaned up, so the source is simply no longer running.</summary>
        public void SimulateSessionLoss() => IsRunning = false;

        public MsiClawInputStartResult StartPrepared(DirectInputDeviceDescriptor descriptor)
        {
            StartCalled = true;
            StartCallCount++;
            Events?.Add("InputStart");
            if (StartResult == MsiClawInputStartStatus.Started) IsRunning = true;
            return new MsiClawInputStartResult(StartResult, StartResult.ToString());
        }

        public Task<bool> WaitForFirstValidStateAsync(CancellationToken cancellationToken)
        {
            Events?.Add("FirstValidState");
            if (!FirstValidState) IsRunning = true; // still running, just never valid
            return Task.FromResult(FirstValidState);
        }

        public Task StopAsync()
        {
            StopCalled = true;
            IsRunning = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalled = true;
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }
}
