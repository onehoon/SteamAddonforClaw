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
            if (!transition.Succeeded)
                return Fail("Pid1902TransitionFailed:" + transition.Status + ":" + transition.Reason, true, null);
        }

        // 6-7. Authoritative post-transition capture; final state must be PID1902 with the SAME
        //      strong physical identity. Never auto-roll PID1902 back to PID1901 on a later failure.
        var finalCapture = await _captureStableNativeState(cancellationToken).ConfigureAwait(false);
        if (!TryReadIdentity(finalCapture, out var finalMode, out var finalIdentity, out var finalReason))
            return Fail("FinalNativeState:" + finalReason, modeWriteIssued, null);
        if (finalMode != MsiClawNativeMode.DirectInput)
            return Fail("FinalModeNotPid1902:" + finalMode, modeWriteIssued, null);
        if (!initialIdentity.StronglyMatches(finalIdentity))
            return Fail("CrossModeIdentityMismatch", modeWriteIssued, null);
        AppLog.Info("ControllerOwnership", "PID1902 transition completed.", ("Event", "Pid1902TransitionCompleted"),
            ("ModeWriteIssued", modeWriteIssued), ("FinalMode", finalMode), ("SamePhysicalIdentity", true));

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

        // 14-15. Retain the live DirectInput source for the process lifetime.
        _ownsInputSource = true;
        _ownedHiddenTarget = target;
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
            if (mode == MsiClawNativeMode.DirectInput)
            {
                var switched = await _switchMode(MsiClawNativeMode.XInput, identity, cancellationToken).ConfigureAwait(false);
                if (!switched.Succeeded)
                    return new(false, "Pid1901RestoreFailed:" + switched.Status + ":" + switched.Reason, target);
                var verified = await _captureStableNativeState(cancellationToken).ConfigureAwait(false);
                if (!TryReadIdentity(verified, out var finalMode, out var finalIdentity, out var verifyReason)
                    || finalMode != MsiClawNativeMode.XInput
                    || !identity.StronglyMatches(finalIdentity))
                    return new(false, "Pid1901RestoreUnverified:" + verifyReason, target);
            }

            AppLog.Info("ControllerOwnership", "Physical ownership released for Center M enable.",
                ("Event", "PhysicalOwnershipReleased"), ("HiddenTarget", target ?? "None"));
            return new(true, "Released", target);
        }
        finally { _gate.Release(); }
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
