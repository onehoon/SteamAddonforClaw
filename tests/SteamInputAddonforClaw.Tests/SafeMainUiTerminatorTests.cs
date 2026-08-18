using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class SafeMainUiTerminatorTests
{
    private const int TrackedPid = 4242;
    private const string ExpectedPath = @"C:\Program Files\WindowsApps\9426MICRO-STARINTERNATION.64797CC12EF8E_3.0.60630.0_x64__kzh8wxbdkxb8p\MSI Center M\MSI Center M.exe";

    private static SafeMainUiTerminationEvidence ValidEvidence() => new(
        HandleStillValid: true,
        HandleProcessId: TrackedPid,
        ProcessAlive: true,
        CurrentProcessName: "MSI Center M",
        CurrentExecutablePath: ExpectedPath,
        SeenVisible: true,
        FreshWindowSnapshot: new MainUiWindowSnapshot(true, 1, 0),
        AdditionalForeignMainUiExists: false);

    // -- Pure precondition decision (Evaluate): each individual failure reason, independent of
    // however the evidence was captured. --

    [Fact]
    public void Evaluate_AllChecksPass_ReturnsTerminated() =>
        Assert.Equal(SafeMainUiTerminationResult.Terminated,
            SafeMainUiTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence()));

    [Fact]
    public void Evaluate_AlreadyExited() =>
        Assert.Equal(SafeMainUiTerminationResult.AlreadyExited,
            SafeMainUiTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence() with { ProcessAlive = false }));

    [Fact]
    public void Evaluate_NeverSeenVisible_IsIdentityMismatch() =>
        Assert.Equal(SafeMainUiTerminationResult.IdentityMismatch,
            SafeMainUiTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence() with { SeenVisible = false }));

    [Fact]
    public void Evaluate_VisibleAgain() =>
        Assert.Equal(SafeMainUiTerminationResult.VisibleAgain,
            SafeMainUiTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence() with { FreshWindowSnapshot = new MainUiWindowSnapshot(true, 1, 1) }));

    [Fact]
    public void Evaluate_PidMismatch_IsIdentityMismatch() =>
        Assert.Equal(SafeMainUiTerminationResult.IdentityMismatch,
            SafeMainUiTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence() with { HandleProcessId = TrackedPid + 1 }));

    [Fact]
    public void Evaluate_HandleInvalid_IsIdentityMismatch() =>
        Assert.Equal(SafeMainUiTerminationResult.IdentityMismatch,
            SafeMainUiTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence() with { HandleStillValid = false }));

    [Fact]
    public void Evaluate_PathMismatch_IsIdentityMismatch() =>
        Assert.Equal(SafeMainUiTerminationResult.IdentityMismatch,
            SafeMainUiTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence() with { CurrentExecutablePath = @"C:\Users\someone\Desktop\MSI Center M.exe" }));

    [Fact]
    public void Evaluate_PathUnreadable_IsIdentityMismatch() =>
        Assert.Equal(SafeMainUiTerminationResult.IdentityMismatch,
            SafeMainUiTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence() with { CurrentExecutablePath = null }));

    [Fact]
    public void Evaluate_ProcessNameMismatch_IsIdentityMismatch() =>
        Assert.Equal(SafeMainUiTerminationResult.IdentityMismatch,
            SafeMainUiTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence() with { CurrentProcessName = "notepad" }));

    [Fact]
    public void Evaluate_ForeignSameNameProcessExists() =>
        Assert.Equal(SafeMainUiTerminationResult.AdditionalMainUiDetected,
            SafeMainUiTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence() with { AdditionalForeignMainUiExists = true }));

    [Fact]
    public void Evaluate_WindowEnumerationUncertain() =>
        Assert.Equal(SafeMainUiTerminationResult.IdentityUncertain,
            SafeMainUiTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence() with { FreshWindowSnapshot = null }));

    [Fact]
    public void Evaluate_NoTerminateRights_IsAccessDenied() =>
        Assert.Equal(SafeMainUiTerminationResult.AccessDenied,
            SafeMainUiTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, false), ValidEvidence()));

    // -- TryTerminate: proves the fresh capture itself drives the decision, never a stale
    // caller-supplied snapshot. Each fake seam is configured to disagree with what a stale
    // "everything looked fine a moment ago" assumption would have been. --

    [Fact]
    public void TryTerminate_AllFreshFactsGood_TerminatesExactlyOnce()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var terminator = new SafeMainUiTerminator(
            new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true),
            new FakeIdentityInspector(LiveProcessProbeStatus.Alive, TrackedPid, "MSI Center M", ExpectedPath),
            new FakeWindowProvider(new MainUiWindowSnapshot(true, 1, 0)),
            new FakeProcessSnapshotSource([new ProcessSnapshotEntry(TrackedPid, "MSI Center M", ExpectedPath)]));

        var result = terminator.TryTerminate(tracked, seenVisible: true, TimeSpan.FromSeconds(1));

        Assert.Equal(SafeMainUiTerminationResult.Terminated, result);
    }

    [Fact]
    public void TryTerminate_FreshWindowSnapshotShowsVisibleAgain_OverridesStaleAssumption_DoesNotTerminate()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);
        var terminator = new SafeMainUiTerminator(
            invoker,
            new FakeIdentityInspector(LiveProcessProbeStatus.Alive, TrackedPid, "MSI Center M", ExpectedPath),
            new FakeWindowProvider(new MainUiWindowSnapshot(true, 1, 1)), // fresh: visible again
            new FakeProcessSnapshotSource([new ProcessSnapshotEntry(TrackedPid, "MSI Center M", ExpectedPath)]));

        var result = terminator.TryTerminate(tracked, seenVisible: true, TimeSpan.FromSeconds(1));

        Assert.Equal(SafeMainUiTerminationResult.VisibleAgain, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public void TryTerminate_FreshEnumerationShowsForeignProcess_OverridesStaleAssumption_DoesNotTerminate()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);
        var terminator = new SafeMainUiTerminator(
            invoker,
            new FakeIdentityInspector(LiveProcessProbeStatus.Alive, TrackedPid, "MSI Center M", ExpectedPath),
            new FakeWindowProvider(new MainUiWindowSnapshot(true, 1, 0)),
            new FakeProcessSnapshotSource([
                new ProcessSnapshotEntry(TrackedPid, "MSI Center M", ExpectedPath),
                new ProcessSnapshotEntry(TrackedPid + 1, "MSI Center M", @"C:\Program Files\WindowsApps\...\MSI Center M\MSI Center M.exe")]));

        var result = terminator.TryTerminate(tracked, seenVisible: true, TimeSpan.FromSeconds(1));

        Assert.Equal(SafeMainUiTerminationResult.AdditionalMainUiDetected, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public void TryTerminate_IdentityInspectorReportsExited_ReturnsAlreadyExited_DoesNotTerminate()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);
        var terminator = new SafeMainUiTerminator(
            invoker,
            new FakeIdentityInspector(LiveProcessProbeStatus.Exited, null, null, null),
            new FakeWindowProvider(new MainUiWindowSnapshot(true, 1, 0)),
            new FakeProcessSnapshotSource([]));

        var result = terminator.TryTerminate(tracked, seenVisible: true, TimeSpan.FromSeconds(1));

        Assert.Equal(SafeMainUiTerminationResult.AlreadyExited, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public void TryTerminate_IdentityInspectorUncertain_ReturnsIdentityMismatch_DoesNotTerminate()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);
        var terminator = new SafeMainUiTerminator(
            invoker,
            new FakeIdentityInspector(LiveProcessProbeStatus.Uncertain, null, null, null),
            new FakeWindowProvider(new MainUiWindowSnapshot(true, 1, 0)),
            new FakeProcessSnapshotSource([]));

        var result = terminator.TryTerminate(tracked, seenVisible: true, TimeSpan.FromSeconds(1));

        Assert.Equal(SafeMainUiTerminationResult.IdentityMismatch, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public void TryTerminate_NativeTerminateFails_ReportsFailed()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var terminator = new SafeMainUiTerminator(
            new RecordingInvoker(terminateSucceeds: false, waitSucceeds: true),
            new FakeIdentityInspector(LiveProcessProbeStatus.Alive, TrackedPid, "MSI Center M", ExpectedPath),
            new FakeWindowProvider(new MainUiWindowSnapshot(true, 1, 0)),
            new FakeProcessSnapshotSource([new ProcessSnapshotEntry(TrackedPid, "MSI Center M", ExpectedPath)]));

        var result = terminator.TryTerminate(tracked, seenVisible: true, TimeSpan.FromSeconds(1));

        Assert.Equal(SafeMainUiTerminationResult.Failed, result);
    }

    [Fact]
    public void TryTerminate_WaitTimesOut_ReportsWaitTimedOut()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var terminator = new SafeMainUiTerminator(
            new RecordingInvoker(terminateSucceeds: true, waitSucceeds: false),
            new FakeIdentityInspector(LiveProcessProbeStatus.Alive, TrackedPid, "MSI Center M", ExpectedPath),
            new FakeWindowProvider(new MainUiWindowSnapshot(true, 1, 0)),
            new FakeProcessSnapshotSource([new ProcessSnapshotEntry(TrackedPid, "MSI Center M", ExpectedPath)]));

        var result = terminator.TryTerminate(tracked, seenVisible: true, TimeSpan.FromMilliseconds(1));

        Assert.Equal(SafeMainUiTerminationResult.WaitTimedOut, result);
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

    private sealed class FakeIdentityInspector(LiveProcessProbeStatus status, int? processId, string? processName, string? executablePath) : IProcessIdentityInspector
    {
        public LiveProcessIdentity Inspect(SafeProcessHandle handle) => new(status, processId, processName, executablePath);
    }

    private sealed class FakeWindowProvider(MainUiWindowSnapshot? snapshot) : IMainUiWindowSnapshotProvider
    {
        public MainUiWindowSnapshot? Capture(int processId) => snapshot;
    }

    private sealed class FakeProcessSnapshotSource(IReadOnlyList<ProcessSnapshotEntry> entries) : IProcessSnapshotSource
    {
        public IReadOnlyList<ProcessSnapshotEntry> GetProcessesByName(string processName) => entries;
    }
}
