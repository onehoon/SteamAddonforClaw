using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

internal enum SafeMainUiTerminationResult
{
    Terminated,
    AlreadyExited,
    IdentityMismatch,
    VisibleAgain,
    AdditionalMainUiDetected,
    IdentityUncertain,
    AccessDenied,
    WaitTimedOut,
    Failed
}

/// <summary>Every fact that must be freshly re-verified immediately before terminating a tracked
/// real MainUI process. Deliberately a plain data bag so the precondition decision
/// (<see cref="SafeMainUiTerminator.Evaluate"/>) is pure and independently testable from however
/// each fact is captured -- but in production, every field here is captured by
/// <see cref="SafeMainUiTerminator.TryTerminate"/> itself, immediately before terminating, never
/// trusted from an arbitrary earlier caller-supplied snapshot.</summary>
internal readonly record struct SafeMainUiTerminationEvidence(
    bool HandleStillValid,
    int? HandleProcessId,
    bool ProcessAlive,
    string? CurrentProcessName,
    string? CurrentExecutablePath,
    bool SeenVisible,
    MainUiWindowSnapshot? FreshWindowSnapshot,
    bool AdditionalForeignMainUiExists);

/// <summary>Native TerminateProcess/WaitForSingleObject seam, isolated so termination decision
/// logic can be unit tested without ever calling into a real process.</summary>
internal interface ITerminateProcessInvoker
{
    bool TryTerminate(SafeProcessHandle handle, out int win32Error);
    bool WaitForExit(SafeProcessHandle handle, TimeSpan timeout);
}

internal sealed class Win32TerminateProcessInvoker : ITerminateProcessInvoker
{
    private const uint WAIT_OBJECT_0 = 0;

    public bool TryTerminate(SafeProcessHandle handle, out int win32Error)
    {
        if (TerminateProcess(handle, 1)) { win32Error = 0; return true; }
        win32Error = Marshal.GetLastWin32Error();
        return false;
    }

    public bool WaitForExit(SafeProcessHandle handle, TimeSpan timeout) =>
        WaitForSingleObject(handle, (uint)timeout.TotalMilliseconds) == WAIT_OBJECT_0;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
}

/// <summary>
/// Terminates ONLY a previously tracked, previously-visible, freshly-revalidated real Center M
/// MainUI process -- never by process name, never a process tree, never any other MSI process.
/// Every fact used for the decision is captured fresh, immediately before TerminateProcess is
/// ever considered: the retained handle is re-inspected directly (never a PID re-lookup), the
/// window snapshot is captured now, and the same-name process list is enumerated now. A stale
/// evidence snapshot captured earlier by a caller can never cause a termination here (research
/// handoff sections 23-24, 36).
/// </summary>
internal sealed class SafeMainUiTerminator(
    ITerminateProcessInvoker? invoker = null,
    IProcessIdentityInspector? identityInspector = null,
    IMainUiWindowSnapshotProvider? windowProvider = null,
    IProcessSnapshotSource? processSnapshotSource = null)
{
    private readonly ITerminateProcessInvoker _invoker = invoker ?? new Win32TerminateProcessInvoker();
    private readonly IProcessIdentityInspector _identityInspector = identityInspector ?? new Win32ProcessIdentityInspector();
    private readonly IMainUiWindowSnapshotProvider _windowProvider = windowProvider ?? new Win32MainUiWindowSnapshotProvider();
    private readonly IProcessSnapshotSource _processSnapshotSource = processSnapshotSource ?? new Win32ProcessSnapshotSource();

    internal static bool PathMatchesExpectedPackage(string? executablePath) =>
        executablePath is not null
        && executablePath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
        && executablePath.EndsWith(@"MSI Center M\MSI Center M.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Captures every precondition fact fresh -- directly from the retained handle, a fresh window
    /// snapshot, and a fresh same-name process enumeration -- immediately before deciding whether
    /// to terminate. <paramref name="seenVisible"/> is the one fact that must come from the
    /// caller's <see cref="MainUiLifecycleObserver"/>, since it is that observer's own tracked
    /// state, not something independently re-derivable here.
    /// </summary>
    internal SafeMainUiTerminationResult TryTerminate(TrackedCenterMMainUi tracked, bool seenVisible, TimeSpan waitTimeout)
    {
        var evidence = CaptureFreshEvidence(tracked, seenVisible);
        var result = Evaluate(tracked, evidence);
        AppLog.Info("CenterM.MainUi", "Safe MainUI termination evaluated.", ("Result", result), ("ProcessId", tracked.ProcessId));

        if (result != SafeMainUiTerminationResult.Terminated) return result;

        if (!_invoker.TryTerminate(tracked.Handle, out var win32Error))
        {
            AppLog.Warn("CenterM.MainUi", "TerminateProcess failed.", null, ("Win32Error", win32Error));
            return SafeMainUiTerminationResult.Failed;
        }

        return _invoker.WaitForExit(tracked.Handle, waitTimeout)
            ? SafeMainUiTerminationResult.Terminated
            : SafeMainUiTerminationResult.WaitTimedOut;
    }

    private SafeMainUiTerminationEvidence CaptureFreshEvidence(TrackedCenterMMainUi tracked, bool seenVisible)
    {
        var identity = _identityInspector.Inspect(tracked.Handle);

        if (identity.Status == LiveProcessProbeStatus.Exited)
            return new SafeMainUiTerminationEvidence(true, tracked.ProcessId, ProcessAlive: false, null, null, seenVisible, null, false);

        if (identity.Status == LiveProcessProbeStatus.Uncertain)
            return new SafeMainUiTerminationEvidence(false, null, ProcessAlive: true, null, null, seenVisible, null, false);

        var windowSnapshot = _windowProvider.Capture(tracked.ProcessId);
        var sameNameProcesses = _processSnapshotSource.GetProcessesByName(CenterMProcessNames.MainUi);
        var foreignExists = sameNameProcesses.Any(p => p.ProcessId != tracked.ProcessId);

        return new SafeMainUiTerminationEvidence(
            HandleStillValid: true,
            HandleProcessId: identity.ProcessId,
            ProcessAlive: true,
            CurrentProcessName: identity.ProcessName,
            CurrentExecutablePath: identity.ExecutablePath,
            SeenVisible: seenVisible,
            FreshWindowSnapshot: windowSnapshot,
            AdditionalForeignMainUiExists: foreignExists);
    }

    /// <summary>Pure precondition evaluation, independently unit-testable. Returns
    /// <see cref="SafeMainUiTerminationResult.Terminated"/> only as a signal that every check
    /// passed and TerminateProcess should now be attempted -- callers must not treat that return
    /// value alone as proof of actual termination.</summary>
    internal static SafeMainUiTerminationResult Evaluate(TrackedCenterMMainUi tracked, SafeMainUiTerminationEvidence evidence)
    {
        if (!evidence.ProcessAlive) return SafeMainUiTerminationResult.AlreadyExited;

        if (!evidence.HandleStillValid || evidence.HandleProcessId != tracked.ProcessId)
            return SafeMainUiTerminationResult.IdentityMismatch;

        if (!string.Equals(evidence.CurrentProcessName, CenterMProcessNames.MainUi, StringComparison.Ordinal))
            return SafeMainUiTerminationResult.IdentityMismatch;

        if (!PathMatchesExpectedPackage(evidence.CurrentExecutablePath))
            return SafeMainUiTerminationResult.IdentityMismatch;

        if (!evidence.SeenVisible)
            return SafeMainUiTerminationResult.IdentityMismatch;

        if (evidence.FreshWindowSnapshot is null)
            return SafeMainUiTerminationResult.IdentityUncertain;

        if (evidence.FreshWindowSnapshot.Value.VisibleMainWindowCount > 0)
            return SafeMainUiTerminationResult.VisibleAgain;

        if (evidence.AdditionalForeignMainUiExists)
            return SafeMainUiTerminationResult.AdditionalMainUiDetected;

        if (!tracked.HasTerminateRights)
            return SafeMainUiTerminationResult.AccessDenied;

        return SafeMainUiTerminationResult.Terminated;
    }
}
