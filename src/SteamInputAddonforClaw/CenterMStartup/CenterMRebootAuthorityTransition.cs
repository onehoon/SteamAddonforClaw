using System.Diagnostics;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Settings;

namespace SteamInputAddonforClaw.CenterMStartup;

internal enum WindowsRestartRequestResult { Requested, Failed }

/// <summary>The one Runtime-owned Windows-restart seam for the reboot-bound authority transition
/// (work order PR3 section 10). Production issues a normal local interactive-user restart
/// (<c>shutdown.exe /r /t 0</c>) -- no <c>/f</c>, no privileged reboot helper.</summary>
internal interface IWindowsRestartRequester
{
    WindowsRestartRequestResult RequestRestart();
}

internal sealed class WindowsRestartRequester : IWindowsRestartRequester
{
    public WindowsRestartRequestResult RequestRestart()
    {
        try
        {
            using var started = Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (started is null) return WindowsRestartRequestResult.Failed;

            // A started process only proves shutdown.exe launched, not that "/r /t 0" was accepted.
            // shutdown.exe exits immediately once it has accepted or rejected the request, so a
            // non-zero exit code (privilege/policy failure) or an unexpectedly long run means the
            // restart was NOT scheduled -- report Failed so the caller shows the manual-restart path.
            if (!started.WaitForExit(milliseconds: 5000))
            {
                AppLog.Warn("CenterM.Authority", "Windows restart command did not complete within the expected window.");
                return WindowsRestartRequestResult.Failed;
            }
            if (started.ExitCode != 0)
            {
                AppLog.Warn("CenterM.Authority", "Windows restart command failed.", null, ("ExitCode", started.ExitCode));
                return WindowsRestartRequestResult.Failed;
            }
            return WindowsRestartRequestResult.Requested;
        }
        catch (Exception exception)
        {
            AppLog.Error("CenterM.Authority", "Windows restart request could not be started.", exception);
            return WindowsRestartRequestResult.Failed;
        }
    }
}

internal interface ICenterMRebootAuthorityTransition
{
    /// <param name="centerMEnabled"><see langword="true"/> = Enable and Restart (restore MSI/stock
    /// authority); <see langword="false"/> = Disable and Restart (switch authority to the Addon).</param>
    Task<FrontendCenterMStartupMutationResult> RequestAsync(bool centerMEnabled, CancellationToken cancellationToken);
}

/// <summary>
/// The first real reboot-bound MSI Center M controller-authority transition (work order PR3). It
/// composes already-merged foundations -- <see cref="CenterMStartupControl"/> (the only startup-root
/// writer), <see cref="StartupSettingsCoordinator"/> (the only Addon startup-registration writer),
/// and <see cref="AddonControllerHidHideBaseline"/> (the persistent HidHide owner) -- into one small
/// ordered flow that ends by requesting an immediate Windows restart. It never performs a live
/// same-session MSI -&gt; Addon controller takeover: no PID switch, no DirectInput, no VIIPER attach.
///
/// There is no generalized transaction/rollback engine: each stage is verified before the next, and
/// a failed stage stops before the reboot and reports the real state. An already-verified stage
/// (extra enabled startup task, zero-target HidHide baseline) is left in place rather than
/// speculatively reverted (section 8).
/// </summary>
internal sealed class CenterMRebootAuthorityTransition : ICenterMRebootAuthorityTransition
{
    private readonly CenterMStartupControl _centerMStartup;
    private readonly StartupSettingsCoordinator _startupSettings;
    private readonly AddonControllerHidHideBaseline _hidHideBaseline;
    private readonly Func<UserTerminationDecision> _lowerLevelRuntimeSafety;
    private readonly Func<bool> _conflictingControllerEnvironment;
    private readonly Func<CancellationToken, Task<(RuntimePrerequisiteAssessment Prerequisites, bool RecoverySafe)>> _captureAdmission;
    private readonly IWindowsRestartRequester _restartRequester;
    private int _inProgress;

    internal CenterMRebootAuthorityTransition(
        CenterMStartupControl centerMStartup,
        StartupSettingsCoordinator startupSettings,
        AddonControllerHidHideBaseline hidHideBaseline,
        Func<UserTerminationDecision> lowerLevelRuntimeSafety,
        Func<bool> conflictingControllerEnvironment,
        Func<CancellationToken, Task<(RuntimePrerequisiteAssessment Prerequisites, bool RecoverySafe)>> captureAdmission,
        IWindowsRestartRequester restartRequester)
    {
        _centerMStartup = centerMStartup;
        _startupSettings = startupSettings;
        _hidHideBaseline = hidHideBaseline;
        _lowerLevelRuntimeSafety = lowerLevelRuntimeSafety;
        _conflictingControllerEnvironment = conflictingControllerEnvironment;
        _captureAdmission = captureAdmission;
        _restartRequester = restartRequester;
    }

    /// <param name="centerMEnabled">The requested next-boot authority: <see langword="true"/> =
    /// restore MSI/stock authority (Enable and Restart), <see langword="false"/> = switch controller
    /// authority to the Addon (Disable and Restart).</param>
    public async Task<FrontendCenterMStartupMutationResult> RequestAsync(bool centerMEnabled, CancellationToken cancellationToken)
    {
        // A single in-memory guard (never persisted) rejects an accidental overlapping frontend
        // request while one ordered transition is mid-flight.
        if (Interlocked.Exchange(ref _inProgress, 1) != 0)
            return Fail(_centerMStartup.Capture(), "Another MSI Center M authority transition is already in progress.");

        AppLog.Info("CenterM.Authority", "Authority transition requested.", ("Request", centerMEnabled ? "Enable" : "Disable"));
        try
        {
            return centerMEnabled
                ? await EnableAsync(cancellationToken).ConfigureAwait(false)
                : await DisableAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancel(_centerMStartup.Capture(), "The MSI Center M authority transition was cancelled before any change was made.");
        }
        finally
        {
            Volatile.Write(ref _inProgress, 0);
        }
    }

    // ---- Disable and Restart: switch controller authority to the Addon for the next boot ----
    private async Task<FrontendCenterMStartupMutationResult> DisableAsync(CancellationToken cancellationToken)
    {
        var snapshot = _centerMStartup.Capture();
        if (snapshot.State == FrontendCenterMStartupState.Unavailable)
            return Unavailable(snapshot);

        // --- read-only preflight (honors the caller token) ---
        if (!_lowerLevelRuntimeSafety().CanTerminate)
            return Fail(snapshot, "Controller authority cannot change while a routing, native-mode, or recovery operation is in progress. Try again once it finishes.");
        if (_conflictingControllerEnvironment())
            return Fail(snapshot, "A conflicting or unverified controller-manager environment prevents entering Addon controller authority. Close or remove other controller software, then retry Disable and Restart.");

        // Disable is the point where the next boot is committed to Addon controller authority, so it
        // must not run on top of an unverified controller state. Both facts are already captured by
        // the one Runtime status snapshot -- no new authority is introduced here.
        var admission = await _captureAdmission(cancellationToken).ConfigureAwait(false);

        // RecoverySafe=false means stale route-scoped recovery could not be safely retired (e.g. the
        // validated recovery journal still exists). Establishing the persistent PR2 baseline now would
        // let the next-boot StartupHidHideRecoveryCleaner mistake it for the old mutation and undo it.
        if (!admission.RecoverySafe)
            return Fail(snapshot,
                "Controller recovery is not in a verified safe state, so MSI Center M was not disabled. Resolve controller recovery and retry Disable and Restart.");

        // A known-missing/unusable virtual-controller prerequisite (USBIP2, libVIIPER, HidHide) must
        // stop the transition before any persistent mutation (work order PR3 section 6.2 item 6).
        if (!admission.Prerequisites.IsRoutingReady)
            return Fail(snapshot,
                $"Required controller components are not ready, so MSI Center M was not disabled. " +
                $"HidHide={admission.Prerequisites.HidHide.Status}, UsbIpWin2={admission.Prerequisites.UsbIpWin2.Status}, Viiper={admission.Prerequisites.Viiper.Status}. " +
                $"Complete first-time setup, then retry Disable and Restart.");

        var inspection = _hidHideBaseline.InspectDisabledModeBaseline([]);
        if (inspection.Outcome is AddonHidHideBaselineOutcome.Conflict or AddonHidHideBaselineOutcome.Unavailable)
            return Fail(snapshot, $"HidHide is not in a safe state for Addon controller isolation: {inspection.Reason}.");
        cancellationToken.ThrowIfCancellationRequested();

        // --- ordered persistent mutation (Runtime-owned scope; a disconnecting frontend must not
        //     abort a transition the user already confirmed, section 9) ---

        // 1. The Runtime must prove it will start at the next logon BEFORE Center M is disabled.
        var registration = _startupSettings.ChangeLaunchAtWindowsStartup(true);
        AppLog.Info("CenterM.Authority", "Mandatory startup verification.", ("Success", registration.Success), ("Message", registration.Message));
        if (!registration.Success)
            return Fail(snapshot, $"The Addon could not be registered to start at the next Windows logon, so MSI Center M was not disabled. {registration.Message}");

        // 2. Persistent zero-target HidHide baseline (no physical PID1902 target is known at PR3).
        var apply = _hidHideBaseline.ApplyDisabledModeBaseline([]);
        AppLog.Info("CenterM.Authority", "HidHide baseline apply.", ("Outcome", apply.Outcome), ("Reason", apply.Reason));
        if (!apply.IsCompliant)
            return Fail(snapshot, $"The Addon HidHide controller baseline could not be applied, so MSI Center M was not disabled: {apply.Reason}. The startup registration was left enabled.");

        // 3. Center M startup roots -> Disabled (exact read-back verified inside the primitive).
        var mutation = await _centerMStartup.SetEnabledAsync(false, CancellationToken.None).ConfigureAwait(false);
        AppLog.Info("CenterM.Authority", "Center M startup mutation.", ("Outcome", mutation.Outcome), ("State", mutation.Snapshot.State));
        if (!mutation.Succeeded)
            return IncompleteMutation(mutation, centerMEnabled: false);

        // 4. Immediate restart.
        return RequestRestart(mutation.Snapshot);
    }

    // ---- Enable and Restart: restore MSI/stock authority for the next boot (PR3 stage) ----
    private async Task<FrontendCenterMStartupMutationResult> EnableAsync(CancellationToken cancellationToken)
    {
        var snapshot = _centerMStartup.Capture();
        if (snapshot.State == FrontendCenterMStartupState.Unavailable)
            return Unavailable(snapshot);

        // A conflicting-controller environment blocks ENTERING Addon authority, never the official
        // release path (section 7.3).
        if (!_lowerLevelRuntimeSafety().CanTerminate)
            return Fail(snapshot, "Controller authority cannot change while a routing, native-mode, or recovery operation is in progress. Try again once it finishes.");
        cancellationToken.ThrowIfCancellationRequested();

        // At PR3 the only persistent Addon controller state is the zero-target HidHide baseline plus
        // the mandatory startup policy. A later PID1902 PR extends the FRONT of this path with the
        // virtual/DirectInput/PID1901 release before HidHide is cleared.
        var clear = _hidHideBaseline.ApplyEnabledModeBaseline([]);
        AppLog.Info("CenterM.Authority", "HidHide baseline clear.", ("Outcome", clear.Outcome), ("Reason", clear.Reason));
        if (!clear.IsCompliant)
            return Fail(snapshot, $"The Addon HidHide controller baseline could not be cleared, so MSI Center M was not enabled: {clear.Reason}. Existing HidHide state was left untouched.");

        var mutation = await _centerMStartup.SetEnabledAsync(true, CancellationToken.None).ConfigureAwait(false);
        AppLog.Info("CenterM.Authority", "Center M startup mutation.", ("Outcome", mutation.Outcome), ("State", mutation.Snapshot.State));
        if (!mutation.Succeeded)
            return IncompleteMutation(mutation, centerMEnabled: true);

        // The mandatory-Runtime policy (PR2.5) stops applying automatically now that the roots read
        // back exactly Enabled -- no separate "release" state.
        return RequestRestart(mutation.Snapshot);
    }

    private FrontendCenterMStartupMutationResult RequestRestart(FrontendCenterMStartupSnapshot snapshot)
    {
        var restart = _restartRequester.RequestRestart();
        if (restart == WindowsRestartRequestResult.Requested)
        {
            AppLog.Info("CenterM.Authority", "Authority transition verified; Windows restart requested.", ("State", snapshot.State));
            return new(FrontendCenterMStartupMutationOutcome.Succeeded, snapshot, null);
        }

        // Every persistent mutation is verified; a reverse mutation now would create more authority
        // ambiguity than preserving the verified next-boot configuration (section 10).
        AppLog.Warn("CenterM.Authority", "Authority transition verified but Windows restart could not be started.", null, ("State", snapshot.State));
        return new(FrontendCenterMStartupMutationOutcome.Failed, snapshot,
            "The controller authority configuration was changed, but Windows restart could not be started. Restart Windows manually to apply the change.");
    }

    /// <summary>A Center M startup mutation that did not succeed (typically a cancelled Windows
    /// elevation prompt) after the ordered persistent preparation already ran. PR3 deliberately
    /// leaves the verified startup-registration / HidHide-baseline preparation in place (section 8),
    /// so a <see cref="FrontendCenterMStartupMutationOutcome.Cancelled"/> result must not be allowed
    /// to read as "nothing changed" -- give it the real partial-state context.</summary>
    private static FrontendCenterMStartupMutationResult IncompleteMutation(FrontendCenterMStartupMutationResult mutation, bool centerMEnabled)
    {
        if (mutation.Outcome != FrontendCenterMStartupMutationOutcome.Cancelled)
            return mutation;
        return mutation with
        {
            FailureMessage = centerMEnabled
                ? "MSI Center M enable was cancelled at the Windows elevation prompt. The Addon HidHide controller baseline was already cleared and remains cleared; retry Enable and Restart."
                : "MSI Center M disable was cancelled at the Windows elevation prompt. The Addon startup registration and HidHide controller baseline were already applied and remain in place; retry Disable and Restart, or choose Enable and Restart to revert.",
        };
    }

    private static FrontendCenterMStartupMutationResult Fail(FrontendCenterMStartupSnapshot snapshot, string reason) =>
        new(FrontendCenterMStartupMutationOutcome.Failed, snapshot, reason);

    private static FrontendCenterMStartupMutationResult Cancel(FrontendCenterMStartupSnapshot snapshot, string reason) =>
        new(FrontendCenterMStartupMutationOutcome.Cancelled, snapshot, reason);

    private static FrontendCenterMStartupMutationResult Unavailable(FrontendCenterMStartupSnapshot snapshot) =>
        new(FrontendCenterMStartupMutationOutcome.Unavailable, snapshot,
            snapshot.FailureMessage ?? "MSI Center M controller authority control is unavailable on this device.");
}
