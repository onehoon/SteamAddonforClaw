using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CenterMMainUiRoutingRetirementTests
{
    private const int Pid = 4242;
    private const string ExpectedPath = @"C:\Program Files\WindowsApps\9426MICRO-STARINTERNATION.64797CC12EF8E_3.0.60630.0_x64__kzh8wxbdkxb8p\MSI Center M\MSI Center M.exe";

    [Fact]
    public async Task No_real_mainui_present_skips_retirement()
    {
        var (retirement, invoker, windowController) = Create(new QueueProcessSnapshotSource([]));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.NoMainUiPresent, result);
        Assert.Equal(0, invoker.TerminateCallCount);
        Assert.Equal(0, windowController.CallCount);
    }

    [Fact]
    public async Task Process_enumeration_uncertain_fails_closed()
    {
        var (retirement, invoker, _) = Create(new QueueProcessSnapshotSource([null]));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.IdentityUncertain, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Multiple_same_name_candidates_terminates_none()
    {
        var snapshots = new QueueProcessSnapshotSource(
        [
            [
                new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath),
                new ProcessSnapshotEntry(Pid + 1, "MSI Center M", ExpectedPath)
            ]
        ]);
        var (retirement, invoker, _) = Create(snapshots);

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.MultipleCandidates, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Candidate_package_path_mismatch_is_not_terminated()
    {
        var snapshots = new QueueProcessSnapshotSource([[new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", @"C:\evil\MSI Center M.exe")]);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity);

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.IdentityMismatch, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Candidate_retained_handle_identity_uncertain_is_not_terminated()
    {
        var snapshots = new QueueProcessSnapshotSource([[new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Uncertain, null, null, null)]);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity);

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.IdentityUncertain, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Hidden_tray_resident_mainui_with_xinput_is_retired_without_minimizing()
    {
        // The primary regression test for the hardware failure: a real MainUI that has lived only
        // in the tray (never observed visible by this process) must still be a valid routing
        // retirement candidate.
        var snapshots = new QueueProcessSnapshotSource(
        [
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)], // discovery
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)], // terminator's fresh same-name recheck
            [] // fresh absence after termination
        ]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 0)]);
        var (retirement, invoker, windowController) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.Retired, result);
        Assert.Equal(0, windowController.CallCount);
        Assert.Equal(1, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Tray_cancellation_during_native_state_verification_propagates_promptly_without_terminating()
    {
        // Tray/hidden: nothing external has happened yet (no SC_MINIMIZE, no controller-mode
        // mutation, no helper/mutex), so caller cancellation must stay fully authoritative --
        // unlike the visible path's post-minimize stabilization window.
        using var cts = new CancellationTokenSource();
        var snapshots = new QueueProcessSnapshotSource([[new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 0)]);
        var probe = new GatedNativeModeProbe();
        var (retirement, invoker, windowController) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window, nativeModeProbe: probe);

        var task = retirement.PrepareExistingMainUiForRoutingAsync(cts.Token);
        await probe.Entered;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(0, invoker.TerminateCallCount);
        Assert.Equal(0, windowController.CallCount);
    }

    [Fact]
    public async Task Tray_stable_not_xinput_fails_after_a_single_capture_without_polling_the_full_timeout()
    {
        var snapshots = new QueueProcessSnapshotSource([[new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 0)]);
        var probe = new CountingNativeModeProbe(CenterMNativeModeProbeResult.NotXInput);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window, nativeModeProbe: probe);

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        // A single stable capture is enough for the tray path -- no bounded polling loop over the
        // full XInput wait timeout, since there is no in-progress transition to wait out.
        Assert.Equal(CenterMMainUiRoutingRetirementResult.XInputNotConfirmed, result);
        Assert.Equal(1, probe.CallCount);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Hidden_mainui_with_directinput_native_state_is_not_terminated()
    {
        var snapshots = new QueueProcessSnapshotSource([[new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 0)]);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: new FixedNativeModeProbe(CenterMNativeModeProbeResult.NotXInput));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.XInputNotConfirmed, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Hidden_mainui_with_uncertain_native_state_is_not_terminated()
    {
        var snapshots = new QueueProcessSnapshotSource([[new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 0)]);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: new FixedNativeModeProbe(CenterMNativeModeProbeResult.Uncertain));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.XInputNotConfirmed, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Hidden_mainui_without_terminate_rights_is_not_terminated()
    {
        var snapshots = new QueueProcessSnapshotSource(
        [
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)],
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]
        ]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 0)]);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput),
            handleOpener: new FakeHandleOpener(grantTerminateRights: false));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.AccessDenied, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Hidden_mainui_exits_naturally_before_termination_cleanly_continues()
    {
        var snapshots = new QueueProcessSnapshotSource(
        [
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)], // discovery
            [] // fresh absence check
        ]);
        // The upfront identity check observes it still alive; the terminator's own fresh capture
        // (immediately before termination) is what discovers the natural exit.
        var identity = new QueueIdentityInspector(
        [
            new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath),
            new LiveProcessIdentity(LiveProcessProbeStatus.Exited, null, null, null)
        ]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 0)]);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.Retired, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Visible_mainui_minimizes_waits_hidden_confirms_xinput_then_terminates()
    {
        var snapshots = new QueueProcessSnapshotSource(
        [
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)], // discovery
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)], // terminator recheck
            [] // fresh absence
        ]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider(
        [
            new MainUiWindowSnapshot(true, 1, 1), // upfront: visible
            new MainUiWindowSnapshot(true, 1, 0), // minimize-wait loop observes hidden
            new MainUiWindowSnapshot(true, 1, 0) // terminator's fresh recheck
        ]);
        var (retirement, invoker, windowController) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.Retired, result);
        Assert.Equal(1, windowController.CallCount);
        Assert.Equal(1, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Visible_mainui_xinput_wait_enforces_a_true_end_to_end_budget()
    {
        // The production probe (MsiClawCenterMNativeModeProbe) is not instantaneous -- a single
        // call can internally settle for the native-state manager's own multi-second timeout. The
        // outer xInputWaitTimeout must be a real end-to-end budget covering the probe calls
        // themselves, not just the polling loop around them: a probe that never returns must still
        // cause the wait to give up once the configured budget elapses, via the per-call budget
        // token this fake observes.
        var snapshots = new QueueProcessSnapshotSource(
        [
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)],
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]
        ]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider(
        [
            new MainUiWindowSnapshot(true, 1, 1), // upfront: visible
            new MainUiWindowSnapshot(true, 1, 0) // minimize-wait loop observes hidden
        ]);
        var probe = new BlocksUntilBudgetCancelledProbe();
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: probe, xInputWaitTimeout: TimeSpan.FromMilliseconds(100), xInputWaitPollInterval: TimeSpan.FromMilliseconds(10));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.XInputNotConfirmed, result);
        Assert.True(probe.SawCancellableToken);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Visible_mainui_minimize_command_failure_does_not_terminate()
    {
        var snapshots = new QueueProcessSnapshotSource([[new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 1)]);
        var (retirement, invoker, windowController) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            minimizeResult: CenterMMainUiMinimizeResult.AmbiguousVisibleWindows);

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.MinimizeFailed, result);
        Assert.Equal(1, windowController.CallCount);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Visible_mainui_that_never_becomes_hidden_times_out_without_terminating()
    {
        var snapshots = new QueueProcessSnapshotSource([[new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 1)]); // stays visible forever
        var (retirement, invoker, windowController) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            minimizeWaitTimeout: TimeSpan.FromMilliseconds(60), minimizeWaitPollInterval: TimeSpan.FromMilliseconds(15));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.MinimizeTimedOut, result);
        Assert.Equal(1, windowController.CallCount);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Late_hidden_observation_past_the_deadline_is_still_classified_as_timeout()
    {
        // The configured timeout is shorter than the poll interval, and the provider only reports
        // Hidden on the sample that arrives after the budget has already elapsed (e.g. Capture()
        // itself took longer than the remaining budget). A poll loop that only checked the deadline
        // BEFORE capturing would wake up, see it hadn't timed out yet when it started waiting, then
        // accept this late Hidden as an in-budget success -- that must never happen.
        var snapshots = new QueueProcessSnapshotSource([[new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new SlowThenHiddenWindowSnapshotProvider(
            initialSnapshot: new MainUiWindowSnapshot(true, 1, 1),
            delayedHiddenSnapshot: new MainUiWindowSnapshot(true, 1, 0),
            captureDelay: TimeSpan.FromMilliseconds(80));
        var (retirement, invoker, windowController) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            minimizeWaitTimeout: TimeSpan.FromMilliseconds(30), minimizeWaitPollInterval: TimeSpan.FromMilliseconds(200));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.MinimizeTimedOut, result);
        Assert.Equal(1, windowController.CallCount);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Window_snapshot_uncertain_during_minimize_wait_is_classified_as_uncertain_not_timeout()
    {
        var snapshots = new QueueProcessSnapshotSource([[new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider(
        [
            new MainUiWindowSnapshot(true, 1, 1), // upfront: visible
            null // minimize-wait loop: enumeration failure, distinct from "stayed visible"
        ]);
        var (retirement, invoker, windowController) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            minimizeWaitTimeout: TimeSpan.FromMilliseconds(200), minimizeWaitPollInterval: TimeSpan.FromMilliseconds(10));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.WindowStateUncertain, result);
        Assert.Equal(1, windowController.CallCount);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Visible_mainui_without_terminate_rights_is_not_minimized()
    {
        // Fails BEFORE ever mutating the user's Center M window/controller lifecycle, since we
        // already know this attempt can never complete termination.
        var snapshots = new QueueProcessSnapshotSource([[new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 1)]);
        var (retirement, invoker, windowController) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            handleOpener: new FakeHandleOpener(grantTerminateRights: false));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.AccessDenied, result);
        Assert.Equal(0, windowController.CallCount);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Cancellation_racing_in_right_after_minimize_still_completes_stabilization_and_never_terminates()
    {
        // SC_MINIMIZE is an external side effect posted to a foreign process that the pipeline
        // cannot roll back. Cancellation arriving right after that must not abandon Center M
        // mid-transition -- hidden/XInput stabilization must still run to completion, and only once
        // that resolves to a known-safe state does cancellation get honored again (without ever
        // terminating the MainUI for a route that no longer exists).
        using var cts = new CancellationTokenSource();
        var snapshots = new QueueProcessSnapshotSource([[new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider(
        [
            new MainUiWindowSnapshot(true, 1, 1), // upfront: visible
            new MainUiWindowSnapshot(true, 1, 0) // minimize-wait loop observes hidden
        ]);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);
        var terminator = new CenterMMainUiRoutingTerminator(invoker, identity, window, snapshots);
        // Cancels the token as a side effect of the minimize request itself succeeding -- simulates
        // cancellation racing in immediately after the external side effect is posted.
        var windowController = new CancelingWindowController(cts, CenterMMainUiMinimizeResult.Requested);
        var retirement = new CenterMMainUiRoutingRetirement(
            new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput),
            processSnapshotSource: snapshots,
            handleOpener: new FakeHandleOpener(),
            identityInspector: identity,
            windowSnapshotProvider: window,
            windowController: windowController,
            terminator: terminator,
            minimizeWaitTimeout: TimeSpan.FromMilliseconds(200),
            minimizeWaitPollInterval: TimeSpan.FromMilliseconds(10),
            xInputWaitTimeout: TimeSpan.FromMilliseconds(200),
            xInputWaitPollInterval: TimeSpan.FromMilliseconds(10),
            terminateWaitTimeout: TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<OperationCanceledException>(() => retirement.PrepareExistingMainUiForRoutingAsync(cts.Token));

        // Stabilization ran to completion despite the cancellation (both queued window snapshots
        // were consumed -- if it had abandoned immediately, the second would never be read).
        Assert.Equal(0, window.RemainingCount);
        Assert.Equal(1, windowController.CallCount);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Cancellation_during_termination_still_completes_absence_check_then_propagates()
    {
        // Once TerminateProcess/WaitForExit has actually started, that operation cannot be undone --
        // it must run to a classified result. VerifyFreshAbsence must then still run to completion
        // (never abandoned), and only once it comes back clean is the caller's cancellation finally
        // honored -- so a cancelled route never reaches Phase-1 helper staging on the strength of a
        // Retired result it never actually wanted.
        using var cts = new CancellationTokenSource();
        var snapshots = new QueueProcessSnapshotSource(
        [
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)], // discovery
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)], // terminator's same-name recheck
            [] // fresh absence after termination
        ]);
        var identity = new QueueIdentityInspector(
        [
            new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath),
            new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)
        ]);
        var window = new QueueWindowSnapshotProvider(
        [
            new MainUiWindowSnapshot(true, 1, 0), // upfront: tray/hidden
            new MainUiWindowSnapshot(true, 1, 0) // terminator's fresh recheck
        ]);
        // Cancels the token as a side effect of WaitForExit confirming the exit -- simulates
        // cancellation racing in exactly while termination is in flight.
        var invoker = new CancelingInvoker(cts);
        var terminator = new CenterMMainUiRoutingTerminator(invoker, identity, window, snapshots);
        var retirement = new CenterMMainUiRoutingRetirement(
            new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput),
            processSnapshotSource: snapshots,
            handleOpener: new FakeHandleOpener(),
            identityInspector: identity,
            windowSnapshotProvider: window,
            terminator: terminator,
            terminateWaitTimeout: TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retirement.PrepareExistingMainUiForRoutingAsync(cts.Token));

        // The exact exit was still classified, and fresh absence was still consumed (the third
        // queued snapshot response was reached) despite the cancellation.
        Assert.Equal(1, invoker.TerminateCallCount);
        Assert.Equal(0, snapshots.RemainingCount);
    }

    [Fact]
    public async Task Window_visible_again_immediately_before_termination_blocks_the_kill()
    {
        var snapshots = new QueueProcessSnapshotSource(
        [
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)],
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]
        ]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider(
        [
            new MainUiWindowSnapshot(true, 1, 0), // upfront: tray/hidden
            new MainUiWindowSnapshot(true, 1, 1) // terminator's fresh recheck: visible again (race C)
        ]);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.IdentityMismatch, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public async Task Termination_wait_timeout_does_not_report_retired()
    {
        var snapshots = new QueueProcessSnapshotSource(
        [
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)],
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)]
        ]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 0)]);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput), waitForExitSucceeds: false);

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.TerminationTimedOut, result);
    }

    [Fact]
    public async Task Another_mainui_present_after_termination_rejects_stale_success()
    {
        var snapshots = new QueueProcessSnapshotSource(
        [
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)], // discovery
            [new ProcessSnapshotEntry(Pid, "MSI Center M", ExpectedPath)], // terminator recheck
            [new ProcessSnapshotEntry(Pid + 1, "MSI Center M", ExpectedPath)] // a NEW real MainUI after retirement
        ]);
        var identity = new QueueIdentityInspector([new LiveProcessIdentity(LiveProcessProbeStatus.Alive, Pid, "MSI Center M", ExpectedPath)]);
        var window = new QueueWindowSnapshotProvider([new MainUiWindowSnapshot(true, 1, 0)]);
        var (retirement, invoker, _) = Create(snapshots, identityInspector: identity, windowSnapshotProvider: window,
            nativeModeProbe: new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput));

        var result = await retirement.PrepareExistingMainUiForRoutingAsync(CancellationToken.None);

        Assert.Equal(CenterMMainUiRoutingRetirementResult.MultipleCandidates, result);
        Assert.Equal(1, invoker.TerminateCallCount);
    }

    private static (CenterMMainUiRoutingRetirement Retirement, RecordingInvoker Invoker, FixedWindowController WindowController) Create(
        QueueProcessSnapshotSource snapshotSource,
        QueueIdentityInspector? identityInspector = null,
        IMainUiWindowSnapshotProvider? windowSnapshotProvider = null,
        ICenterMNativeModeProbe? nativeModeProbe = null,
        CenterMMainUiMinimizeResult minimizeResult = CenterMMainUiMinimizeResult.Requested,
        IProcessHandleOpener? handleOpener = null,
        TimeSpan? minimizeWaitTimeout = null,
        TimeSpan? minimizeWaitPollInterval = null,
        TimeSpan? xInputWaitTimeout = null,
        TimeSpan? xInputWaitPollInterval = null,
        bool waitForExitSucceeds = true)
    {
        var identity = identityInspector ?? new QueueIdentityInspector([]);
        var window = windowSnapshotProvider ?? new QueueWindowSnapshotProvider([]);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: waitForExitSucceeds);
        var terminator = new CenterMMainUiRoutingTerminator(invoker, identity, window, snapshotSource);
        var windowController = new FixedWindowController(minimizeResult);
        var retirement = new CenterMMainUiRoutingRetirement(
            nativeModeProbe ?? new FixedNativeModeProbe(CenterMNativeModeProbeResult.XInput),
            processSnapshotSource: snapshotSource,
            handleOpener: handleOpener ?? new FakeHandleOpener(),
            identityInspector: identity,
            windowSnapshotProvider: window,
            windowController: windowController,
            terminator: terminator,
            minimizeWaitTimeout: minimizeWaitTimeout ?? TimeSpan.FromMilliseconds(200),
            minimizeWaitPollInterval: minimizeWaitPollInterval ?? TimeSpan.FromMilliseconds(20),
            xInputWaitTimeout: xInputWaitTimeout ?? TimeSpan.FromMilliseconds(200),
            xInputWaitPollInterval: xInputWaitPollInterval ?? TimeSpan.FromMilliseconds(20),
            terminateWaitTimeout: TimeSpan.FromMilliseconds(50));
        return (retirement, invoker, windowController);
    }

    private sealed class QueueProcessSnapshotSource(IEnumerable<IReadOnlyList<ProcessSnapshotEntry>?> responses) : IProcessSnapshotSource
    {
        private readonly Queue<IReadOnlyList<ProcessSnapshotEntry>?> _queue = new(responses);
        private IReadOnlyList<ProcessSnapshotEntry>? _last = [];

        internal int RemainingCount => _queue.Count;

        public IReadOnlyList<ProcessSnapshotEntry>? GetProcessesByName(string processName)
        {
            if (_queue.Count > 0) _last = _queue.Dequeue();
            return _last;
        }
    }

    private sealed class QueueIdentityInspector(IEnumerable<LiveProcessIdentity> responses) : IProcessIdentityInspector
    {
        private readonly Queue<LiveProcessIdentity> _queue = new(responses);
        private LiveProcessIdentity _last;

        public LiveProcessIdentity Inspect(SafeProcessHandle handle)
        {
            if (_queue.Count > 0) _last = _queue.Dequeue();
            return _last;
        }
    }

    private sealed class QueueWindowSnapshotProvider(IEnumerable<MainUiWindowSnapshot?> responses) : IMainUiWindowSnapshotProvider
    {
        private readonly Queue<MainUiWindowSnapshot?> _queue = new(responses);
        private MainUiWindowSnapshot? _last;

        internal int RemainingCount => _queue.Count;

        public MainUiWindowSnapshot? Capture(int processId)
        {
            if (_queue.Count > 0) _last = _queue.Dequeue();
            return _last;
        }
    }

    /// <summary>Returns <paramref name="initialSnapshot"/> on the first call (the upfront
    /// visibility check), then sleeps <paramref name="captureDelay"/> before returning
    /// <paramref name="delayedHiddenSnapshot"/> on every call after that -- simulates a Capture()
    /// that is itself slow enough to cross the wait deadline while it runs, so the caller must
    /// re-check the deadline after the call returns rather than trusting a pre-call check.</summary>
    private sealed class SlowThenHiddenWindowSnapshotProvider(
        MainUiWindowSnapshot initialSnapshot, MainUiWindowSnapshot delayedHiddenSnapshot, TimeSpan captureDelay) : IMainUiWindowSnapshotProvider
    {
        private bool _first = true;

        public MainUiWindowSnapshot? Capture(int processId)
        {
            if (_first)
            {
                _first = false;
                return initialSnapshot;
            }

            Thread.Sleep(captureDelay);
            return delayedHiddenSnapshot;
        }
    }

    private sealed class FixedNativeModeProbe(CenterMNativeModeProbeResult result) : ICenterMNativeModeProbe
    {
        public Task<CenterMNativeModeProbeResult> CaptureAsync(CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class CountingNativeModeProbe(CenterMNativeModeProbeResult result) : ICenterMNativeModeProbe
    {
        internal int CallCount { get; private set; }
        public Task<CenterMNativeModeProbeResult> CaptureAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    /// <summary>Blocks inside <see cref="CaptureAsync"/> (observing the supplied cancellation token,
    /// like a real implementation awaiting native work) until the test signals a capture has
    /// started -- lets a test deterministically cancel while a capture is genuinely in flight,
    /// without timing sleeps.</summary>
    private sealed class GatedNativeModeProbe : ICenterMNativeModeProbe
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Entered => _entered.Task;

        public async Task<CenterMNativeModeProbeResult> CaptureAsync(CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return CenterMNativeModeProbeResult.XInput;
        }
    }

    /// <summary>Never returns on its own -- proves <see cref="CenterMMainUiRoutingRetirement"/>
    /// passes each probe call a genuinely cancellable, time-boxed token (the per-call budget) rather
    /// than relying solely on an outer poll-loop deadline that would never fire while a single call
    /// is still in flight.</summary>
    private sealed class BlocksUntilBudgetCancelledProbe : ICenterMNativeModeProbe
    {
        internal bool SawCancellableToken { get; private set; }

        public async Task<CenterMNativeModeProbeResult> CaptureAsync(CancellationToken cancellationToken)
        {
            SawCancellableToken |= cancellationToken.CanBeCanceled;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return CenterMNativeModeProbeResult.Uncertain;
        }
    }

    /// <summary>Cancels the supplied token as a side effect of a successful minimize request --
    /// simulates cancellation racing in immediately after the external SC_MINIMIZE side effect is
    /// posted, without needing real timing gates.</summary>
    private sealed class CancelingWindowController(CancellationTokenSource cancelOnMinimize, CenterMMainUiMinimizeResult result) : ICenterMMainUiWindowController
    {
        internal int CallCount { get; private set; }

        public CenterMMainUiMinimizeResult TryMinimizeRecognizedMainUi(int processId)
        {
            CallCount++;
            if (result == CenterMMainUiMinimizeResult.Requested) cancelOnMinimize.Cancel();
            return result;
        }
    }

    private sealed class FixedWindowController(CenterMMainUiMinimizeResult result) : ICenterMMainUiWindowController
    {
        internal int CallCount { get; private set; }
        public CenterMMainUiMinimizeResult TryMinimizeRecognizedMainUi(int processId)
        {
            CallCount++;
            return result;
        }
    }

    private sealed class RecordingInvoker(bool terminateSucceeds, bool waitSucceeds) : ITerminateProcessInvoker
    {
        internal int TerminateCallCount { get; private set; }

        public bool TryTerminate(SafeProcessHandle handle, out int win32Error)
        {
            TerminateCallCount++;
            win32Error = terminateSucceeds ? 0 : 5;
            return terminateSucceeds;
        }

        public bool WaitForExit(SafeProcessHandle handle, TimeSpan timeout) => waitSucceeds;
    }

    /// <summary>Cancels the supplied token as a side effect of confirming a successful exit --
    /// simulates cancellation racing in exactly while TerminateProcess/WaitForExit is in flight,
    /// without needing real timing gates.</summary>
    private sealed class CancelingInvoker(CancellationTokenSource cancelOnExitConfirmed) : ITerminateProcessInvoker
    {
        internal int TerminateCallCount { get; private set; }

        public bool TryTerminate(SafeProcessHandle handle, out int win32Error)
        {
            TerminateCallCount++;
            win32Error = 0;
            return true;
        }

        public bool WaitForExit(SafeProcessHandle handle, TimeSpan timeout)
        {
            cancelOnExitConfirmed.Cancel();
            return true;
        }
    }

    /// <summary>Opens a REAL (non-pseudo) handle to this test process itself, via
    /// <c>OpenProcess</c> against the actual current PID -- the <c>GetCurrentProcess()</c>
    /// pseudo-handle (-1) trips <see cref="SafeProcessHandle"/>'s own IsInvalid check when routed
    /// through the production <see cref="TrackedCenterMMainUi.Create"/> path (unlike
    /// <see cref="TrackedCenterMMainUi.CreateForTesting"/>, which bypasses that check entirely).</summary>
    private sealed class FakeHandleOpener(bool grantTerminateRights = true) : IProcessHandleOpener
    {
        private const uint PROCESS_TERMINATE = 0x0001;

        public SafeProcessHandle Open(int processId, uint desiredAccess)
        {
            if (!grantTerminateRights && (desiredAccess & PROCESS_TERMINATE) != 0)
                return new SafeProcessHandle(IntPtr.Zero, false);
            return OpenProcess(desiredAccess, false, Environment.ProcessId);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);
    }
}
