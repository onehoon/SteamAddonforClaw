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
    public async Task Disable_is_blocked_by_a_conflicting_controller_environment()
    {
        var h = new Harness(this) { StartEnabled = true, ConflictingEnvironment = true };
        var result = await h.Build().RequestAsync(centerMEnabled: false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.Empty(h.Order);
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

    [Fact]
    public async Task Disable_is_blocked_up_front_by_a_hidhide_conflict()
    {
        var h = new Harness(this) { StartEnabled = true };
        h.Hid.Whitelist.Add(@"C:\Program Files\ClawTweaks\ClawTweaks.exe");
        h.Hid.Active = true;
        var result = await h.Build().RequestAsync(centerMEnabled: false, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.Empty(h.Order);
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
        Assert.Equal(new[] { "physical-release", "hidhide:enable", "centerm:true", "restart" }, h.Order);
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
        Assert.Equal(new[] { "physical-release", "hidhide:enable", "centerm:true", "restart" }, h.Order);
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
    public async Task Enable_is_not_blocked_by_a_conflicting_controller_environment()
    {
        var h = new Harness(this) { StartEnabled = false, ConflictingEnvironment = true };
        var result = await h.Build().RequestAsync(centerMEnabled: true, CancellationToken.None);
        Assert.Equal(FrontendCenterMStartupMutationOutcome.Succeeded, result.Outcome);
    }

    [Fact]
    public async Task Enable_stops_before_center_m_when_the_baseline_cannot_be_cleared()
    {
        var h = new Harness(this) { StartEnabled = false };
        h.Hid.Whitelist.Add(AddonExe);
        h.Hid.Active = true;
        h.Hid.KeepHiddenOnRemove = true; // removal cannot be verified -> not compliant
        h.Hid.Hidden.Add(@"HID\VID_0DB0&PID_1902&MI_00\7&ABCDEF&0&0000");
        var result = await h.Build().RequestAsync(centerMEnabled: true, CancellationToken.None);

        Assert.Equal(FrontendCenterMStartupMutationOutcome.Failed, result.Outcome);
        Assert.DoesNotContain("centerm:true", h.Order);
    }

    [Fact]
    public async Task Enable_is_blocked_while_a_lower_level_runtime_operation_owns_the_controller()
    {
        var h = new Harness(this)
        {
            StartEnabled = false,
            Safety = new UserTerminationDecision(false, UserTerminationBlockReason.NativeModeActive),
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
        Assert.Equal(new[] { "physical-release", "hidhide:enable", "centerm:true" }, h.Order);
        Assert.False(h.Hid.Active); // baseline was already cleared
        Assert.NotNull(result.FailureMessage);
        Assert.DoesNotContain("nothing", result.FailureMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cleared", result.FailureMessage!, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Third-party controller-manager admission (Disable entry gate, fail-closed) ----

    [Fact]
    public void Admission_allows_entry_only_when_the_detector_positively_proves_no_manager()
        => Assert.False(AddonProcessHost.IsConflictingControllerEnvironment(
            new StubEnvironmentProvider(ControllerManagerKind.None)));

    [Theory]
    [InlineData("ClawTweaks")]
    [InlineData("HandheldCompanion")]
    [InlineData("Winhanced")]
    [InlineData("Multiple")]
    [InlineData("Indeterminate")]
    public void Admission_blocks_entry_for_any_detected_or_unresolved_manager(string kind)
        => Assert.True(AddonProcessHost.IsConflictingControllerEnvironment(
            new StubEnvironmentProvider(Enum.Parse<ControllerManagerKind>(kind))));

    [Fact]
    public void Admission_blocks_entry_when_the_assessment_throws()
        => Assert.True(AddonProcessHost.IsConflictingControllerEnvironment(new ThrowingEnvironmentProvider()));

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

    // ---- Harness ----

    private sealed class Harness
    {
        private readonly CenterMRebootAuthorityTransitionTests _owner;
        public Harness(CenterMRebootAuthorityTransitionTests owner) => _owner = owner;

        public bool CenterMAvailable { get; init; } = true;
        public bool StartEnabled { get; init; }
        public bool StartupSucceeds { get; init; } = true;
        public bool CenterMHelperCompletes { get; init; } = true;
        public bool CenterMHelperCancels { get; init; }
        public bool PrerequisitesReady { get; init; } = true;
        public bool RecoverySafe { get; init; } = true;
        public bool ConflictingEnvironment { get; init; }
        public SteamInputAddonforClaw.Devices.MSI.Claw.PhysicalOwnershipReleaseResult PhysicalRelease { get; init; } =
            SteamInputAddonforClaw.Devices.MSI.Claw.PhysicalOwnershipReleaseResult.NothingOwned;
        public UserTerminationDecision Safety { get; init; } = new(true, UserTerminationBlockReason.None);
        public Func<Task>? BeforeCenterMMutation { get; set; }

        public List<string> Order { get; } = [];
        public FakeHid Hid { get; } = new();
        public Roots Roots { get; } = new();

        public CenterMRebootAuthorityTransition Build(FakeRestart? restart = null)
        {
            Roots.Server = Roots.Updater = StartEnabled;
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
            var baseline = new AddonControllerHidHideBaseline(Hid, AddonExe);

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
                () => ConflictingEnvironment,
                _ => Task.FromResult((prerequisites, RecoverySafe)),
                _ =>
                {
                    Order.Add("physical-release");
                    return Task.FromResult(PhysicalRelease);
                },
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

    private sealed class StubEnvironmentProvider(ControllerManagerKind kind) : IControllerEnvironmentAssessmentProvider
    {
        public ControllerEnvironmentAssessmentSnapshot Capture() => new(
            Array.Empty<ControllerSoftwareStatus>(),
            new ControllerManagerClassification(kind, ControllerManagerClassificationReason.ControllerManagerStateIndeterminate),
            new ControllerEnvironmentCompatibilityAssessment(
                ControllerEnvironmentCompatibilityStatus.Indeterminate,
                ControllerEnvironmentCompatibilityReason.ControllerSoftwareStateIndeterminate));
    }

    private sealed class ThrowingEnvironmentProvider : IControllerEnvironmentAssessmentProvider
    {
        public ControllerEnvironmentAssessmentSnapshot Capture() => throw new InvalidOperationException("assessment unavailable");
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

        public HidHideInspection Inspect() => new(
            Inverse ? HidHideInspectionStatus.InverseWhitelist
                : Active ? HidHideInspectionStatus.Available : HidHideInspectionStatus.Disabled,
            new HashSet<string>(Whitelist, StringComparer.OrdinalIgnoreCase),
            Hidden.ToList(), Whitelist.ToList(), Active, Inverse, HasUnresolvedApplicationWhitelistEntries: false);

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
