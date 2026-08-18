using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

internal enum CenterMRoutingTerminationResult
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

/// <summary>Same shape as <see cref="SafeMainUiTerminationEvidence"/> minus <c>SeenVisible</c> --
/// the routing-retirement use case has its own authority (routing is about to take exclusive
/// controller ownership) instead of the OEM1 hidden-after-visible lifecycle contract.</summary>
internal readonly record struct CenterMRoutingTerminationEvidence(
    bool HandleStillValid,
    int? HandleProcessId,
    bool ProcessAlive,
    string? CurrentProcessName,
    string? CurrentExecutablePath,
    MainUiWindowSnapshot? FreshWindowSnapshot,
    bool AdditionalForeignMainUiExists,
    bool SameNameEnumerationUncertain = false);

/// <summary>
/// Routing-entry counterpart to <see cref="SafeMainUiTerminator"/>: terminates ONLY a freshly
/// re-verified real Center M MainUI process, never by process name, never a process tree, never any
/// other MSI process. Deliberately a SEPARATE type -- <see cref="SafeMainUiTerminator"/>'s existing
/// OEM1 <c>SeenVisible</c> contract is untouched by this class and continues to guard its own
/// callers exactly as before. This type's authority instead comes from the caller (
/// <see cref="CenterMMainUiRoutingRetirement"/>) having already verified the process is hidden and
/// the physical controller is XInput before ever calling <see cref="TryTerminate"/> -- every other
/// fact (exact retained handle, exact process name, exact package path, fresh window state, fresh
/// same-name enumeration, terminate rights) is still re-verified fresh here, immediately before
/// termination, exactly like <see cref="SafeMainUiTerminator"/> does for its own use case.
/// </summary>
internal sealed class CenterMMainUiRoutingTerminator(
    ITerminateProcessInvoker? invoker = null,
    IProcessIdentityInspector? identityInspector = null,
    IMainUiWindowSnapshotProvider? windowProvider = null,
    IProcessSnapshotSource? processSnapshotSource = null)
{
    private readonly ITerminateProcessInvoker _invoker = invoker ?? new Win32TerminateProcessInvoker();
    private readonly IProcessIdentityInspector _identityInspector = identityInspector ?? new Win32ProcessIdentityInspector();
    private readonly IMainUiWindowSnapshotProvider _windowProvider = windowProvider ?? new Win32MainUiWindowSnapshotProvider();
    private readonly IProcessSnapshotSource _processSnapshotSource = processSnapshotSource ?? new Win32ProcessSnapshotSource();

    internal CenterMRoutingTerminationResult TryTerminate(TrackedCenterMMainUi tracked, TimeSpan waitTimeout)
    {
        CenterMRoutingTerminationResult result;
        try
        {
            var evidence = CaptureFreshEvidence(tracked);
            result = Evaluate(tracked, evidence);
        }
        catch (Exception ex)
        {
            AppLog.Warn("CenterM.RoutingGuard", "Fresh routing-retirement termination evidence capture failed.", ex, ("ProcessId", tracked.ProcessId));
            return CenterMRoutingTerminationResult.IdentityUncertain;
        }

        AppLog.Info("CenterM.RoutingGuard", "Routing-retirement MainUI termination evaluated.", ("Result", result), ("ProcessId", tracked.ProcessId));

        if (result != CenterMRoutingTerminationResult.Terminated) return result;

        AppLog.Info("CenterM.RoutingGuard", "MainUI routing retirement termination started.", ("ProcessId", tracked.ProcessId));
        if (!_invoker.TryTerminate(tracked.Handle, out var win32Error))
        {
            AppLog.Warn("CenterM.RoutingGuard", "TerminateProcess failed during routing retirement.", null, ("Win32Error", win32Error));
            return CenterMRoutingTerminationResult.Failed;
        }

        if (!_invoker.WaitForExit(tracked.Handle, waitTimeout)) return CenterMRoutingTerminationResult.WaitTimedOut;

        AppLog.Info("CenterM.RoutingGuard", "MainUI routing retirement exit confirmed.", ("ProcessId", tracked.ProcessId));
        return CenterMRoutingTerminationResult.Terminated;
    }

    private CenterMRoutingTerminationEvidence CaptureFreshEvidence(TrackedCenterMMainUi tracked)
    {
        var identity = _identityInspector.Inspect(tracked.Handle);

        if (identity.Status == LiveProcessProbeStatus.Exited)
            return new(true, tracked.ProcessId, ProcessAlive: false, null, null, null, false);

        if (identity.Status == LiveProcessProbeStatus.Uncertain)
            return new(false, null, ProcessAlive: true, null, null, null, false);

        var windowSnapshot = _windowProvider.Capture(tracked.ProcessId);
        var sameNameProcesses = _processSnapshotSource.GetProcessesByName(CenterMProcessNames.MainUi);

        return new(
            HandleStillValid: true,
            HandleProcessId: identity.ProcessId,
            ProcessAlive: true,
            CurrentProcessName: identity.ProcessName,
            CurrentExecutablePath: identity.ExecutablePath,
            FreshWindowSnapshot: windowSnapshot,
            AdditionalForeignMainUiExists: sameNameProcesses?.Any(p => p.ProcessId != tracked.ProcessId) ?? false,
            SameNameEnumerationUncertain: sameNameProcesses is null);
    }

    /// <summary>Pure precondition evaluation, independently unit-testable. Mirrors
    /// <see cref="SafeMainUiTerminator.Evaluate"/> exactly, minus the <c>SeenVisible</c> check, plus
    /// requiring the fresh window snapshot to already be hidden (the routing caller only reaches
    /// this point after its own minimize-wait/tray classification already established that).</summary>
    internal static CenterMRoutingTerminationResult Evaluate(TrackedCenterMMainUi tracked, CenterMRoutingTerminationEvidence evidence)
    {
        if (!evidence.ProcessAlive) return CenterMRoutingTerminationResult.AlreadyExited;

        if (!evidence.HandleStillValid || evidence.HandleProcessId != tracked.ProcessId)
            return CenterMRoutingTerminationResult.IdentityMismatch;

        if (!string.Equals(evidence.CurrentProcessName, CenterMProcessNames.MainUi, StringComparison.Ordinal))
            return CenterMRoutingTerminationResult.IdentityMismatch;

        if (!SafeMainUiTerminator.PathMatchesExpectedPackage(evidence.CurrentExecutablePath))
            return CenterMRoutingTerminationResult.IdentityMismatch;

        if (evidence.FreshWindowSnapshot is null)
            return CenterMRoutingTerminationResult.IdentityUncertain;

        // The process can exit in the window between the identity inspection above and this fresh
        // window capture -- Win32MainUiWindowSnapshotProvider reports that as ProcessAlive: false,
        // distinct from (and checked before) visibility, so a benign natural-exit race is classified
        // as AlreadyExited rather than attempted as a termination against an already-exited handle.
        if (!evidence.FreshWindowSnapshot.Value.ProcessAlive)
            return CenterMRoutingTerminationResult.AlreadyExited;

        if (evidence.FreshWindowSnapshot.Value.VisibleMainWindowCount > 0)
            return CenterMRoutingTerminationResult.VisibleAgain;

        if (evidence.SameNameEnumerationUncertain)
            return CenterMRoutingTerminationResult.IdentityUncertain;

        if (evidence.AdditionalForeignMainUiExists)
            return CenterMRoutingTerminationResult.AdditionalMainUiDetected;

        if (!tracked.HasTerminateRights)
            return CenterMRoutingTerminationResult.AccessDenied;

        return CenterMRoutingTerminationResult.Terminated;
    }
}
