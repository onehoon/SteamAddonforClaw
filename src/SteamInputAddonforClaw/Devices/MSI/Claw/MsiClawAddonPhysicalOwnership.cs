using System.Diagnostics;
using System.Text.Json;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Input.DirectInput;
using SteamInputAddonforClaw.Prerequisites;

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

internal enum MsiClawInitialAcquisitionRetryReason
{
    None,
    BootRumbleTargetPidNotPresent,
    Pid1902TargetPidNotPresent,
}

internal sealed record MsiClawPhysicalOwnershipResult(
    MsiClawPhysicalOwnershipOutcome Outcome,
    string Reason,
    bool ModeWriteIssued,
    IReadOnlyList<string> HiddenTargets,
    MsiClawInitialAcquisitionRetryReason InitialAcquisitionRetryReason = MsiClawInitialAcquisitionRetryReason.None,
    bool NativeDeviceAbsentAtInitialCapture = false)
{
    internal bool IsOwned => Outcome == MsiClawPhysicalOwnershipOutcome.Owned;
    internal string? PrimaryHiddenTarget => HiddenTargets.FirstOrDefault();

    internal static MsiClawPhysicalOwnershipResult NotApplicable(string reason) =>
        new(MsiClawPhysicalOwnershipOutcome.NotApplicable, reason, false, []);
}

/// <summary>Result of the narrow PR5 release seam the official Center M Enable-and-Restart path
/// runs before it clears HidHide. <see cref="HiddenTargets"/> is the complete exact Addon-owned
/// target set, including any optional auxiliary collections.</summary>
internal sealed record PhysicalOwnershipReleaseResult(bool Succeeded, string Reason, IReadOnlyList<string> HiddenTargets)
{
    internal static PhysicalOwnershipReleaseResult NothingOwned { get; } = new(true, "NoPhysicalOwnership", []);
}

internal enum DeveloperRumbleRearmPhysicalOutcome { Completed, Unavailable, Failed }

internal sealed record DeveloperRumbleRearmPhysicalResult(
    DeveloperRumbleRearmPhysicalOutcome Outcome,
    string Reason,
    bool XInputTransitionVerified,
    bool DirectInputTransitionVerified,
    bool PhysicalOwnershipRestored,
    bool ModeWriteIssued)
{
    internal bool Succeeded => Outcome == DeveloperRumbleRearmPhysicalOutcome.Completed;
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

    /// <summary>The strong physical identity committed by the currently owned PID1902 session.</summary>
    MsiClawPhysicalIdentity? OwnedPhysicalIdentity { get; }
    MsiClawPhysicalInputIdentity? CurrentIdentity { get; }
    long CurrentSessionGeneration { get; }
    string? OwnedPrimaryHiddenTarget { get; }

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

    /// <summary>One explicit Developer-only A2VM rumble re-arm. The caller has already stopped and
    /// drained the active virtual presentation. This method owns the physical serialization gate,
    /// stops the current DirectInput session with cleanup proof, verifies PID1902->PID1901->PID1902,
    /// then enters the existing stopped-session recovery core.</summary>
    Task<string?> CheckDeveloperRumbleRearmAdmissionAsync();
    Task<DeveloperRumbleRearmPhysicalResult> RunDeveloperRumbleRearmAsync(Func<bool> mayBeginModeMutation);
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
internal sealed class MsiClawAddonPhysicalOwnership : IMsiClawAddonPhysicalOwnership, IMsiClawPhysicalInputIdentityProvider
{
    private readonly Func<FrontendCenterMStartupState> _captureCenterMStartupState;
    private readonly Func<CancellationToken, Task<NativeStateCaptureResult>> _captureStableNativeState;
    private readonly Func<MsiClawNativeMode, MsiClawPhysicalIdentity, CancellationToken, Task<MsiClawModeTransitionResult>> _switchMode;
    private readonly Func<IReadOnlyList<DirectInputDeviceDescriptor>> _enumerateDirectInputDevices;
    private readonly Func<string, ControllerDeviceInfo?> _resolvePnpDevice;
    private readonly Func<IReadOnlyList<ControllerDeviceInfo>> _enumeratePnpDevices;
    private readonly Func<ushort, ushort, IReadOnlyList<ControllerDeviceInfo>> _enumeratePnpDevicesByVidPid;
    private readonly Func<ushort, ushort, bool> _isPnpDevicePresent;
    private readonly IMsiClawPreparedInputSource _inputSource;
    private readonly Func<IReadOnlyCollection<string>, AddonHidHideBaselineResult> _applyHidHideTargets;
    private readonly Func<IReadOnlyList<string>> _captureExistingOwnedHiddenTargets;
    private readonly IMsiClawGamepadModeClient? _gamepadModeClient;
    private readonly HandheldDeviceModelId? _hardwareDeviceModel;
    private readonly Func<BootSessionAttemptResult> _claimA2vmBootAttempt;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<long> _getTimestamp;
    private readonly TimeSpan _directInputSettleWindow;
    private readonly TimeSpan _directInputSettleInterval;
    private readonly TimeSpan _a2vmBootPid1901SettleWindow;
    private readonly TimeSpan _a2vmBootPid1901SettleInterval;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ownsInputSource;
    private string? _ownedPrimaryHiddenTarget;
    private IReadOnlyList<string> _ownedHiddenTargets = [];
    // Production rumble physical identity/generation (work order section 6). Guarded by its own tiny
    // lock, not _gate: MsiClawRumbleSink reads these on a VIIPER-owned callback thread and must never
    // block on the async acquisition/recovery gate. The generation advances only when a real live
    // DirectInput session is committed -- never for a virtual Xbox360<->SteamDeck presentation switch.
    private readonly Lock _identitySync = new();
    private MsiClawPhysicalInputIdentity? _currentIdentity;
    private long _currentSessionGeneration;
    // PR8 section 6: the strong physical identity committed by a successful acquisition. Kept only in
    // memory, never persisted, and never cleared on a recovery failure -- the official
    // Enable-and-Restart release still needs it as ownership evidence.
    private MsiClawPhysicalIdentity? _ownedPhysicalIdentity;
    private bool _releasedForEnable;
    private int _disposed;

    private sealed record GamepadModeNormalizationResult(
        bool Succeeded,
        bool WriteIssued,
        MsiClawGamepadMode? ObservedMode,
        string Reason);

    private enum A2vmBootPid1901ProbeStatus
    {
        Ready,
        TargetPidNotPresent,
        CommandEndpointNotPresent,
        OldPidStillPresent,
        AmbiguousLogicalTarget,
        AmbiguousCommandEndpoint,
        StrongIdentityUnavailable,
        ReadFailed,
    }

    private sealed record A2vmBootPid1901ProbeResult(
        A2vmBootPid1901ProbeStatus Status,
        MsiClawPhysicalIdentity? Identity = null,
        InvalidOperationException? ReadException = null);

    private sealed record A2vmBootPid1901Resolution(
        bool Succeeded,
        MsiClawPhysicalIdentity? Identity,
        string Reason,
        bool CanDeferInitialAcquisition);

    internal MsiClawAddonPhysicalOwnership(
        Func<FrontendCenterMStartupState> captureCenterMStartupState,
        Func<CancellationToken, Task<NativeStateCaptureResult>> captureStableNativeState,
        Func<MsiClawNativeMode, MsiClawPhysicalIdentity, CancellationToken, Task<MsiClawModeTransitionResult>> switchMode,
        Func<IReadOnlyList<DirectInputDeviceDescriptor>> enumerateDirectInputDevices,
        Func<string, ControllerDeviceInfo?> resolvePnpDevice,
        Func<IReadOnlyList<ControllerDeviceInfo>> enumeratePnpDevices,
        Func<ushort, ushort, IReadOnlyList<ControllerDeviceInfo>> enumeratePnpDevicesByVidPid,
        Func<ushort, ushort, bool> isPnpDevicePresent,
        IMsiClawPreparedInputSource inputSource,
        Func<IReadOnlyCollection<string>, AddonHidHideBaselineResult> applyHidHideTargets,
        Func<IReadOnlyList<string>> captureExistingOwnedHiddenTargets,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? directInputSettleWindow = null,
        TimeSpan? directInputSettleInterval = null,
        IMsiClawGamepadModeClient? gamepadModeClient = null,
        HandheldDeviceModelId? hardwareDeviceModel = null,
        Func<BootSessionAttemptResult>? claimA2vmBootAttempt = null,
        Func<long>? getTimestamp = null,
        TimeSpan? a2vmBootPid1901SettleWindow = null,
        TimeSpan? a2vmBootPid1901SettleInterval = null)
    {
        _captureCenterMStartupState = captureCenterMStartupState;
        _captureStableNativeState = captureStableNativeState;
        _switchMode = switchMode;
        _enumerateDirectInputDevices = enumerateDirectInputDevices;
        _resolvePnpDevice = resolvePnpDevice;
        _enumeratePnpDevices = enumeratePnpDevices;
        _enumeratePnpDevicesByVidPid = enumeratePnpDevicesByVidPid;
        _isPnpDevicePresent = isPnpDevicePresent;
        _inputSource = inputSource;
        _applyHidHideTargets = applyHidHideTargets;
        _captureExistingOwnedHiddenTargets = captureExistingOwnedHiddenTargets;
        _gamepadModeClient = gamepadModeClient;
        _hardwareDeviceModel = hardwareDeviceModel;
        _claimA2vmBootAttempt = claimA2vmBootAttempt ?? BootSession.TryClaimA2vmRumbleAttempt;
        _delay = delay ?? Task.Delay;
        _getTimestamp = getTimestamp ?? Stopwatch.GetTimestamp;
        _directInputSettleWindow = directInputSettleWindow ?? TimeSpan.FromSeconds(3);
        _directInputSettleInterval = directInputSettleInterval ?? TimeSpan.FromMilliseconds(150);
        _a2vmBootPid1901SettleWindow = a2vmBootPid1901SettleWindow ?? TimeSpan.FromSeconds(5);
        _a2vmBootPid1901SettleInterval = a2vmBootPid1901SettleInterval ?? TimeSpan.FromMilliseconds(250);
    }

    public IMsiClawPreparedInputSource? LiveInputSource => _ownsInputSource ? _inputSource : null;

    public MsiClawPhysicalIdentity? OwnedPhysicalIdentity => _ownsInputSource ? _ownedPhysicalIdentity : null;

    public string? OwnedPrimaryHiddenTarget => _ownsInputSource ? _ownedPrimaryHiddenTarget : null;

    public MsiClawPhysicalInputIdentity? CurrentIdentity { get { lock (_identitySync) return _currentIdentity; } }

    public long CurrentSessionGeneration { get { lock (_identitySync) return _currentSessionGeneration; } }

    /// <summary>Publishes the just-committed live DirectInput session's identity from the same verified
    /// descriptor acquisition/recovery already proved, and advances the physical-session generation.</summary>
    private void PublishLivePhysicalSession(DirectInputDeviceDescriptor descriptor)
    {
        lock (_identitySync)
        {
            _currentIdentity = new MsiClawPhysicalInputIdentity(
                descriptor.InstanceGuid,
                descriptor.DevicePath ?? string.Empty,
                descriptor.PnpInstanceId ?? string.Empty,
                descriptor.PhysicalIdentity ?? string.Empty);
            _currentSessionGeneration++;
        }
    }

    /// <summary>Marks the owned DirectInput session no longer usable for normal rumble writes (real
    /// loss, Center M Enable-and-Restart release, or teardown). A stale in-flight write is then
    /// rejected by <see cref="MsiClawRumbleSink"/> on the null identity.</summary>
    private void ClearLivePhysicalSession()
    {
        lock (_identitySync) _currentIdentity = null;
    }

    public async Task<MsiClawPhysicalOwnershipResult> AcquireAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return Fail("OwnerDisposed", false);
            if (_releasedForEnable) return Fail("ReleasedForCenterMEnable", false);
            if (_ownsInputSource) return new(MsiClawPhysicalOwnershipOutcome.Owned, "AlreadyOwned", false, _ownedHiddenTargets);
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
            return Fail("InitialNativeState:" + reason, false,
                nativeDeviceAbsentAtInitialCapture: initialCapture.Status == NativeStateCaptureStatus.DeviceNotFound);
        if (initialMode is not (MsiClawNativeMode.XInput or MsiClawNativeMode.DirectInput))
            return Fail("UnsupportedInitialMode:" + initialMode, false);
        AppLog.Info("ControllerOwnership", "Native state captured.", ("Event", "NativeStateCaptured"),
            ("Mode", initialMode), ("IdentityConfidence", initialIdentity.Confidence),
            ("ModeWriteRequired", initialMode == MsiClawNativeMode.XInput));

        var bootAttempt = BootSessionAttemptResult.Unavailable;
        if (IsA2vmBootRumblePrimeModel(_hardwareDeviceModel))
        {
            if (_captureCenterMStartupState() != FrontendCenterMStartupState.Disabled)
                return Fail("AuthorityChangedBeforeBootRumbleAttempt", false);

            bootAttempt = _claimA2vmBootAttempt();
            AppLog.Info("ControllerOwnership", "A2VM boot-scoped rumble re-arm attempt marker was evaluated.",
                ("Event", "A2vmBootRumbleAttemptMarker"),
                ("Model", _hardwareDeviceModel!.Value.Value),
                ("Outcome", bootAttempt),
                ("WillRunBootCycle", initialMode == MsiClawNativeMode.DirectInput
                    && bootAttempt == BootSessionAttemptResult.Claimed));
        }

        var pidTransitionWriteIssued = false;
        var bootRumbleCycleEnabled = initialMode == MsiClawNativeMode.DirectInput
            && bootAttempt == BootSessionAttemptResult.Claimed;
        if (bootRumbleCycleEnabled)
        {
            MsiClawControlHidDevice? controlHid = null;
            try
            {
                controlHid = new MsiClawControlHidResolver().Resolve(
                    _enumeratePnpDevices(), MsiClawNativeMode.DirectInput, initialIdentity);
            }
            catch (Exception exception)
            {
                AppLog.Warn("ControllerOwnership", "A2VM boot rumble re-arm control-HID preflight failed; optional mode cycle is skipped.", exception,
                    ("Event", "A2vmBootRumbleControlHidPreflightFailed"));
            }

            if (controlHid is null)
            {
                bootRumbleCycleEnabled = false;
                AppLog.Info("ControllerOwnership", "A2VM boot rumble re-arm skipped because the exact PID1902 control HID is unavailable or ambiguous.",
                    ("Event", "A2vmBootRumbleCycleSkipped"),
                    ("Reason", "ExactControlHidUnavailableOrAmbiguous"));
            }
        }

        if (bootRumbleCycleEnabled)
        {
            // The one optional A2VM boot cycle starts from PID1902. Consume the marker before this
            // first native write, then reuse the existing verified PID1901->PID1902 acquisition below.
            if (_captureCenterMStartupState() != FrontendCenterMStartupState.Disabled)
                return Fail("AuthorityChangedBeforeBootRumblePid1901Transition", false);

            var toXInput = await _switchMode(MsiClawNativeMode.XInput, initialIdentity, cancellationToken).ConfigureAwait(false);
            pidTransitionWriteIssued = true;
            var latePid1901Target = !toXInput.Succeeded
                && IsTargetNotPresentAfterVerifiedWrite(toXInput, MsiClawNativeMode.DirectInput, MsiClawNativeMode.XInput);
            if (!IsCrossModeTransitionProven(toXInput, out var toXInputFailure) && !latePid1901Target)
                return Fail("BootRumblePid1901TransitionFailed:" + toXInputFailure, true,
                    IsTargetNotPresentAfterVerifiedWrite(toXInput, MsiClawNativeMode.DirectInput, MsiClawNativeMode.XInput)
                        ? MsiClawInitialAcquisitionRetryReason.BootRumbleTargetPidNotPresent
                        : MsiClawInitialAcquisitionRetryReason.None);

            var pid1901Resolution = await ResolveBootRumblePid1901EndpointAsync(
                toXInput, latePid1901Target, cancellationToken).ConfigureAwait(false);
            if (!pid1901Resolution.Succeeded || pid1901Resolution.Identity is not { } xInputIdentity)
                return Fail("BootRumblePid1901StateUnverified:" + pid1901Resolution.Reason, true,
                    pid1901Resolution.CanDeferInitialAcquisition
                        ? MsiClawInitialAcquisitionRetryReason.BootRumbleTargetPidNotPresent
                        : MsiClawInitialAcquisitionRetryReason.None);

            initialMode = MsiClawNativeMode.XInput;
            initialIdentity = xInputIdentity;
        }

        // Normal PID1901 -> PID1902 acquisition, including the second half of a boot-only A2VM cycle.
        // Recheck Center M at the actual native mutation boundary in either case.
        if (initialMode == MsiClawNativeMode.XInput)
        {
            if (_captureCenterMStartupState() != FrontendCenterMStartupState.Disabled)
                return Fail("AuthorityChangedBeforeModeWrite", pidTransitionWriteIssued);
            var transition = await _switchMode(MsiClawNativeMode.DirectInput, initialIdentity, cancellationToken).ConfigureAwait(false);
            pidTransitionWriteIssued = true;
            // PR11 section 6.2: the Addon's own controlled transition is the cross-mode continuity
            // bridge -- the write succeeded, the old PID1901 disappeared, exactly one present PID1902
            // target logical group appeared, and both source and target topology verified. The
            // Windows physical-root/container string legitimately changes across a real MSI native
            // mode switch, so it is NOT compared here.
            if (!IsCrossModeTransitionProven(transition, out var transitionFailure))
                return Fail("Pid1902TransitionFailed:" + transitionFailure, true,
                    IsTargetNotPresentAfterVerifiedWrite(transition, MsiClawNativeMode.XInput, MsiClawNativeMode.DirectInput)
                        ? MsiClawInitialAcquisitionRetryReason.Pid1902TargetPidNotPresent
                        : MsiClawInitialAcquisitionRetryReason.None);
            AppLog.Info("ControllerOwnership", "PID1901->PID1902 transition verified.", ("Event", "Pid1902TransitionVerified"),
                ("OldPidDisappeared", transition.OldPidDisappeared), ("TargetPidAppeared", transition.TargetPidAppeared),
                ("SourceIdentityVerified", transition.SourceIdentityVerified), ("TargetTopologyVerified", transition.TargetTopologyVerified),
                ("CrossModeTransitionVerified", true), ("CurrentTargetTopologyVerified", true));
        }

        // 6-7. Authoritative post-transition capture; final state must be PID1902 / Strong.
        //      Never auto-roll PID1902 back to PID1901 on a later failure.
        var finalCapture = await _captureStableNativeState(cancellationToken).ConfigureAwait(false);
        if (!TryReadIdentity(finalCapture, out var finalMode, out var finalIdentity, out var finalReason))
            return Fail("FinalNativeState:" + finalReason, pidTransitionWriteIssued);
        if (finalMode != MsiClawNativeMode.DirectInput)
            return Fail("FinalModeNotPid1902:" + finalMode, pidTransitionWriteIssued);
        // Strong physical-identity equality is a SAME-MODE predicate only. Apply it only on an
        // already-PID1902 boot (no mode write); a PID1901->PID1902 transition is proven by the
        // controlled-transition evidence above plus the live-DirectInput proof below (PR11 section 4).
        if (!pidTransitionWriteIssued && !initialIdentity.StronglyMatches(finalIdentity))
            return Fail("SameModeIdentityMismatch", pidTransitionWriteIssued);

        var gamepadMode = await EnsureDirectInputGamepadModeAsync(
            finalIdentity, "Startup", cancellationToken).ConfigureAwait(false);
        if (!gamepadMode.Succeeded)
            return Fail(gamepadMode.Reason, pidTransitionWriteIssued || gamepadMode.WriteIssued);
        var anyModeWriteIssued = pidTransitionWriteIssued || gamepadMode.WriteIssued;
        AppLog.Info("ControllerOwnership", "PID1902 transition completed.", ("Event", "Pid1902TransitionCompleted"),
            ("ModeWriteIssued", anyModeWriteIssued), ("FinalMode", finalMode),
            ("SamePhysicalIdentity", !pidTransitionWriteIssued), ("CrossModeTransitionVerified", pidTransitionWriteIssued));

        // 8. Bounded DirectInput descriptor resolution (same logic whether or not a mode write ran).
        var descriptor = await ResolveDirectInputDescriptorAsync(cancellationToken).ConfigureAwait(false);
        if (descriptor is null)
            return Fail("DirectInputNotResolved", anyModeWriteIssued);

        // 9-10. The selected DirectInput PnP collection must be the exact primary PID1902 collection
        //       AND belong to the same strong native physical MSI Claw.
        if (!MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(descriptor.PnpInstanceId))
            return Fail("DirectInputNotPrimaryCollection", anyModeWriteIssued);
        var pnpDevice = _resolvePnpDevice(descriptor.PnpInstanceId!);
        if (pnpDevice is null)
            return Fail("DirectInputPnpNodeMissing", anyModeWriteIssued);
        var directInputIdentity = MsiClawPhysicalIdentity.From(pnpDevice);
        if (directInputIdentity.Confidence != MsiClawIdentityConfidence.Strong || !finalIdentity.StronglyMatches(directInputIdentity))
            return Fail("DirectInputPhysicalIdentityMismatch", anyModeWriteIssued);
        AppLog.Info("ControllerOwnership", "DirectInput candidate resolved.", ("Event", "DirectInputCandidateResolved"),
            ("PnpInstanceId", descriptor.PnpInstanceId), ("SamePhysicalIdentity", true));

        // 11. Acquire DirectInput and require a first valid state before any HidHide target mutation.
        //     For an already-PID1902 boot no mode write ran, so DirectInput acquire is the first
        //     process-owned controller mutation -- do the one fresh authority read here instead.
        if (!anyModeWriteIssued && _captureCenterMStartupState() != FrontendCenterMStartupState.Disabled)
            return Fail("AuthorityChangedBeforeDirectInputAcquire", false);
        var start = _inputSource.StartPrepared(descriptor);
        if (!start.Started || !_inputSource.IsRunning)
            return Fail("DirectInputStartFailed:" + start.Status, anyModeWriteIssued);
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
            return Fail("FirstValidStateNotObserved", anyModeWriteIssued);
        }
        AppLog.Info("ControllerOwnership", "DirectInput ready.", ("Event", "DirectInputReady"), ("FirstValidState", true));

        // 12-13. Reconcile the persistent PR2 HidHide baseline to the exact current target set,
        // verified by read-back. The primary collection is mandatory; auxiliary collections are
        // included only when their exact instance, usage, and physical root are uniquely proven.
        var target = descriptor.PnpInstanceId!;
        if (!MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(target))
        {
            await SafeStopAsync().ConfigureAwait(false);
            return Fail("HidHideTargetNotPrimaryCollection", anyModeWriteIssued);
        }
        MsiClawHidHideTargetResolution targetResolution;
        try
        {
            targetResolution = MsiClawHardware.ResolveOwnedPid1902HidHideTargets(pnpDevice, _enumeratePnpDevices());
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerOwnership", "PID1902 HidHide target-set resolution threw.", exception);
            await SafeStopAsync().ConfigureAwait(false);
            return Fail("HidHideTargetSetResolutionThrew", anyModeWriteIssued);
        }
        if (targetResolution.Targets.Count == 0 || !string.Equals(targetResolution.Targets[0], target, StringComparison.OrdinalIgnoreCase))
        {
            await SafeStopAsync().ConfigureAwait(false);
            return Fail("HidHideTargetSetMissingPrimary", anyModeWriteIssued);
        }
        foreach (var diagnostic in targetResolution.Diagnostics)
            AppLog.Warn("ControllerOwnership", "An auxiliary PID1902 HidHide target was omitted.", null, ("Event", "AuxiliaryHidHideTargetOmitted"), ("Diagnostic", diagnostic));

        // Remember the exact target set now, so a later partial/verification failure in the
        // persistent apply cannot lose any target the owner attempted to reconcile.
        _ownedPrimaryHiddenTarget ??= target;
        _ownedHiddenTargets = targetResolution.Targets;
        AddonHidHideBaselineResult baseline;
        try
        {
            baseline = _applyHidHideTargets(targetResolution.Targets);
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerOwnership", "HidHide target reconciliation threw.", exception);
            await SafeStopAsync().ConfigureAwait(false);
            return Fail("HidHideReconcileThrew", anyModeWriteIssued);
        }
        if (!baseline.IsCompliant)
        {
            await SafeStopAsync().ConfigureAwait(false);
            return Fail("HidHideReconcile:" + baseline.Outcome + ":" + baseline.Reason, anyModeWriteIssued);
        }
        AppLog.Info("ControllerOwnership", "Physical isolation verified.", ("Event", "PhysicalIsolationVerified"),
            ("PrimaryHiddenTarget", target), ("HiddenTargetCount", targetResolution.Targets.Count),
            ("HiddenTargets", string.Join(";", targetResolution.Targets)),
            ("ControlHidden", targetResolution.Targets.Count > 1 && MsiClawHardware.TryClassifyOwnedPid1902HidHideTarget(targetResolution.Targets[1], out var controlKind) && controlKind == MsiClawHidHideTargetKind.Control),
            ("ConsumerHidden", targetResolution.Targets.Any(candidate => MsiClawHardware.TryClassifyOwnedPid1902HidHideTarget(candidate, out var consumerKind) && consumerKind == MsiClawHidHideTargetKind.Consumer)),
            ("HidHideOutcome", baseline.Outcome));

        // 14-15. Retain the live DirectInput source for the process lifetime. The strong physical
        //        identity is remembered here (PR8 section 6) so a later unexpected DirectInput session
        //        loss can prove a recovery candidate is this same owned controller.
        _ownsInputSource = true;
        _ownedPrimaryHiddenTarget = target;
        _ownedPhysicalIdentity = finalIdentity;
        PublishLivePhysicalSession(descriptor);
        AppLog.Info("ControllerOwnership", "Physical ownership acquired.", ("Result", "Owned"),
            ("ModeWriteIssued", anyModeWriteIssued), ("PrimaryHiddenTarget", target),
            ("HiddenTargetCount", _ownedHiddenTargets.Count));
        return new(MsiClawPhysicalOwnershipOutcome.Owned, "PhysicalOwnershipVerified", anyModeWriteIssued, _ownedHiddenTargets);
    }

    private async Task<GamepadModeNormalizationResult> EnsureDirectInputGamepadModeAsync(
        MsiClawPhysicalIdentity pid1902Identity,
        string context,
        CancellationToken cancellationToken)
    {
        if (_gamepadModeClient is not { } gamepadModeClient)
        {
            AppLog.Warn("ControllerOwnership", "GamepadMode client is unavailable.", null,
                ("Event", "GamepadModeInvariantObserved"), ("Context", context),
                ("Succeeded", false), ("Reason", "GamepadModeClientUnavailable"));
            return new(false, false, null, "GamepadModeClientUnavailable");
        }

        var observed = await gamepadModeClient.QueryAsync(pid1902Identity, cancellationToken).ConfigureAwait(false);
        AppLog.Info("ControllerOwnership", "GamepadMode invariant observed.",
            ("Event", "GamepadModeInvariantObserved"), ("Context", context),
            ("Succeeded", observed.Succeeded), ("ObservedMode", observed.Mode),
            ("Reason", observed.Reason));
        if (observed.Succeeded && observed.Mode == MsiClawGamepadMode.DirectInput)
            return new(true, false, observed.Mode, "GamepadModeDirectInputVerified");

        if (_captureCenterMStartupState() != FrontendCenterMStartupState.Disabled)
        {
            return new(false, false, observed.Mode,
                "AuthorityChangedBeforeGamepadModeNormalization");
        }

        AppLog.Info("ControllerOwnership", "GamepadMode normalization started.",
            ("Event", "GamepadModeNormalizationStarted"), ("Context", context),
            ("TargetMode", MsiClawGamepadMode.DirectInput), ("ObservedMode", observed.Mode),
            ("QuerySucceeded", observed.Succeeded));
        var normalized = await gamepadModeClient.SwitchAndVerifyAsync(
            pid1902Identity, MsiClawGamepadMode.DirectInput, cancellationToken).ConfigureAwait(false);
        var verified = normalized.Succeeded
            && normalized.WriteIssued
            && normalized.ReadbackVerified
            && normalized.Mode == MsiClawGamepadMode.DirectInput;
        var reason = verified
            ? "GamepadModeDirectInputVerified"
            : "GamepadModeDirectInputNotVerified:" + normalized.Reason;
        AppLog.Info("ControllerOwnership", "GamepadMode normalization verification completed.",
            ("Event", "GamepadModeNormalizationVerified"), ("Context", context),
            ("Succeeded", verified), ("ObservedMode", normalized.Mode),
            ("WriteIssued", normalized.WriteIssued),
            ("ReadbackVerified", normalized.ReadbackVerified), ("Reason", reason));
        return new(verified, normalized.WriteIssued, normalized.Mode, reason);
    }

    public Task<PhysicalOwnershipReleaseResult> ReleaseForCenterMEnableAsync(CancellationToken cancellationToken) =>
        ReleaseToXInputAsync(cancellationToken, "CenterMEnable");

    private async Task<PhysicalOwnershipReleaseResult> ReleaseToXInputAsync(
        CancellationToken cancellationToken,
        string releaseReason)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Prefer the exact target set verified/attempted by this process; otherwise recover the
            // already-proven persisted exact set (previous-boot target + auxiliary collections).
            var targets = _ownedHiddenTargets.Count != 0
                ? _ownedHiddenTargets
                : _captureExistingOwnedHiddenTargets();
            _releasedForEnable = true;
            AppLog.Info("ControllerOwnership", "Physical release to XInput started.",
                ("Event", "PhysicalOwnershipReleaseStarted"),
                ("Reason", releaseReason));
            if (_ownsInputSource)
            {
                await _inputSource.StopAsync().ConfigureAwait(false);
                if (_inputSource.IsRunning)
                    return new(false, "DirectInputStillRunning", targets);
                _ownsInputSource = false;
                ClearLivePhysicalSession();
            }

            // Restore the same strongly-verified physical MSI Claw to PID1901 for the normal
            // Center M Enable-and-Restart stock-authority path.
            var current = await _captureStableNativeState(cancellationToken).ConfigureAwait(false);
            if (!TryReadIdentity(current, out var mode, out var identity, out var reason))
                return new(false, "ReleaseNativeState:" + reason, targets);
            if (mode is not (MsiClawNativeMode.XInput or MsiClawNativeMode.DirectInput))
                return new(false, "UnsupportedReleaseMode:" + mode, targets);
            if (mode == MsiClawNativeMode.DirectInput)
            {
                // PR11 section 9: the inverse PID1902 -> PID1901 transition is proven by the Addon's
                // own controlled-transition evidence and a fresh XInput/PID1901 capture. The Windows
                // physical-root string legitimately changes across the mode switch, so PID1902 and
                // PID1901 identities are NOT compared with StronglyMatches here.
                var switched = await _switchMode(MsiClawNativeMode.XInput, identity, cancellationToken).ConfigureAwait(false);
                if (!IsCrossModeTransitionProven(switched, out var switchFailure))
                    return new(false, "Pid1901RestoreFailed:" + switchFailure, targets);
                var verified = await _captureStableNativeState(cancellationToken).ConfigureAwait(false);
                // PR11 section 10: split verification failures -- do not concatenate an unrelated
                // "Ok" (TryReadIdentity succeeded) when a later predicate is what actually failed.
                if (!TryReadIdentity(verified, out var finalMode, out _, out var verifyReason))
                    return new(false, "Pid1901RestoreUnverified:" + verifyReason, targets);
                if (finalMode != MsiClawNativeMode.XInput)
                    return new(false, "Pid1901RestoreFinalModeNotXInput:" + finalMode, targets);
            }

            AppLog.Info("ControllerOwnership", "Physical ownership released to XInput.",
                ("Event", "PhysicalOwnershipReleased"),
                ("Reason", releaseReason), ("PrimaryHiddenTarget", _ownedPrimaryHiddenTarget ?? "None"),
                ("HiddenTargetCount", targets.Count), ("HiddenTargets", string.Join(";", targets)));
            return new(true, "Released", targets);
        }
        finally { _gate.Release(); }
    }

    public async Task<MsiClawPhysicalOwnershipResult> RecoverLostInputAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return RecoveryFail("OwnerDisposed");
            if (_releasedForEnable)
            {
                AppLog.Info("ControllerOwnership", "Owned physical input recovery skipped because controller authority was already released for Center M enable.",
                    ("Event", "OwnedPhysicalRecoverySkipped"), ("Reason", "ReleasedForCenterMEnable"), ("ModeWriteIssued", false));
                return new(MsiClawPhysicalOwnershipOutcome.Failed, "ReleasedForCenterMEnable", false, _ownedHiddenTargets);
            }
            return await RecoverLostInputCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<string?> CheckDeveloperRumbleRearmAdmissionAsync()
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return "OwnerDisposed";
            if (_releasedForEnable) return "ReleasedForCenterMEnable";
            if (!_ownsInputSource || !_inputSource.IsRunning) return "OwnedDirectInputSourceUnavailable";
            if (_captureCenterMStartupState() != FrontendCenterMStartupState.Disabled) return "CenterMAuthorityNotDisabled";
            if (_ownedPhysicalIdentity is not { Confidence: MsiClawIdentityConfidence.Strong } ownedIdentity
                || ownedIdentity.VendorId != MsiClawHardware.VendorId
                || ownedIdentity.ProductId != MsiClawHardware.DirectInputProductId
                || _ownedPrimaryHiddenTarget is not { } ownedTarget
                || !MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(ownedTarget)
                || !_ownedHiddenTargets.Contains(ownedTarget, StringComparer.OrdinalIgnoreCase))
                return "OwnedPhysicalIdentityOrExactTargetUnavailable";

            var liveSession = CurrentIdentity;
            if (liveSession is null || string.IsNullOrWhiteSpace(liveSession.PhysicalIdentity)
                || !string.Equals(liveSession.PnpInstanceId, ownedTarget, StringComparison.OrdinalIgnoreCase))
                return "LivePhysicalSessionDoesNotMatchOwnedTarget";

            var capture = await _captureStableNativeState(CancellationToken.None).ConfigureAwait(false);
            if (!TryReadIdentity(capture, out var mode, out var identity, out var reason)
                || mode != MsiClawNativeMode.DirectInput
                || identity.ProductId != MsiClawHardware.DirectInputProductId
                || !ownedIdentity.StronglyMatches(identity))
                return "InitialPid1902StateNotVerified:" + reason;

            var targetDevice = _resolvePnpDevice(ownedTarget);
            if (targetDevice is null || !targetDevice.Present
                || !MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(targetDevice.InstanceId)
                || !ownedIdentity.StronglyMatches(MsiClawPhysicalIdentity.From(targetDevice)))
                return "ExactPrimaryPid1902TargetNotPresent";

            var control = new MsiClawControlHidResolver().Resolve(_enumeratePnpDevices(), MsiClawNativeMode.DirectInput, identity);
            return control is null ? "CurrentControlHidUnavailable" : null;
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerOwnership", "Developer rumble re-arm admission check failed.", exception,
                ("Event", "DeveloperRumbleRearmAdmissionUnavailable"));
            return "AdmissionCheckFailed:" + exception.GetType().Name;
        }
        finally { _gate.Release(); }
    }

    public async Task<DeveloperRumbleRearmPhysicalResult> RunDeveloperRumbleRearmAsync(Func<bool> mayBeginModeMutation)
    {
        ArgumentNullException.ThrowIfNull(mayBeginModeMutation);
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        var modeWriteIssued = false;
        var xInputTransitionVerified = false;
        var directInputTransitionVerified = false;
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
                return RearmUnavailable("OwnerDisposed", IsHealthyOwnedInput());
            if (_releasedForEnable)
                return RearmUnavailable("ReleasedForCenterMEnable", false);
            if (!_ownsInputSource || !_inputSource.IsRunning)
                return RearmUnavailable("OwnedDirectInputSourceUnavailable", false);
            if (_ownedPhysicalIdentity is not { Confidence: MsiClawIdentityConfidence.Strong } ownedIdentity
                || ownedIdentity.VendorId != MsiClawHardware.VendorId
                || ownedIdentity.ProductId != MsiClawHardware.DirectInputProductId
                || _ownedPrimaryHiddenTarget is not { } ownedTarget
                || !MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(ownedTarget)
                || !_ownedHiddenTargets.Contains(ownedTarget, StringComparer.OrdinalIgnoreCase))
                return RearmUnavailable("OwnedPhysicalIdentityOrExactTargetUnavailable", false);

            var liveSession = CurrentIdentity;
            if (liveSession is null
                || string.IsNullOrWhiteSpace(liveSession.PhysicalIdentity)
                || !string.Equals(liveSession.PnpInstanceId, ownedTarget, StringComparison.OrdinalIgnoreCase))
                return RearmUnavailable("LivePhysicalSessionDoesNotMatchOwnedTarget", false);

            if (_captureCenterMStartupState() != FrontendCenterMStartupState.Disabled)
                return RearmUnavailable("CenterMAuthorityNotDisabled", IsHealthyOwnedInput());

            var initialCapture = await _captureStableNativeState(CancellationToken.None).ConfigureAwait(false);
            if (!TryReadIdentity(initialCapture, out var initialMode, out var initialIdentity, out var captureReason)
                || initialMode != MsiClawNativeMode.DirectInput
                || initialIdentity.ProductId != MsiClawHardware.DirectInputProductId
                || !ownedIdentity.StronglyMatches(initialIdentity))
                return RearmUnavailable("InitialPid1902StateNotVerified:" + captureReason, IsHealthyOwnedInput());

            var targetDevice = _resolvePnpDevice(ownedTarget);
            if (targetDevice is null
                || !targetDevice.Present
                || !MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(targetDevice.InstanceId)
                || !ownedIdentity.StronglyMatches(MsiClawPhysicalIdentity.From(targetDevice)))
                return RearmUnavailable("ExactPrimaryPid1902TargetNotPresent", IsHealthyOwnedInput());

            if (new MsiClawControlHidResolver().Resolve(
                    _enumeratePnpDevices(), MsiClawNativeMode.DirectInput, initialIdentity) is null)
                return RearmUnavailable("CurrentControlHidUnavailable", IsHealthyOwnedInput());

            AppLog.Info("ControllerOwnership", "Developer rumble re-arm physical sequence started.",
                ("Event", "DeveloperRumbleRearmPhysicalStarted"), ("Mode", initialMode),
                ("PhysicalSessionGeneration", CurrentSessionGeneration), ("PrimaryHiddenTarget", ownedTarget));

            // Reserve this single operation with the Runtime before stopping the input source.
            // Suspend/shutdown/Overlay/authority admission that wins before this point prevents the
            // attempt from interrupting the live PID1902 session. A failed cleanup still issues no
            // mode write; once the reservation succeeds, bounded lifecycle work continues under the
            // Runtime owner even if the frontend disconnects.
            if (!mayBeginModeMutation())
                return RearmUnavailable("AdmissionChangedBeforeFirstModeWrite", IsHealthyOwnedInput());

            // Clear the live rumble identity before stopping DirectInput. The presentation owner has
            // already stopped/joined publication, drained callbacks, sent STOP, and invalidated the
            // retained rumble HID handle.
            _ownsInputSource = false;
            ClearLivePhysicalSession();
            bool cleanupProven;
            try
            {
                cleanupProven = await _inputSource.StopAndConfirmCleanupAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                AppLog.Warn("ControllerOwnership", "Developer rumble re-arm DirectInput stop threw.", exception,
                    ("Event", "DeveloperRumbleRearmDirectInputStopFailed"));
                return new(DeveloperRumbleRearmPhysicalOutcome.Failed, "DirectInputCleanupUnproven", false, false, false, false);
            }
            if (!cleanupProven || _inputSource.IsRunning)
            {
                AppLog.Warn("ControllerOwnership", "Developer rumble re-arm DirectInput cleanup was not proven.", null,
                    ("Event", "DeveloperRumbleRearmDirectInputStopFailed"),
                    ("CleanupProven", cleanupProven), ("SourceStillRunning", _inputSource.IsRunning), ("ModeWriteIssued", false));
                return new(DeveloperRumbleRearmPhysicalOutcome.Failed, "DirectInputCleanupUnproven", false, false, false, false);
            }
            AppLog.Info("ControllerOwnership", "Developer rumble re-arm DirectInput source stopped and cleaned.",
                ("Event", "DeveloperRumbleRearmDirectInputStopped"), ("CleanupProven", true), ("ModeWriteIssued", false));

            // The authority is re-read immediately before the first native command. The Runtime
            // reservation prevents its own authority transition from entering concurrently.
            if (_captureCenterMStartupState() != FrontendCenterMStartupState.Disabled)
                return new(DeveloperRumbleRearmPhysicalOutcome.Unavailable, "AdmissionChangedBeforeFirstModeWrite", false, false, false, false);

            modeWriteIssued = true;
            var toXInput = await _switchMode(MsiClawNativeMode.XInput, ownedIdentity, CancellationToken.None).ConfigureAwait(false);
            if (!IsCrossModeTransitionProven(toXInput, out var xInputTransitionFailure))
            {
                var observed = await CaptureObservedModeForRearmAsync("XInputTransitionFailed").ConfigureAwait(false);
                AppLog.Warn("ControllerOwnership", "Developer rumble re-arm XInput transition was not verified.", null,
                    ("Event", "DeveloperRumbleRearmXInputTransitionResult"), ("Verified", false),
                    ("Reason", xInputTransitionFailure), ("ObservedMode", observed));
                return new(DeveloperRumbleRearmPhysicalOutcome.Failed, "XInputTransitionFailed:" + xInputTransitionFailure, false, false, false, true);
            }
            AppLog.Info("ControllerOwnership", "Developer rumble re-arm XInput PID transition verified.",
                ("Event", "DeveloperRumbleRearmXInputTransitionResult"), ("Verified", true),
                ("OldPidDisappeared", toXInput.OldPidDisappeared), ("TargetPidAppeared", toXInput.TargetPidAppeared),
                ("SourceIdentityVerified", toXInput.SourceIdentityVerified), ("TargetTopologyVerified", toXInput.TargetTopologyVerified));

            var xInputCapture = await _captureStableNativeState(CancellationToken.None).ConfigureAwait(false);
            if (!TryReadIdentity(xInputCapture, out var xInputMode, out var xInputIdentity, out var xInputReason)
                || xInputMode != MsiClawNativeMode.XInput
                || xInputIdentity.ProductId != MsiClawHardware.XInputProductId)
            {
                var observed = await CaptureObservedModeForRearmAsync("XInputCaptureFailed").ConfigureAwait(false);
                return new(DeveloperRumbleRearmPhysicalOutcome.Failed,
                    "FreshPid1901StateNotVerified:" + xInputReason + ";Observed=" + observed, false, false, false, true);
            }
            xInputTransitionVerified = true;

            var toDirectInput = await _switchMode(MsiClawNativeMode.DirectInput, xInputIdentity, CancellationToken.None).ConfigureAwait(false);
            if (!IsCrossModeTransitionProven(toDirectInput, out var directInputTransitionFailure))
            {
                var observed = await CaptureObservedModeForRearmAsync("DirectInputTransitionFailed").ConfigureAwait(false);
                AppLog.Warn("ControllerOwnership", "Developer rumble re-arm DirectInput transition was not verified.", null,
                    ("Event", "DeveloperRumbleRearmDirectInputTransitionResult"), ("Verified", false),
                    ("Reason", directInputTransitionFailure), ("ObservedMode", observed));
                return new(DeveloperRumbleRearmPhysicalOutcome.Failed, "DirectInputTransitionFailed:" + directInputTransitionFailure, true, false, false, true);
            }
            AppLog.Info("ControllerOwnership", "Developer rumble re-arm DirectInput PID transition verified.",
                ("Event", "DeveloperRumbleRearmDirectInputTransitionResult"), ("Verified", true),
                ("OldPidDisappeared", toDirectInput.OldPidDisappeared), ("TargetPidAppeared", toDirectInput.TargetPidAppeared),
                ("SourceIdentityVerified", toDirectInput.SourceIdentityVerified), ("TargetTopologyVerified", toDirectInput.TargetTopologyVerified));

            var finalCapture = await _captureStableNativeState(CancellationToken.None).ConfigureAwait(false);
            if (!TryReadIdentity(finalCapture, out var finalMode, out var finalIdentity, out var finalReason)
                || finalMode != MsiClawNativeMode.DirectInput
                || finalIdentity.ProductId != MsiClawHardware.DirectInputProductId)
            {
                var observed = await CaptureObservedModeForRearmAsync("FinalPid1902CaptureFailed").ConfigureAwait(false);
                return new(DeveloperRumbleRearmPhysicalOutcome.Failed,
                    "FreshFinalPid1902StateNotVerified:" + finalReason + ";Observed=" + observed, true, false, false, true);
            }
            directInputTransitionVerified = true;

            // The two verified native transitions establish cross-mode continuity. Adopt only the
            // fresh final PID1902 identity, then let the normal stopped-session recovery core prove
            // the original exact HidHide target, DirectInput input, and ownership publication.
            _ownedPhysicalIdentity = finalIdentity;
            var recovered = await RecoverLostInputCoreAsync(CancellationToken.None, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            if (!recovered.IsOwned || !_inputSource.IsRunning)
            {
                AppLog.Warn("ControllerOwnership", "Developer rumble re-arm physical ownership recovery failed.", null,
                    ("Event", "DeveloperRumbleRearmPhysicalRecovered"), ("Verified", false), ("Reason", recovered.Reason),
                    ("PhysicalMotorEffectVerified", false));
                return new(DeveloperRumbleRearmPhysicalOutcome.Failed, "PhysicalRecoveryFailed:" + recovered.Reason,
                    true, true, false, true);
            }

            AppLog.Info("ControllerOwnership", "Developer rumble re-arm physical ownership restored.",
                ("Event", "DeveloperRumbleRearmPhysicalRecovered"), ("Verified", true),
                ("PrimaryHiddenTarget", recovered.PrimaryHiddenTarget ?? "None"),
                ("PhysicalSessionGeneration", CurrentSessionGeneration), ("PhysicalMotorEffectVerified", false));
            return new(DeveloperRumbleRearmPhysicalOutcome.Completed, "PhysicalOwnershipRestored", true, true, true, true);
        }
        catch (Exception exception)
        {
            AppLog.Error("ControllerOwnership", "Developer rumble re-arm physical operation failed unexpectedly.", exception,
                ("Event", "DeveloperRumbleRearmFailed"), ("ModeWriteIssued", modeWriteIssued),
                ("PhysicalMotorEffectVerified", false));
            var observed = modeWriteIssued
                ? await CaptureObservedModeForRearmAsync("PhysicalOperationException").ConfigureAwait(false)
                : "NotObserved";
            return new(DeveloperRumbleRearmPhysicalOutcome.Failed,
                "PhysicalOperationThrew:" + exception.GetType().Name + ";Observed=" + observed,
                xInputTransitionVerified, directInputTransitionVerified, IsHealthyOwnedInput(), modeWriteIssued);
        }
        finally { _gate.Release(); }
    }

    private DeveloperRumbleRearmPhysicalResult RearmUnavailable(string reason, bool physicalOwnershipRestored) =>
        new(DeveloperRumbleRearmPhysicalOutcome.Unavailable, reason, false, false, physicalOwnershipRestored, false);

    private bool IsHealthyOwnedInput() =>
        _ownsInputSource
        && _inputSource.IsRunning
        && _ownedPhysicalIdentity is { Confidence: MsiClawIdentityConfidence.Strong }
        && _ownedPrimaryHiddenTarget is { } target
        && MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(target)
        && CurrentIdentity is { } current
        && string.Equals(current.PnpInstanceId, target, StringComparison.OrdinalIgnoreCase);

    private async Task<string> CaptureObservedModeForRearmAsync(string stage)
    {
        try
        {
            var capture = await _captureStableNativeState(CancellationToken.None).ConfigureAwait(false);
            if (TryReadIdentity(capture, out var mode, out var identity, out _))
            {
                AppLog.Info("ControllerOwnership", "Developer rumble re-arm observed actual native state after transition failure.",
                    ("Event", "DeveloperRumbleRearmObservedNativeState"), ("Stage", stage),
                    ("Mode", mode), ("ProductId", identity.ProductId));
                return $"{mode}/0x{identity.ProductId:X4}";
            }
            return capture.Status + ":" + capture.Reason;
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerOwnership", "Developer rumble re-arm could not capture the observed native mode.", exception,
                ("Event", "DeveloperRumbleRearmObservedNativeState"), ("Stage", stage));
            return "Unavailable:" + exception.GetType().Name;
        }
    }

    private async Task<MsiClawPhysicalOwnershipResult> RecoverLostInputCoreAsync(
        CancellationToken cancellationToken,
        TimeSpan? firstValidStateTimeout = null)
    {
        // 10.2 / PR10. "Ownership was committed" is proven by the retained strong physical identity +
        //       exact hidden target that a successful acquisition/recovery stored (and never cleared),
        //       NOT by a currently-live session. This lets recovery re-enter after an earlier
        //       DeviceNotFound failure -- e.g. when a real Windows Device Arrival fires minutes later.
        //       The released-for-Center-M-enable gate is already enforced by RecoverLostInputAsync.
        if (_ownedPhysicalIdentity is not { Confidence: MsiClawIdentityConfidence.Strong } ownedIdentity
            || _ownedPrimaryHiddenTarget is not { } ownedTarget
            || !MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(ownedTarget))
            return RecoveryFail("OwnerNotCommitted");

        // A still-running source needs no recovery.
        if (_inputSource.IsRunning)
            return new(MsiClawPhysicalOwnershipOutcome.Owned, "RecoveryNotNeeded", false, _ownedHiddenTargets);

        AppLog.Info("ControllerOwnership", "Owned physical input recovery started.",
            ("Event", "OwnedPhysicalRecoveryStarted"), ("PrimaryHiddenTarget", ownedTarget),
            ("HiddenTargetCount", _ownedHiddenTargets.Count));

        // The dead owned session is accepted for recovery. Do NOT clear the owned identity/target --
        // the official Enable-and-Restart release must still be able to clear exactly this entry.
        _ownsInputSource = false;
        // The live rumble physical session is gone until recovery commits a fresh verified descriptor.
        ClearLivePhysicalSession();

        // 10.3. Reuse the same bounded native/PnP settle capture PR5 was given.
        var capture = await _captureStableNativeState(cancellationToken).ConfigureAwait(false);
        if (!TryReadIdentity(capture, out var mode, out var identity, out var reason))
            return RecoveryFail("PhysicalDeviceMissing:" + reason);

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
        var pidTransitionWriteIssued = false;
        MsiClawPhysicalIdentity finalPid1902Identity;
        if (mode == MsiClawNativeMode.DirectInput)
        {
            if (!ownedIdentity.StronglyMatches(identity))
                return RecoveryFail("PhysicalIdentityMismatch");
            finalPid1902Identity = identity;
        }
        else if (mode == MsiClawNativeMode.XInput)
        {
            AppLog.Warn("ControllerOwnership", "Owned physical-state drift detected.", null,
                ("Event", "OwnedPhysicalStateDriftDetected"), ("CurrentMode", "PID1901"), ("CurrentIdentityConfidence", identity.Confidence));

            // 12. Fresh Center M startup-root authority read immediately before the reclaim write.
            if (_captureCenterMStartupState() != FrontendCenterMStartupState.Disabled)
                return RecoveryFail("AuthorityNotDisabled");

            AppLog.Info("ControllerOwnership", "Owned physical PID1902 reclaim started.",
                ("Event", "OwnedPhysicalPid1902ReclaimStarted"), ("PrimaryHiddenTarget", ownedTarget));

            // 7. Exactly one PID1901 -> PID1902 transition per recovery invocation, expecting the
            //    fresh current PID1901 identity as the source. No retry loop, no PID1901 fallback.
            var transition = await _switchMode(MsiClawNativeMode.DirectInput, identity, cancellationToken).ConfigureAwait(false);
            pidTransitionWriteIssued = true;
            if (!IsCrossModeTransitionProven(transition, out var transitionFailure))
                return RecoveryFail("OwnedPhysicalStateDriftReclaimFailed:" + transitionFailure, true);

            // 8. Mandatory bounded post-reclaim verification: fresh final PID1902 / Strong. The
            //    cross-mode root string is proven by the controlled transition + the live DirectInput
            //    proof below, not by StronglyMatches against the pre-write identities.
            var reclaimed = await _captureStableNativeState(cancellationToken).ConfigureAwait(false);
            if (!TryReadIdentity(reclaimed, out var reclaimedMode, out var reclaimedIdentity, out var reclaimReason))
                return RecoveryFail("OwnedPhysicalStateDriftReclaimUnverified:" + reclaimReason, true);
            if (reclaimedMode != MsiClawNativeMode.DirectInput)
                return RecoveryFail("OwnedPhysicalStateDriftReclaimFinalModeNotPid1902:" + reclaimedMode, true);
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
            return RecoveryFail("PhysicalDeviceMissing:UnsupportedMode:" + mode);
        }
        var gamepadMode = await EnsureDirectInputGamepadModeAsync(
            finalPid1902Identity, "Recovery", cancellationToken).ConfigureAwait(false);
        if (!gamepadMode.Succeeded)
            return RecoveryFail(gamepadMode.Reason, pidTransitionWriteIssued || gamepadMode.WriteIssued);
        var anyModeWriteIssued = pidTransitionWriteIssued || gamepadMode.WriteIssued;
        AppLog.Info("ControllerOwnership", "Owned physical recovery native state proven.",
            ("Event", "OwnedPhysicalRecoveryNativeProven"), ("CurrentNativeMode", MsiClawNativeMode.DirectInput),
            ("ModeWriteIssued", anyModeWriteIssued), ("CrossModeTransitionVerified", pidTransitionWriteIssued));

        // 10.5. Re-resolve the DirectInput descriptor through the same bounded selector path.
        var descriptor = await ResolveDirectInputDescriptorAsync(cancellationToken).ConfigureAwait(false);
        if (descriptor is null)
            return RecoveryFail("DirectInputNotResolved", anyModeWriteIssued);
        if (!MsiClawHardware.IsPrimaryDirectInputHidCollectionInstanceId(descriptor.PnpInstanceId))
            return RecoveryFail("DirectInputNotResolved:NotPrimaryCollection", anyModeWriteIssued);
        var pnpDevice = _resolvePnpDevice(descriptor.PnpInstanceId!);
        if (pnpDevice is null)
            return RecoveryFail("DirectInputNotResolved:PnpNodeMissing", anyModeWriteIssued);
        var directInputIdentity = MsiClawPhysicalIdentity.From(pnpDevice);
        // Same-mode PID1902 <-> PID1902 comparison: the fresh final PID1902 native identity must match
        // the resolved PID1902 DirectInput PnP collection (PR11 sections 4, 8.2).
        if (directInputIdentity.Confidence != MsiClawIdentityConfidence.Strong || !finalPid1902Identity.StronglyMatches(directInputIdentity))
            return RecoveryFail("DirectInputPhysicalIdentityMismatch", anyModeWriteIssued);

        // 10.6. PR8 only reacquires the exact same persistent hidden target. A changed exact PnP
        //       collection is HidHide target migration -- explicitly a later PR.
        var recoveredTarget = descriptor.PnpInstanceId!;
        if (!string.Equals(recoveredTarget, ownedTarget, StringComparison.OrdinalIgnoreCase))
            return RecoveryFail("RecoveredTargetChanged", anyModeWriteIssued);
        AppLog.Info("ControllerOwnership", "Owned physical recovery DirectInput candidate resolved.",
            ("Event", "OwnedPhysicalRecoveryDirectInputResolved"), ("RecoveredTarget", recoveredTarget));

        // 10.8. A fresh shared Center M authority read immediately before the first recovery mutation.
        //       The bounded settle capture above may have taken time.
        if (_captureCenterMStartupState() != FrontendCenterMStartupState.Disabled)
            return RecoveryFail("AuthorityNotDisabled", anyModeWriteIssued);

        // 10.7. Verify/repair the persistent HidHide baseline for the current exact target set BEFORE
        //       restarting DirectInput -- a virtual presentation is already attached to this source,
        //       so physical isolation must be proven before non-neutral input can resume.
        var previousTargets = _ownedHiddenTargets;
        MsiClawHidHideTargetResolution recoveredResolution;
        try
        {
            recoveredResolution = MsiClawHardware.ResolveOwnedPid1902HidHideTargets(pnpDevice, _enumeratePnpDevices());
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerOwnership", "Owned physical recovery target-set resolution threw.", exception);
            return RecoveryFail("HidHideTargetSetResolutionThrew", anyModeWriteIssued);
        }
        if (recoveredResolution.Targets.Count == 0
            || !string.Equals(recoveredResolution.Targets[0], ownedTarget, StringComparison.OrdinalIgnoreCase))
            return RecoveryFail("HidHideTargetSetMissingPrimary", anyModeWriteIssued);
        foreach (var diagnostic in recoveredResolution.Diagnostics)
            AppLog.Warn("ControllerOwnership", "An auxiliary PID1902 HidHide target was omitted during recovery.", null,
                ("Event", "AuxiliaryHidHideTargetOmitted"), ("Diagnostic", diagnostic));
        // Retain both the previously committed and newly attempted exact entries until the
        // reconciliation succeeds, so a partial mutation can still be released explicitly.
        _ownedHiddenTargets = previousTargets.Concat(recoveredResolution.Targets)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        AddonHidHideBaselineResult baseline;
        try
        {
            baseline = _applyHidHideTargets(recoveredResolution.Targets);
        }
        catch (Exception exception)
        {
            AppLog.Warn("ControllerOwnership", "Owned physical recovery HidHide reconciliation threw.", exception);
            return RecoveryFail("HidHideReconcileFailed:Threw", anyModeWriteIssued);
        }
        if (!baseline.IsCompliant)
            return RecoveryFail("HidHideReconcileFailed:" + baseline.Outcome + ":" + baseline.Reason, anyModeWriteIssued);
        _ownedHiddenTargets = recoveredResolution.Targets;
        AppLog.Info("ControllerOwnership", "Owned physical recovery isolation verified.",
            ("Event", "OwnedPhysicalRecoveryIsolationVerified"), ("PrimaryHiddenTarget", ownedTarget),
            ("HiddenTargetCount", _ownedHiddenTargets.Count), ("HiddenTargets", string.Join(";", _ownedHiddenTargets)),
            ("HidHideOutcome", baseline.Outcome));

        // 10.9. Restart the SAME input source object. Never construct a replacement.
        var start = _inputSource.StartPrepared(descriptor);
        if (!start.Started || !_inputSource.IsRunning)
        {
            await SafeStopAsync().ConfigureAwait(false);
            return RecoveryFail("DirectInputStartFailed:" + start.Status, anyModeWriteIssued);
        }
        bool ready;
        try
        {
            var firstState = _inputSource.WaitForFirstValidStateAsync(cancellationToken);
            ready = firstValidStateTimeout is { } timeout
                ? await firstState.WaitAsync(timeout, cancellationToken).ConfigureAwait(false)
                : await firstState.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await SafeStopAsync().ConfigureAwait(false);
            throw;
        }
        catch (TimeoutException)
        {
            await SafeStopAsync().ConfigureAwait(false);
            return RecoveryFail("FirstValidStateTimedOut", anyModeWriteIssued);
        }
        if (!ready || !_inputSource.IsRunning)
        {
            await SafeStopAsync().ConfigureAwait(false);
            return RecoveryFail("FirstValidStateNotObserved", anyModeWriteIssued);
        }

        // 10.10 / PR11 section 8.4. Commit. The primary hidden target is unchanged; the existing
        //        publisher is already reading this same source and resumes receiving live snapshots.
        //        (After a PID1901->PID1902 reclaim, _ownedPhysicalIdentity was already refreshed to
        //        the fresh Strong PID1902 identity right after the post-reclaim capture verified.)
        _ownsInputSource = true;
        PublishLivePhysicalSession(descriptor);
        var successReason = pidTransitionWriteIssued ? "OwnedPhysicalStateDriftReclaimed" : "OwnedPhysicalInputRecovered";
        AppLog.Info("ControllerOwnership", "Owned physical input recovery succeeded.",
            ("Event", "OwnedPhysicalRecoverySucceeded"), ("Reason", successReason),
            ("PrimaryHiddenTarget", ownedTarget), ("HiddenTargetCount", _ownedHiddenTargets.Count),
            ("ModeWriteIssued", anyModeWriteIssued), ("DirectInputStartStatus", start.Status));
        return new(MsiClawPhysicalOwnershipOutcome.Owned, successReason, anyModeWriteIssued, _ownedHiddenTargets);
    }

    private MsiClawPhysicalOwnershipResult RecoveryFail(string reason, bool modeWriteIssued = false)
    {
        AppLog.Warn("ControllerOwnership", "Owned physical input recovery failed.", null,
            ("Event", "OwnedPhysicalRecoveryFailed"), ("Reason", reason), ("ModeWriteIssued", modeWriteIssued));
        return new(MsiClawPhysicalOwnershipOutcome.Failed, reason, modeWriteIssued, _ownedHiddenTargets);
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
    private async Task<A2vmBootPid1901Resolution> ResolveBootRumblePid1901EndpointAsync(
        MsiClawModeTransitionResult transition,
        bool allowLateTargetSettle,
        CancellationToken cancellationToken)
    {
        var startedAt = _getTimestamp();
        var budget = _a2vmBootPid1901SettleWindow;
        var sinceWriteAtStart = transition.SinceCommandWriteMs ?? transition.TotalMs;

        if (allowLateTargetSettle)
        {
            AppLog.Info("ControllerOwnership", "A2VM boot rumble re-arm is continuing the verified first-leg PID1901 settle without repeating its mode command.",
                ("Event", "A2vmBootRumblePid1901SettleStarted"),
                ("Model", _hardwareDeviceModel?.Value),
                ("InitialTransitionResult", transition.Status),
                ("WriteSucceeded", transition.WriteSucceeded),
                ("OldPidDisappeared", transition.OldPidDisappeared),
                ("SinceCommandWriteMs", sinceWriteAtStart),
                ("AdditionalBudgetMs", (long)budget.TotalMilliseconds));
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_captureCenterMStartupState() != FrontendCenterMStartupState.Disabled)
                return new(false, null, "CenterMAuthorityChangedDuringPid1901Settle", false);

            var probe = ObserveFreshBootPid1901Endpoint();
            var elapsedMs = GetElapsedMilliseconds(startedAt, _getTimestamp());
            if (probe.Status == A2vmBootPid1901ProbeStatus.Ready && probe.Identity is { } identity)
            {
                if (_captureCenterMStartupState() != FrontendCenterMStartupState.Disabled)
                    return new(false, null, "CenterMAuthorityChangedDuringPid1901Settle", false);

                AppLog.Info("ControllerOwnership", "A2VM boot rumble re-arm verified one fresh strong PID1901 command endpoint and disappearance of PID1902.",
                    ("Event", "A2vmBootRumblePid1901Verified"),
                    ("Model", _hardwareDeviceModel?.Value),
                    ("VerificationPath", allowLateTargetSettle ? "BoundedTargetSettle" : "StrictTransitionFreshProbe"),
                    ("IdentityConfidence", identity.Confidence),
                    ("LogicalTargetCount", 1),
                    ("CommandEndpointCount", 1),
                    ("OldPid1902Present", false),
                    ("SinceCommandWriteMs", sinceWriteAtStart + elapsedMs),
                    ("AdditionalSettleElapsedMs", elapsedMs));
                return new(true, identity, "Ok", false);
            }

            if (probe.Status is A2vmBootPid1901ProbeStatus.AmbiguousLogicalTarget
                or A2vmBootPid1901ProbeStatus.AmbiguousCommandEndpoint
                or A2vmBootPid1901ProbeStatus.StrongIdentityUnavailable)
            {
                AppLog.Warn("ControllerOwnership", "A2VM boot rumble re-arm could not prove a unique strong PID1901 command endpoint.", null,
                    ("Event", "A2vmBootRumblePid1901SettleFailed"),
                    ("Model", _hardwareDeviceModel?.Value),
                    ("ProbeResult", probe.Status),
                    ("SinceCommandWriteMs", sinceWriteAtStart + elapsedMs));
                return new(false, null, probe.Status.ToString(), false);
            }

            if (probe.Status == A2vmBootPid1901ProbeStatus.ReadFailed)
            {
                if (probe.ReadException is { } readException)
                {
                    var nativeErrorCode = readException.InnerException is System.ComponentModel.Win32Exception innerWin32Exception
                        ? innerWin32Exception.NativeErrorCode
                        : (int?)null;
                    AppLog.Debug("ControllerOwnership", "A2vmBootRumblePid1901TargetProbeFailed",
                        ("Stage", "TargetScopedPid1901Observation"),
                        ("ExceptionType", readException.GetType().Name),
                        ("HResult", readException.HResult),
                        ("NativeErrorCode", nativeErrorCode));
                }

                AppLog.Warn("ControllerOwnership", "A2VM boot rumble re-arm could not classify a target-scoped PnP read failure as transient; no second native write was issued.", null,
                    ("Event", "A2vmBootRumblePid1901SettleFailed"),
                    ("Model", _hardwareDeviceModel?.Value),
                    ("ProbeResult", probe.Status),
                    ("SinceCommandWriteMs", sinceWriteAtStart + elapsedMs),
                    ("CanDeferInitialAcquisition", false));
                return new(false, null, probe.Status.ToString(), false);
            }

            if (probe.Status == A2vmBootPid1901ProbeStatus.OldPidStillPresent)
            {
                AppLog.Warn("ControllerOwnership", "A2VM boot rumble re-arm observed PID1902 still present; no second native write was issued.", null,
                    ("Event", "A2vmBootRumblePid1901SettleFailed"),
                    ("Model", _hardwareDeviceModel?.Value),
                    ("ProbeResult", probe.Status),
                    ("SinceCommandWriteMs", sinceWriteAtStart + elapsedMs),
                    ("CanDeferInitialAcquisition", false));
                return new(false, null, probe.Status.ToString(), false);
            }

            if (!allowLateTargetSettle)
            {
                AppLog.Warn("ControllerOwnership", "A2VM boot rumble re-arm could not freshly resolve the PID1901 command endpoint after the strict transition proof.", null,
                    ("Event", "A2vmBootRumblePid1901SettleFailed"),
                    ("Model", _hardwareDeviceModel?.Value),
                    ("ProbeResult", probe.Status),
                    ("SinceCommandWriteMs", sinceWriteAtStart + elapsedMs),
                    ("CanDeferInitialAcquisition", false));
                return new(false, null, probe.Status.ToString(), false);
            }

            if (elapsedMs >= budget.TotalMilliseconds)
            {
                var canDefer = allowLateTargetSettle
                    && (probe.Status is A2vmBootPid1901ProbeStatus.TargetPidNotPresent
                        or A2vmBootPid1901ProbeStatus.CommandEndpointNotPresent);
                AppLog.Info("ControllerOwnership", "A2VM boot rumble PID1901 settle ended without a fresh command endpoint; no follow-up native write was issued.",
                    ("Event", "A2vmBootRumblePid1901SettleFailed"),
                    ("Model", _hardwareDeviceModel?.Value),
                    ("ProbeResult", probe.Status),
                    ("SinceCommandWriteMs", sinceWriteAtStart + elapsedMs),
                    ("AdditionalSettleElapsedMs", elapsedMs),
                    ("CanDeferInitialAcquisition", canDefer));
                return new(false, null, probe.Status.ToString(), canDefer);
            }

            var remaining = budget - TimeSpan.FromMilliseconds(elapsedMs);
            var delay = remaining < _a2vmBootPid1901SettleInterval ? remaining : _a2vmBootPid1901SettleInterval;
            if (delay > TimeSpan.Zero)
                await _delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private A2vmBootPid1901ProbeResult ObserveFreshBootPid1901Endpoint()
    {
        try
        {
            if (_isPnpDevicePresent(MsiClawHardware.VendorId, MsiClawHardware.DirectInputProductId))
                return new(A2vmBootPid1901ProbeStatus.OldPidStillPresent);

            var targetDevices = _enumeratePnpDevicesByVidPid(MsiClawHardware.VendorId, MsiClawHardware.XInputProductId)
                .Where(device => device.Present
                    && device.VendorId == MsiClawHardware.VendorId
                    && device.ProductId == MsiClawHardware.XInputProductId)
                .ToArray();
            if (targetDevices.Length == 0)
                return new(A2vmBootPid1901ProbeStatus.TargetPidNotPresent);

            var logicalTargets = targetDevices
                .GroupBy(MsiClawLogicalIdentity.GetLogicalKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (logicalTargets.Length != 1)
                return new(A2vmBootPid1901ProbeStatus.AmbiguousLogicalTarget);

            var commandEndpoints = targetDevices
                .Where(device => device.UsagePage == 0xFFA0 && device.Usage == 0x0001)
                .ToArray();
            if (commandEndpoints.Length == 0)
                return new(A2vmBootPid1901ProbeStatus.CommandEndpointNotPresent);
            if (commandEndpoints.Length != 1)
                return new(A2vmBootPid1901ProbeStatus.AmbiguousCommandEndpoint);

            var identity = MsiClawPhysicalIdentity.From(commandEndpoints[0]);
            if (identity.Confidence != MsiClawIdentityConfidence.Strong)
                return new(A2vmBootPid1901ProbeStatus.StrongIdentityUnavailable);

            return new(A2vmBootPid1901ProbeStatus.Ready, identity);
        }
        catch (InvalidOperationException exception)
        {
            return new(A2vmBootPid1901ProbeStatus.ReadFailed, ReadException: exception);
        }
    }

    private static long GetElapsedMilliseconds(long startedAt, long currentTimestamp) =>
        Math.Max(0, (long)((currentTimestamp - startedAt) * 1000d / Stopwatch.Frequency));

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

    internal static bool IsTargetNotPresentAfterVerifiedWrite(
        MsiClawModeTransitionResult transition,
        MsiClawNativeMode expectedSource,
        MsiClawNativeMode expectedTarget) =>
        transition.Status == MsiClawModeTransitionStatus.TargetDeviceDidNotAppear
        && transition.FromMode == expectedSource
        && transition.TargetMode == expectedTarget
        && transition.WriteSucceeded
        && transition.OldPidDisappeared
        && !transition.TargetPidPresent
        && !transition.TargetPidAppeared
        && transition.SourceIdentityVerified
        && !transition.TargetTopologyVerified;

    internal static bool IsA2vmBootRumblePrimeModel(HandheldDeviceModelId? modelId) =>
        modelId?.Value is "msi.claw.a2vm.7" or "msi.claw.a2vm.8";

    internal static bool SupportsManualRumbleRearm(HandheldDeviceModelId modelId) =>
        modelId.Value is "msi.claw.a2vm.7" or "msi.claw.a2vm.8" or "msi.claw.cg3em";

    internal static bool TryReadIdentity(NativeStateCaptureResult capture, out MsiClawNativeMode mode, out MsiClawPhysicalIdentity identity, out string reason)
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

    private MsiClawPhysicalOwnershipResult Fail(
        string reason,
        bool modeWriteIssued,
        MsiClawInitialAcquisitionRetryReason retryReason = MsiClawInitialAcquisitionRetryReason.None,
        bool nativeDeviceAbsentAtInitialCapture = false)
    {
        AppLog.Warn("ControllerOwnership", "Physical ownership failed.", null,
            ("Result", "Failed"), ("Reason", reason), ("ModeWriteIssued", modeWriteIssued),
            ("InitialAcquisitionRetryReason", retryReason));
        return new(MsiClawPhysicalOwnershipOutcome.Failed, reason, modeWriteIssued, _ownedHiddenTargets,
            retryReason, nativeDeviceAbsentAtInitialCapture);
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
                ClearLivePhysicalSession();
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
