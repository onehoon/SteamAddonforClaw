using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Feedback;
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
    public async Task SessionPathUsesTypedPublisherAndCleanupOrder()
    {
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);

        var created = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.True(created.Succeeded, created.Reason);

        var rollback = await stage.RollbackMutationAsync(CancellationToken.None);
        Assert.True(rollback.Succeeded, rollback.Reason);
        Assert.Equal(["Start", "Neutral", "Remove", "CompleteCleanup", "Dispose"], session.Trace);
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
    public async Task BusRemovalRetryDoesNotReplayDeviceRemoval()
    {
        var session = new FakeCanonicalSession { CleanupFailure = CanonicalPendingCleanupPhase.BusRemoval };
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        Assert.False((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(1, session.RemoveCalls);
        Assert.Equal(["Start", "Neutral", "Remove", "CompleteCleanup", "Retry:BusRemoval", "Dispose"], session.Trace);
    }

    [Fact]
    public async Task ServerCloseRetryDoesNotReplayDeviceRemoval()
    {
        var session = new FakeCanonicalSession { CleanupFailure = CanonicalPendingCleanupPhase.ServerClose };
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        Assert.False((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(1, session.RemoveCalls);
        Assert.Equal(["Start", "Neutral", "Remove", "CompleteCleanup", "Retry:ServerClose", "Dispose"], session.Trace);
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
        var hidHide = new FakeHidHide { Inspection = new(HidHideInspectionStatus.Available, new HashSet<string>(), ["owned"]) };
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), hidHide);
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("HidHideOutputAlreadyBlocked", result.Reason);
        Assert.Contains("owned", hidHide.Inspection.HiddenDeviceEntries!);
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
        // during the attempt does NOT actually disappear after RemoveDevice() in this fixture --
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
        var session = new FakeCanonicalSession { OnRemoveDeviceCalled = () => enumerator.DeviceRemoved = true };
        var stage = Create(session, enumerator, new FakeHidHide(), TimeSpan.FromMilliseconds(50));
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Rollback=SteamDeckRemoved", result.Reason);
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

    // On the single canonical session path, a CompleteRuntimeCleanup() failure IS reported as a
    // rollback failure by design (see CanonicalSteamDeckOutputStage.RollbackCoreAsync's
    // "CanonicalSessionCleanupPending" branch) -- already covered by BusRemovalRetryDoesNotReplayDeviceRemoval
    // and ServerCloseRetryDoesNotReplayDeviceRemoval above.

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
    public async Task LivePublisherStartsAfterNeutralAndStopsBeforeDeviceRemoval()
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
    public async Task FeedbackCallbackRegistrationFailureRollsBackAfterNeutral()
    {
        var session = new FakeCanonicalSession { SetOutputCallbackResult = false };
        var sink = new RecordingRumbleSink();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), sink: sink);
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("SteamDeckFeedbackCallbackRegistrationFailed", result.Reason);
        Assert.Equal(["Start", "Neutral", "SetOutputCallback", "Remove", "CompleteCleanup", "Dispose"], session.Trace);
        Assert.Equal([TwoMotorRumble.Stopped], sink.Values);
    }

    [Fact]
    public async Task RumblePreflightFailureLeavesSteamDeckRoutingActiveWithoutCallback()
    {
        var session = new FakeCanonicalSession();
        var sink = new RecordingRumbleSink();
        sink.Results.Enqueue(new(PhysicalRumbleWriteStatus.Unavailable, "NoVerifiedEndpoint"));
        var ticks = new ManualTicks();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot(), reportTicks: ticks, sink: sink);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.DoesNotContain("SetOutputCallback", session.Trace);
        ticks.Tick();
        await session.InputEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Single(sink.Values);
    }

    [Fact]
    public async Task SuccessfulRumblePreflightPrecedesCallbackAndLivePublisher()
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
        Assert.Equal(["Neutral", "RumblePreflightStop", "SetOutputCallback", "PublisherLive"], trace);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task FailedRumblePreflightLeavesRoutingActiveWithoutCallbackOrFinalStop()
    {
        var session = new FakeCanonicalSession();
        var sink = new RecordingRumbleSink();
        sink.Results.Enqueue(new(PhysicalRumbleWriteStatus.Failed, "OpenFailed"));
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), sink: sink);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.DoesNotContain("SetOutputCallback", session.Trace);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Single(sink.Values);
    }

    [Fact]
    public async Task FinalStopFailureBlocksClearAndRemovalUntilRetry()
    {
        var session = new FakeCanonicalSession();
        var sink = new RecordingRumbleSink();
        sink.Results.Enqueue(new(PhysicalRumbleWriteStatus.Succeeded, "preflight"));
        sink.Results.Enqueue(new(PhysicalRumbleWriteStatus.Failed, "test"));
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), sink: sink);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var first = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.False(first.Succeeded);
        Assert.Equal("PhysicalRumbleFinalStopFailed", first.Reason);
        Assert.Equal(0, session.ClearOutputCallbackCalls);
        Assert.Equal(0, session.RemoveCalls);

        sink.Results.Enqueue(new(PhysicalRumbleWriteStatus.Succeeded, "retry"));
        var second = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.True(second.Succeeded, second.Reason);
        Assert.Equal(1, session.ClearOutputCallbackCalls);
        Assert.Equal(1, session.RemoveCalls);
        Assert.Equal([TwoMotorRumble.Stopped, TwoMotorRumble.Stopped, TwoMotorRumble.Stopped], sink.Values);
    }

    [Fact]
    public async Task CallbackClearFailureRetainsCallbackForRetry()
    {
        var session = new FakeCanonicalSession { ClearOutputCallbackResult = false };
        var sink = new RecordingRumbleSink();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), sink: sink);
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var first = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.False(first.Succeeded);
        Assert.Equal("SteamDeckFeedbackCallbackClearFailed", first.Reason);
        Assert.Equal(1, session.ClearOutputCallbackCalls);
        Assert.Equal(0, session.RemoveCalls);

        session.ClearOutputCallbackResult = true;
        var second = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.True(second.Succeeded, second.Reason);
        Assert.Equal(2, session.ClearOutputCallbackCalls);
        Assert.Equal(1, session.RemoveCalls);
        Assert.Equal([TwoMotorRumble.Stopped, TwoMotorRumble.Stopped, TwoMotorRumble.Stopped], sink.Values);
    }

    [Fact]
    public async Task AdmittedCallbackDrainsBeforeStageStopClearAndRemove()
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
        await authority.RevocationStarted.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, session.ClearOutputCallbackCalls);
        Assert.Equal(0, session.RemoveCalls);
        sink.Release.TrySetResult();
        await callbackTask;
        Assert.True((await rollbackTask).Succeeded);
        Assert.Equal([TwoMotorRumble.Stopped, new TwoMotorRumble(0x1234, 0x5678), TwoMotorRumble.Stopped], sink.Values);
        Assert.Equal(1, session.ClearOutputCallbackCalls);
        Assert.Equal(1, session.RemoveCalls);
        Assert.Equal(["Neutral", "Stop", "SetOutputCallback", "Nonzero", "Stop", "ClearOutputCallback", "RemoveDevice"], order);
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
        Assert.Equal([TwoMotorRumble.Stopped, TwoMotorRumble.Stopped], sink.Values);
        release.TrySetResult();
        await callbackTask;
        Assert.Equal([TwoMotorRumble.Stopped, TwoMotorRumble.Stopped], sink.Values);
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
    public async Task RemoveDeviceFailureLogsRollbackTimingAndPreservesFailureResult()
    {
        var session = new FakeCanonicalSession { RemoveResult = false };
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot());
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var result = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("VirtualDeviceRemoveFailed", result.Reason);
        AppLog.DrainForTests();
        var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Equal(1, log.Split("Event=SteamDeckOutputRollbackFailed", StringSplitOptions.None).Length - 1);
        Assert.Contains("Reason=VirtualDeviceRemoveFailed", log);
        Assert.Contains("RemoveDeviceMs=", log);
    }

    [Fact]
    public async Task LivePublisherFaultRequestsOneFailClosedNotification()
    {
        var session = new FakeCanonicalSession { InputAccepted = false };
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot());
        var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stage.SetOutputFaultHandler(() => { fault.TrySetResult(); return ValueTask.CompletedTask; });
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        await fault.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task LivePublisherFaultHandlerFailureIsObservedWithoutRetrying()
    {
        var session = new FakeCanonicalSession { InputAccepted = false };
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], [UsbIpHost(), Device("owned")], []]), new FakeHidHide(), snapshot: new FakeSnapshot());
        var calls = 0;
        stage.SetOutputFaultHandler(() => { Interlocked.Increment(ref calls); throw new InvalidOperationException("fail-close failed"); });
        await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
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
        var keyboardLeaf = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205\\KBD", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205"], [], "Keyboard", null, null, 0x28DE, 0x1205, true);
        var mouseLeaf = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205\\MOUSE", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205"], [], "Mouse", null, null, 0x28DE, 0x1205, true);
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([[], [UsbIpHost(), keyboardLeaf, mouseLeaf], [UsbIpHost(), keyboardLeaf, mouseLeaf], [UsbIpHost(), keyboardLeaf, mouseLeaf], []]), new FakeHidHide());
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
        var controllerLeaf = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205\\CONTROLLER", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
        var keyboardLeaf = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205\\KBD", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205"], [], "Keyboard", null, null, 0x28DE, 0x1205, true);
        var mouseLeaf = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205\\MOUSE", container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205"], [], "Mouse", null, null, 0x28DE, 0x1205, true);
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([
            [], // before
            [UsbIpHost(), controllerLeaf], // t1: Controller interface appears first
            [UsbIpHost(), controllerLeaf, keyboardLeaf], // t2: Keyboard sibling joins
            [UsbIpHost(), controllerLeaf, keyboardLeaf, mouseLeaf], // t3: Mouse sibling joins
            [UsbIpHost(), controllerLeaf, keyboardLeaf, mouseLeaf], // stable (2/3 consecutive)
            [UsbIpHost(), controllerLeaf, keyboardLeaf, mouseLeaf], // stable (3/3 consecutive) -> resolved
            [], // rollback: all three verified absent after native remove
        ]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);
        Assert.True(result.Succeeded, result.Reason);

        var rollback = await stage.RollbackMutationAsync(CancellationToken.None);
        Assert.True(rollback.Succeeded, rollback.Reason);
        Assert.Equal(1, session.RemoveCalls);
    }

    [Fact]
    public async Task DifferentContainerIdAppearingDuringStabilizationIsAmbiguous()
    {
        // Unlike a true sibling of the SAME composite device (same ContainerId, see the convergence
        // test above), a genuinely different device (different ContainerId) appearing WHILE the
        // resolver is still stabilizing an existing candidate group must still fail closed as
        // Ambiguous -- stabilization must not be mistaken for "wait indefinitely and merge anything
        // that shows up".
        var containerA = Guid.NewGuid();
        var containerB = Guid.NewGuid();
        var leafA = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205\\A", containerA, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
        var leafB = new ControllerDeviceInfo("USB\\VID_28DE&PID_1205\\B", containerB, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([
            [], // before
            [UsbIpHost(), leafA], // t1: group A candidate observed, stabilization begins
            [UsbIpHost(), leafA, leafB], // t2: a genuinely different ContainerId's node appears
            [], // rollback: both potential candidates verified absent
        ]), new FakeHidHide());
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("AmbiguousVirtualDeviceIdentity", result.Reason);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task SameCountWithDifferentContainerIdsNeverStabilizes()
    {
        var leafA = DeviceWithContainer("USB\\VID_28DE&PID_1205\\A", Guid.NewGuid());
        var leafB = DeviceWithContainer("USB\\VID_28DE&PID_1205\\B", Guid.NewGuid());
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([
            [], [UsbIpHost(), leafA], [UsbIpHost(), leafB], [UsbIpHost(), leafA], [UsbIpHost(), leafB], []
        ]), new FakeHidHide(), TimeSpan.FromMilliseconds(4));
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("VirtualDeviceIdentityDidNotStabilize", result.Reason);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task SameLogicalKeyWithDifferentInstanceIdsNeverStabilizes()
    {
        var container = Guid.NewGuid();
        var first = DeviceWithContainer("USB\\VID_28DE&PID_1205\\FIRST", container);
        var second = DeviceWithContainer("USB\\VID_28DE&PID_1205\\SECOND", container);
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([
            [], [UsbIpHost(), first], [UsbIpHost(), second], [UsbIpHost(), first], [UsbIpHost(), second], []
        ]), new FakeHidHide(), TimeSpan.FromMilliseconds(4));
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("VirtualDeviceIdentityDidNotStabilize", result.Reason);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task CandidateResolvedOnceThenDisappearingFailsClosedAtTimeout()
    {
        var candidate = Device("USB\\VID_28DE&PID_1205\\TRANSIENT");
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([
            [], [UsbIpHost(), candidate], [], [], []
        ]), new FakeHidHide(), TimeSpan.FromMilliseconds(4));
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("VirtualDeviceIdentityDidNotStabilize", result.Reason);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task CandidateAppearingTooLateForThreeStableSamplesFailsClosed()
    {
        var candidate = Device("USB\\VID_28DE&PID_1205\\LATE");
        var session = new FakeCanonicalSession();
        var stage = Create(session, new FakeEnumerator([
            [], [], [UsbIpHost(), candidate], [UsbIpHost(), candidate], []
        ]), new FakeHidHide(), TimeSpan.FromMilliseconds(2));
        await stage.PrepareMutationAsync(CancellationToken.None);

        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("VirtualDeviceIdentityDidNotStabilize", result.Reason);
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    private CanonicalSteamDeckOutputStage Create(FakeCanonicalSession session, IControllerDeviceEnumerator enumerator, FakeHidHide hid, TimeSpan? timeout = null, bool storeWriteFailsAfterSeed = false, IControllerStateSnapshotSource? snapshot = null, IInputReportTickSource? reportTicks = null, IPhysicalRumbleSink? sink = null, FeedbackAuthority? authority = null)
    {
        Directory.CreateDirectory(_directory);
        var store = new RecoveryJournalStore(Path.Combine(_directory, "recovery.json"));
        var recovery = new RecoveryManager(storeWriteFailsAfterSeed ? new FailingReplaceStore(store) : store);
        var journal = new RecoveryJournal(RecoveryManager.CurrentSchemaVersion, _session, DateTimeOffset.UtcNow, null, new());
        File.WriteAllText(Path.Combine(_directory, "recovery.json"), System.Text.Json.JsonSerializer.Serialize(journal));
        return new(() => session, enumerator, new(new SteamDeckVirtualDeviceIdentityPolicy()), new(), recovery, () => _session, hid, snapshot ?? new FakeSnapshot(), timeout, TimeSpan.FromMilliseconds(1), reportTicks, authority ?? new FeedbackAuthority(), sink);
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
    private static ControllerDeviceInfo Device(string id) => new(id, Guid.Empty, null, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);
    private static ControllerDeviceInfo DeviceWithContainer(string id, Guid container) => new(id, container, UsbIpHostInstanceId, [UsbIpHostInstanceId], "USB", ["HID\\VID_28DE&PID_1205"], [], "HIDClass", null, null, 0x28DE, 0x1205, true);

    public CanonicalSteamDeckOutputStageTests()
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
    }

    public void Dispose()
    {
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class FakeEnumerator(IReadOnlyList<IReadOnlyList<ControllerDeviceInfo>> states) : IControllerDeviceEnumerator
    { private int _index; public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => states[Math.Min(_index++, states.Count - 1)]; }

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
        public bool NeutralAccepted { get; init; } = true;
        public bool InputAccepted { get; init; } = true;
        public bool RemoveResult { get; init; } = true;
        public bool StartResult { get; init; } = true;
        public bool SetOutputCallbackResult { get; init; } = true;
        public bool ClearOutputCallbackResult { get; set; } = true;
        public bool BlockInput { get; init; }
        public Action? OnRemoveDeviceCalled;
        public int RemoveCalls { get; private set; }
        public int ClearOutputCallbackCalls { get; private set; }
        public List<string>? ExternalTrace { get; set; }
        public SteamDeckOutputCallback? Callback { get; private set; }
        public TaskCompletionSource InputEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseInput { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CanonicalSteamDeckSessionState State { get; private set; } = CanonicalSteamDeckSessionState.Clean;
        public CanonicalPendingCleanupPhase PendingCleanupPhase { get; private set; }
        public uint? BusId => State == CanonicalSteamDeckSessionState.Clean ? null : 1u;
        public uint? LogicalDeviceId => State == CanonicalSteamDeckSessionState.Clean ? null : 7u;

        public bool Start() { Trace.Add("Start"); if (!StartResult) return false; State = CanonicalSteamDeckSessionState.Active; return true; }

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
        public bool SetState(SteamDeckDeviceState state)
        {
            if (InputObservedAfterTraceCount < 0) InputObservedAfterTraceCount = Trace.Count;
            ExternalTrace?.Add("PublisherLive");
            InputEntered.TrySetResult();
            if (BlockInput) ReleaseInput.Task.GetAwaiter().GetResult();
            return InputAccepted;
        }

        public bool SetNeutral()
        {
            Trace.Add("Neutral");
            ExternalTrace?.Add("Neutral");
            return NeutralAccepted;
        }

        public bool SetOutputCallback(SteamDeckOutputCallback callback) { Trace.Add("SetOutputCallback"); ExternalTrace?.Add("SetOutputCallback"); Callback = callback; return State == CanonicalSteamDeckSessionState.Active && SetOutputCallbackResult; }
        public bool ClearOutputCallback() { Trace.Add("ClearOutputCallback"); ExternalTrace?.Add("ClearOutputCallback"); ClearOutputCallbackCalls++; return State == CanonicalSteamDeckSessionState.Active && ClearOutputCallbackResult; }

        public bool RemoveDevice()
        {
            Trace.Add("Remove");
            ExternalTrace?.Add("RemoveDevice");
            RemoveCalls++;
            OnRemoveDeviceCalled?.Invoke();
            // A known/classified remove failure (RemoveResult=false) leaves State unchanged (still
            // Active) so the stage classifies it as "VirtualDeviceRemoveFailed" rather than
            // "CanonicalSessionUnsafe" -- distinct from an actually-Unsafe session, which no test
            // fixture here needs to simulate.
            if (!RemoveResult) return false;
            State = CanonicalSteamDeckSessionState.DeviceRemoved;
            return true;
        }

        public bool RetryPendingCleanup()
        {
            Trace.Add($"Retry:{PendingCleanupPhase}");
            PendingCleanupPhase = CanonicalPendingCleanupPhase.None;
            State = CanonicalSteamDeckSessionState.Clean;
            return true;
        }

        public bool CompleteRuntimeCleanup()
        {
            Trace.Add("CompleteCleanup");
            if (!BusRemoved || CleanupFailure is { } failure)
            {
                PendingCleanupPhase = CleanupFailure ?? CanonicalPendingCleanupPhase.BusRemoval;
                State = CanonicalSteamDeckSessionState.CleanupPending;
                return false;
            }
            State = CanonicalSteamDeckSessionState.Clean;
            return true;
        }

        public void Dispose() => Trace.Add("Dispose");
    }

    private sealed class FakeSnapshot : IControllerStateSnapshotSource
    { public ControllerState LatestState => new(new AuxiliaryButtonState([false, false])); }

    private sealed class RecordingRumbleSink : IPhysicalRumbleSink
    {
        public List<TwoMotorRumble> Values { get; } = [];
        public Queue<PhysicalRumbleWriteResult> Results { get; } = new();
        public List<string>? Trace { get; set; }
        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble) { Values.Add(rumble); if (rumble == TwoMotorRumble.Stopped) Trace?.Add("RumblePreflightStop"); return Results.Count > 0 ? Results.Dequeue() : new(PhysicalRumbleWriteStatus.Succeeded, ""); }
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
        public void Tick() { Assert.NotEmpty(_waiters); _waiters.Dequeue().TrySetResult(true); }
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
