using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Feedback;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Routing;
using SteamInputAddonforClaw.VirtualOutput.Viiper;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CanonicalSteamDeckOutputStageTests
{
    [Fact]
    public async Task Existing_exact_deck_target_blocks_attach_before_viiper()
    {
        var session = new FakeSession();
        var stage = Create(session, new FakeEnumerator([Deck("present")]));

        var result = await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal("SteamDeckOutputConflict", result.Reason);
        Assert.Empty(session.Trace);
    }

    [Fact]
    public async Task Conflict_inspection_failure_blocks_attach()
    {
        var session = new FakeSession();
        var stage = Create(session, new FakeEnumerator(throwOnEnumerate: true));

        var result = await stage.PrepareMutationAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal("SteamDeckOutputConflictInspectionUnavailable", result.Reason);
        Assert.Empty(session.Trace);
    }

    [Fact]
    public async Task Successful_attach_does_not_wait_for_post_attach_pnp()
    {
        var session = new FakeSession();
        var enumerator = new FakeEnumerator([]);
        var stage = Create(session, enumerator);

        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(1, enumerator.EnumerateCalls);
        Assert.Equal(["Start", "Neutral"], session.Trace);

        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(["Start", "Neutral", "Detach", "Dispose"], session.Trace);
    }

    [Fact]
    public async Task Reconcile_fails_when_viiper_reports_detached()
    {
        var session = new FakeSession { AttachmentState = USBDeviceAttachmentState.Detached };
        var stage = Create(session, new FakeEnumerator([]));

        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var result = await stage.ReconcileOwnedStateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("SteamDeckAttachmentNotAttached", result.Reason);
    }

    [Fact]
    public async Task Neutral_rejection_rolls_back_without_starting_publisher()
    {
        var session = new FakeSession { NeutralAccepted = false };
        var stage = Create(session, new FakeEnumerator([]));

        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        var result = await stage.ExecuteMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("NeutralReportRejected", result.Reason);
        Assert.Equal(["Start", "Neutral", "Detach", "Dispose"], session.Trace);
    }

    [Fact]
    public async Task Retryable_detach_keeps_cleanup_pending_until_retry_succeeds()
    {
        var session = new FakeSession { DetachResult = false };
        var stage = Create(session, new FakeEnumerator([]));

        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var first = await stage.RollbackMutationAsync(CancellationToken.None);
        Assert.False(first.Succeeded);
        Assert.Equal(CanonicalSteamDeckSessionState.CleanupPending, session.State);
        Assert.DoesNotContain("Dispose", session.Trace);

        session.DetachResult = true;
        var second = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.True(second.Succeeded, second.Reason);
        Assert.Equal(CanonicalSteamDeckSessionState.Clean, session.State);
        Assert.Equal(2, session.DetachCalls);
        Assert.Contains("Dispose", session.Trace);
    }

    [Fact]
    public async Task Unsafe_detach_fails_closed_without_disposing_session()
    {
        var session = new FakeSession { DetachResult = false, UnsafeOnDetach = true };
        var stage = Create(session, new FakeEnumerator([]));

        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var result = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("CanonicalSessionUnsafe", result.Reason);
        Assert.DoesNotContain("Dispose", session.Trace);
    }

    [Fact]
    public async Task Publisher_fault_requests_one_fail_closed_notification()
    {
        var session = new FakeSession { InputAccepted = false };
        var ticks = new ManualTickSource();
        var stage = Create(session, new FakeEnumerator([]), ticks);
        var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        stage.SetOutputFaultHandler(() => { fault.TrySetResult(); return ValueTask.CompletedTask; });

        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        ticks.Tick();

        await fault.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task Pause_and_resume_reuse_the_same_viiper_session()
    {
        var session = new FakeSession();
        var stage = Create(session, new FakeEnumerator([]));

        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True(await stage.PausePresentationAsync(reportOutputFaultOnFailure: false));
        Assert.True(await stage.ResumePresentationAsync());
        Assert.Equal(1, session.Trace.Count(value => value == "Start"));
        Assert.Equal(CanonicalSteamDeckSessionState.Active, session.State);

        Assert.True((await stage.RollbackMutationAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task Teardown_continues_when_callback_cleanup_is_rejected()
    {
        var session = new FakeSession { ClearOutputCallbackResult = false };
        var sink = new RecordingRumbleSink();
        var stage = new CanonicalSteamDeckOutputStage(
            () => session,
            new FakeEnumerator([]),
            new FakeSnapshot(),
            new BlockingTickSource(),
            new FeedbackAuthority(),
            sink);

        Assert.True((await stage.PrepareMutationAsync(CancellationToken.None)).Succeeded);
        Assert.True((await stage.ExecuteMutationAsync(CancellationToken.None)).Succeeded);

        var result = await stage.RollbackMutationAsync(CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Contains("ClearCallback", session.Trace);
        Assert.Contains("Detach", session.Trace);
        Assert.Contains("Dispose", session.Trace);
    }

    private static CanonicalSteamDeckOutputStage Create(FakeSession session, FakeEnumerator enumerator, IInputReportTickSource? ticks = null) =>
        new(() => session, enumerator, new FakeSnapshot(), ticks ?? new BlockingTickSource());

    private static ControllerDeviceInfo Deck(string id) => new($"USB\\VID_28DE&PID_1205\\{id}", null, null, [], "USB", [], [], "HID", null, null, 0x28DE, 0x1205, true);

    private sealed class FakeEnumerator(IReadOnlyList<ControllerDeviceInfo>? devices = null, bool throwOnEnumerate = false) : IControllerDeviceEnumerator
    {
        public int EnumerateCalls { get; private set; }
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices() => devices ?? [];
        public IReadOnlyList<ControllerDeviceInfo> EnumeratePresentDevices(ushort vendorId, ushort productId)
        {
            EnumerateCalls++;
            if (throwOnEnumerate) throw new InvalidOperationException("enumeration failed");
            return devices ?? [];
        }
    }

    private sealed class BlockingTickSource : IInputReportTickSource
    {
        public async ValueTask<bool> WaitForTickAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return false;
        }
    }

    private sealed class ManualTickSource : IInputReportTickSource
    {
        private TaskCompletionSource<bool> _next = NewCompletionSource();

        public ValueTask<bool> WaitForTickAsync(CancellationToken cancellationToken) =>
            new(_next.Task.WaitAsync(cancellationToken));

        public void Tick()
        {
            var next = Interlocked.Exchange(ref _next, NewCompletionSource());
            next.TrySetResult(true);
        }

        private static TaskCompletionSource<bool> NewCompletionSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class FakeSnapshot : IControllerStateSnapshotSource { public ControllerState LatestState => default; }

    private sealed class FakeSession : ICanonicalSteamDeckSession
    {
        public List<string> Trace { get; } = [];
        public CanonicalSteamDeckSessionState State { get; private set; } = CanonicalSteamDeckSessionState.Clean;
        public CanonicalPendingCleanupPhase PendingCleanupPhase { get; private set; }
        public uint? BusId => State == CanonicalSteamDeckSessionState.Clean ? null : 1;
        public uint? LogicalDeviceId => State == CanonicalSteamDeckSessionState.Clean ? null : 2;
        public USBDeviceAttachmentState AttachmentState { get; set; } = USBDeviceAttachmentState.Attached;
        public bool NeutralAccepted { get; set; } = true;
        public bool InputAccepted { get; set; } = true;
        public bool DetachResult { get; set; } = true;
        public bool UnsafeOnDetach { get; set; }
        public bool ClearOutputCallbackResult { get; set; } = true;
        public int DetachCalls { get; private set; }
        public bool Start() { Trace.Add("Start"); State = CanonicalSteamDeckSessionState.Active; return true; }
        public bool SetState(SteamDeckDeviceState state) { Trace.Add(state.Equals(default(SteamDeckDeviceState)) ? "Neutral" : "State"); return State == CanonicalSteamDeckSessionState.Active && InputAccepted; }
        public bool SetNeutral() { Trace.Add("Neutral"); return State == CanonicalSteamDeckSessionState.Active && NeutralAccepted; }
        public bool SetOutputCallback(SteamDeckOutputCallback callback) { Trace.Add("Callback"); return true; }
        public bool ClearOutputCallback() { Trace.Add("ClearCallback"); return ClearOutputCallbackResult; }
        public bool DetachDevice()
        {
            Trace.Add("Detach");
            DetachCalls++;
            if (UnsafeOnDetach)
            {
                State = CanonicalSteamDeckSessionState.Unsafe;
                PendingCleanupPhase = CanonicalPendingCleanupPhase.None;
                return false;
            }
            if (!DetachResult)
            {
                State = CanonicalSteamDeckSessionState.CleanupPending;
                PendingCleanupPhase = CanonicalPendingCleanupPhase.AttachmentDetach;
                return false;
            }
            State = CanonicalSteamDeckSessionState.Clean;
            PendingCleanupPhase = CanonicalPendingCleanupPhase.None;
            return true;
        }
        public bool RetryPendingCleanup() => DetachDevice();
        public bool TryGetTrackedAttachmentState(out USBDeviceAttachmentState state) { state = AttachmentState; return State == CanonicalSteamDeckSessionState.Active; }
        public void Dispose() => Trace.Add("Dispose");
    }

    private sealed class RecordingRumbleSink : IPhysicalRumbleSink
    {
        public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble) => new(PhysicalRumbleWriteStatus.Succeeded, "OK");
    }
}
