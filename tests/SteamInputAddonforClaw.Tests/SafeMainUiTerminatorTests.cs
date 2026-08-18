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

    [Fact]
    public void AllChecksPass_Terminates_AndCallsNativeTerminateExactlyOnce()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);
        var terminator = new SafeMainUiTerminator(invoker);

        var result = terminator.TryTerminate(tracked, ValidEvidence(), TimeSpan.FromSeconds(2));

        Assert.Equal(SafeMainUiTerminationResult.Terminated, result);
        Assert.Equal(1, invoker.TerminateCallCount);
    }

    [Fact]
    public void AlreadyExited_DoesNotCallTerminate()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);
        var evidence = ValidEvidence() with { ProcessAlive = false };

        var result = new SafeMainUiTerminator(invoker).TryTerminate(tracked, evidence, TimeSpan.FromSeconds(1));

        Assert.Equal(SafeMainUiTerminationResult.AlreadyExited, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public void NeverSeenVisible_RejectsAsIdentityMismatch_DoesNotCallTerminate()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);
        var evidence = ValidEvidence() with { SeenVisible = false };

        var result = new SafeMainUiTerminator(invoker).TryTerminate(tracked, evidence, TimeSpan.FromSeconds(1));

        Assert.Equal(SafeMainUiTerminationResult.IdentityMismatch, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public void VisibleAgain_Rejects_DoesNotCallTerminate()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);
        var evidence = ValidEvidence() with { FreshWindowSnapshot = new MainUiWindowSnapshot(true, 1, 1) };

        var result = new SafeMainUiTerminator(invoker).TryTerminate(tracked, evidence, TimeSpan.FromSeconds(1));

        Assert.Equal(SafeMainUiTerminationResult.VisibleAgain, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public void PidMismatch_Rejects_DoesNotCallTerminate()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);
        var evidence = ValidEvidence() with { HandleProcessId = TrackedPid + 1 };

        var result = new SafeMainUiTerminator(invoker).TryTerminate(tracked, evidence, TimeSpan.FromSeconds(1));

        Assert.Equal(SafeMainUiTerminationResult.IdentityMismatch, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public void HandleInvalid_Rejects_DoesNotCallTerminate()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);
        var evidence = ValidEvidence() with { HandleStillValid = false };

        var result = new SafeMainUiTerminator(invoker).TryTerminate(tracked, evidence, TimeSpan.FromSeconds(1));

        Assert.Equal(SafeMainUiTerminationResult.IdentityMismatch, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public void PathMismatch_Rejects_DoesNotCallTerminate()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);
        var evidence = ValidEvidence() with { CurrentExecutablePath = @"C:\Users\someone\Desktop\MSI Center M.exe" };

        var result = new SafeMainUiTerminator(invoker).TryTerminate(tracked, evidence, TimeSpan.FromSeconds(1));

        Assert.Equal(SafeMainUiTerminationResult.IdentityMismatch, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public void PathUnreadable_Rejects_DoesNotCallTerminate()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);
        var evidence = ValidEvidence() with { CurrentExecutablePath = null };

        var result = new SafeMainUiTerminator(invoker).TryTerminate(tracked, evidence, TimeSpan.FromSeconds(1));

        Assert.Equal(SafeMainUiTerminationResult.IdentityMismatch, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public void ProcessNameMismatch_Rejects_DoesNotCallTerminate()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);
        var evidence = ValidEvidence() with { CurrentProcessName = "notepad" };

        var result = new SafeMainUiTerminator(invoker).TryTerminate(tracked, evidence, TimeSpan.FromSeconds(1));

        Assert.Equal(SafeMainUiTerminationResult.IdentityMismatch, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public void ForeignSameNameProcessExists_Rejects_DoesNotCallTerminate()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);
        var evidence = ValidEvidence() with { AdditionalForeignMainUiExists = true };

        var result = new SafeMainUiTerminator(invoker).TryTerminate(tracked, evidence, TimeSpan.FromSeconds(1));

        Assert.Equal(SafeMainUiTerminationResult.AdditionalMainUiDetected, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public void WindowEnumerationUncertain_Rejects_DoesNotCallTerminate()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);
        var evidence = ValidEvidence() with { FreshWindowSnapshot = null };

        var result = new SafeMainUiTerminator(invoker).TryTerminate(tracked, evidence, TimeSpan.FromSeconds(1));

        Assert.Equal(SafeMainUiTerminationResult.IdentityUncertain, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public void NoTerminateRights_ReportsAccessDenied_DoesNotCallTerminate()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: false);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: true);

        var result = new SafeMainUiTerminator(invoker).TryTerminate(tracked, ValidEvidence(), TimeSpan.FromSeconds(1));

        Assert.Equal(SafeMainUiTerminationResult.AccessDenied, result);
        Assert.Equal(0, invoker.TerminateCallCount);
    }

    [Fact]
    public void NativeTerminateFails_ReportsFailed()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: false, waitSucceeds: true);

        var result = new SafeMainUiTerminator(invoker).TryTerminate(tracked, ValidEvidence(), TimeSpan.FromSeconds(1));

        Assert.Equal(SafeMainUiTerminationResult.Failed, result);
        Assert.Equal(1, invoker.TerminateCallCount);
    }

    [Fact]
    public void WaitTimesOut_ReportsWaitTimedOut()
    {
        var tracked = TrackedCenterMMainUi.CreateForTesting(TrackedPid, ExpectedPath, hasTerminateRights: true);
        var invoker = new RecordingInvoker(terminateSucceeds: true, waitSucceeds: false);

        var result = new SafeMainUiTerminator(invoker).TryTerminate(tracked, ValidEvidence(), TimeSpan.FromMilliseconds(1));

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
}
