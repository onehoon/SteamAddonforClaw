using System.Diagnostics;
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

internal enum MainUiHideWaitStatus { Hidden, Exited, TimedOut, Uncertain }

internal readonly record struct MainUiHideWaitResult(MainUiHideWaitStatus Status, MainUiWindowSnapshot? Snapshot);

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

    /// <summary>
    /// Cancellation is only honored freely up to (and including) the point where a minimize request
    /// has just been posted to a foreign MSI process -- that <c>SC_MINIMIZE</c> is an external side
    /// effect the pipeline cannot roll back. Once posted, the resulting hidden/XInput transition is
    /// always finished stabilizing/classifying with <see cref="CancellationToken.None"/> before
    /// cancellation is ever observed again, so a cancelled routing request never abandons Center M
    /// mid-transition -- it is either fully resolved (hidden + XInput confirmed, then retirement is
    /// abandoned via <see cref="OperationCanceledException"/> WITHOUT terminating it) or the
    /// resolution itself is reported as a definite failure (never guessed).
    /// </summary>
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
            return await FinishExitedMainUiAsync(cancellationToken).ConfigureAwait(false);
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

        // Fail before ever mutating the user's Center M window/controller lifecycle if we already
        // know this attempt can never complete termination -- there is no benefit to minimizing (or
        // observing tray XInput) first, just to fail with AccessDenied afterward.
        if (!tracked.HasTerminateRights)
        {
            LogFailed(tracked.ProcessId, "AccessDenied");
            return CenterMMainUiRoutingRetirementResult.AccessDenied;
        }

        var windowSnapshot = _windowSnapshotProvider.Capture(tracked.ProcessId);
        if (windowSnapshot is null)
        {
            LogFailed(tracked.ProcessId, "WindowStateUncertain");
            return CenterMMainUiRoutingRetirementResult.WindowStateUncertain;
        }
        if (!windowSnapshot.Value.ProcessAlive)
            return await FinishExitedMainUiAsync(cancellationToken).ConfigureAwait(false);

        AppLog.Info("CenterM.RoutingGuard", "Existing real MainUI detected for routing retirement.",
            ("ProcessId", tracked.ProcessId), ("WindowState", windowSnapshot.Value.VisibleMainWindowCount > 0 ? "Visible" : "Hidden"));

        if (windowSnapshot.Value.VisibleMainWindowCount > 0)
        {
            // Safe cancellation point: nothing external has happened yet.
            cancellationToken.ThrowIfCancellationRequested();

            AppLog.Info("CenterM.RoutingGuard", "MainUI minimize requested.", ("ProcessId", tracked.ProcessId));
            var minimizeResult = _windowController.TryMinimizeRecognizedMainUi(tracked.ProcessId);
            if (minimizeResult != CenterMMainUiMinimizeResult.Requested)
            {
                LogFailed(tracked.ProcessId, $"MinimizeFailed:{minimizeResult}");
                return CenterMMainUiRoutingRetirementResult.MinimizeFailed;
            }

            // SC_MINIMIZE was just posted to a foreign process -- an external side effect the
            // pipeline cannot undo. From here on, finish stabilizing/classifying THIS transition
            // (hidden-wait and the XInput wait it leads into) with CancellationToken.None; a
            // cancelled caller is only honored again once that classification is complete.
            var afterMinimize = await WaitUntilHiddenOrExitedAsync(tracked.ProcessId, CancellationToken.None).ConfigureAwait(false);
            switch (afterMinimize.Status)
            {
                case MainUiHideWaitStatus.Uncertain:
                    LogFailed(tracked.ProcessId, "WindowStateUncertain");
                    return CenterMMainUiRoutingRetirementResult.WindowStateUncertain;
                case MainUiHideWaitStatus.TimedOut:
                    LogFailed(tracked.ProcessId, "MinimizeTimedOut");
                    return CenterMMainUiRoutingRetirementResult.MinimizeTimedOut;
                case MainUiHideWaitStatus.Exited:
                    return await FinishExitedMainUiAsync(cancellationToken).ConfigureAwait(false);
            }

            AppLog.Info("CenterM.RoutingGuard", "MainUI hidden confirmed.", ("ProcessId", tracked.ProcessId));

            AppLog.Info("CenterM.RoutingGuard", "Waiting for native XInput before retirement.", ("ProcessId", tracked.ProcessId));
            // Still non-cancelable: this XInput wait is the continuation of the SAME external
            // transition the minimize we just posted caused -- Center M's own minimize lifecycle
            // (GamePadListener stop -> GoToXInputMode) is expected to converge here.
            var xInputConfirmed = await WaitForXInputAsync(CancellationToken.None).ConfigureAwait(false);
            if (!xInputConfirmed)
            {
                LogFailed(tracked.ProcessId, "XInputNotConfirmed");
                return CenterMMainUiRoutingRetirementResult.XInputNotConfirmed;
            }
        }
        else
        {
            // Tray/hidden: the Addon has not posted a message, mutated controller mode, or created
            // any resource yet -- there is no external side effect to finish stabilizing. Caller
            // cancellation stays fully authoritative, and a single stable native-state capture
            // (which already performs its own internal topology-settle wait) is enough; there is no
            // "transition in progress" to poll for, so this must not spin for the full XInput wait
            // timeout on a definite non-XInput reading.
            AppLog.Info("CenterM.RoutingGuard", "Tray-resident MainUI already hidden; minimize skipped.", ("ProcessId", tracked.ProcessId));
            cancellationToken.ThrowIfCancellationRequested();
            var mode = await _nativeModeProbe.CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (mode != CenterMNativeModeProbeResult.XInput)
            {
                LogFailed(tracked.ProcessId, "XInputNotConfirmed");
                return CenterMMainUiRoutingRetirementResult.XInputNotConfirmed;
            }
        }

        AppLog.Info("CenterM.RoutingGuard", "Native XInput confirmed before MainUI retirement.", ("ProcessId", tracked.ProcessId));

        // The externally-mutated MSI state is now fully known and safe (hidden + physical XInput).
        // Only now is cancellation honored again -- a cancelled routing request stops here WITHOUT
        // terminating the MainUI, rather than killing it for a route that no longer exists.
        cancellationToken.ThrowIfCancellationRequested();

        var terminationResult = _terminator.TryTerminate(tracked, _terminateWaitTimeout);
        switch (terminationResult)
        {
            case CenterMRoutingTerminationResult.Terminated:
            case CenterMRoutingTerminationResult.AlreadyExited:
                return await FinishExitedMainUiAsync(cancellationToken).ConfigureAwait(false);

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

    /// <summary>Common tail for every path that just observed the real MainUI as gone (terminated,
    /// or exited naturally on its own): always finishes the fresh same-name absence classification
    /// first -- that evidence step is never abandoned merely because the caller cancelled. Only
    /// once a CLEAN <see cref="CenterMMainUiRoutingRetirementResult.Retired"/> comes back is caller
    /// cancellation honored, immediately before returning to <see cref="CenterMMainUiRoutingGuard"/>
    /// -- so a cancelled routing request never proceeds into Phase-1 helper staging (a filesystem
    /// mutation, not a pure read) on the strength of a retirement result it never actually wanted.</summary>
    private async Task<CenterMMainUiRoutingRetirementResult> FinishExitedMainUiAsync(CancellationToken cancellationToken)
    {
        var absence = await VerifyFreshAbsenceAsync().ConfigureAwait(false);
        if (absence == CenterMMainUiRoutingRetirementResult.Retired)
            cancellationToken.ThrowIfCancellationRequested();
        return absence;
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

    /// <summary>Monotonic (<see cref="Stopwatch"/>-based) bounded wait -- never wall-clock
    /// (<see cref="DateTime.UtcNow"/>), which a system clock correction could move backwards and
    /// silently extend this safety wait well past its nominal timeout. A <see langword="null"/>
    /// snapshot (enumeration failure) is reported as <see cref="MainUiHideWaitStatus.Uncertain"/>,
    /// distinct from and never conflated with <see cref="MainUiHideWaitStatus.TimedOut"/> (the
    /// window was observed, just never became hidden in time).</summary>
    private async Task<MainUiHideWaitResult> WaitUntilHiddenOrExitedAsync(int processId, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = _windowSnapshotProvider.Capture(processId);
            if (snapshot is null) return new(MainUiHideWaitStatus.Uncertain, null);
            if (!snapshot.Value.ProcessAlive) return new(MainUiHideWaitStatus.Exited, snapshot);
            if (snapshot.Value.VisibleMainWindowCount == 0) return new(MainUiHideWaitStatus.Hidden, snapshot);
            if (Stopwatch.GetElapsedTime(started) >= _minimizeWaitTimeout) return new(MainUiHideWaitStatus.TimedOut, snapshot);
            await Task.Delay(_minimizeWaitPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Monotonic bounded wait -- see <see cref="WaitUntilHiddenOrExitedAsync"/>'s remarks
    /// on why <see cref="DateTime.UtcNow"/> is never used for this kind of safety-lifecycle timing.
    /// <see cref="_xInputWaitTimeout"/> is enforced as a true end-to-end budget covering the probe
    /// calls themselves, not just the outer poll loop: the underlying native-state manager's own
    /// stable-capture can internally settle for its own multi-second timeout, so without an inner
    /// per-call budget a single slow probe call could make this wait run for roughly double its
    /// configured timeout before the outer deadline is ever checked again. Each probe call is given
    /// only the REMAINING budget via a linked, time-boxed token -- a timeout expiring that inner
    /// token (and not the caller's own token) is treated as "not confirmed in time", never as caller
    /// cancellation.</summary>
    private async Task<bool> WaitForXInputAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = _xInputWaitTimeout - Stopwatch.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero) return false;

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(remaining);

            CenterMNativeModeProbeResult result;
            try
            {
                result = await _nativeModeProbe.CaptureAsync(budget.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
            {
                // The retirement stabilization budget expired inside the probe call itself, not the
                // caller's own token -- report as not-confirmed-in-time, not as cancellation.
                return false;
            }

            if (result == CenterMNativeModeProbeResult.XInput) return true;

            remaining = _xInputWaitTimeout - Stopwatch.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero) return false;

            var delay = remaining < _xInputWaitPollInterval ? remaining : _xInputWaitPollInterval;
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void LogFailed(int? processId, string reason) =>
        AppLog.Warn("CenterM.RoutingGuard", "Existing MainUI retirement failed.", null, ("ProcessId", processId), ("Reason", reason));
}
