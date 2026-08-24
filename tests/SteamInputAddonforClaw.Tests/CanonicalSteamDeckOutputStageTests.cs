using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Feedback;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.Contracts.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// Addon safety-shell coverage for <see cref="CanonicalSteamDeckOutputStage"/> against exact identity
/// <c>28DE:1205</c>: factory failure, neutral-before-live, bus/server cleanup retry without replaying
/// device removal, PnP timeout, identity failure rollback, stale-node persistence, HidHide inspection
/// failure/pre-existing block, recovery intent write failure, cancellation during creation, and
/// rollback ordering. Every test here goes through the single canonical session-factory constructor.
/// </summary>
[Collection("AppLog")]
public sealed class CanonicalSteamDeckOutputStageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ClawSteamDeckOutputTests", Guid.NewGuid().ToString("N"));
    private readonly Guid _session = Guid.NewGuid();

    [Fact]
    public async Task PausePresentation_is_idempotent_when_already_paused()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide());
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True(await stage.PausePresentationAsync(CancellationToken.None, reportOutputFaultOnFailure: false));
        var neutralCount = session.Trace.Count(x => x == "Neutral");

        Assert.True(await stage.PausePresentationAsync(CancellationToken.None, reportOutputFaultOnFailure: false));

        Assert.Equal(neutralCount, session.Trace.Count(x => x == "Neutral"));
    }

    [Fact]
    public async Task PersistentIdentityCache_replaces_only_after_successful_fallback_resolution()
    {
        var first = Device("USB\\VID_28DE&PID_1205\\CACHE_A");
        var firstStage = Create(new FakeCanonicalSession(), new FakeEnumerator([
            [], [UsbIpHost(), first], [UsbIpHost(), first]
        ]), new FakeHidHide());
        Assert.True((await firstStage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await firstStage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var cachePath = CanonicalSteamDeckOutputStage.TestOnlyIdentityCachePath!;
        Assert.Contains("CACHE_A", File.ReadAllText(cachePath), StringComparison.OrdinalIgnoreCase);

        var second = Device("USB\\VID_28DE&PID_1205\\CACHE_B");
        var secondStage = Create(new FakeCanonicalSession(), new FakeEnumerator([
            [], [UsbIpHost(), second], [UsbIpHost(), second]
        ]), new FakeHidHide());
        Assert.True((await secondStage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await secondStage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var latest = File.ReadAllText(cachePath);
        Assert.DoesNotContain("CACHE_A", latest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CACHE_B", latest, StringComparison.OrdinalIgnoreCase);

        var hitEnumerator = new FakeEnumerator([
            [], [UsbIpHost(), second], [UsbIpHost(), second]
        ], directLookup: true);
        var hitStage = Create(new FakeCanonicalSession(), hitEnumerator, new FakeHidHide());
        Assert.True((await hitStage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await hitStage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True(hitEnumerator.DirectLookupCalls > 0);
        Assert.Equal(1, hitEnumerator.EnumerateCalls);
    }

    [Fact]
    public async Task ReconcileOwnedState_healthy_canonical_route_is_strict_noop()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")]]), new FakeHidHide());
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        var traceCount = session.Trace.Count;

        var result = await stage.ReconcileOwnedStateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("Healthy", result.Reason);
        Assert.Equal(traceCount, session.Trace.Count);
    }

    [Fact]
    public async Task ReconcileOwnedState_unexpected_stopped_publisher_fails_closed()
    {
        var session = new FakeCanonicalSession { InputAccepted = false };
        var ticks = new ManualTicks();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")]]), new FakeHidHide(), snapshot: new FakeSnapshot(), reportTicks: ticks);
        var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stage.SetOutputFaultHandler(() => { fault.TrySetResult(); return ValueTask.CompletedTask; });
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        ticks.Tick();
        await fault.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(10);

        var result = await stage.ReconcileOwnedStateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("SteamDeckPublisherNotRunning", result.Reason);
        Assert.Equal(1, session.Trace.Count(value => value == "Start"));
    }

    [Fact]
    public async Task ReconcileOwnedState_attached_without_owned_pnp_fails_without_mutation()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [], [], [], []]), new FakeHidHide());
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        var traceCount = session.Trace.Count;

        var result = await stage.ReconcileOwnedStateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("SteamDeckOwnedPnPAbsent", result.Reason);
        Assert.Equal(traceCount, session.Trace.Count);
    }

    [Fact]
    public async Task ReconcileOwnedState_pnp_presence_without_viiper_attachment_fails_closed()
    {
        var session = new FakeCanonicalSession { AttachmentState = USBDeviceAttachmentState.Detached };
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")]]), new FakeHidHide());
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var result = await stage.ReconcileOwnedStateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("SteamDeckAttachmentNotAttached", result.Reason);
    }

    [Fact]
    public async Task ReconcileOwnedState_does_not_adopt_foreign_matching_pnp()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned"), Device("foreign")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")]]), new FakeHidHide());
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var result = await stage.ReconcileOwnedStateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("SteamDeckPnPOwnershipAmbiguous", result.Reason);
    }

    [Fact]
    public async Task ReconcileOwnedState_allows_exact_preexisting_matching_pnp()
    {
        var preExisting = CanonicalDeckGroup(Guid.NewGuid(), "PREEXISTING");
        var owned = CanonicalDeckGroup(Guid.NewGuid(), "OWNED");
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([
            [UsbIpHost(), ..preExisting],
            [UsbIpHost(), ..preExisting, ..owned],
            [UsbIpHost(), ..preExisting, ..owned],
            [UsbIpHost(), ..preExisting, ..owned],
            [UsbIpHost(), ..preExisting, ..owned]
        ]), new FakeHidHide());
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var result = await stage.ReconcileOwnedStateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("Healthy", result.Reason);
    }

    [Fact]
    public async Task ReconcileOwnedState_resumes_paused_presentation_without_recreating_session()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")]]), new FakeHidHide());
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True(await stage.PausePresentationAsync());
        var bus = session.BusId; var logical = session.LogicalDeviceId;
        session.Trace.Clear();

        var result = await stage.ReconcileOwnedStateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal("Repaired", result.Reason);
        Assert.Equal(bus, session.BusId);
        Assert.Equal(logical, session.LogicalDeviceId);
        Assert.DoesNotContain("Start", session.Trace);
        Assert.DoesNotContain("Remove", session.Trace);
    }

    [Fact]
    public void Steam_pulse_is_rejected_when_output_stage_is_inactive()
    {
        var stage = Create(new FakeCanonicalSession(), new FakeEnumerator([[]]), new FakeHidHide());
        Assert.False(stage.TryRequestSteamPulse());
    }

    [Fact]
    public async Task Steam_pulse_is_rejected_during_and_after_output_rollback()
    {
        using var releaseDetach = new ManualResetEventSlim(false);
        var session = new FakeCanonicalSession { OnDetachDeviceCalled = () => releaseDetach.Wait() };
        var stage = Create(session,
            new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]),
            new FakeHidHide());

        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var rollback = stage.RollbackMutationAsync(CancellationToken.None).AsTask();
        await session.DetachEntered.Task;
        Assert.False(stage.TryRequestSteamPulse());

        releaseDetach.Set();
        Assert.True((await rollback).Succeeded);
        Assert.False(stage.TryRequestSteamPulse());
    }

    [Fact]
    public async Task SessionPathUsesTypedPublisherAndCleanupOrder()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);

        var created = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.True(created.Succeeded, created.Reason);

        var rollback = await stage.RollbackMutationAsync(CancellationToken.None);
        Assert.True(rollback.Succeeded, rollback.Reason);
        Assert.Equal(["Start", "Neutral", "Remove", "Dispose"], session.Trace);
    }

    [Fact]
    public async Task SuccessfulCreationLogsCanonicalSessionTimingWithoutObsoleteFields()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);

        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);

        Assert.Contains("Event=SteamDeckOutputCreated", log);
        Assert.Contains("CanonicalSessionStartMs=", log);
        Assert.DoesNotContain("RuntimeStartMs=", log);
        Assert.DoesNotContain("CreateDeviceMs=", log);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task SessionStartFailureLogsTimingBeforeFailureTraceWithNormalizedOperation()
    {
        var session = new FakeCanonicalSession { StartResult = false };
        var stage = Create(session, new FakeEnumerator([[]]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);

        Assert.Contains("Event=SteamDeckOutputCreationFailed", log);
        Assert.Contains("FailedOperation=CanonicalSessionStart", log);
        Assert.Contains("Reason=CanonicalSessionStartFailed", log);
        Assert.Contains("CanonicalSessionStartMs=", log);
        Assert.DoesNotContain("RuntimeStartMs=", log);
        Assert.DoesNotContain("CreateDeviceMs=", log);
    }

    [Fact]
    public async Task Unavailable_persistent_runtime_fails_route_without_residual_recovery_ownership()
    {
        var session = new UnavailableCanonicalSteamDeckSession();
        var stage = Create(session, new FakeEnumerator([[]]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("CanonicalSessionStartFailed", result.Reason);
        Assert.Equal(RecoveryStatus.NoRecoveryNeeded, new RecoveryManager(new RecoveryJournalStore(Path.Combine(_directory, "recovery.json"))).LoadJournal().Status);
    }

    [Fact]
    public async Task DetachRetryDoesNotReplayLogicalRemoval()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(1, session.RemoveCalls);
        Assert.Equal(["Start", "Neutral", "Remove", "Dispose"], session.Trace);
    }

    [Fact]
    public async Task RouteExitDoesNotClosePersistentRuntime()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(1, session.RemoveCalls);
        Assert.Equal(["Start", "Neutral", "Remove", "Dispose"], session.Trace);
    }

    [Fact]
    public async Task Retryable_detach_is_retried_explicitly_without_logical_removal()
    {
        var session = new FakeCanonicalSession { RemoveResult = false };
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.False((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        session.RemoveResult = true;
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(1, session.RemoveCalls);
        Assert.Contains("Retry:AttachmentDetach", session.Trace);
    }

    [Fact]
    public async Task FactoryFailureLeavesNoRecoveryBoundaryOrOwnershipUncertainty()
    {
        var stage = CreateFactoryFailure(new FakeEnumerator([[]]), new FakeHidHide());
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        var rollback = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("InvalidOperationException", result.Reason);
        Assert.True(rollback.Succeeded);
        Assert.Equal(RecoveryStatus.Success, new RecoveryManager(new RecoveryJournalStore(Path.Combine(_directory, "factory-failure-recovery.json"))).LoadJournal().Status);
    }

    [Fact]
    public async Task SuccessfulCreationResolvesPnPAndSendsOneNeutralReport()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide());
        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(1, session.Trace.Count(t => t == "Neutral"));
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task HidHideInspectionFailureRollsBackAndLeavesNoOwnedRuntimeDevice()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide { Inspection = new(HidHideInspectionStatus.ConfigurationUnavailable, new HashSet<string>()) });
        await stage.PrepareMutationAsync(CancellationToken.None);
        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(1, session.RemoveCalls);
    }

    [Fact]
    public async Task PreExistingHidHideOutputBlockIsPreserved()
    {
        var session = new FakeCanonicalSession();
        var hidHide = new FakeHidHide { Inspection = new(HidHideInspectionStatus.Available, new HashSet<string>(), ["USB\\VID_28DE&PID_1205\\owned"]) };
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), hidHide);
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("HidHideOutputAlreadyBlocked", result.Reason);
        Assert.Contains("USB\\VID_28DE&PID_1205\\owned", hidHide.Inspection.HiddenDeviceEntries!);
        Assert.Equal(1, session.RemoveCalls);
    }

    [Fact]
    public async Task PnPTimeoutRollsBackAddDeviceSuccess()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[], []]), new FakeHidHide(), TimeSpan.FromMilliseconds(1));
        await stage.PrepareMutationAsync(CancellationToken.None);
        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(1, session.RemoveCalls);
    }

    [Fact]
    public async Task PnPTimeoutEmitsExactlyOneBoundedIdentityDiagnosticDumpNotOnePerPoll()
    {
        var session = new FakeCanonicalSession();
        // ~100 polling iterations at the fixed 1ms poll interval used by the Create() helper.
        var stage = Create(session, new FakeEnumerator([[], []]), new FakeHidHide(), TimeSpan.FromMilliseconds(100));
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        var occurrences = log.Split("SteamDeckIdentityDiagnosticSummary").Length - 1;
        Assert.Equal(1, occurrences);
        Assert.Contains("FailedOperation=PnPResolve", log);
    }

    [Fact]
    public async Task IdentityFailureRollbackFailsWhenPotentialDeckNodeStaysPresentAfterRemoval()
    {
        // The usbip-win2 host ancestor record is missing from the snapshot, so identity resolution
        // correctly fails closed (MissingUsbIpWin2Ancestor). But the 28DE:1205 node that appeared
        // during the attempt does NOT actually disappear after DetachDevice() in this fixture --
        // rollback's absence verification must catch that using the exact InstanceId observed at
        // failure time, not by re-running the same strict ownership predicate that already rejected
        // it (which would trivially report "no matching candidate" -> false-positive absence).
        var deck = Device("USB\\VID_28DE&PID_1205\\STAYS");
        var enumerator = new DeckPresenceEnumerator(deck);
        var session = new FakeCanonicalSession();
        var stage = Create(session, enumerator, new FakeHidHide(), TimeSpan.FromMilliseconds(50));
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("VirtualDevicePnPStillPresent", result.Reason);
        Assert.Equal(1, session.RemoveCalls);
    }

    [Fact]
    public async Task IdentityFailureRollbackSucceedsAfterPotentialDeckNodeDisappears()
    {
        var deck = Device("USB\\VID_28DE&PID_1205\\DISAPPEARS");
        var enumerator = new DeckPresenceEnumerator(deck);
        var session = new FakeCanonicalSession { OnDetachDeviceCalled = () => enumerator.DeviceRemoved = true };
        var stage = Create(session, enumerator, new FakeHidHide(), TimeSpan.FromMilliseconds(50));
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Rollback=SteamDeckDetached", result.Reason);
        Assert.DoesNotContain("VirtualDevicePnPStillPresent", result.Reason);
        Assert.Equal(1, session.RemoveCalls);
    }

    [Fact]
    public async Task SuccessfulResolutionEmitsNoIdentityDiagnosticDump()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.DoesNotContain("SteamDeckIdentityDiagnosticSummary", log);
        await stage.RollbackMutationAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RecoveryIntentWriteFailureDoesNotEnterCreatingRollbackBoundary()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[]]), new FakeHidHide(), storeWriteFailsAfterSeed: true);
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        var rollback = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("VirtualDeviceRecoveryIntentFailed", result.Reason);
        Assert.True(rollback.Succeeded);
        // The intent write fails before Start()/Remove() are ever reached (no native mutation, no
        // recovery boundary entered), but the lazily-created canonical session is still disposed by
        // the Prepared-state rollback branch -- "Dispose" is the only trace entry.
        Assert.Equal(["Dispose"], session.Trace);
        Assert.Equal(0, session.RemoveCalls);
    }

    [Fact]
    public async Task CallerCancellationDuringPnPWaitRollsBackIntentAndDevice()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[], []]), new FakeHidHide(), TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        await stage.PrepareMutationAsync(CancellationToken.None);

        var creation = stage.ExecuteMutationAsync(cancellation.Token).AsTask();
        Assert.True(SpinWait.SpinUntil(() => session.Trace.Contains("Start"), TimeSpan.FromSeconds(5)));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => creation);
        Assert.Equal(1, session.RemoveCalls);
        Assert.Equal(RecoveryStatus.NoRecoveryNeeded, new RecoveryManager(new RecoveryJournalStore(Path.Combine(_directory, "recovery.json"))).LoadJournal().Status);
    }

    [Fact]
    public async Task CancellationBoundaryStopsBeforeMutation()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[]]), new FakeHidHide());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await stage.PrepareMutationAsync(cancellation.Token));
        Assert.Empty(session.Trace);
    }

    [Fact]
    public async Task InactiveAndDoubleRollbackAreSuccessfulNoOps()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[]]), new FakeHidHide());
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        await stage.PrepareMutationAsync(CancellationToken.None);
        var rollback = await stage.RollbackMutationAsync(CancellationToken.None);
        Assert.True(rollback.Succeeded);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task LivePublisherStartsAfterNeutralAndStopsBeforeDetach()
    {
        var session = new FakeCanonicalSession { BlockInput = true };
        var ticks = new ManualTicks();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot(), reportTicks: ticks);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        ticks.Tick(); await session.InputEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Live input started strictly after Neutral (Trace snapshot taken at the first SetState call
        // must already contain Start+Neutral and nothing past it).
        Assert.Equal(["Start", "Neutral"], session.Trace.Take(session.InputObservedAfterTraceCount));
        var rollback = stage.RollbackMutationAsync(CancellationToken.None).AsTask();
        // Live input still blocked inside SetState -- device removal must not have started yet.
        await Task.Yield(); Assert.Equal(0, session.RemoveCalls);
        session.ReleaseInput.TrySetResult();
        Assert.True((await rollback).Succeeded);
        Assert.Contains("Remove", session.Trace);
    }

    [Fact]
    public async Task NeutralRejectionDoesNotStartPublisher()
    {
        var session = new FakeCanonicalSession { NeutralAccepted = false };
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot());
        await stage.PrepareMutationAsync(CancellationToken.None);
        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.DoesNotContain("Input", session.Trace);
    }

    [Fact]
    public async Task FeedbackCallbackRegistrationFailureDoesNotBlockRouting()
    {
        var session = new FakeCanonicalSession { SetOutputCallbackResult = false };
        var sink = new RecordingRumbleSink();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), sink: sink);
        await stage.PrepareMutationAsync(CancellationToken.None);

        try
        {
            var result = await stage.ExecuteMutationAsync(CancellationToken.None);

            Assert.True(result.Succeeded, result.Reason);
            Assert.Equal(["Start", "Neutral", "SetOutputCallback"], session.Trace);
        }
        finally
        {
            await stage.RollbackMutationAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task FeedbackCancellationFailureDoesNotBlockSteamStructuralTeardown()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), sink: new ThrowingCancelSink());
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var result = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(1, session.ClearOutputCallbackCalls);
        Assert.Equal(1, session.RemoveCalls);
    }

    [Fact]
    public async Task DeveloperHapticStageCommandUsesNonOffGeneratorType()
    {
        var session = new FakeCanonicalSession();
        var sink = new RecordingRumbleSink();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), sink: sink);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var outcome = await stage.RunDeveloperVibrationTestAsync(FrontendVibrationTestCommand.Haptic, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(PhysicalRumbleWriteStatus.Succeeded, outcome.CommandResult?.Status);
        Assert.Contains(new TwoMotorRumble(32896, 32896), sink.Snapshot());
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task RumbleAvailabilityFailureLeavesSteamDeckRoutingActiveWithoutCallback()
    {
        var session = new FakeCanonicalSession();
        var sink = new RecordingRumbleSink();
        sink.Results.Enqueue(new(PhysicalRumbleWriteStatus.Unavailable, "NoVerifiedEndpoint"));
        var ticks = new ManualTicks();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot(), reportTicks: ticks, sink: sink);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Contains("SetOutputCallback", session.Trace);
        ticks.Tick();
        await session.InputEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Contains("SetOutputCallback", session.Trace);
    }

    [Fact]
    public async Task RoutingActivationDoesNotPerformRumblePreflight()
    {
        var session = new FakeCanonicalSession();
        var trace = new List<string>();
        session.ExternalTrace = trace;
        var sink = new RecordingRumbleSink { Trace = trace };
        var ticks = new ManualTicks();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot(), reportTicks: ticks, sink: sink);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        ticks.Tick();
        await session.InputEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["Neutral", "SetOutputCallback", "PublisherLive"], trace);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task TeardownClearsPendingQuickAccessPulse()
    {
        var session = new FakeCanonicalSession();
        var ticks = new ManualTicks();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot(), reportTicks: ticks);

        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        stage.RequestQuickAccessPulse();
        ticks.Tick();
        await session.WaitForSetStateCountAsync(1);
        Assert.Equal((byte)1, session.QuickAccessValues[0]);

        // Stop/teardown must clear the pending pulse via the overlay's Clear() -- not just stop
        // publishing. Reach into the stage's owned overlay (private field, same-assembly test access
        // via reflection) to prove Clear() actually ran, independent of whether the stage is ever
        // reactivated.
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        var overlayField = typeof(CanonicalSteamDeckOutputStage).GetField("_systemButtonOverlay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var overlay = (SteamDeckSystemButtonOverlay)overlayField.GetValue(stage)!;
        Assert.Equal((byte)0, overlay.Apply(default).QuickAccess);
    }

    [Fact]
    public async Task RumbleFailureLeavesRoutingActiveWithoutCallbackOrFinalStop()
    {
        var session = new FakeCanonicalSession();
        var sink = new RecordingRumbleSink();
        sink.Results.Enqueue(new(PhysicalRumbleWriteStatus.Failed, "OpenFailed"));
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), sink: sink);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Contains("SetOutputCallback", session.Trace);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task BlockedPhysicalStopDoesNotBlockStructuralTeardown()
    {
        // Regression for the fail-close hang: the final motor STOP is best-effort. A failure to
        // deliver it must not make Steam Deck / VIIPER ownership uncertain, so it must not stop
        // callback clear, canonical device removal, or (transitively, via the pipeline's rollback
        // barrier) PhysicalInput/NativeMode rollback and PID1901 restoration from running.
        var session = new FakeCanonicalSession();
        var sink = new RecordingRumbleSink();
        sink.BlockWrites = true;
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), sink: sink);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var rollback = stage.RollbackMutationAsync(CancellationToken.None).AsTask();
        await sink.WriteEntered.Task;
        await session.DetachEntered.Task;
        sink.ReleaseWrite.TrySetResult();
        var result = await rollback;

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(1, session.ClearOutputCallbackCalls);
        Assert.Equal(1, session.RemoveCalls);
        Assert.Equal([TwoMotorRumble.Stopped], sink.Snapshot());
    }

    [Fact]
    public async Task FinalStopFailure_AllowsPipelineToReachPhysicalAndNativeRollback()
    {
        // Locks the actual bug: this real CanonicalSteamDeckOutputStage, driven through the real
        // RoutingPipelineExecutor (not a FakeStage), must return Success from its own rollback after
        // a final-STOP failure so the pipeline's SteamOutput rollback barrier does not stop PhysicalInput
        // and NativeMode rollback -- the path that restores stock XInput/PID1901 -- from running.
        var session = new FakeCanonicalSession();
        var sink = new RecordingRumbleSink();
        sink.Results.Enqueue(new(PhysicalRumbleWriteStatus.Failed, "device-lost"));

        var steam = Create(
            session,
            new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]),
            new FakeHidHide(),
            sink: sink);

        Assert.True((await steam.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await steam.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var trace = new List<RoutingStageKind>();
        var physical = new RollbackProbeStage(RoutingStageKind.PhysicalInput, trace);
        var native = new RollbackProbeStage(RoutingStageKind.NativeMode, trace);

        var executor = new RoutingPipelineExecutor([native, physical, steam]);
        var plan = RoutingPipelinePlan.AllDisabled with
        {
            NativeMode = RoutingStageMode.Enabled,
            PhysicalInput = RoutingStageMode.Enabled,
            SteamOutput = RoutingStageMode.Enabled
        };

        var result = await executor.RollbackAsync(plan, CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal([RoutingStageKind.PhysicalInput, RoutingStageKind.NativeMode], trace);
        Assert.Equal(1, session.ClearOutputCallbackCalls);
        Assert.Equal(1, session.RemoveCalls);
        var stopResult = await sink.WriteCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(PhysicalRumbleWriteStatus.Failed, stopResult.Status);
        Assert.Equal("device-lost", stopResult.Reason);
        Assert.Contains(TwoMotorRumble.Stopped, sink.Snapshot());
    }

    private sealed class RollbackProbeStage(RoutingStageKind kind, List<RoutingStageKind> trace) : IRoutingPipelineStage
    {
        public RoutingStageKind Kind => kind;
        public ValueTask<RoutingStageOperationResult> ObserveAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(RoutingStageOperationResult.Success());
        public ValueTask<RoutingStageOperationResult> PrepareMutationAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(RoutingStageOperationResult.Success());
        public ValueTask<RoutingStageOperationResult> ExecuteMutationAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(RoutingStageOperationResult.Success());
        public ValueTask<RoutingStageOperationResult> RollbackMutationAsync(CancellationToken cancellationToken)
        {
            trace.Add(kind);
            return ValueTask.FromResult(RoutingStageOperationResult.Success());
        }
    }

    [Fact]
    public async Task CallbackClearFailureDoesNotBlockStructuralTeardown()
    {
        var session = new FakeCanonicalSession { ClearOutputCallbackResult = false };
        var sink = new RecordingRumbleSink();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), sink: sink);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var first = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.True(first.Succeeded, first.Reason);
        Assert.Equal(1, session.ClearOutputCallbackCalls);
        Assert.Equal(1, session.RemoveCalls);

        session.ClearOutputCallbackResult = true;
        var second = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.True(second.Succeeded, second.Reason);
        Assert.Equal(1, session.ClearOutputCallbackCalls);
        Assert.Equal(1, session.RemoveCalls);
    }

    [Fact]
    public async Task AdmittedCallbackDoesNotBlockStageStopClearAndRemove()
    {
        var session = new FakeCanonicalSession();
        var order = new List<string>();
        session.ExternalTrace = order;
        var sink = new BlockingRumbleSink(order);
        var authority = new FeedbackAuthority();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), sink: sink, authority: authority);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        var callbackTask = Task.Run(() => Invoke(session.Callback!, RumbleReport(0x1234, 0x5678)));
        await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var rollbackTask = stage.RollbackMutationAsync(CancellationToken.None).AsTask();
        Assert.True((await rollbackTask).Succeeded);
        sink.Release.TrySetResult();
        await callbackTask;
        Assert.Equal(1, session.ClearOutputCallbackCalls);
        Assert.Equal(1, session.RemoveCalls);
        Assert.Contains("ClearOutputCallback", order);
    }

    [Fact]
    public async Task CallbackPausedBeforeLeaseIsRejectedAfterStageStop()
    {
        var session = new FakeCanonicalSession();
        var sink = new RecordingRumbleSink();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), sink: sink);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stage.FeedbackBeforeLease = () => { entered.TrySetResult(); release.Task.GetAwaiter().GetResult(); };
        var callbackTask = Task.Run(() => Invoke(session.Callback!, RumbleReport(0x1234, 0x5678)));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        release.TrySetResult();
        await callbackTask;
        Assert.DoesNotContain(new TwoMotorRumble(0x1234, 0x5678), sink.Snapshot());
    }

    [Fact]
    public async Task NeutralRejectionRetainsFailureOperationTimingAndLogsOnce()
    {
        var session = new FakeCanonicalSession { NeutralAccepted = false };
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot());
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Equal(1, log.Split("Event=SteamDeckOutputCreationFailed", StringSplitOptions.None).Length - 1);
        Assert.Contains("FailedOperation=NeutralReport", log);
        Assert.Contains("CanonicalSessionStartMs=", log);
        Assert.Contains("NeutralReportMs=", log);
        Assert.Equal(1, session.RemoveCalls);
    }

    [Fact]
    public async Task DetachDeviceFailureLogsRollbackTimingAndPreservesFailureResult()
    {
        var session = new FakeCanonicalSession { RemoveResult = false };
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot());
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var result = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("VirtualDeviceDetachFailed", result.Reason);
        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Equal(1, log.Split("Event=SteamDeckOutputRollbackFailed", StringSplitOptions.None).Length - 1);
        Assert.Contains("Reason=VirtualDeviceDetachFailed", log);
        Assert.Contains("DetachMs=", log);
    }

    [Fact]
    public async Task LivePublisherFaultRequestsOneFailClosedNotification()
    {
        var session = new FakeCanonicalSession { InputAccepted = false };
        var ticks = new ManualTicks();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot(), reportTicks: ticks);
        var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stage.SetOutputFaultHandler(() => { fault.TrySetResult(); return ValueTask.CompletedTask; });
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        ticks.Tick();
        await fault.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task LivePublisherFaultHandlerFailureIsObservedWithoutRetrying()
    {
        var session = new FakeCanonicalSession { InputAccepted = false };
        var ticks = new ManualTicks();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot(), reportTicks: ticks);
        var calls = 0;
        stage.SetOutputFaultHandler(() => { Interlocked.Increment(ref calls); throw new InvalidOperationException("fail-close failed"); });
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        ticks.Tick();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref calls) == 1, TimeSpan.FromSeconds(5)));
        await Task.Delay(100);
        Assert.Equal(1, calls);
        AppLog.DrainForTests();
        Assert.Contains("Reason=OutputFaultHandlerFailed", LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath));
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    // --- Composite-device (28DE:1205 is a COMPOSITE USB device: keyboard + mouse + controller
    // interfaces under one container) PnP identity resolution edge cases ------------------------

    [Fact]
    public async Task MultiplePnPNodesSharingSameContainerIdResolveToOneLogicalDeck()
    {
        var container = Guid.NewGuid();
        var root = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205\\ROOT", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["USB\\VID_28DE&PID_1205"], [], "USB", null, null, 0x28DE, 0x1205, true);
        var keyboardLeaf = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205&MI_00\\KBD", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["USB\\VID_28DE&PID_1205&MI_00"], [], "Keyboard", null, null, 0x28DE, 0x1205, true);
        var mouseLeaf = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205&MI_01\\MOUSE", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["USB\\VID_28DE&PID_1205&MI_01"], [], "Mouse", null, null, 0x28DE, 0x1205, true);
        var controllerLeaf = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205&MI_02\\CONTROLLER", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["USB\\VID_28DE&PID_1205&MI_02"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
        var keyboardHid = new ControllerDeviceInfo("HID\\VID_28DE&PID_1205&MI_00\\KBD", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "HID", ["HID\\VID_28DE&PID_1205&MI_00"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
        var mouseHid = new ControllerDeviceInfo("HID\\VID_28DE&PID_1205&MI_01\\MOUSE", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "HID", ["HID\\VID_28DE&PID_1205&MI_01"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
        var controllerHid = new ControllerDeviceInfo("HID\\VID_28DE&PID_1205&MI_02\\CONTROLLER", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "HID", ["HID\\VID_28DE&PID_1205&MI_02"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
        var session = new FakeCanonicalSession();
        var complete = new[] { UsbIpHost(), root, keyboardLeaf, mouseLeaf, controllerLeaf, keyboardHid, mouseHid, controllerHid };
        var stage = Create(session, new FakeEnumerator([[], complete, complete, []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task TwoDifferentContainerIdCandidateGroupsAreAmbiguous()
    {
        var containerA = Guid.NewGuid();
        var containerB = Guid.NewGuid();
        var leafA = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205\\A", containerA, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
        var leafB = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205\\B", containerB, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), leafA, leafB], []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("AmbiguousVirtualDeviceIdentity", result.Reason);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task PreExistingDeckNodeIsNotMistakenForANewlyAppearedCandidate()
    {
        // A 28DE:1205 node already present BEFORE the mutation (e.g. a leftover node from a prior
        // run) must not be treated as this attempt's newly created device -- the resolver only
        // considers devices new since the `before` snapshot.
        var preExisting = Device("USB\\VID_28DE&PID_1205\\PREEXISTING");
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[UsbIpHost(), preExisting], [UsbIpHost(), preExisting], [UsbIpHost(), preExisting]]), new FakeHidHide(), TimeSpan.FromMilliseconds(50));
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task DeckNodeWithNoUsbIpWin2AncestorIsRejected()
    {
        // A 28DE:1205 node with the exact right VID/PID but with no usbip-win2 host ancestor in its
        // device tree must be rejected, not resolved -- it cannot be an Addon-owned VIIPER device.
        var orphan = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205\\ORPHAN", Guid.NewGuid(), null, [], "USB", ["HID\\VID_28DE&PID_1205"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
        var session = new FakeCanonicalSession();
        // Trailing [] entries: FakeEnumerator clamps to the last provided state once its poll count
        // is exhausted, so rollback's own absence-verification polling (after the identity-resolution
        // timeout above has already consumed several) settles on genuine absence rather than the
        // orphan node appearing to persist forever.
        var stage = Create(session, new FakeEnumerator([[], [orphan], [orphan], []]), new FakeHidHide(), TimeSpan.FromMilliseconds(50));
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task LateArrivingSiblingsOfTheSameCompositeDeviceConvergeIntoOneFullyOwnedDeck()
    {
        // Steam Deck is a composite device: its Keyboard/Mouse/Controller HID interfaces can enumerate
        // as separate sibling PnP nodes with real-world timing skew between them. The Controller
        // interface appearing first is normal, in-progress composite enumeration -- NOT a resolved,
        // complete device -- so the resolver must keep polling and let the Keyboard and Mouse siblings
        // (same ContainerId) join the same logical group before declaring resolution complete. Once
        // the candidate set stops growing, all three siblings must resolve together as ONE fully-owned
        // logical Deck device, and teardown must then cleanly verify absence/ownership for all three.
        var container = Guid.NewGuid();
        var controllerLeaf = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205&MI_02\\CONTROLLER", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205&MI_02"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
        var keyboardLeaf = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205&MI_00\\KBD", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205&MI_00"], [], "Keyboard", null, null, 0x28DE, 0x1205, true);
        var mouseLeaf = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205&MI_01\\MOUSE", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205&MI_01"], [], "Mouse", null, null, 0x28DE, 0x1205, true);
        var root = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205\\ROOT", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["USB\\VID_28DE&PID_1205"], [], "USB", null, null, 0x28DE, 0x1205, true);
        var keyboardHid = new ControllerDeviceInfo("HID\\VID_28DE&PID_1205&MI_00\\KBD", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "HID", ["HID\\VID_28DE&PID_1205&MI_00"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
        var mouseHid = new ControllerDeviceInfo("HID\\VID_28DE&PID_1205&MI_01\\MOUSE", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "HID", ["HID\\VID_28DE&PID_1205&MI_01"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
        var controllerHid = new ControllerDeviceInfo("HID\\VID_28DE&PID_1205&MI_02\\CONTROLLER", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "HID", ["HID\\VID_28DE&PID_1205&MI_02"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([
            [], // before
            [UsbIpHost(), keyboardLeaf], // partial composite: not ready
            [UsbIpHost(), keyboardLeaf, mouseLeaf], // still partial
            [UsbIpHost(), root, keyboardLeaf, mouseLeaf, controllerLeaf, keyboardHid, mouseHid, controllerHid], // first complete composite identity
            [], // rollback: all three verified absent after native remove
        ]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.True(result.Succeeded, result.Reason);

        var cache = File.ReadAllText(CanonicalSteamDeckOutputStage.TestOnlyIdentityCachePath!);
        Assert.Contains("CONTROLLER", cache, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("KBD", cache, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MOUSE", cache, StringComparison.OrdinalIgnoreCase);

        var rollback = await stage.RollbackMutationAsync(CancellationToken.None);
        Assert.True(rollback.Succeeded, rollback.Reason);
        Assert.Equal(1, session.RemoveCalls);
    }

    [Fact]
    public async Task FirstValidDeckIdentityIsAcceptedWithoutStabilizationWindow()
    {
        // A genuinely different device (different ContainerId) appearing alongside an otherwise
        // valid candidate must still fail closed as Ambiguous; removing repeated snapshots must
        // not weaken ownership checks.
        var containerA = Guid.NewGuid();
        var containerB = Guid.NewGuid();
        var leafA = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205\\A", containerA, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
        var leafB = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205\\B", containerB, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([
            [], // before
            [UsbIpHost(), leafA, leafB], // ambiguity in the current snapshot fails immediately
            [], // rollback: both potential candidates verified absent
        ]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Ambiguous", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task StableDeckIdentityDoesNotRequireRepeatedSamples()
    {
        var leafA = Device("USB\\VID_28DE&PID_1205\\A");
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([
            [], [UsbIpHost(), leafA], [UsbIpHost(), leafA], []
        ]), new FakeHidHide(), TimeSpan.FromMilliseconds(4));
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task ValidLogicalDeckIdentityIsAcceptedOnFirstEvidence()
    {
        var first = Device("USB\\VID_28DE&PID_1205\\FIRST");
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([
            [], [UsbIpHost(), first], [UsbIpHost(), first], []
        ]), new FakeHidHide(), TimeSpan.FromMilliseconds(4));
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task CurrentExactCandidateIsAcceptedWithoutASecondSnapshot()
    {
        var candidate = Device("USB\\VID_28DE&PID_1205\\TRANSIENT");
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([
            [], [UsbIpHost(), candidate], []
        ]), new FakeHidHide(), TimeSpan.FromMilliseconds(4));
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task CandidateAppearingBeforeTimeoutCanResolveImmediately()
    {
        var candidate = Device("USB\\VID_28DE&PID_1205\\LATE");
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([
            [], [], [UsbIpHost(), candidate], [UsbIpHost(), candidate], []
        ]), new FakeHidHide(), TimeSpan.FromMilliseconds(100));
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    // --- Deck presentation pause/resume (SD7 foundation; no Game Bar/Xbox360 caller) -------------

    [Fact]
    public async Task PauseStopsLiveWritesBeforeWritingNeutral()
    {
        var session = new FakeCanonicalSession { BlockInput = true };
        var ticks = new ManualTicks();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot(), reportTicks: ticks);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        ticks.Tick();
        await session.InputEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // A live SetState is now blocked inside the fake session (BlockInput). PausePresentationAsync
        // must not proceed to write neutral while that in-flight call is still blocked.
        var pause = stage.PausePresentationAsync();

        // Deterministic: the live SetState is still blocked, so publisher StopAsync cannot
        // have completed and pause cannot have reached the second neutral write.
        Assert.False(pause.IsCompleted);
        Assert.Equal(1, session.Trace.Count(t => t == "Neutral"));

        session.ReleaseInput.TrySetResult();
        Assert.True(await pause);
        Assert.Equal(2, session.Trace.Count(t => t == "Neutral"));
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task NoPublicationWhilePaused()
    {
        var session = new FakeCanonicalSession();
        var ticks = new ManualTicks();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot(), reportTicks: ticks);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        Assert.True(await stage.PausePresentationAsync());
        var quickAccessCountAfterPause = session.QuickAccessValues.Count;

        // The publisher's tick loop is stopped, so no live waiter remains to consume a tick: driving
        // one must not be observed as another live SetState. The Tick() failure itself is the
        // deterministic proof -- no delay needed.
        Assert.Throws<InvalidOperationException>(() => ticks.Tick());
        Assert.Equal(quickAccessCountAfterPause, session.QuickAccessValues.Count);
        Assert.Equal(CanonicalSteamDeckSessionState.Active, session.State);

        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task ResumeRestartsSamePublisherAndSessionWithoutRecreation()
    {
        var session = new FakeCanonicalSession();
        var ticks = new ManualTicks();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot(), reportTicks: ticks);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        Assert.True(await stage.PausePresentationAsync());
        Assert.True(await stage.ResumePresentationAsync());
        Assert.Equal(1, session.Trace.Count(t => t == "Start"));

        ticks.Tick();
        await session.WaitForSetStateCountAsync(1);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task RepeatedPauseResumeDoesNotDetachOrRecreateSession()
    {
        var session = new FakeCanonicalSession();
        var ticks = new ManualTicks();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot(), reportTicks: ticks);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        Assert.True(await stage.PausePresentationAsync());
        Assert.True(await stage.ResumePresentationAsync());
        Assert.True(await stage.PausePresentationAsync());
        Assert.True(await stage.ResumePresentationAsync());

        Assert.Equal(0, session.RemoveCalls);
        Assert.Equal(1, session.Trace.Count(t => t == "Start"));
        Assert.DoesNotContain("Dispose", session.Trace);
        Assert.Equal(CanonicalSteamDeckSessionState.Active, session.State);

        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task RollbackFromPausedStillUsesFullTeardownPath()
    {
        var session = new FakeCanonicalSession();
        var ticks = new ManualTicks();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot(), reportTicks: ticks);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        Assert.True(await stage.PausePresentationAsync());

        var rollback = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.True(rollback.Succeeded, rollback.Reason);
        Assert.Equal(1, session.RemoveCalls);
        Assert.Contains("Remove", session.Trace);
        Assert.Contains("Dispose", session.Trace);
    }

    [Fact]
    public async Task PauseFailsWhenNotActive()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[]]), new FakeHidHide());
        Assert.False(await stage.PausePresentationAsync());
    }

    [Fact]
    public async Task ResumeFailsWhenNotPreviouslyPaused()
    {
        var session = new FakeCanonicalSession();
        var ticks = new ManualTicks();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot(), reportTicks: ticks);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        Assert.False(await stage.ResumePresentationAsync());

        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task NeutralRejectionDuringPauseFailsClosedWithoutRestartingPublisher()
    {
        var session = new FakeCanonicalSession();
        var ticks = new ManualTicks();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot(), reportTicks: ticks);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stage.SetOutputFaultHandler(() => { fault.TrySetResult(); return ValueTask.CompletedTask; });
        session.NeutralAccepted = false;

        var paused = await stage.PausePresentationAsync();

        Assert.False(paused);
        await fault.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Publisher was proven stopped and must not be silently restarted on this failure path.
        Assert.False(await stage.ResumePresentationAsync());

        session.NeutralAccepted = true;
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    private CanonicalSteamDeckOutputStage Create(ICanonicalSteamDeckSession session, IControllerDeviceEnumerator enumerator, FakeHidHide hid, TimeSpan? timeout = null, bool storeWriteFailsAfterSeed = false, IControllerStateSnapshotSource? snapshot = null, IInputReportTickSource? reportTicks = null, IPhysicalRumbleSink? sink = null, FeedbackAuthority? authority = null)
    {
        Directory.CreateDirectory(_directory);
        var store = new RecoveryJournalStore(Path.Combine(_directory, "recovery.json"));
        var recovery = new RecoveryManager(storeWriteFailsAfterSeed ? new FailingReplaceStore(store) : store);
        var journal = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, _session, DateTimeOffset.UtcNow, null, new());
        File.WriteAllText(Path.Combine(_directory, "recovery.json"), System.Text.Json.JsonSerializer.Serialize(journal));
        return new(() => session, enumerator, new(new SteamDeckVirtualDeviceIdentityPolicy()), new(), recovery, () => _session, hid, snapshot ?? new FakeSnapshot(), timeout, TimeSpan.FromMilliseconds(1), reportTicks ?? new ManualTicks(), authority ?? new FeedbackAuthority(), sink);
    }

    private CanonicalSteamDeckOutputStage CreateFactoryFailure(IControllerDeviceEnumerator enumerator, FakeHidHide hid)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "factory-failure-recovery.json");
        var store = new RecoveryJournalStore(path);
        var journal = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, _session, DateTimeOffset.UtcNow, null, new());
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(journal));
        return new(() => throw new InvalidOperationException("canonical DLL load failed"), enumerator, new(new SteamDeckVirtualDeviceIdentityPolicy()), new(), new RecoveryManager(store), () => _session, hid, new FakeSnapshot(), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1));
    }

    private const string UsbIpHostInstanceId = "ROOT\\USB\\0000";
    private static ControllerDeviceInfo UsbIpHost() => new(UsbIpHostInstanceId, null, null, [], "ROOT", ["ROOT\\USBIP_WIN2\\UDE"], [], "System", null, "usbip2_ude", null, null, true);
    private static IReadOnlyList<ControllerDeviceInfo> CanonicalDeckGroup(Guid container, string suffix)
    {
        var nodes = new List<ControllerDeviceInfo>
        {
            new($"USB\\VID_28DE&PID_1205\\{suffix}_ROOT", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["USB\\VID_28DE&PID_1205"], [], "USB", null, null, 0x28DE, 0x1205, true)
        };
        foreach (var (mi, name) in new[] { ("00", "KBD"), ("01", "MOUSE"), ("02", "CONTROLLER") })
        {
            nodes.Add(new($"USB\\VID_28DE&PID_1205&MI_{mi}\\{suffix}_{name}", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", [$"USB\\VID_28DE&PID_1205&MI_{mi}"], [], "USB", null, null, 0x28DE, 0x1205, true));
            nodes.Add(new($"HID\\VID_28DE&PID_1205&MI_{mi}\\{suffix}_{name}", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "HID", [$"HID\\VID_28DE&PID_1205&MI_{mi}"], [], "HIDClass", null, null, 0x28DE, 0x1205, true));
        }
        return nodes;
    }
    private static ControllerDeviceInfo Device(string id) => new(id.Contains('\\') ? id : $"USB\\VID_28DE&PID_1205\\{id}", Guid.Empty, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
    private static ControllerDeviceInfo DeviceWithContainer(string id, Guid container) => new(id, container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);

    public CanonicalSteamDeckOutputStageTests()
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
        CanonicalSteamDeckOutputStage.TestOnlyIdentityCachePath = Path.Combine(_directory, "steamdeck-pnp-cache.json");
    }

    public void Dispose()
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Off;
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        CanonicalSteamDeckOutputStage.TestOnlyIdentityCachePath = null;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class FakeEnumerator : IControllerDeviceEnumerator
    {
        private readonly IReadOnlyList<IReadOnlyList<ControllerDeviceInfo>> _states;
        private readonly bool _directLookup;
        private int _index;
        private IReadOnlyList<ControllerDeviceInfo> Current => _states[Math.Min(_index, _states.Count - 1)];
        public FakeEnumerator(IReadOnlyList<IReadOnlyList<ControllerDeviceInfo>> states, bool directLookup = false)
        {
            _directLookup = directLookup;
            _states = states.Select(CanonicalizeSyntheticState).ToArray();
        }
        public int DirectLookupCalls { get; private set; }
        public int EnumerateCalls { get; private set; }
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
        {
            EnumerateCalls++;
            return _states[Math.Min(_index++, _states.Count - 1)];
        }
        public ControllerDeviceInfo? FindPresentDevice(string instanceId)
        {
            DirectLookupCalls++;
            if (!_directLookup) return null;
            return Current.FirstOrDefault(device =>
                string.Equals(device.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        }
        public IReadOnlyList<string> EnumeratePresentInstanceIds(ushort vendorId, ushort productId)
        {
            var current = Current;
            if (_index < _states.Count - 1) _index++;
            return current.Where(device => device.Present && device.VendorId == vendorId && device.ProductId == productId)
                .Select(device => device.InstanceId).ToArray();
        }

        private static IReadOnlyList<ControllerDeviceInfo> CanonicalizeSyntheticState(IReadOnlyList<ControllerDeviceInfo> state)
        {
            var targets = state.Where(device => device.Present
                && device.VendorId == SteamDeckVirtualDeviceIdentityPolicy.VendorId
                && device.ProductId == SteamDeckVirtualDeviceIdentityPolicy.ProductId).ToArray();
            if (targets.Length == 0 || targets.Any(device => device.ContainerId != Guid.Empty)
                || targets.Any(device => device.InstanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase)))
                return state;

            // Keep one-node fixtures in the same parent-based logical group so their original
            // instance ID remains available for cache assertions. Multi-node fixtures retain
            // their independent synthetic identities for ambiguity/reconciliation coverage.
            var container = targets.All(device => device.ContainerId == Guid.Empty) ? Guid.Empty : Guid.NewGuid();
            var nodes = new List<ControllerDeviceInfo>();
            if (!targets.Any(device => device.InstanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)
                && device.InstanceId.Contains("VID_28DE&PID_1205\\", StringComparison.OrdinalIgnoreCase)
                && !device.InstanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase)))
            {
                nodes.Add(new("USB\\VID_28DE&PID_1205\\SYNTH_ROOT", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["USB\\VID_28DE&PID_1205"], [], "USB", null, null, 0x28DE, 0x1205, true));
            }
            foreach (var (mi, name) in new[] { ("00", "KBD"), ("01", "MOUSE"), ("02", "CONTROLLER") })
            {
                nodes.Add(new($"USB\\VID_28DE&PID_1205&MI_{mi}\\SYNTH_{name}", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", [$"USB\\VID_28DE&PID_1205&MI_{mi}"], [], "USB", null, null, 0x28DE, 0x1205, true));
                nodes.Add(new($"HID\\VID_28DE&PID_1205&MI_{mi}\\SYNTH_{name}", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "HID", [$"HID\\VID_28DE&PID_1205&MI_{mi}"], [], "HIDClass", null, null, 0x28DE, 0x1205, true));
            }
            return state.Concat(nodes).ToArray();
        }
    }

    // Returns [] for the very first call (the "before" snapshot) and thereafter either [deck] or []
    // depending on DeviceRemoved, regardless of how many times WaitForIdentityAsync polls.
    private sealed class DeckPresenceEnumerator(ControllerDeviceInfo deck) : IControllerDeviceEnumerator
    {
        private bool _beforeCallConsumed;
        public bool DeviceRemoved { get; set; }
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices()
        {
            if (!_beforeCallConsumed) { _beforeCallConsumed = true; return []; }
            return DeviceRemoved ? [] : [deck];
        }
    }

    private sealed class FakeCanonicalSession : ICanonicalSteamDeckSession
    {
        public List<string> Trace { get; } = [];
        public CanonicalPendingCleanupPhase? CleanupFailure { get; init; }
        public bool BusRemoved { get; init; } = true;
        public bool NeutralAccepted { get; set; } = true;
        public bool InputAccepted { get; init; } = true;
        public bool RemoveResult { get; set; } = true;
        public bool StartResult { get; init; } = true;
        public bool SetOutputCallbackResult { get; init; } = true;
        public bool ClearOutputCallbackResult { get; set; } = true;
        public bool BlockInput { get; init; }
        public Action? OnDetachDeviceCalled;
        public int RemoveCalls { get; private set; }
        public int ClearOutputCallbackCalls { get; private set; }
        public List<string>? ExternalTrace { get; set; }
        public SteamDeckOutputCallback? Callback { get; private set; }
        public TaskCompletionSource InputEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DetachEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseInput { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CanonicalSteamDeckSessionState State { get; private set; } = CanonicalSteamDeckSessionState.Clean;
        public USBDeviceAttachmentState AttachmentState { get; set; } = USBDeviceAttachmentState.Attached;
        public CanonicalPendingCleanupPhase PendingCleanupPhase { get; private set; }
        public uint? BusId => State == CanonicalSteamDeckSessionState.Clean ? null : 1u;
        public uint? LogicalDeviceId => State == CanonicalSteamDeckSessionState.Clean ? null : 7u;

        public bool Start() { Trace.Add("Start"); if (!StartResult) return false; State = CanonicalSteamDeckSessionState.Active; return true; }
        public bool TryGetTrackedAttachmentState(out USBDeviceAttachmentState state) { state = AttachmentState; return true; }

        // The stage calls SetNeutral() directly for its one-time neutral report before starting the
        // publisher; the publisher (constructed with this session as its sink) calls SetState()
        // directly for every live tick thereafter. Deliberately does NOT add to Trace: several tests
        // here (e.g. SessionPathUsesTypedPublisherAndCleanupOrder) exercise the real production
        // high-resolution-timer publisher with no manual tick source, so SetState can legitimately
        // fire an unbounded, non-deterministic number of times on a background thread between Neutral
        // and Remove -- tracing it there would make Trace assertions flaky. Ordering relative to
        // Neutral/Remove is instead observed via InputObservedAfterTraceCount below, using tests that
        // drive ticks manually (ManualTicks) for determinism.
        public int InputObservedAfterTraceCount { get; private set; } = -1;
        public List<byte> QuickAccessValues { get; } = [];
        public bool SetState(SteamDeckDeviceState state)
        {
            if (InputObservedAfterTraceCount < 0) InputObservedAfterTraceCount = Trace.Count;
            lock (QuickAccessValues) QuickAccessValues.Add(state.QuickAccess);
            ExternalTrace?.Add("PublisherLive");
            InputEntered.TrySetResult();
            if (BlockInput) ReleaseInput.Task.GetAwaiter().GetResult();
            return InputAccepted;
        }

        public async Task WaitForSetStateCountAsync(int count, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
            while (true)
            {
                lock (QuickAccessValues) { if (QuickAccessValues.Count >= count) return; }
                if (DateTime.UtcNow >= deadline) throw new TimeoutException($"SetState was not called {count} times within the timeout.");
                await Task.Delay(5);
            }
        }

        public bool SetNeutral()
        {
            Trace.Add("Neutral");
            ExternalTrace?.Add("Neutral");
            return NeutralAccepted;
        }

        public bool SetOutputCallback(SteamDeckOutputCallback callback) { Trace.Add("SetOutputCallback"); ExternalTrace?.Add("SetOutputCallback"); Callback = callback; return State == CanonicalSteamDeckSessionState.Active && SetOutputCallbackResult; }
        public bool ClearOutputCallback() { Trace.Add("ClearOutputCallback"); ExternalTrace?.Add("ClearOutputCallback"); ClearOutputCallbackCalls++; return State == CanonicalSteamDeckSessionState.Active && ClearOutputCallbackResult; }

        public bool DetachDevice()
        {
            Trace.Add("Remove");
            ExternalTrace?.Add("DetachDevice");
            RemoveCalls++;
            DetachEntered.TrySetResult();
            OnDetachDeviceCalled?.Invoke();
            // A known/classified remove failure (RemoveResult=false) leaves State unchanged (still
            // Active) so the stage classifies it as "VirtualDeviceRemoveFailed" rather than
            // "CanonicalSessionUnsafe" -- distinct from an actually-Unsafe session, which no test
            // fixture here needs to simulate.
            if (!RemoveResult)
            {
                PendingCleanupPhase = CanonicalPendingCleanupPhase.AttachmentDetach;
                State = CanonicalSteamDeckSessionState.CleanupPending;
                return false;
            }
            State = CanonicalSteamDeckSessionState.Clean;
            AttachmentState = USBDeviceAttachmentState.Detached;
            return true;
        }

        public bool RetryPendingCleanup()
        {
            Trace.Add($"Retry:{PendingCleanupPhase}");
            PendingCleanupPhase = CanonicalPendingCleanupPhase.None;
            State = CanonicalSteamDeckSessionState.Clean;
            return true;
        }

        public void Dispose() => Trace.Add("Dispose");
    }

    private sealed class FakeSnapshot : IControllerStateSnapshotSource
    { public ControllerState LatestState => new(new AuxiliaryButtonState([false, false])); }

    private sealed class RecordingRumbleSink : IPhysicalRumbleSink
    {
        private readonly object _gate = new();
        public List<TwoMotorRumble> Values { get; } = [];
        public Queue<PhysicalRumbleWriteResult> Results { get; } = new();
        public TaskCompletionSource<PhysicalRumbleWriteResult> WriteCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool BlockWrites { get; set; }
        public TaskCompletionSource WriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string>? Trace { get; set; }
        public TwoMotorRumble[] Snapshot() { lock (_gate) return [.. Values]; }
        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
        {
            lock (_gate) Values.Add(rumble);
            if (BlockWrites) { WriteEntered.TrySetResult(); ReleaseWrite.Task.GetAwaiter().GetResult(); }
            if (rumble == TwoMotorRumble.Stopped) Trace?.Add("RumblePreflightStop");
            var result = Results.Count > 0 ? Results.Dequeue() : new(PhysicalRumbleWriteStatus.Succeeded, "");
            WriteCompleted.TrySetResult(result);
            return result;
        }
    }

    private sealed class ThrowingCancelSink : IPhysicalRumbleSink
    {
        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble) => new(PhysicalRumbleWriteStatus.Succeeded, "OK");
        public void CancelPendingWrite() => throw new IOException("cancel failed");
    }

    private sealed class BlockingRumbleSink : IPhysicalRumbleSink
    {
        private readonly List<string> _order;
        public BlockingRumbleSink(List<string> order) => _order = order;
        public List<TwoMotorRumble> Values { get; } = [];
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
        {
            Values.Add(rumble);
            if (rumble != TwoMotorRumble.Stopped) { _order.Add("Nonzero"); Entered.TrySetResult(); Release.Task.GetAwaiter().GetResult(); }
            else _order.Add("Stop");
            return new(PhysicalRumbleWriteStatus.Succeeded, "");
        }
    }

    private static byte[] RumbleReport(ushort large, ushort small) => [0xEB, 9, 0, 0, 0, (byte)large, (byte)(large >> 8), (byte)small, (byte)(small >> 8), 2, 0];

    private static void Invoke(SteamDeckOutputCallback callback, byte[] report)
    {
        var memory = Marshal.AllocHGlobal(report.Length);
        try { Marshal.Copy(report, 0, memory, report.Length); callback(0, memory, (uint)report.Length); }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private sealed class ManualTicks : IInputReportTickSource
    {
        private readonly Queue<TaskCompletionSource<bool>> _waiters = new();
        public ValueTask<bool> WaitForTickAsync(CancellationToken token)
        { var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); _waiters.Enqueue(waiter); token.Register(() => waiter.TrySetCanceled(token)); return new(waiter.Task); }
        // Skips any already-completed (e.g. cancelled by a prior publisher StopAsync) waiter still
        // sitting in the queue, so a Tick() issued after a pause/resume cycle reaches the *current*
        // live await rather than silently no-op'ing on a stale one. Throws if no live waiter remains
        // at all -- e.g. immediately after a pause, before any resume -- which is itself the proof
        // that no publisher tick loop is currently awaiting a tick.
        public void Tick()
        {
            while (_waiters.Count > 0)
            {
                var waiter = _waiters.Dequeue();
                if (waiter.TrySetResult(true)) return;
            }
            throw new InvalidOperationException("No live tick waiter is currently pending.");
        }
    }

    private sealed class FakeHidHide : IHidHideClient
    { public HidHideInspection Inspection { get; init; } = new(HidHideInspectionStatus.Available, new HashSet<string>()); public HidHideInspection Inspect() => Inspection; public bool AddApplication(string p) => true; public bool RemoveApplication(string p) => true; public bool AddHiddenDevice(string p) => true; public bool RemoveHiddenDevice(string p) => true; }

    private sealed class FailingReplaceStore(RecoveryJournalStore inner) : IRecoveryJournalStore
    {
        public string JournalPath => inner.JournalPath;
        public bool Exists() => inner.Exists();
        public string ReadText() => inner.ReadText();
        public void WriteNew(RecoveryJournal value) => inner.WriteNew(value);
        public void ReplaceExisting(RecoveryJournal value) => throw new IOException("replace failed");
        public void Delete() => inner.Delete();
    }
}
