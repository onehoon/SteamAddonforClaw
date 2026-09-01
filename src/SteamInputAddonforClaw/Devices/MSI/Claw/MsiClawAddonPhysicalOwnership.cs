using System.Diagnostics;
using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Input.DirectInput;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal enum MsiClawPhysicalOwnershipOutcome
{
    /// <summary>Center M is not exactly Disabled or PR4 admission was not Ready -- ownership never started.</summary>
    NotApplicable,
    /// <summary>The same physical MSI Claw is PID1902, a live DirectInput session produced a valid
    /// state, and the exact primary gamepad collection is persistently hidden and read-back verified.</summary>
    Owned,
    /// <summary>A required step could not be positively proven. No virtual controller is attached;
    /// PID1902 is never rolled back to PID1901 while Center M remains Disabled.</summary>
    Failed,
}

internal sealed record MsiClawPhysicalOwnershipResult(
    MsiClawPhysicalOwnershipOutcome Outcome,
    string Reason,
    bool ModeWriteIssued,
    string? HiddenTarget)
{
    internal bool IsOwned => Outcome == MsiClawPhysicalOwnershipOutcome.Owned;

    internal static MsiClawPhysicalOwnershipResult NotApplicable(string reason) =>
        new(MsiClawPhysicalOwnershipOutcome.NotApplicable, reason, false, null);
}

/// <summary>Result of the narrow PR5 release seam the official Center M Enable-and-Restart path
/// runs before it clears HidHide (work order PR5 section 16). <see cref="HiddenTarget"/> is the exact
/// PID1902 primary gamepad collection PR5 persisted, so the authority transition clears exactly that
/// entry rather than <c>[]</c>.</summary>
internal sealed record PhysicalOwnershipReleaseResult(bool Succeeded, string Reason, string? HiddenTarget)
{
    internal static PhysicalOwnershipReleaseResult NothingOwned { get; } = new(true, "NoPhysicalOwnership", null);
}

internal interface IMsiClawAddonPhysicalOwnership : IAsyncDisposable
{
    /// <summary>One-shot startup acquisition. Re-reads the shared Center M authority immediately
    /// before the first physical mutation, reconciles the same physical MSI Claw to PID1902, acquires
    /// verified DirectInput, and persists/verifies the exact HidHide target. Attaches no virtual
    /// controller.</summary>
    Task<MsiClawPhysicalOwnershipResult> AcquireAsync(CancellationToken cancellationToken);

    /// <summary>The process-owned live DirectInput source after a successful acquisition (PR6 consumes
    /// the SAME source). Null before success or after teardown.</summary>
    IMsiClawPreparedInputSource? LiveInputSource { get; }

    /// <summary>The official Center M Enable-and-Restart release: retire the process-owned DirectInput
    /// session, then restore the same strongly-verified physical MSI Claw to PID1901. Runs through the
    /// same owner gate as acquisition, so the two can never interleave. Does NOT clear HidHide or
    /// enable Center M roots -- the authority transition does that next with the returned target.</summary>
    Task<PhysicalOwnershipReleaseResult> ReleaseForCenterMEnableAsync(CancellationToken cancellationToken);

    /// <summary>PR8: reacquire an unexpectedly lost owned DirectInput session on the SAME input source
    /// object, only when the same strongly-identified MSI Claw is still PID1902 with the same exact
    /// persistent HidHide target. Runs through the same owner gate. Verifies/repairs the persistent
    /// HidHide baseline BEFORE restarting DirectInput and requires a first valid state before it
    /// commits. Never issues a PID mode write and never restores PID1901.</summary>
    Task<MsiClawPhysicalOwnershipResult> RecoverLostInputAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The first real physical ownership operation for the durable Addon controller architecture
/// (work order PR5). It composes existing low-level primitives only -- the MSI native-state
/// manager/mode controller, the verified DirectInput selection/reader path, and the PR2 persistent
/// HidHide baseline owner -- into one ordered one-shot acquisition, then keeps the DirectInput source
/// alive for the process lifetime.
///
/// It deliberately does NOT use the old route-scoped native-mode session coordinator or physical
/// isolation stage, does not journal anything in the routing recovery journal, and never attaches a
/// virtual X360/SteamDeck presentation. On failure it releases only process-owned handles: durable
/// Addon authority is never silently released and PID1902 is never converted back to PID1901.
/// </summary>
internal sealed class MsiClawAddonPhysicalOwnership : IMsiClawAddonPhysicalOwnership
{
    private readonly Func<FrontendCenterMStartupState> _captureCenterMStartupState;
    private readonly Func<CancellationToken, Task<NativeStateCaptureResult>> _captureStableNativeState;
    private readonly Func<MsiClawNativeMode, MsiClawPhysicalIdentity, CancellationToken, Task<MsiClawModeTransitionResult>> _switchMode;
    private readonly Func<IReadOnlyList<DirectInputDeviceDescriptor>> _enumerateDirectInputDevices;
    private readonly Func<string, ControllerDeviceInfo?> _resolvePnpDevice;
    private readonly IMsiClawPreparedInputSource _inputSource;
    private readonly Func<string, AddonHidHideBaselineResult> _applyHidHideTarget;
    private readonly Func<string?> _captureExistingOwnedHiddenTarget;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _directInputSettleWindow;
    private readonly TimeSpan _directInputSettleInterval;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ownsInputSource;
    private string? _ownedHiddenTarget;
    // PR8 section 6: the strong physical identity committed by a successful acquisition. Kept only in
    // memory, never persisted, and never cleared on a recovery failure -- the official
    // Enable-and-Restart release still needs it as ownership evidence.
    private MsiClawPhysicalIdentity? _ownedPhysicalIdentity;
    private bool _releasedForEnable;
    private int _disposed;

    internal MsiClawAddonPhysicalOwnership(
        Func<FrontendCenterMStartupState> captureCenterMStartupState,
        Func<CancellationToken, Task<NativeStateCaptureResult>> captureStableNativeState,
        Func<MsiClawNativeMode, MsiClawPhysicalIdentity, CancellationToken, Task<MsiClawModeTransitionResult>> switchMode,
        Func<IReadOnlyList<DirectInputDeviceDescriptor>> enumerateDirectInputDevices,
        Func<string, ControllerDeviceInfo?> resolvePnpDevice,
        IMsiClawPreparedInputSource inputSource,
        Func<string, AddonHidHideBaselineResult> applyHidHideTarget,
        Func<string?> captureExistingOwnedHiddenTarget,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? directInputSettleWindow = null,
        TimeSpan? directInputSettleInterval = null)
    {
        _captureCenterMStartupState = captureCenterMStartupState;
        _captureStableNativeState = captureStableNativeState;
        _switchMode = switchMode;
        _enumerateDirectInputDevices = enumerateDirectInputDevices;
        _resolvePnpDevice = resolvePnpDevice;
        _inputSource = inputSource;
        _applyHidHideTarget = applyHidHideTarget;
        _captureExistingOwnedHiddenTarget = captureExistingOwnedHiddenTarget;
        _delay = delay ?? Task.Delay;
        _directInputSettleWindow = directInputSettleWindow ?? TimeSpan.FromSeconds(3);
        _directInputSettleInterval = directInputSettleInterval ?? TimeSpan.FromMilliseconds(150);
    }

    public IMsiClawPreparedInputSource? LiveInputSource => _ownsInputSource ? _inputSource : null;

    public async Task<MsiClawPhysicalOwnershipResult> AcquireAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return Fail("OwnerDisposed", false, null);
            if (_releasedForEnable) return Fail("ReleasedForCenterMEnable", false, null);
            if (_ownsInputSource) return new(MsiClawPhysicalOwnershipOutcome.Owned, "AlreadyOwned", false, null);
            return await AcquireCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<MsiClawPhysicalOwnershipResult> AcquireCoreAsync(CancellationToken cancellationToken)
    {
        AppLog.Info("ControllerOwnership", "Physical ownership started.", ("Event", "PhysicalOwnershipStarted"));

        // 1-4. Stable current native state + strong initial physical identity. The authoritative
        //      fresh Center M authority read happens at the ACTUAL first mutation boundary below --
        //      not here -- because CaptureStableCurrentSnapshotAsync can wait a bounded PnP window
        //      during which the user could run Enable and Restart.
        var initialCapture = await _captureStableNativeState(cancellationToken).ConfigureAwait(false);
        if (!TryReadIdentity(initialCapture, out var initialMode, out var initialIdentity, out var reason))
            return Fail("InitialNativeState:" + reason, false, null);
        if (initialMode is not (MsiClawNativeMode.XInput or MsiClawNativeMode.DirectInput))
            return Fail("UnsupportedInitialMode:" + initialMode, false, null);
        AppLog.Info("ControllerOwnership", "Native state captured.", ("Event", "NativeStateCaptured"),
            ("Mode", initialMode), ("IdentityConfidence", initialIdentity.Confidence),
            ("ModeWriteRequired", initialMode == MsiClawNativeMode.XInput));

        // 5. PID1901 -> PID1902 once, for the same strong physical MSI Claw. The mode write is the
        //    first physical mutation, so the single required fresh authority read is immediately here.
        var modeWriteIssued = false;
        if (initialMode == MsiClawNativeMode.XInput)
        {
            if (_captureCenterMStartupState() != FrontendCenterMStartupState.Disabled)
                return Fail("AuthorityChangedBeforeModeWrite", false, null);
            var transition = await _switchMode(MsiClawNativeMode.DirectInput, initialIdentity, cancellationToken).ConfigureAwait(false);
            modeWriteIssued = true;
            // PR11 section 6.2: the Addon's own controlled transition is the cross-mode continuity
            // bridge -- the write succeeded, the old PID1901 disappeared, exactly one present PID1902
            // target logical group appeared, and both source and target topology verified. The
            // Windows physical-root/container string legitimately changes across a real MSI native
            // mode switch, so it is NOT compared here.
            if (!IsCrossModeTransitionProven(transition, out var transitionFailure))
                return Fail("Pid1902TransitionFailed:" + transitionFailure, true, null);
            AppLog.Info("ControllerOwnership", "PID1901->PID1902 transition verified.", ("Event", "Pid1902TransitionVerified"),
                ("OldPidDisappeared", transition.OldPidDisappeared), ("TargetPidAppeared", transition.TargetPidAppeared),
                ("SourceIdentityVerified", transition.SourceIdentityVerified), ("TargetTopologyVerified", transition.TargetTopologyVerified),
                ("CrossModeTransitionVerified", true), ("CurrentTargetTopologyVerified", true));
        }

        // 6-7. Authoritative post-transition capture; final state must be PID1902 / Strong.
        //      Never auto-roll PID1902 back to PID1901 on a later failure.
        var finalCapture = await _captureStableNativeState(cancellationToken).ConfigureAwait(false);
        if (!TryReadIdentity(finalCapture, out var finalMode, out var finalIdentity, out var finalReason))
            return Fail("FinalNativeState:" + finalReason, modeWriteIssued, null);
        if (finalMode != MsiClawNativeMode.DirectInput)
            return Fail("FinalModeNotPid1902:" + finalMode, modeWriteIssued, null);
        // Strong physical-identity equality is a SAME-MODE predicate only. Apply it only on an
        // already-PID1902 boot (no mode write); a PID1901->PID1902 transition is proven by the
        // controlled-transition evidence above plus the live-DirectInput proof below (PR11 section 4).
        if (!modeWriteIssued && !initialIdentity.StronglyMatches(finalIdentity))
            return Fail("SameModeIdentityMismatch", modeWriteIssued, null);
        AppLog.Info("ControllerOwnership", "PID1902 transition completed.", ("Event", "Pid1902TransitionCompleted"),
            ("ModeWriteIssued", modeWriteIssued), ("FinalMode", finalMode),
            ("SamePhysicalIdentity", !modeWriteIssued), ("CrossModeTransitionVerified", modeWriteIssued));

        // 8. Bounded DirectInput descriptor resolution (same logic whether or not a mode write ran).
        var descriptor = await ResolveDirectInputDescriptorAsync(cancellationToken).ConfigureAwait(false);
        if (descriptor is null)
            return Fail("DirectInputNotResolved", modeWriteIssued, null);

        // 9-10. The selected DirectInput PnP collection must be the exact primary PID1902 collection
        //       AND belong to the same strong native physical MSI Claw.
        if (!MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(descriptor.PnpInstanceId))
            return Fail("DirectInputNotPrimaryCollection", modeWriteIssued, null);
        var pnpDevice = _resolvePnpDevice(descriptor.PnpInstanceId!);
        if (pnpDevice is null)
            return Fail("DirectInputPnpNodeMissing", modeWriteIssued, null);
        var directInputIdentity = MsiClawPhysicalIdentity.From(pnpDevice);
        if (directInputIdentity.Confidence != MsiClawIdentityConfidence.Strong || !finalIdentity.StronglyMatches(directInputIdentity))
            return Fail("DirectInputPhysicalIdentityMismatch", modeWriteIssued, null);
        AppLog.Info("ControllerOwnership", "DirectInput candidate resolved.", ("Event", "DirectInputCandidateResolved"),
            ("PnpInstanceId", descriptor.PnpInstanceId), ("SamePhysicalIdentity", true));

        // 11. Acquire DirectInput and require a first valid state before any HidHide target mutation.
        //     For an already-PID1902 boot no mode write ran, so DirectInput acquire is the first
        //     process-owned controller mutation -- do the one fresh authority read here instead.
        if (!modeWriteIssued && _captureCenterMStartupState() != FrontendCenterMStartupState.Disabled)
            return Fail("AuthorityChangedBeforeDirectInputAcquire", false, null);
        var start = _inputSource.StartPrepared(descriptor);
        if (!start.Started || !_inputSource.IsRunning)
            return Fail("DirectInputStartFailed:" + start.Status, modeWriteIssued, null);
        bool ready;
        try
        {
            ready = await _inputSource.WaitForFirstValidStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await SafeStopAsync().ConfigureAwait(false);
            throw;
        }
        if (!ready || !_inputSource.IsRunning)
        {
            await SafeStopAsync().ConfigureAwait(false);
            return Fail("FirstValidStateNotObserved", modeWriteIssued, null);
        }
        AppLog.Info("ControllerOwnership", "DirectInput ready.", ("Event", "DirectInputReady"), ("FirstValidState", true));

        // 12-13. Reconcile the persistent PR2 HidHide baseline to the exact target, verified by read-back.
        var target = descriptor.PnpInstanceId!;
        if (!MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(target))
        {
            await SafeStopAsync().ConfigureAwait(false);
            return Fail("HidHideTargetNotPrimaryCollection", modeWriteIssued, null);
        }
        // Remember the exact verified target now, so a later partial/verification failure in the
        // persistent apply cannot lose it -- the official Enable-and-Restart release must still be
        // able to clear exactly this entry (work order PR5 review).
        _ownedHiddenTarget ??= target;
        AddonHidHideBaselineResult baseline;
        try
        {
            baseline = _applyHidHideTarget(target);
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerOwnership", "HidHide target reconciliation threw.", exception);
            await SafeStopAsync().ConfigureAwait(false);
            return Fail("HidHideReconcileThrew", modeWriteIssued, null);
        }
        if (!baseline.IsCompliant)
        {
            await SafeStopAsync().ConfigureAwait(false);
            return Fail("HidHideReconcile:" + baseline.Outcome + ":" + baseline.Reason, modeWriteIssued, null);
        }
        AppLog.Info("ControllerOwnership", "Physical isolation verified.", ("Event", "PhysicalIsolationVerified"),
            ("HiddenTarget", target), ("HidHideOutcome", baseline.Outcome));

        // 14-15. Retain the live DirectInput source for the process lifetime. The strong physical
        //        identity is remembered here (PR8 section 6) so a later unexpected DirectInput session
        //        loss can prove a recovery candidate is this same owned controller.
        _ownsInputSource = true;
        _ownedHiddenTarget = target;
        _ownedPhysicalIdentity = finalIdentity;
        AppLog.Info("ControllerOwnership", "Physical ownership acquired.", ("Result", "Owned"),
            ("ModeWriteIssued", modeWriteIssued), ("HiddenTarget", target));
        return new(MsiClawPhysicalOwnershipOutcome.Owned, "PhysicalOwnershipVerified", modeWriteIssued, target);
    }

    public async Task<PhysicalOwnershipReleaseResult> ReleaseForCenterMEnableAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Prefer the exact target verified this process; otherwise recover the one already-proven
            // owned target from the persistent HidHide configuration (previous-boot target + a
            // current acquisition failure, or a partial persistent apply).
            var target = _ownedHiddenTarget ?? _captureExistingOwnedHiddenTarget();
            _releasedForEnable = true;
            if (_ownsInputSource)
            {
                await _inputSource.StopAsync().ConfigureAwait(false);
                if (_inputSource.IsRunning)
                    return new(false, "DirectInputStillRunning", target);
                _ownsInputSource = false;
            }

            // Restore the same strongly-verified physical MSI Claw to PID1901. Center M roots are
            // about to become Enabled, so PID1901 is now the desired stock authority.
            var current = await _captureStableNativeState(cancellationToken).ConfigureAwait(false);
            if (!TryReadIdentity(current, out var mode, out var identity, out var reason))
                return new(false, "ReleaseNativeState:" + reason, target);
            if (mode is not (MsiClawNativeMode.XInput or MsiClawNativeMode.DirectInput))
                return new(false, "UnsupportedReleaseMode:" + mode, target);
            if (mode == MsiClawNativeMode.DirectInput)
            {
                // PR11 section 9: the inverse PID1902 -> PID1901 transition is proven by the Addon's
                // own controlled-transition evidence and a fresh XInput/PID1901 capture. The Windows
                // physical-root string legitimately changes across the mode switch, so PID1902 and
                // PID1901 identities are NOT compared with StronglyMatches here.
                var switched = await _switchMode(MsiClawNativeMode.XInput, identity, cancellationToken).ConfigureAwait(false);
                if (!IsCrossModeTransitionProven(switched, out var switchFailure))
                    return new(false, "Pid1901RestoreFailed:" + switchFailure, target);
                var verified = await _captureStableNativeState(cancellationToken).ConfigureAwait(false);
                // PR11 section 10: split verification failures -- do not concatenate an unrelated
                // "Ok" (TryReadIdentity succeeded) when a later predicate is what actually failed.
                if (!TryReadIdentity(verified, out var finalMode, out _, out var verifyReason))
                    return new(false, "Pid1901RestoreUnverified:" + verifyReason, target);
                if (finalMode != MsiClawNativeMode.XInput)
                    return new(false, "Pid1901RestoreFinalModeNotXInput:" + finalMode, target);
            }

            AppLog.Info("ControllerOwnership", "Physical ownership released for Center M enable.",
                ("Event", "PhysicalOwnershipReleased"), ("HiddenTarget", target ?? "None"));
            return new(true, "Released", target);
        }
        finally { _gate.Release(); }
    }

    public async Task<MsiClawPhysicalOwnershipResult> RecoverLostInputAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return RecoveryFail("OwnerDisposed", _ownedHiddenTarget);
            if (_releasedForEnable) return RecoveryFail("ReleasedForCenterMEnable", _ownedHiddenTarget);
            return await RecoverLostInputCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<MsiClawPhysicalOwnershipResult> RecoverLostInputCoreAsync(CancellationToken cancellationToken)
    {
        // 10.2 / PR10. "Ownership was committed" is proven by the retained strong physical identity +
        //       exact hidden target that a successful acquisition/recovery stored (and never cleared),
        //       NOT by a currently-live session. This lets recovery re-enter after an earlier
        //       DeviceNotFound failure -- e.g. when a real Windows Device Arrival fires minutes later.
        //       The released-for-Center-M-enable gate is already enforced by RecoverLostInputAsync.
        if (_ownedPhysicalIdentity is not { Confidence: MsiClawIdentityConfidence.Strong } ownedIdentity
            || _ownedHiddenTarget is not { } ownedTarget
            || !MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(ownedTarget))
            return RecoveryFail("OwnerNotCommitted", _ownedHiddenTarget);

        // A still-running source needs no recovery.
        if (_inputSource.IsRunning)
            return new(MsiClawPhysicalOwnershipOutcome.Owned, "RecoveryNotNeeded", false, ownedTarget);

        AppLog.Info("ControllerOwnership", "Owned physical input recovery started.",
            ("Event", "OwnedPhysicalRecoveryStarted"), ("OwnedHiddenTarget", ownedTarget));

        // The dead owned session is accepted for recovery. Do NOT clear the owned identity/target --
        // the official Enable-and-Restart release must still be able to clear exactly this entry.
        _ownsInputSource = false;

        // 10.3. Reuse the same bounded native/PnP settle capture PR5 was given.
        var capture = await _captureStableNativeState(cancellationToken).ConfigureAwait(false);
        if (!TryReadIdentity(capture, out var mode, out var identity, out var reason))
            return RecoveryFail("PhysicalDeviceMissing:" + reason, ownedTarget);

        // 10.4 / PR9 + PR11 section 8. Reconcile the current observed physical mode to the desired
        // PID1902:
        //   - already PID1902 / DirectInput: same-mode recovery -- the current capture must strongly
        //     match the committed owned identity (PR8);
        //   - PID1901 / XInput while Center M is still exactly Disabled and the capture proves a
        //     single unambiguous supported MSI Claw: owned physical-state drift. The reclaim uses the
        //     fresh PID1901 identity as the source expectation and the Addon-issued transition as the
        //     cross-mode bridge -- the old PID1902 root string is NOT compared to the PID1901 root
        //     string (hardware validation proved it changes across a real MSI native mode switch);
        //   - anything else: fail closed. (Ambiguity is already fail-closed inside the native capture.)
        var modeWriteIssued = false;
        MsiClawPhysicalIdentity finalPid1902Identity;
        if (mode == MsiClawNativeMode.DirectInput)
        {
            if (!ownedIdentity.StronglyMatches(identity))
                return RecoveryFail("PhysicalIdentityMismatch", ownedTarget);
            finalPid1902Identity = identity;
        }
        else if (mode == MsiClawNativeMode.XInput)
        {
            AppLog.Warn("ControllerOwnership", "Owned physical-state drift detected.", null,
                ("Event", "OwnedPhysicalStateDriftDetected"), ("CurrentMode", "PID1901"), ("CurrentIdentityConfidence", identity.Confidence));

            // 12. Fresh Center M startup-root authority read immediately before the reclaim write.
            if (_captureCenterMStartupState() != FrontendCenterMStartupState.Disabled)
                return RecoveryFail("AuthorityNotDisabled", ownedTarget);

            AppLog.Info("ControllerOwnership", "Owned physical PID1902 reclaim started.",
                ("Event", "OwnedPhysicalPid1902ReclaimStarted"), ("HiddenTarget", ownedTarget));

            // 7. Exactly one PID1901 -> PID1902 transition per recovery invocation, expecting the
            //    fresh current PID1901 identity as the source. No retry loop, no PID1901 fallback.
            var transition = await _switchMode(MsiClawNativeMode.DirectInput, identity, cancellationToken).ConfigureAwait(false);
            modeWriteIssued = true;
            if (!IsCrossModeTransitionProven(transition, out var transitionFailure))
                return RecoveryFail("OwnedPhysicalStateDriftReclaimFailed:" + transitionFailure, ownedTarget, true);

            // 8. Mandatory bounded post-reclaim verification: fresh final PID1902 / Strong. The
            //    cross-mode root string is proven by the controlled transition + the live DirectInput
            //    proof below, not by StronglyMatches against the pre-write identities.
            var reclaimed = await _captureStableNativeState(cancellationToken).ConfigureAwait(false);
            if (!TryReadIdentity(reclaimed, out var reclaimedMode, out var reclaimedIdentity, out var reclaimReason))
                return RecoveryFail("OwnedPhysicalStateDriftReclaimUnverified:" + reclaimReason, ownedTarget, true);
            if (reclaimedMode != MsiClawNativeMode.DirectInput)
                return RecoveryFail("OwnedPhysicalStateDriftReclaimFinalModeNotPid1902:" + reclaimedMode, ownedTarget, true);
            finalPid1902Identity = reclaimedIdentity;
            // Prior ownership was already committed before the loss. The controlled cross-mode
            // transition + this fresh Strong PID1902 capture now define the CURRENT same-mode PID1902
            // identity -- adopt it immediately so a later PR10 deferred Device Arrival that retries
            // after a subsequent recovery-tail failure compares the current identity, not the stale
            // pre-drift one (review). The exact committed HidHide target is unchanged and still
            // protects the rest of the recovery tail.
            _ownedPhysicalIdentity = reclaimedIdentity;
            AppLog.Info("ControllerOwnership", "Owned physical PID1902 reclaim completed.",
                ("Event", "OwnedPhysicalPid1902ReclaimCompleted"), ("FinalMode", "PID1902"),
                ("FinalIdentityConfidence", reclaimedIdentity.Confidence), ("CrossModeTransitionVerified", true));
        }
        else
        {
            return RecoveryFail("PhysicalDeviceMissing:UnsupportedMode:" + mode, ownedTarget);
        }
        AppLog.Info("ControllerOwnership", "Owned physical recovery native state proven.",
            ("Event", "OwnedPhysicalRecoveryNativeProven"), ("CurrentNativeMode", MsiClawNativeMode.DirectInput),
            ("ModeWriteIssued", modeWriteIssued));

        // 10.5. Re-resolve the DirectInput descriptor through the same bounded selector path.
        var descriptor = await ResolveDirectInputDescriptorAsync(cancellationToken).ConfigureAwait(false);
        if (descriptor is null)
            return RecoveryFail("DirectInputNotResolved", ownedTarget, modeWriteIssued);
        if (!MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(descriptor.PnpInstanceId))
            return RecoveryFail("DirectInputNotResolved:NotPrimaryCollection", ownedTarget, modeWriteIssued);
        var pnpDevice = _resolvePnpDevice(descriptor.PnpInstanceId!);
        if (pnpDevice is null)
            return RecoveryFail("DirectInputNotResolved:PnpNodeMissing", ownedTarget, modeWriteIssued);
        var directInputIdentity = MsiClawPhysicalIdentity.From(pnpDevice);
        // Same-mode PID1902 <-> PID1902 comparison: the fresh final PID1902 native identity must match
        // the resolved PID1902 DirectInput PnP collection (PR11 sections 4, 8.2).
        if (directInputIdentity.Confidence != MsiClawIdentityConfidence.Strong || !finalPid1902Identity.StronglyMatches(directInputIdentity))
            return RecoveryFail("DirectInputPhysicalIdentityMismatch", ownedTarget, modeWriteIssued);

        // 10.6. PR8 only reacquires the exact same persistent hidden target. A changed exact PnP
        //       collection is HidHide target migration -- explicitly a later PR.
        var recoveredTarget = descriptor.PnpInstanceId!;
        if (!string.Equals(recoveredTarget, ownedTarget, StringComparison.OrdinalIgnoreCase))
            return RecoveryFail("RecoveredTargetChanged", ownedTarget, modeWriteIssued);
        AppLog.Info("ControllerOwnership", "Owned physical recovery DirectInput candidate resolved.",
            ("Event", "OwnedPhysicalRecoveryDirectInputResolved"), ("RecoveredTarget", recoveredTarget));

        // 10.8. A fresh shared Center M authority read immediately before the first recovery mutation.
        //       The bounded settle capture above may have taken time.
        if (_captureCenterMStartupState() != FrontendCenterMStartupState.Disabled)
            return RecoveryFail("AuthorityNotDisabled", ownedTarget, modeWriteIssued);

        // 10.7. Verify/repair the persistent HidHide baseline for the SAME exact target BEFORE
        //       restarting DirectInput -- a virtual presentation is already attached to this source,
        //       so physical isolation must be proven before non-neutral input can resume.
        AddonHidHideBaselineResult baseline;
        try
        {
            baseline = _applyHidHideTarget(ownedTarget);
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerOwnership", "Owned physical recovery HidHide reconciliation threw.", exception);
            return RecoveryFail("HidHideReconcileFailed:Threw", ownedTarget, modeWriteIssued);
        }
        if (!baseline.IsCompliant)
            return RecoveryFail("HidHideReconcileFailed:" + baseline.Outcome + ":" + baseline.Reason, ownedTarget, modeWriteIssued);
        AppLog.Info("ControllerOwnership", "Owned physical recovery isolation verified.",
            ("Event", "OwnedPhysicalRecoveryIsolationVerified"), ("HiddenTarget", ownedTarget), ("HidHideOutcome", baseline.Outcome));

        // 10.9. Restart the SAME input source object. Never construct a replacement.
        var start = _inputSource.StartPrepared(descriptor);
        if (!start.Started || !_inputSource.IsRunning)
        {
            await SafeStopAsync().ConfigureAwait(false);
            return RecoveryFail("DirectInputStartFailed:" + start.Status, ownedTarget, modeWriteIssued);
        }
        bool ready;
        try
        {
            ready = await _inputSource.WaitForFirstValidStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await SafeStopAsync().ConfigureAwait(false);
            throw;
        }
        if (!ready || !_inputSource.IsRunning)
        {
            await SafeStopAsync().ConfigureAwait(false);
            return RecoveryFail("FirstValidStateNotObserved", ownedTarget, modeWriteIssued);
        }

        // 10.10 / PR11 section 8.4. Commit. The exact hidden target is unchanged; the existing
        //        publisher is already reading this same source and resumes receiving live snapshots.
        //        (After a PID1901->PID1902 reclaim, _ownedPhysicalIdentity was already refreshed to
        //        the fresh Strong PID1902 identity right after the post-reclaim capture verified.)
        _ownsInputSource = true;
        var successReason = modeWriteIssued ? "OwnedPhysicalStateDriftReclaimed" : "OwnedPhysicalInputRecovered";
        AppLog.Info("ControllerOwnership", "Owned physical input recovery succeeded.",
            ("Event", "OwnedPhysicalRecoverySucceeded"), ("Reason", successReason),
            ("HiddenTarget", ownedTarget), ("ModeWriteIssued", modeWriteIssued), ("DirectInputStartStatus", start.Status));
        return new(MsiClawPhysicalOwnershipOutcome.Owned, successReason, modeWriteIssued, ownedTarget);
    }

    private static MsiClawPhysicalOwnershipResult RecoveryFail(string reason, string? hiddenTarget, bool modeWriteIssued = false)
    {
        AppLog.Warn("ControllerOwnership", "Owned physical input recovery failed.", null,
            ("Event", "OwnedPhysicalRecoveryFailed"), ("Reason", reason), ("ModeWriteIssued", modeWriteIssued));
        return new(MsiClawPhysicalOwnershipOutcome.Failed, reason, modeWriteIssued, hiddenTarget);
    }

    private async Task<DirectInputDeviceDescriptor?> ResolveDirectInputDescriptorAsync(CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(_directInputSettleWindow.TotalSeconds * Stopwatch.Frequency);
        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;
            MsiClawDirectInputSelectionResult selection;
            try
            {
                selection = MsiClawDirectInputDeviceSelector.Select(_enumerateDirectInputDevices());
            }
            catch (Exception exception)
            {
                AppLog.Warn("ControllerOwnership", "DirectInput enumeration failed.", exception, ("Attempt", attempt));
                selection = new(MsiClawDirectInputSelectionStatus.NotFound, null, 0, exception.GetType().Name);
            }

            if (selection.IsSelected)
                return selection.Descriptor;

            // NotFound and the explicitly-transient "unresolved identity" descriptor shape (the
            // enumerator tolerating an inspection/topology lookup miss right after PID re-enumeration)
            // are normal PnP settle states -- retry them inside the bounded window. Proven-invalid
            // topology (multiple physical/PnP identities, insufficient buttons) stays fail-closed.
            var retryableSettle = selection.Status == MsiClawDirectInputSelectionStatus.NotFound
                || (selection.Status == MsiClawDirectInputSelectionStatus.Indeterminate && selection.Reason == "PhysicalIdentityUnverified");
            if (!retryableSettle)
            {
                AppLog.Warn("ControllerOwnership", "DirectInput selection is not safely retryable.", null, ("Reason", selection.Reason));
                return null;
            }
            if (Stopwatch.GetTimestamp() >= deadline)
            {
                AppLog.Warn("ControllerOwnership", "DirectInput selection window expired.", null, ("Attempts", attempt), ("Reason", selection.Reason));
                return null;
            }
            await _delay(_directInputSettleInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>PR11 sections 3-6: an Addon-issued cross-mode PID transition is proven by the
    /// transition's own evidence -- a successful write, the old PID gone, exactly one present target
    /// logical group, and verified source + target topology -- NOT by physical-root string equality
    /// (hardware validation proved that string is not stable across a real MSI native mode switch).</summary>
    private static bool IsCrossModeTransitionProven(MsiClawModeTransitionResult transition, out string failure)
    {
        if (!transition.Succeeded) { failure = transition.Status + ":" + transition.Reason; return false; }
        if (!transition.WriteSucceeded) { failure = "WriteNotConfirmed"; return false; }
        if (!transition.OldPidDisappeared) { failure = "OldPidStillPresent"; return false; }
        if (!transition.TargetPidAppeared) { failure = "TargetPidDidNotAppear"; return false; }
        if (!transition.SourceIdentityVerified) { failure = "SourceIdentityNotVerified"; return false; }
        if (!transition.TargetTopologyVerified) { failure = "TargetTopologyNotVerified"; return false; }
        failure = string.Empty;
        return true;
    }

    private static bool TryReadIdentity(NativeStateCaptureResult capture, out MsiClawNativeMode mode, out MsiClawPhysicalIdentity identity, out string reason)
    {
        mode = MsiClawNativeMode.Other;
        identity = null!;
        if (!capture.AllowsMutation || capture.Snapshot is null)
        {
            reason = capture.Status + ":" + capture.Reason;
            return false;
        }
        MsiClawNativeStatePayload? payload;
        try { payload = capture.Snapshot.Payload.Deserialize<MsiClawNativeStatePayload>(); }
        catch (JsonException) { reason = "MalformedPayload"; return false; }
        if (payload is null) { reason = "MalformedPayload"; return false; }
        identity = MsiClawPhysicalIdentity.FromPayload(payload);
        if (identity.Confidence != MsiClawIdentityConfidence.Strong) { reason = "IdentityNotStrong"; return false; }
        mode = payload.Mode;
        reason = "Ok";
        return true;
    }

    private async Task SafeStopAsync()
    {
        try
        {
            await _inputSource.StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerOwnership", "DirectInput stop after failed acquisition threw.", exception);
        }
    }

    private static MsiClawPhysicalOwnershipResult Fail(string reason, bool modeWriteIssued, string? hiddenTarget)
    {
        AppLog.Warn("ControllerOwnership", "Physical ownership failed.", null, ("Result", "Failed"), ("Reason", reason), ("ModeWriteIssued", modeWriteIssued));
        return new(MsiClawPhysicalOwnershipOutcome.Failed, reason, modeWriteIssued, hiddenTarget);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Release only the process-owned DirectInput session. The exact HidHide target is
            // persistent configuration and PID1902 is the desired state while Center M is Disabled --
            // neither is touched on teardown (work order PR5 section 17).
            if (_ownsInputSource)
            {
                _ownsInputSource = false;
                await SafeStopAsync().ConfigureAwait(false);
            }
            try { await _inputSource.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { AppLog.Warn("ControllerOwnership", "DirectInput dispose threw.", exception); }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
