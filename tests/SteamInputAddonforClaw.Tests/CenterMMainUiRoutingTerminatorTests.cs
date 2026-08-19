using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.CenterM;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class CenterMMainUiRoutingTerminatorTests
{
    private const int TrackedPid = 4242;
    private const string ExpectedPath = @"C:\Program Files\WindowsApps\9426MICRO-STARINTERNATION.64797CC12EF8E_3.0.60630.0_x64__kzh8wxbdkxb8p\MSI Center M\MSI Center M.exe";

    private static CenterMRoutingTerminationEvidence ValidEvidence() => new(
        HandleStillValid: true,
        HandleProcessId: TrackedPid,
        ProcessAlive: true,
        CurrentProcessName: "MSI Center M",
        CurrentExecutablePath: ExpectedPath,
        FreshWindowSnapshot: new MainUiWindowSnapshot(true, 1, 0),
        AdditionalForeignMainUiExists: false);

    // -- The key difference from SafeMainUiTerminator: no SeenVisible field exists at all here, so
    // there is nothing to require -- a tray-resident MainUI that was never observed visible by this
    // process is a perfectly valid termination candidate for the routing use case. --

    [Fact]
    public void Evaluate_AllChecksPass_ReturnsTerminated() =>
        Assert.Equal(CenterMRoutingTerminationResult.Terminated,
            CenterMMainUiRoutingTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence()));

    [Fact]
    public void Evaluate_AlreadyExited() =>
        Assert.Equal(CenterMRoutingTerminationResult.AlreadyExited,
            CenterMMainUiRoutingTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence() with { ProcessAlive = false }));

    [Fact]
    public void Evaluate_VisibleAgain_BlocksTermination() =>
        Assert.Equal(CenterMRoutingTerminationResult.VisibleAgain,
            CenterMMainUiRoutingTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence() with { FreshWindowSnapshot = new MainUiWindowSnapshot(true, 1, 1) }));

    [Fact]
    public void Evaluate_PidMismatch_IsIdentityMismatch() =>
        Assert.Equal(CenterMRoutingTerminationResult.IdentityMismatch,
            CenterMMainUiRoutingTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence() with { HandleProcessId = TrackedPid + 1 }));

    [Fact]
    public void Evaluate_PathMismatch_IsIdentityMismatch() =>
        Assert.Equal(CenterMRoutingTerminationResult.IdentityMismatch,
            CenterMMainUiRoutingTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence() with { CurrentExecutablePath = @"C:\Users\someone\Desktop\MSI Center M.exe" }));

    [Fact]
    public void Evaluate_ForeignSameNameProcessExists() =>
        Assert.Equal(CenterMRoutingTerminationResult.AdditionalMainUiDetected,
            CenterMMainUiRoutingTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence() with { AdditionalForeignMainUiExists = true }));

    [Fact]
    public void Evaluate_ProcessExitsBetweenIdentityInspectionAndWindowCapture_IsAlreadyExited() =>
        // Win32MainUiWindowSnapshotProvider reports ProcessAlive: false when the process exits in
        // the window between the identity handle inspection and this fresh window capture -- that
        // must be classified as a benign natural exit, checked before visibility, never attempted
        // as a termination against an already-exited process.
        Assert.Equal(CenterMRoutingTerminationResult.AlreadyExited,
            CenterMMainUiRoutingTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true),
                ValidEvidence() with { ProcessAlive = true, FreshWindowSnapshot = new MainUiWindowSnapshot(false, 0, 0) }));

    [Fact]
    public void Evaluate_WindowEnumerationUncertain() =>
        Assert.Equal(CenterMRoutingTerminationResult.IdentityUncertain,
            CenterMMainUiRoutingTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence() with { FreshWindowSnapshot = null }));

    [Fact]
    public void Evaluate_SameNameEnumerationUncertain() =>
        Assert.Equal(CenterMRoutingTerminationResult.IdentityUncertain,
            CenterMMainUiRoutingTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, true), ValidEvidence() with { SameNameEnumerationUncertain = true }));

    [Fact]
    public void Evaluate_NoTerminateRights_IsAccessDenied() =>
        Assert.Equal(CenterMRoutingTerminationResult.AccessDenied,
            CenterMMainUiRoutingTerminator.Evaluate(TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, false), ValidEvidence()));

    [Fact]
    public void TryTerminate_AllFreshFactsGood_TerminatesExactlyOnce()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);
        var terminator = new CenterMMainUiRoutingTerminator(
            invoker,
            new FakeIdentityInspector(LiveProcessProbeStatus.Alive, TrackedPid, "MSI Center M", ExpectedPath),
            new FakeWindowProvider(new MainUiWindowSnapshot(true, 1, 0)),
            new FakeProcessSnapshotSource([new ProcessSnapshotEntry(TrackedPid, "MSI Center M", ExpectedPath)]));

        var result = terminator.TryTerminate(tracked, TimeSpan.FromSeconds(1));

        Assert.Equal(CenterMRoutingTerminationResult.Terminated, result);
        Assert.Equal(1, invoker.TerminateCallCount);
    }

    [Fact]
    public void TryTerminate_FreshWindowSnapshotShowsVisibleAgain_DoesNotTerminate()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);
        var terminator = new CenterMMainUiRoutingTerminator(
            invoker,
            new FakeIdentityInspector(LiveProcessProbeStatus.Alive, TrackedPid, "MSI Center M", ExpectedPath),
            new FakeWindowProvider(new MainUiWindowSnapshot(true, 1, 1)),
            new FakeProcessSnapshotSource([new ProcessSnapshotEntry(TrackedPid, "MSI Center M", ExpectedPath)]));

        var result = terminator.TryTerminate(tracked, TimeSpan.FromSeconds(1));

        Assert.Equal(CenterMRoutingTerminationResult.VisibleAgain, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public void TryTerminate_WaitTimesOut_ReportsWaitTimedOut()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var terminator = new CenterMMainUiRoutingTerminator(
            new RecordingInvoker(terminateSucceeds: true, waitSucceeds: false),
            new FakeIdentityInspector(LiveProcessProbeStatus.Alive, TrackedPid, "MSI Center M", ExpectedPath),
            new FakeWindowProvider(new MainUiWindowSnapshot(true, 1, 0)),
            new FakeProcessSnapshotSource([new ProcessSnapshotEntry(TrackedPid, "MSI Center M", ExpectedPath)]));

        var result = terminator.TryTerminate(tracked, TimeSpan.FromMilliseconds(1));

        Assert.Equal(CenterMRoutingTerminationResult.WaitTimedOut, result);
    }

    [Fact]
    public void TryTerminate_TerminateDeniedButExactHandleAlreadySignaled_IsAlreadyExited()
    {
        // TerminateProcess can legitimately fail (e.g. ERROR_ACCESS_DENIED) when the target already
        // exited naturally in the race between fresh-evidence capture and this call -- the exact
        // retained handle's own WaitForExit signal is still authoritative and must not be skipped
        // just because the mutation request itself returned false.
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: false, waitSucceeds: true);
        var terminator = new CenterMMainUiRoutingTerminator(
            invoker,
            new FakeIdentityInspector(LiveProcessProbeStatus.Alive, TrackedPid, "MSI Center M", ExpectedPath),
            new FakeWindowProvider(new MainUiWindowSnapshot(true, 1, 0)),
            new FakeProcessSnapshotSource([new ProcessSnapshotEntry(TrackedPid, "MSI Center M", ExpectedPath)]));

        var result = terminator.TryTerminate(tracked, TimeSpan.FromSeconds(1));

        Assert.Equal(CenterMRoutingTerminationResult.AlreadyExited, result);
        Assert.Equal(1, invoker.TerminateCallCount);
        Assert.Equal(1, invoker.WaitForExitCallCount);
    }

    [Fact]
    public void TryTerminate_TerminateDeniedAndExactHandleNeverSignals_IsFailed()
    {
        // A real termination failure -- distinct from the already-exited race above -- must never be
        // silently downgraded into a clean exit just because WaitForExit was still called.
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: false, waitSucceeds: false);
        var terminator = new CenterMMainUiRoutingTerminator(
            invoker,
            new FakeIdentityInspector(LiveProcessProbeStatus.Alive, TrackedPid, "MSI Center M", ExpectedPath),
            new FakeWindowProvider(new MainUiWindowSnapshot(true, 1, 0)),
            new FakeProcessSnapshotSource([new ProcessSnapshotEntry(TrackedPid, "MSI Center M", ExpectedPath)]));

        var result = terminator.TryTerminate(tracked, TimeSpan.FromMilliseconds(1));

        Assert.Equal(CenterMRoutingTerminationResult.Failed, result);
        Assert.Equal(1, invoker.TerminateCallCount);
        Assert.Equal(1, invoker.WaitForExitCallCount);
    }

    private sealed class RecordingInvoker(bool terminateSucceeds, bool waitSucceeds) : ITerminateProcessInvoker
    {
        internal int TerminateCallCount { get; private set; }
        internal int WaitForExitCallCount { get; private set; }

        public bool TryTerminate(SafeProcessHandle handle, out int win32Error)
        {
            TerminateCallCount++;
            win32Error = terminateSucceeds ? 0 : 5;
            return terminateSucceeds;
        }

        public bool WaitForExit(SafeProcessHandle handle, TimeSpan timeout)
        {
            WaitForExitCallCount++;
            return waitSucceeds;
        }
    }

    private sealed class FakeIdentityInspector(LiveProcessProbeStatus status, int? processId, string? processName, string? executablePath) : IProcessIdentityInspector
    {
        public LiveProcessIdentity Inspect(SafeProcessHandle handle) => new(status, processId, processName, executablePath);
    }

    private sealed class FakeWindowProvider(MainUiWindowSnapshot? snapshot) : IMainUiWindowSnapshotProvider
    {
        public MainUiWindowSnapshot? Capture(int processId) => snapshot;
    }

    private sealed class FakeProcessSnapshotSource(IReadOnlyList<ProcessSnapshotEntry>? entries) : IProcessSnapshotSource
    {
        public IReadOnlyList<ProcessSnapshotEntry>? GetProcessesByName(string processName) => entries;
    }
}
