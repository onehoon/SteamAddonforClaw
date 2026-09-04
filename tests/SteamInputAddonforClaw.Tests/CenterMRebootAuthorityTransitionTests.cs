using System.Reflection;
using SteamInputAddonforClaw.CenterMStartup;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Hosting;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Work order PR3: the first real reboot-bound MSI Center M controller-authority
/// transition. It composes the already-merged PR2 HidHide baseline, the PR2.5 mandatory-startup
/// coordinator, and the PR1 Center M startup control into one ordered flow that ends with an
/// immediate Windows restart. It performs NO live same-session controller takeover -- no PID switch,
/// no DirectInput, no VIIPER -- and it does not start <c>shutdown.exe</c> anywhere but the single
/// injected restart seam.</summary>
[Collection("AppLog")]
public sealed class CenterMRebootAuthorityTransitionTests : IDisposable
{
    private const string AddonExe = @"C:\Program Files\SteamInputAddonForClaw\SteamInputAddonforClaw.exe";
    private const string OfficialCli = @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe";
    private const string OfficialClient = @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideClient.exe";
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    public CenterMRebootAuthorityTransitionTests()
        => SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _testDirectory;

    public void Dispose()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = null;
        if (Directory.Exists(_testDirectory)) Directory.Delete(_testDirectory, recursive: true);
    }

    // ---- Disable and Restart ----

    [Fact]
    public async Task Disable_happy_path_runs_the_exact_order_and_then_restarts()
    {
        var h = new Harness(this) { StartEnabled = true };
        var restart = new FakeRestart { Result = WindowsRestartRequestResult.Requested };
        var transition = h.Build(restart: restart);

        var result = await transition.RequestAsync(centerMEnabled: false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Succeeded, result.Outcome);
        Assert.Equal(FrontendCenterMStartupState.Disabled, result.Snapshot.State);
        Assert.Equal(1, restart.Calls);
        // startup registration proven BEFORE Center M is touched, HidHide baseline BEFORE Center M.
        Assert.Equal(new[] { "startup:true", "hidhide:disable", "centerm:false", "restart" }, h.Order);
        // No live takeover primitives.
        Assert.True(h.Hid.Active);
        Assert.Empty(h.Hid.Hidden); // zero-target baseline; no fabricated PID1902 target
    }

    [Fact]
    public async Task Disable_is_unavailable_when_center_m_control_is_unavailable()
    {
        var h = new Harness(this) { CenterMAvailable = false };
        var result = await h.Build().RequestAsync(centerMEnabled: false, CancellationToken.None);
        Assert.Equal(FrontendCenterMStartupMutationOutcome.Unavailable, result.Outcome);
        Assert.DoesNotContain("centerm:false", h.Order);
    }

    [Fact]
    public async Task Disable_is_blocked_while_a_lower_level_runtime_operation_owns_the_controller()
    {
        var h = new Harness(this)
        {
            StartEnabled = true,
            Safety = new UserTerminationDecision(false, UserTerminationBlockReason.RoutingTransition),
        };
        var result = await h.Build().RequestAsync(centerMEnabled: false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.Empty(h.Order); // nothing mutated
    }

    [Fact]
    public async Task Disable_stops_before_center_m_when_startup_registration_fails()
    {
        var h = new Harness(this) { StartEnabled = true, StartupSucceeds = false };
        var result = await h.Build().RequestAsync(centerMEnabled: false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.Equal(new[] { "startup:true" }, h.Order);
    }

    [Fact]
    public async Task Disable_stops_before_center_m_when_the_hidhide_baseline_cannot_be_applied()
    {
        var h = new Harness(this) { StartEnabled = true };
        h.Hid.FailAddApplication = true;
        var result = await h.Build().RequestAsync(centerMEnabled: false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.Equal(new[] { "startup:true", "hidhide:disable" }, h.Order);
    }

    [Fact] // PR10 addendum: a foreign whitelist entry is normalized away, not a conflict...
    public async Task Disable_normalizes_a_foreign_whitelist_entry_instead_of_blocking()
    {
        var h = new Harness(this) { StartEnabled = true };
        h.Hid.Whitelist.Add(@"C:\Program Files\ClawTweaks\ClawTweaks.exe");
        h.Hid.Active = true;
        var result = await h.Build().RequestAsync(centerMEnabled: false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Succeeded, result.Outcome);
        Assert.DoesNotContain(h.Hid.Whitelist, e => e.Contains("ClawTweaks", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // ...but a state that cannot be normalized through the verified control path still blocks
    public async Task Disable_normalizes_an_unresolved_hidhide_whitelist_entry_via_exact_replace()
    {
        var h = new Harness(this) { StartEnabled = true };
        h.Hid.HasUnresolvedWhitelist = true;
        h.Hid.Active = true;
        var result = await h.Build().RequestAsync(centerMEnabled: false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Succeeded, result.Outcome);
        Assert.False(h.Hid.HasUnresolvedWhitelist);
    }

    [Fact] // review [P1]: unresolved is no longer a pre-emptive admission conflict -- a client with no
           // real exact-replace path fails closed at the normalization mutation instead.
    public async Task Disable_fails_closed_when_the_unresolved_entry_cannot_be_replaced()
    {
        var h = new Harness(this) { StartEnabled = true };
        h.Hid.HasUnresolvedWhitelist = true;
        h.Hid.Active = true;
        h.Hid.FailReplaceApplications = true;
        var result = await h.Build().RequestAsync(centerMEnabled: false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.DoesNotContain("centerm:false", h.Order);
    }

    [Fact]
    public async Task Disable_returns_the_center_m_mutation_result_when_the_privileged_write_fails()
    {
        var h = new Harness(this) { StartEnabled = true, CenterMHelperCompletes = false };
        var restart = new FakeRestart();
        var result = await h.Build(restart: restart).RequestAsync(centerMEnabled: false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.Equal(0, restart.Calls);
        Assert.DoesNotContain("restart", h.Order);
    }

    [Fact]
    public async Task Disable_that_verifies_but_cannot_start_the_restart_is_failed_with_no_rollback()
    {
        var h = new Harness(this) { StartEnabled = true };
        var restart = new FakeRestart { Result = WindowsRestartRequestResult.Failed };
        var result = await h.Build(restart: restart).RequestAsync(centerMEnabled: false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.Equal(FrontendCenterMStartupState.Disabled, result.Snapshot.State);
        Assert.Equal(1, restart.Calls);
        // verified persistent state is deliberately left in place
        Assert.Equal(FrontendCenterMStartupState.Disabled, h.Roots.Classify());
        Assert.True(h.Hid.Active);
    }

    [Fact]
    public async Task Disable_before_any_mutation_honors_a_cancelled_token()
    {
        var h = new Harness(this) { StartEnabled = true };
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await h.Build().RequestAsync(centerMEnabled: false, cts.Token);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Cancelled, result.Outcome);
        Assert.Empty(h.Order);
    }

    [Fact]
    public async Task A_second_overlapping_request_is_rejected_while_one_is_in_flight()
    {
        var h = new Harness(this) { StartEnabled = true };
        var gate = new TaskCompletionSource();
        h.BeforeCenterMMutation = () => gate.Task;
        var transition = h.Build();

        var first = transition.RequestAsync(centerMEnabled: false, CancellationToken.None);
        var second = await transition.RequestAsync(centerMEnabled: false, CancellationToken.None);
        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, second.Outcome);

        gate.SetResult();
        Assert.Equal(FrontendCenterMStartupMutationOutcome.Succeeded, (await first).Outcome);
    }

    [Fact]
    public async Task Disable_stops_before_any_mutation_when_a_controller_prerequisite_is_known_not_ready()
    {
        // A first-time / incomplete install can have HidHide available while USBIP2 or libVIIPER is
        // missing. Committing the next boot to Addon authority then would be a broken handheld.
        var h = new Harness(this) { StartEnabled = true, PrerequisitesReady = false };
        var restart = new FakeRestart();

        var result = await h.Build(restart: restart).RequestAsync(centerMEnabled: false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.Empty(h.Order);            // no startup:true, no HidHide mutation, no Center M mutation
        Assert.Equal(0, restart.Calls);
        Assert.False(h.Hid.Active);
    }

    [Fact]
    public async Task Disable_stops_before_any_mutation_when_controller_recovery_is_not_verified_safe()
    {
        // StartupCoordinator can bring the Runtime up with RecoverySafe=false when stale route-scoped
        // recovery could not be retired. Establishing the persistent PR2 baseline then would let the
        // next-boot cleaner undo it.
        var h = new Harness(this) { StartEnabled = true, RecoverySafe = false };
        var restart = new FakeRestart();

        var result = await h.Build(restart: restart).RequestAsync(centerMEnabled: false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.Empty(h.Order);
        Assert.Equal(0, restart.Calls);
        Assert.False(h.Hid.Active);
    }

    [Fact]
    public async Task Disable_helper_cancel_after_preparation_keeps_the_prepared_state_and_never_says_nothing_changed()
    {
        var h = new Harness(this) { StartEnabled = true, CenterMHelperCancels = true };

        var result = await h.Build().RequestAsync(centerMEnabled: false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Cancelled, result.Outcome);
        // The ordered persistent preparation ran and is deliberately left in place (no rollback).
        Assert.Equal(new[] { "startup:true", "hidhide:disable", "centerm:false" }, h.Order);
        Assert.True(h.Hid.Active);
        Assert.DoesNotContain("restart", h.Order);
        Assert.NotNull(result.FailureMessage);
        Assert.DoesNotContain("nothing", result.FailureMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remain in place", result.FailureMessage!, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Enable and Restart ----

    [Fact]
    public async Task Enable_happy_path_clears_the_baseline_then_restarts()
    {
        var h = new Harness(this) { StartEnabled = false };
        h.Hid.Whitelist.Add(AddonExe);
        h.Hid.Active = true;
        var restart = new FakeRestart { Result = WindowsRestartRequestResult.Requested };

        var result = await h.Build(restart: restart).RequestAsync(centerMEnabled: true, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Succeeded, result.Outcome);
        Assert.Equal(FrontendCenterMStartupState.Enabled, result.Snapshot.State);
        Assert.Equal(new[] { "physical-release", "stock-baseline", "hidhide:enable", "centerm:true", "restart" }, h.Order);
        Assert.False(h.Hid.Active);
        Assert.Empty(h.Hid.Whitelist);
    }

    [Fact]
    public async Task Enable_after_pr5_ownership_releases_then_clears_the_exact_persisted_target()
    {
        const string ownedTarget = @"HID\VID_0DB0&PID_1902&MI_00&COL01\7&abcdef&0&0000";
        var h = new Harness(this)
        {
            StartEnabled = false,
            PhysicalRelease = new SteamInputAddonforClaw.Devices.MSI.Claw.PhysicalOwnershipReleaseResult(true, "Released", ownedTarget),
        };
        h.Hid.Whitelist.Add(AddonExe);
        h.Hid.Hidden.Add(ownedTarget);
        h.Hid.Active = true;

        var result = await h.Build().RequestAsync(centerMEnabled: true, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Succeeded, result.Outcome);
        // physical release (DI stop + PID1901 restore) runs before HidHide is cleared.
        Assert.Equal(new[] { "physical-release", "stock-baseline", "hidhide:enable", "centerm:true", "restart" }, h.Order);
        Assert.DoesNotContain(ownedTarget, h.Hid.Hidden); // the exact PR5 target was cleared, not treated as foreign
        Assert.Empty(h.Hid.Whitelist);
        Assert.False(h.Hid.Active);
    }

    [Fact]
    public async Task Enable_fails_when_physical_ownership_cannot_be_released()
    {
        var h = new Harness(this)
        {
            StartEnabled = false,
            PhysicalRelease = new SteamInputAddonforClaw.Devices.MSI.Claw.PhysicalOwnershipReleaseResult(false, "Pid1901RestoreUnverified", "target"),
        };
        h.Hid.Whitelist.Add(AddonExe);
        h.Hid.Active = true;

        var result = await h.Build().RequestAsync(centerMEnabled: true, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.Equal(new[] { "physical-release" }, h.Order); // stopped before HidHide clear
        Assert.Contains(AddonExe, h.Hid.Whitelist); // untouched
    }

    [Fact]
    public async Task Enable_stops_before_center_m_when_the_baseline_cannot_be_cleared()
    {
        const string ownedTarget = @"HID\VID_0DB0&PID_1902&MI_00&COL01\7&abcdef&0&0000";
        var h = new Harness(this)
        {
            StartEnabled = false,
            PhysicalRelease = new SteamInputAddonforClaw.Devices.MSI.Claw.PhysicalOwnershipReleaseResult(true, "Released", ownedTarget),
        };
        h.Hid.Whitelist.Add(AddonExe);
        h.Hid.Active = true;
        h.Hid.KeepHiddenOnRemove = true; // the owned target removal cannot be verified -> not compliant
        h.Hid.Hidden.Add(ownedTarget);
        var result = await h.Build().RequestAsync(centerMEnabled: true, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.DoesNotContain("centerm:true", h.Order);
    }

    [Fact]
    public async Task Enable_cancellation_after_the_release_boundary_still_completes()
    {
        const string ownedTarget = @"HID\VID_0DB0&PID_1902&MI_00&COL01\7&abcdef&0&0000";
        using var cts = new CancellationTokenSource();
        var h = new Harness(this)
        {
            StartEnabled = false,
            PhysicalRelease = new SteamInputAddonforClaw.Devices.MSI.Claw.PhysicalOwnershipReleaseResult(true, "Released", ownedTarget),
            OnPhysicalRelease = () => cts.Cancel(), // frontend pipe drops mid-release
        };
        h.Hid.Whitelist.Add(AddonExe);
        h.Hid.Hidden.Add(ownedTarget);
        h.Hid.Active = true;

        var result = await h.Build().RequestAsync(centerMEnabled: true, cts.Token);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Succeeded, result.Outcome);
        Assert.Equal(new[] { "physical-release", "stock-baseline", "hidhide:enable", "centerm:true", "restart" }, h.Order);
    }

    [Fact]
    public async Task Enable_cancellation_before_the_release_returns_cancelled_with_no_mutation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var h = new Harness(this) { StartEnabled = false };
        h.Hid.Whitelist.Add(AddonExe);
        h.Hid.Active = true;

        var result = await h.Build().RequestAsync(centerMEnabled: true, cts.Token);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Cancelled, result.Outcome);
        Assert.Empty(h.Order);
    }

    [Fact]
    public async Task Enable_is_blocked_while_a_lower_level_runtime_operation_owns_the_controller()
    {
        var h = new Harness(this)
        {
            StartEnabled = false,
            Safety = new UserTerminationDecision(false, UserTerminationBlockReason.RoutingTransition),
        };
        var result = await h.Build().RequestAsync(centerMEnabled: true, CancellationToken.None);
        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.Empty(h.Order);
    }

    [Fact]
    public async Task Enable_helper_cancel_after_baseline_clear_reports_the_cleared_state_not_nothing_changed()
    {
        var h = new Harness(this) { StartEnabled = false, CenterMHelperCancels = true };
        h.Hid.Whitelist.Add(AddonExe);
        h.Hid.Active = true;

        var result = await h.Build().RequestAsync(centerMEnabled: true, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Cancelled, result.Outcome);
        Assert.Equal(new[] { "physical-release", "stock-baseline", "hidhide:enable", "centerm:true" }, h.Order);
        Assert.False(h.Hid.Active); // baseline was already cleared
        Assert.NotNull(result.FailureMessage);
        Assert.DoesNotContain("nothing", result.FailureMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cleared", result.FailureMessage!, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Architecture guards ----

    [Fact]
    public void Transition_owner_has_no_pid_directinput_or_viiper_dependency()
    {
        var parameterTypes = typeof(CenterMRebootAuthorityTransition)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single().GetParameters().Select(p => p.ParameterType.FullName ?? "").ToArray();

        Assert.All(parameterTypes, name =>
        {
            Assert.DoesNotContain("DirectInput", name);
            Assert.DoesNotContain("Viiper", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Pid1901", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Pid1902", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Routing", name);
        });
    }

    [Fact]
    public void Production_restart_seam_uses_a_plain_shutdown_r_with_no_force_flag()
    {
        var root = TestPaths.RepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src/SteamInputAddonforClaw/CenterMStartup/CenterMRebootAuthorityTransition.cs"));
        Assert.Contains("\"shutdown.exe\", \"/r /t 0\"", source);
        Assert.DoesNotContain("/r /f", source);
        Assert.DoesNotContain("/f /t", source);
        // A started process is not an accepted restart: the seam must verify the command result.
        Assert.Contains("WaitForExit", source);
        Assert.Contains("ExitCode", source);
    }

    // ================= PR12: stock-safe uninstall preparation (work order section 22) =================

    [Fact] // 22.1 -- Disabled + active Full1902 ownership happy path: strict order, NO restart.
    public async Task Prepare_for_uninstall_disabled_happy_path_runs_the_exact_order()
    {
        var h = new Harness(this)
        {
            PhysicalRelease = new(true, "Released", @"HID\VID_0DB0&PID_1902&MI_00&COL01\owned"),
        };
        h.Hid.Whitelist.Add(AddonExe);
        h.Hid.Hidden.Add(@"HID\VID_0DB0&PID_1902&MI_00&COL01\owned");
        h.Hid.Active = true;

        var result = await h.Build().PrepareForUninstallAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("UninstallPrepared", result.Reason);
        Assert.Equal(new[] { "physical-release", "stock-baseline", "hidhide:enable", "centerm:true", "startup-remove" }, h.Order);
        Assert.DoesNotContain("restart", h.Order);
        Assert.Equal(FrontendCenterMStartupState.Enabled, h.Roots.Classify());
        Assert.False(h.Hid.Active);
        Assert.DoesNotContain(AddonExe, h.Hid.Whitelist);
    }

    [Fact] // 22.2 / 22.3 -- presentation/physical release failure stops everything downstream.
    public async Task Prepare_for_uninstall_stops_when_physical_release_fails()
    {
        var h = new Harness(this) { PhysicalRelease = new(false, "VirtualPresentationReleaseFailed", null) };

        var result = await h.Build().PrepareForUninstallAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(new[] { "physical-release" }, h.Order);
        Assert.Equal(0, h.StockBaselineCalls);
        Assert.Equal(0, h.StartupRemovalCalls);
        Assert.NotEqual(FrontendCenterMStartupState.Enabled, h.Roots.Classify());
    }

    [Fact] // 22.4 -- NothingOwned but current PID1902: NOT accepted as stock; the stock baseline must switch.
    public async Task Prepare_for_uninstall_does_not_treat_nothing_owned_as_stock_safe()
    {
        var h = new Harness(this)
        {
            PhysicalRelease = SteamInputAddonforClaw.Devices.MSI.Claw.PhysicalOwnershipReleaseResult.NothingOwned,
            StockBaselineSucceeds = true,
            StockBaselineModeWrite = true, // the stock baseline had to switch PID1902 -> PID1901
            StockBaselineReason = "XInputVerified",
        };

        var result = await h.Build().PrepareForUninstallAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, h.StockBaselineCalls); // stock proof always runs, even with no owner object
        Assert.Contains("stock-baseline", h.Order);
    }

    [Fact] // 22.5 -- NothingOwned + already PID1901: idempotent proceed.
    public async Task Prepare_for_uninstall_when_already_stock_pid1901_proceeds_idempotently()
    {
        var h = new Harness(this)
        {
            PhysicalRelease = SteamInputAddonforClaw.Devices.MSI.Claw.PhysicalOwnershipReleaseResult.NothingOwned,
            StockBaselineModeWrite = false,
            StockBaselineReason = "AlreadyXInput",
        };

        Assert.True((await h.Build().PrepareForUninstallAsync(CancellationToken.None)).Succeeded);
    }

    [Fact] // 22.6 -- stock baseline failure blocks HidHide clear / Center M enable / startup removal.
    public async Task Prepare_for_uninstall_stops_when_the_stock_baseline_cannot_be_proven()
    {
        var h = new Harness(this) { StockBaselineSucceeds = false, StockBaselineReason = "CurrentMsiStateUnavailable" };

        var result = await h.Build().PrepareForUninstallAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("StockBaseline", result.Reason);
        Assert.Equal(new[] { "physical-release", "stock-baseline" }, h.Order);
        Assert.Equal(0, h.StartupRemovalCalls);
    }

    [Fact] // 22.7 -- persisted exact HidHide target fallback when no owner returns one.
    public async Task Prepare_for_uninstall_uses_the_safely_persisted_owned_target_for_hidhide_release()
    {
        const string persisted = @"HID\VID_0DB0&PID_1902&MI_00&COL01\persisted";
        var h = new Harness(this)
        {
            PhysicalRelease = SteamInputAddonforClaw.Devices.MSI.Claw.PhysicalOwnershipReleaseResult.NothingOwned, // HiddenTarget null
            PersistedOwnedTarget = persisted,
        };
        h.Hid.Whitelist.Add(AddonExe);
        h.Hid.Hidden.Add(persisted);
        h.Hid.Active = true;

        var result = await h.Build().PrepareForUninstallAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(persisted, h.Hid.Hidden); // the exact persisted Addon target was removed
    }

    [Fact] // 22.8 -- HidHide release failure blocks Center M enable and startup removal.
    public async Task Prepare_for_uninstall_stops_when_hidhide_release_cannot_be_verified()
    {
        const string owned = @"HID\VID_0DB0&PID_1902&MI_00&COL01\owned";
        var h = new Harness(this) { PhysicalRelease = new(true, "Released", owned) };
        h.Hid.Whitelist.Add(AddonExe);
        h.Hid.Hidden.Add(owned);
        h.Hid.Active = true;
        h.Hid.KeepHiddenOnRemove = true; // the owned target removal cannot be verified

        var result = await h.Build().PrepareForUninstallAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("HidHideRelease", result.Reason);
        Assert.DoesNotContain("centerm:true", h.Order);
        Assert.Equal(0, h.StartupRemovalCalls);
    }

    [Fact] // 22.9 -- Center M Enable failure: startup task stays, result fails.
    public async Task Prepare_for_uninstall_stops_when_center_m_cannot_be_enabled()
    {
        var h = new Harness(this) { CenterMHelperCompletes = false };

        var result = await h.Build().PrepareForUninstallAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("centerm:true", h.Order);
        Assert.Equal(0, h.StartupRemovalCalls);
    }

    [Fact] // 22.10 -- startup-task removal failure AFTER stock restore: fail, but never re-enter Addon authority.
    public async Task Prepare_for_uninstall_startup_task_removal_failure_does_not_reverse_stock_authority()
    {
        var h = new Harness(this)
        {
            PhysicalRelease = new(true, "Released", @"HID\VID_0DB0&PID_1902&MI_00&COL01\owned"),
            StartupTaskRemovalSucceeds = false,
        };
        h.Hid.Whitelist.Add(AddonExe);
        h.Hid.Hidden.Add(@"HID\VID_0DB0&PID_1902&MI_00&COL01\owned");
        h.Hid.Active = true;

        var result = await h.Build().PrepareForUninstallAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("StartupTaskRemoval", result.Reason);
        // Stock authority already restored -- and NOT reversed by the failed startup-task removal.
        Assert.Equal(FrontendCenterMStartupState.Enabled, h.Roots.Classify());
        Assert.False(h.Hid.Active);
        Assert.Equal(new[] { "physical-release", "stock-baseline", "hidhide:enable", "centerm:true", "startup-remove" }, h.Order);
    }

    [Fact] // 22.11 -- Center M already Enabled: still independently proves stock, no PID1902 acquisition.
    public async Task Prepare_for_uninstall_when_center_m_already_enabled_still_proves_stock()
    {
        var h = new Harness(this)
        {
            StartEnabled = true,
            PhysicalRelease = new(true, "Released", @"HID\VID_0DB0&PID_1902&MI_00&COL01\owned"),
        };
        h.Hid.Whitelist.Add(AddonExe);
        h.Hid.Hidden.Add(@"HID\VID_0DB0&PID_1902&MI_00&COL01\owned");
        h.Hid.Active = true;

        var result = await h.Build().PrepareForUninstallAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, h.StockBaselineCalls);
        Assert.Equal(new[] { "physical-release", "stock-baseline", "hidhide:enable", "centerm:true", "startup-remove" }, h.Order);
        Assert.Equal(FrontendCenterMStartupState.Enabled, h.Roots.Classify());
    }

    [Theory] // 22.12 -- Partial / Unavailable root truth fails closed with NO mutation.
    [InlineData("partial")]
    [InlineData("unavailable")]
    public async Task Prepare_for_uninstall_fails_closed_on_ambiguous_center_m_authority(string kind)
    {
        var h = kind == "partial"
            ? new Harness(this) { StartPartial = true }
            : new Harness(this) { CenterMAvailable = false };

        var result = await h.Build().PrepareForUninstallAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("CenterMAuthorityAmbiguous", result.Reason);
        Assert.Empty(h.Order);
    }

    [Fact] // 22.12 -- a lower-level routing/native operation in progress blocks preparation.
    public async Task Prepare_for_uninstall_is_blocked_while_a_lower_level_operation_owns_the_controller()
    {
        var h = new Harness(this) { Safety = new UserTerminationDecision(false, UserTerminationBlockReason.RoutingTransition) };

        var result = await h.Build().PrepareForUninstallAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(h.Order);
    }

    // ---- Full1902 Policy B: stock-authority-restored boundary (Win+G suppression release) ----

    [Fact]
    public async Task Stock_authority_restored_callback_fires_once_only_at_the_verified_success_boundary()
    {
        var h = new Harness(this) { StartEnabled = false };
        h.Hid.Whitelist.Add(AddonExe);
        h.Hid.Active = true;

        var result = await h.Build().RequestAsync(centerMEnabled: true, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, h.StockAuthorityRestoredCalls);
        // After physical-release + stock-baseline + hidhide:enable + centerm:true, BEFORE restart.
        Assert.Equal(4, h.StockAuthorityRestoredAtOrderIndex);
    }

    [Theory]
    [InlineData("physical")]  // physical PID1901 release failed
    [InlineData("stock")]     // independent current-world PID1901 proof failed
    [InlineData("hidhide")]   // Enabled-mode HidHide release could not be verified
    [InlineData("centerm")]   // Center M root enable / read-back failed
    public async Task Stock_authority_restored_callback_does_not_fire_when_any_fail_closed_step_fails(string failAt)
    {
        var h = new Harness(this)
        {
            StartEnabled = false,
            PhysicalRelease = failAt == "physical"
                ? new SteamInputAddonforClaw.Devices.MSI.Claw.PhysicalOwnershipReleaseResult(false, "DirectInputStopFailed", null)
                : new SteamInputAddonforClaw.Devices.MSI.Claw.PhysicalOwnershipReleaseResult(true, "Released", @"HID\VID_0DB0&PID_1902&MI_00&COL01\owned"),
            StockBaselineSucceeds = failAt != "stock",
            CenterMHelperCompletes = failAt != "centerm",
        };
        h.Hid.Whitelist.Add(AddonExe);
        h.Hid.Hidden.Add(@"HID\VID_0DB0&PID_1902&MI_00&COL01\owned");
        h.Hid.Active = true;
        if (failAt == "hidhide") h.Hid.KeepHiddenOnRemove = true;

        var result = await h.Build().RequestAsync(centerMEnabled: true, CancellationToken.None);

        Assert.NotEqual(FrontendCenterMStartupMutationOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, h.StockAuthorityRestoredCalls); // suppression stays owned
    }

    [Fact] // NothingOwned is Succeeded=true, but the independent stock proof is still required.
    public async Task Stock_authority_restored_callback_does_not_fire_for_nothing_owned_without_stock_proof()
    {
        var h = new Harness(this)
        {
            StartEnabled = false,
            PhysicalRelease = SteamInputAddonforClaw.Devices.MSI.Claw.PhysicalOwnershipReleaseResult.NothingOwned,
            StockBaselineSucceeds = false,
            StockBaselineReason = "CurrentMsiStateUnavailable",
        };

        var result = await h.Build().RequestAsync(centerMEnabled: true, CancellationToken.None);

        Assert.NotEqual(FrontendCenterMStartupMutationOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, h.StockAuthorityRestoredCalls);
    }

    [Fact] // Uninstall preparation shares the same core -> same boundary callback.
    public async Task Stock_authority_restored_callback_fires_for_uninstall_preparation_too()
    {
        var h = new Harness(this)
        {
            PhysicalRelease = new(true, "Released", @"HID\VID_0DB0&PID_1902&MI_00&COL01\owned"),
        };
        h.Hid.Whitelist.Add(AddonExe);
        h.Hid.Hidden.Add(@"HID\VID_0DB0&PID_1902&MI_00&COL01\owned");
        h.Hid.Active = true;

        var result = await h.Build().PrepareForUninstallAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, h.StockAuthorityRestoredCalls);
        Assert.Equal(4, h.StockAuthorityRestoredAtOrderIndex); // before startup-remove
    }

    // ---- Harness ----

    private sealed class Harness
    {
        private readonly CenterMRebootAuthorityTransitionTests _owner;
        public Harness(CenterMRebootAuthorityTransitionTests owner) => _owner = owner;

        public bool CenterMAvailable { get; init; } = true;
        public bool StartEnabled { get; init; }
        public bool StartPartial { get; init; }
        public bool StartupSucceeds { get; init; } = true;
        public bool CenterMHelperCompletes { get; init; } = true;
        public bool CenterMHelperCancels { get; init; }
        public bool PrerequisitesReady { get; init; } = true;
        public bool RecoverySafe { get; init; } = true;
        public SteamInputAddonforClaw.Devices.MSI.Claw.PhysicalOwnershipReleaseResult PhysicalRelease { get; init; } =
            SteamInputAddonforClaw.Devices.MSI.Claw.PhysicalOwnershipReleaseResult.NothingOwned;
        public Action? OnPhysicalRelease { get; set; }
        public UserTerminationDecision Safety { get; init; } = new(true, UserTerminationBlockReason.None);
        public Func<Task>? BeforeCenterMMutation { get; set; }
        // ---- PR12 stock-restoration knobs ----
        public bool StockBaselineSucceeds { get; init; } = true;
        public bool StockBaselineModeWrite { get; init; }
        public string StockBaselineReason { get; init; } = "AlreadyXInput";
        public string? PersistedOwnedTarget { get; init; }
        public bool StartupTaskRemovalSucceeds { get; init; } = true;
        public int StockBaselineCalls { get; private set; }
        public int StartupRemovalCalls { get; private set; }
        public int StockAuthorityRestoredCalls { get; private set; }
        /// <summary>Order.Count at the moment the stock-authority-restored callback fired -- i.e. how
        /// many ordered steps had already completed. Proves it runs only at the verified boundary.</summary>
        public int StockAuthorityRestoredAtOrderIndex { get; private set; } = -1;

        public List<string> Order { get; } = [];
        public FakeHid Hid { get; } = new();
        public Roots Roots { get; } = new();

        public CenterMRebootAuthorityTransition Build(FakeRestart? restart = null)
        {
            Roots.Server = StartPartial || StartEnabled;
            Roots.Updater = !StartPartial && StartEnabled;
            Roots.Service = StartEnabled ? CenterMFoundationServiceMode.Automatic : CenterMFoundationServiceMode.Disabled;

            var reader = new CenterMStartupStateReader(
                name => name == CenterMStartupStateReader.ServerTaskName ? Roots.Server
                    : name == CenterMStartupStateReader.UpdaterTaskName ? Roots.Updater
                    : null,
                () => Roots.Service);
            var invoker = new FakeInvoker(this);
            var centerM = new CenterMStartupControl(CenterMAvailable, reader, invoker);

            var store = new SettingsStore(Path.Combine(_owner._testDirectory, "settings.json"));
            var coordinator = new StartupSettingsCoordinator(new AppSettings(), store,
                new FakeStartupManager(Order, () => StartupSucceeds), isLaunchAtWindowsStartupRequired: () => true);

            Hid.OrderSink = Order;
            var baseline = new AddonControllerHidHideBaseline(Hid, AddonExe, () => [OfficialCli, OfficialClient]);

            static PrerequisiteAssessment Ready(PrerequisiteKind kind) => new(kind, PrerequisiteStatus.Ready, "ready");
            var prerequisites = PrerequisitesReady
                ? new RuntimePrerequisiteAssessment(Ready(PrerequisiteKind.HidHide), Ready(PrerequisiteKind.UsbIpWin2), Ready(PrerequisiteKind.Viiper))
                : new RuntimePrerequisiteAssessment(Ready(PrerequisiteKind.HidHide),
                    new PrerequisiteAssessment(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Missing, "not installed"),
                    Ready(PrerequisiteKind.Viiper));

            var r = restart ?? new FakeRestart { Result = WindowsRestartRequestResult.Requested };
            r.Order = Order;
            return new CenterMRebootAuthorityTransition(
                centerM, coordinator, baseline,
                () => Safety,
                _ => Task.FromResult((prerequisites, RecoverySafe)),
                token =>
                {
                    // PR5 review: EnableAsync must pass CancellationToken.None past the mutation
                    // boundary so a frontend disconnect cannot abort a confirmed transition.
                    Assert.False(token.CanBeCanceled);
                    Order.Add("physical-release");
                    OnPhysicalRelease?.Invoke();
                    return Task.FromResult(PhysicalRelease);
                },
                // PR12: independent stock-baseline PID1901 proof.
                token =>
                {
                    Assert.False(token.CanBeCanceled);
                    StockBaselineCalls++;
                    Order.Add("stock-baseline");
                    return Task.FromResult(new SteamInputAddonforClaw.Startup.StockCenterMStartupBaselineResult(
                        StockBaselineSucceeds, StockBaselineModeWrite, StockBaselineReason));
                },
                () => PersistedOwnedTarget,
                () =>
                {
                    StartupRemovalCalls++;
                    Order.Add("startup-remove");
                    return StartupTaskRemovalSucceeds ? StartupRegistrationResult.Disabled() : StartupRegistrationResult.Failed();
                },
                // Full1902 Policy B: the verified stock-authority-restored boundary callback. Counted
                // rather than added to Order so the existing ordering assertions stay unchanged.
                () => { StockAuthorityRestoredCalls++; StockAuthorityRestoredAtOrderIndex = Order.Count; },
                r);
        }

        private sealed class FakeInvoker(Harness h) : ICenterMStartupHelperInvoker
        {
            public async Task<CenterMStartupHelperResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
            {
                if (h.BeforeCenterMMutation is { } wait) await wait().ConfigureAwait(false);
                h.Order.Add($"centerm:{enabled.ToString().ToLowerInvariant()}");
                if (h.CenterMHelperCancels)
                    return new(CenterMStartupHelperOutcome.Cancelled, false, false, false, false, h.Roots.Service, null);
                if (!h.CenterMHelperCompletes)
                    return new(CenterMStartupHelperOutcome.HelperUnavailable, false, false, false, false, h.Roots.Service, "helper failed");
                h.Roots.Server = h.Roots.Updater = enabled;
                h.Roots.Service = enabled ? CenterMFoundationServiceMode.Automatic : CenterMFoundationServiceMode.Disabled;
                return new(CenterMStartupHelperOutcome.Completed, true, true, h.Roots.Server, h.Roots.Updater, h.Roots.Service, null);
            }
        }
    }

    private sealed class Roots
    {
        public bool Server;
        public bool Updater;
        public CenterMFoundationServiceMode Service;
        public FrontendCenterMStartupState Classify() => CenterMStartupControl.Classify(Server, Updater, Service);
    }

    private sealed class FakeStartupManager(List<string> order, Func<bool> ok) : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled)
        {
            order.Add($"startup:{enabled.ToString().ToLowerInvariant()}");
            return ok() ? StartupRegistrationResult.Enabled() : StartupRegistrationResult.Failed();
        }
    }

    private sealed class FakeRestart : IWindowsRestartRequester
    {
        public WindowsRestartRequestResult Result { get; set; } = WindowsRestartRequestResult.Requested;
        public List<string>? Order { get; set; }
        public int Calls { get; private set; }

        public WindowsRestartRequestResult RequestRestart()
        {
            Calls++;
            if (Result == WindowsRestartRequestResult.Requested) Order?.Add("restart");
            return Result;
        }
    }

    private sealed class FakeHid : IHidHideClient
    {
        public List<string> Whitelist { get; } = [];
        public List<string> Hidden { get; } = [];
        public bool Active { get; set; }
        public bool Inverse { get; set; }
        public bool FailAddApplication { get; set; }
        public bool KeepHiddenOnRemove { get; set; }
        public bool HasUnresolvedWhitelist { get; set; }
        public bool FailReplaceApplications { get; set; }

        public HidHideInspection Inspect() => new(
            Inverse ? HidHideInspectionStatus.InverseWhitelist
                : Active ? HidHideInspectionStatus.Available : HidHideInspectionStatus.Disabled,
            new HashSet<string>(Whitelist, StringComparer.OrdinalIgnoreCase),
            Hidden.ToList(), Whitelist.ToList(), Active, Inverse, HasUnresolvedApplicationWhitelistEntries: HasUnresolvedWhitelist);

        public bool ReplaceApplications(IReadOnlyCollection<string> executablePaths)
        {
            RecordOnce("hidhide:disable");
            if (FailReplaceApplications) return false;
            Whitelist.Clear();
            Whitelist.AddRange(executablePaths);
            HasUnresolvedWhitelist = false;
            return true;
        }

        public bool AddApplication(string executablePath)
        {
            RecordOnce("hidhide:disable");
            if (FailAddApplication) return false;
            if (!Whitelist.Any(e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase)))
                Whitelist.Add(executablePath);
            return true;
        }

        public bool RemoveApplication(string executablePath)
        {
            RecordOnce("hidhide:enable");
            Whitelist.RemoveAll(e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase));
            return true;
        }

        public bool AddHiddenDevice(string deviceEntry)
        {
            RecordOnce("hidhide:disable");
            if (!Hidden.Contains(deviceEntry, StringComparer.OrdinalIgnoreCase)) Hidden.Add(deviceEntry);
            return true;
        }

        public bool RemoveHiddenDevice(string deviceEntry)
        {
            RecordOnce("hidhide:enable");
            if (!KeepHiddenOnRemove) Hidden.RemoveAll(e => string.Equals(e, deviceEntry, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        public bool SetActive(bool active) { RecordOnce(active ? "hidhide:disable" : "hidhide:enable"); Active = active; return true; }
        public bool SupportsInverseWhitelistMutation => true;
        public bool SetInverseWhitelist(bool inverse) { Inverse = inverse; return true; }

        // The harness injects the shared order list through a field set in Build().
        public List<string>? OrderSink { get; set; }
        private void RecordOnce(string tag)
        {
            if (OrderSink is { } sink && (sink.Count == 0 || sink[^1] != tag)) sink.Add(tag);
        }
    }
}

internal static class TestPaths
{
    public static string RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "SteamInputAddonforClaw.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
