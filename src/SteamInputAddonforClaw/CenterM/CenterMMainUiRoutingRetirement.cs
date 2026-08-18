using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.CenterM;

internal enum CenterMMainUiRoutingRetirementResult
{
    /// <summary>No real MainUI was present -- retirement is a no-op, proceed straight to Phase-1 arm.</summary>
    NoMainUiPresent,
    /// <summary>An existing real MainUI was found and successfully retired (or had already exited
    /// on its own before/while being retired); fresh absence is confirmed. Proceed to Phase-1 arm.</summary>
    Retired,
    IdentityUncertain,
    MultipleCandidates,
    IdentityMismatch,
    AccessDenied,
    WindowStateUncertain,
    MinimizeFailed,
    MinimizeTimedOut,
    XInputNotConfirmed,
    TerminationFailed,
    TerminationTimedOut,
    AbsenceCheckFailed
}

/// <summary>
/// Retires an already-running real MSI Center M MainUI before the Phase-1
/// <see cref="CenterMMainUiRoutingGuard"/> helper/mutex arm sequence, so routing entry is no longer
/// unconditionally refused merely because a real MainUI already exists (tray-resident or visible).
///
/// Tray/hidden path: verify native XInput, then terminate.
/// Visible path: request a normal minimize, wait for the window to actually hide, verify native
/// XInput, then terminate.
///
/// Deliberately does NOT weaken <see cref="SafeMainUiTerminator"/>'s existing OEM1 <c>SeenVisible</c>
/// contract -- termination here goes through the separate <see cref="CenterMMainUiRoutingTerminator"/>,
/// whose authority comes from routing being about to take exclusive controller ownership plus fresh
/// identity/window/native-state evidence, not from the OEM1 hidden-after-visible lifecycle. Never
/// sends any controller-mode command itself (observation only, via <see cref="ICenterMNativeModeProbe"/>);
/// never terminates the Server/Launcher/ControlMode backend processes.
/// </summary>
internal sealed class CenterMMainUiRoutingRetirement
{
    private readonly IProcessSnapshotSource _processSnapshotSource;
    private readonly IProcessHandleOpener? _handleOpener;
    private readonly IProcessIdentityInspector _identityInspector;
    private readonly IMainUiWindowSnapshotProvider _windowSnapshotProvider;
    private readonly ICenterMMainUiWindowController _windowController;
    private readonly ICenterMNativeModeProbe _nativeModeProbe;
    private readonly CenterMMainUiRoutingTerminator _terminator;
    private readonly TimeSpan _minimizeWaitTimeout;
    private readonly TimeSpan _minimizeWaitPollInterval;
    private readonly TimeSpan _xInputWaitTimeout;
    private readonly TimeSpan _xInputWaitPollInterval;
    private readonly TimeSpan _terminateWaitTimeout;

    internal CenterMMainUiRoutingRetirement(
        ICenterMNativeModeProbe nativeModeProbe,
        IProcessSnapshotSource? processSnapshotSource = null,
        IProcessHandleOpener? handleOpener = null,
        IProcessIdentityInspector? identityInspector = null,
        IMainUiWindowSnapshotProvider? windowSnapshotProvider = null,
        ICenterMMainUiWindowController? windowController = null,
        CenterMMainUiRoutingTerminator? terminator = null,
        TimeSpan? minimizeWaitTimeout = null,
        TimeSpan? minimizeWaitPollInterval = null,
        TimeSpan? xInputWaitTimeout = null,
        TimeSpan? xInputWaitPollInterval = null,
        TimeSpan? terminateWaitTimeout = null)
    {
        _nativeModeProbe = nativeModeProbe ?? throw new ArgumentNullException(nameof(nativeModeProbe));
        _processSnapshotSource = processSnapshotSource ?? new Win32ProcessSnapshotSource();
        _handleOpener = handleOpener;
        _identityInspector = identityInspector ?? new Win32ProcessIdentityInspector();
        _windowSnapshotProvider = windowSnapshotProvider ?? new Win32MainUiWindowSnapshotProvider();
        _windowController = windowController ?? new Win32CenterMMainUiWindowController();
        _terminator = terminator ?? new CenterMMainUiRoutingTerminator(processSnapshotSource: _processSnapshotSource, windowProvider: _windowSnapshotProvider, identityInspector: _identityInspector);
        _minimizeWaitTimeout = minimizeWaitTimeout ?? TimeSpan.FromSeconds(5);
        _minimizeWaitPollInterval = minimizeWaitPollInterval ?? TimeSpan.FromMilliseconds(200);
        _xInputWaitTimeout = xInputWaitTimeout ?? TimeSpan.FromSeconds(5);
        _xInputWaitPollInterval = xInputWaitPollInterval ?? TimeSpan.FromMilliseconds(250);
        _terminateWaitTimeout = terminateWaitTimeout ?? TimeSpan.FromSeconds(5);
    }

    internal async Task<CenterMMainUiRoutingRetirementResult> PrepareExistingMainUiForRoutingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = _processSnapshotSource.GetProcessesByName(CenterMProcessNames.MainUi);
        if (snapshot is null)
        {
            LogFailed(null, "ProcessEnumerationUncertain");
            return CenterMMainUiRoutingRetirementResult.IdentityUncertain;
        }
        if (snapshot.Count == 0) return CenterMMainUiRoutingRetirementResult.NoMainUiPresent;
        if (snapshot.Count > 1)
        {
            LogFailed(null, "MultipleCandidates");
            return CenterMMainUiRoutingRetirementResult.MultipleCandidates;
        }

        var candidate = snapshot[0];
        using var tracked = TrackedCenterMMainUi.Create(candidate.ProcessId, candidate.ExecutablePath, _handleOpener);
        if (!tracked.HasRetainedHandle)
        {
            LogFailed(candidate.ProcessId, "HandleUnavailable");
            return CenterMMainUiRoutingRetirementResult.IdentityUncertain;
        }

        // Never trust the original snapshot's process name/path -- the retained handle is
        // authoritative. Re-verify identity now, before doing any minimize/wait work for an entity
        // that may not even be the real MainUI.
        var identity = _identityInspector.Inspect(tracked.Handle);
        if (identity.Status == LiveProcessProbeStatus.Exited)
            return await VerifyFreshAbsenceAsync().ConfigureAwait(false);
        if (identity.Status == LiveProcessProbeStatus.Uncertain)
        {
            LogFailed(tracked.ProcessId, "IdentityUncertain");
            return CenterMMainUiRoutingRetirementResult.IdentityUncertain;
        }
        if (identity.ProcessId != tracked.ProcessId
            || !string.Equals(identity.ProcessName, CenterMProcessNames.MainUi, StringComparison.Ordinal)
            || !SafeMainUiTerminator.PathMatchesExpectedPackage(identity.ExecutablePath))
        {
            LogFailed(tracked.ProcessId, "IdentityMismatch");
            return CenterMMainUiRoutingRetirementResult.IdentityMismatch;
        }

        var windowSnapshot = _windowSnapshotProvider.Capture(tracked.ProcessId);
        if (windowSnapshot is null)
        {
            LogFailed(tracked.ProcessId, "WindowStateUncertain");
            return CenterMMainUiRoutingRetirementResult.WindowStateUncertain;
        }
        if (!windowSnapshot.Value.ProcessAlive)
            return await VerifyFreshAbsenceAsync().ConfigureAwait(false);

        AppLog.Info("CenterM.RoutingGuard", "Existing real MainUI detected for routing retirement.",
            ("ProcessId", tracked.ProcessId), ("WindowState", windowSnapshot.Value.VisibleMainWindowCount > 0 ? "Visible" : "Hidden"));

        if (windowSnapshot.Value.VisibleMainWindowCount > 0)
        {
            AppLog.Info("CenterM.RoutingGuard", "MainUI minimize requested.", ("ProcessId", tracked.ProcessId));
            var minimizeResult = _windowController.TryMinimizeRecognizedMainUi(tracked.ProcessId);
            if (minimizeResult != CenterMMainUiMinimizeResult.Requested)
            {
                LogFailed(tracked.ProcessId, $"MinimizeFailed:{minimizeResult}");
                return CenterMMainUiRoutingRetirementResult.MinimizeFailed;
            }

            var afterMinimize = await WaitUntilHiddenOrExitedAsync(tracked.ProcessId, cancellationToken).ConfigureAwait(false);
            if (afterMinimize is null)
            {
                LogFailed(tracked.ProcessId, "MinimizeTimedOut");
                return CenterMMainUiRoutingRetirementResult.MinimizeTimedOut;
            }
            if (!afterMinimize.Value.ProcessAlive)
                return await VerifyFreshAbsenceAsync().ConfigureAwait(false);

            AppLog.Info("CenterM.RoutingGuard", "MainUI hidden confirmed.", ("ProcessId", tracked.ProcessId));
        }
        else
        {
            AppLog.Info("CenterM.RoutingGuard", "Tray-resident MainUI already hidden; minimize skipped.", ("ProcessId", tracked.ProcessId));
        }

        AppLog.Info("CenterM.RoutingGuard", "Waiting for native XInput before retirement.", ("ProcessId", tracked.ProcessId));
        var xInputConfirmed = await WaitForXInputAsync(cancellationToken).ConfigureAwait(false);
        if (!xInputConfirmed)
        {
            LogFailed(tracked.ProcessId, "XInputNotConfirmed");
            return CenterMMainUiRoutingRetirementResult.XInputNotConfirmed;
        }
        AppLog.Info("CenterM.RoutingGuard", "Native XInput confirmed before MainUI retirement.", ("ProcessId", tracked.ProcessId));

        cancellationToken.ThrowIfCancellationRequested();

        var terminationResult = _terminator.TryTerminate(tracked, _terminateWaitTimeout);
        switch (terminationResult)
        {
            case CenterMRoutingTerminationResult.Terminated:
            case CenterMRoutingTerminationResult.AlreadyExited:
                return await VerifyFreshAbsenceAsync().ConfigureAwait(false);

            case CenterMRoutingTerminationResult.WaitTimedOut:
                LogFailed(tracked.ProcessId, "TerminationTimedOut");
                return CenterMMainUiRoutingRetirementResult.TerminationTimedOut;

            case CenterMRoutingTerminationResult.IdentityMismatch:
            case CenterMRoutingTerminationResult.VisibleAgain:
                LogFailed(tracked.ProcessId, terminationResult.ToString());
                return CenterMMainUiRoutingRetirementResult.IdentityMismatch;

            case CenterMRoutingTerminationResult.AdditionalMainUiDetected:
                LogFailed(tracked.ProcessId, "AdditionalMainUiDetected");
                return CenterMMainUiRoutingRetirementResult.MultipleCandidates;

            case CenterMRoutingTerminationResult.AccessDenied:
                LogFailed(tracked.ProcessId, "AccessDenied");
                return CenterMMainUiRoutingRetirementResult.AccessDenied;

            case CenterMRoutingTerminationResult.IdentityUncertain:
                LogFailed(tracked.ProcessId, "IdentityUncertain");
                return CenterMMainUiRoutingRetirementResult.IdentityUncertain;

            default:
                LogFailed(tracked.ProcessId, "TerminationFailed");
                return CenterMMainUiRoutingRetirementResult.TerminationFailed;
        }
    }

    private async Task<CenterMMainUiRoutingRetirementResult> VerifyFreshAbsenceAsync()
    {
        var fresh = _processSnapshotSource.GetProcessesByName(CenterMProcessNames.MainUi);
        if (fresh is null)
        {
            LogFailed(null, "AbsenceCheckUncertain");
            return CenterMMainUiRoutingRetirementResult.AbsenceCheckFailed;
        }
        if (fresh.Count > 0)
        {
            // Do not assume retiring/observing the exit of the prior candidate proves global
            // absence -- another real MainUI may have appeared concurrently. This attempt is not
            // retried here; the caller (ArmAsync) simply fails this arm and a later call re-runs
            // retirement fresh.
            LogFailed(null, "AnotherMainUiPresentAfterRetirement");
            return CenterMMainUiRoutingRetirementResult.MultipleCandidates;
        }

        AppLog.Info("CenterM.RoutingGuard", "Existing MainUI retirement completed.", ("Result", "Retired"));
        await Task.CompletedTask.ConfigureAwait(false);
        return CenterMMainUiRoutingRetirementResult.Retired;
    }

    private async Task<MainUiWindowSnapshot?> WaitUntilHiddenOrExitedAsync(int processId, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + _minimizeWaitTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = _windowSnapshotProvider.Capture(processId);
            if (snapshot is null) return null;
            if (!snapshot.Value.ProcessAlive || snapshot.Value.VisibleMainWindowCount == 0) return snapshot;
            if (DateTime.UtcNow >= deadline) return null;
            await Task.Delay(_minimizeWaitPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> WaitForXInputAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + _xInputWaitTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _nativeModeProbe.CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (result == CenterMNativeModeProbeResult.XInput) return true;
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(_xInputWaitPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void LogFailed(int? processId, string reason) =>
        AppLog.Warn("CenterM.RoutingGuard", "Existing MainUI retirement failed.", null, ("ProcessId", processId), ("Reason", reason));
}
