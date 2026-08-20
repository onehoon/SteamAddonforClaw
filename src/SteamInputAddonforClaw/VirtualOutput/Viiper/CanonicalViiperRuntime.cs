using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

/// <summary>
/// State of the process/runtime-lifetime persistent VIIPER owner. Kept intentionally small (see
/// docs/VIIPER_IMPLEMENTATION_RULES.md "overengineering guard"): it only needs to answer real
/// native ownership questions -- can this owner safely accept route-time attachment, does native
/// cleanup remain pending, has ownership become unsafe/unknown, is final teardown complete.
/// </summary>
internal enum CanonicalViiperRuntimeState
{
    Ready,
    CleanupPending,
    Unsafe,
    Closed
}

/// <summary>Which native resource cleanup is currently retrying, when a retryable failure leaves
/// cleanup incomplete -- either during staged initialization unwind (see
/// <see cref="CanonicalViiperRuntime.TryInitialize"/>) or final teardown (see
/// <see cref="CanonicalViiperRuntime.TeardownAsync"/>).</summary>
internal enum CanonicalViiperRuntimeTeardownPhase
{
    None,
    DeckDetach,
    Xbox360Detach,
    DeckRemove,
    Xbox360Remove,
    BusRemoval,
    ServerClose
}

/// <summary>
/// The one process/runtime-lifetime canonical VIIPER owner (PR2): one <see cref="ICanonicalViiperNativeApi"/>,
/// one USB server, one caller-owned bus, one persistent Steam Deck logical device, and one
/// persistent Xbox360 logical device -- both created once, detached-ready
/// (<c>autoAttachLocalhost: false</c>). Owns only native VIIPER lifecycle: it does NOT own Steam
/// policy, routing eligibility, physical PID1901/PID1902 switching, HidHide, PnP policy, the
/// controller input publisher, feedback authority, Game Bar policy, or OEM1 -- those remain
/// exactly where they are today (see docs/VIIPER_INTEGRATION.md).
///
/// <see cref="USBDeviceAttachResult.UnsafeOutcomeUnknown"/> and <see cref="USBDeviceAttachResult.Invalid"/>
/// classifications from the native ABI (and their Detach/Remove equivalents) are both a hard
/// fail-closed boundary: once <see cref="State"/> becomes <see cref="CanonicalViiperRuntimeState.Unsafe"/>,
/// no further native attach/detach/remove/bus/server call is ever made against this owner again --
/// evidence is preserved instead of guessing at recovery. Native ownership (server/bus/device
/// handles) is never discarded on a failed or incomplete initialization: any staged failure keeps
/// the SAME owner object alive (in <see cref="CanonicalViiperRuntimeState.CleanupPending"/> or
/// <see cref="CanonicalViiperRuntimeState.Unsafe"/>) so cleanup can be retried; <c>null</c> is only
/// returned once cleanup is proven to have reached nothing-owned (or nothing was ever acquired).
/// </summary>
internal sealed class CanonicalViiperRuntime
{
    private readonly ICanonicalViiperNativeApi _native;
    private readonly SemaphoreSlim _serial = new(1, 1);

    private bool _busCreated;
    private bool _deckCreated;
    private bool _xbox360Created;

    private CanonicalViiperRuntime(ICanonicalViiperNativeApi native, nuint serverHandle)
    {
        _native = native;
        ServerHandle = serverHandle;
        // Not Ready yet: the owner exists the moment a native server handle is acquired, but
        // nothing downstream is confirmed safe until TryInitialize reaches the end. Starting in
        // CleanupPending (rather than losing this object on a later staged failure) is what makes
        // the fail-closed contract possible -- see the class doc comment.
        State = CanonicalViiperRuntimeState.CleanupPending;
    }

    internal CanonicalViiperRuntimeState State { get; private set; }
    internal CanonicalViiperRuntimeTeardownPhase TeardownPhase { get; private set; }
    internal nuint ServerHandle { get; private set; }
    internal uint BusId { get; private set; }
    internal nuint DeckDeviceHandle { get; private set; }
    internal uint DeckLogicalDeviceId { get; private set; }
    internal nuint Xbox360DeviceHandle { get; private set; }
    internal uint Xbox360LogicalDeviceId { get; private set; }

    /// <summary>
    /// Staged initialization (work order section 2/26): NewUSBServer -&gt; CreateUSBBus -&gt;
    /// CreateSteamDeckDevice(autoAttach=false) -&gt; GetUSBDeviceIdentity -&gt;
    /// GetUSBDeviceAttachmentState(Deck)==Detached -&gt; CreateXbox360Device(autoAttach=false) -&gt;
    /// GetUSBDeviceIdentity -&gt; GetUSBDeviceAttachmentState(X360)==Detached. Any staged failure
    /// unwinds only the known-safe resources already acquired, in reverse order, and stops the
    /// moment a native result is not known-safe (no destructive cleanup past that point). Callers
    /// must fail routing closed rather than fall back to a second/legacy ownership path (work
    /// order section 27) whenever the returned owner's <see cref="State"/> is not
    /// <see cref="CanonicalViiperRuntimeState.Ready"/>.
    ///
    /// Returns <c>null</c> only when nothing was ever acquired, or when a staged failure's unwind
    /// is confirmed to have released everything back to nothing owned. Any other staged failure
    /// returns the SAME owner object (in <see cref="CanonicalViiperRuntimeState.CleanupPending"/>
    /// or <see cref="CanonicalViiperRuntimeState.Unsafe"/>, holding the real handles) so native
    /// ownership is never silently discarded -- <see cref="TeardownAsync"/> can resume cleanup
    /// later for the CleanupPending case.
    /// </summary>
    internal static CanonicalViiperRuntime? TryInitialize(ICanonicalViiperNativeApi native, string loopbackAddress)
    {
        ArgumentNullException.ThrowIfNull(native);

        var address = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8(loopbackAddress);
        nuint serverHandle;
        try
        {
            var config = new USBServerConfig
            {
                Addr = address,
                ConnectionTimeoutMs = 30_000,
                DeviceHandlerConnectTimeoutMs = 5_000,
                WriteBatchFlushIntervalMs = 1
            };
            if (!native.NewUSBServer(ref config, out serverHandle, CanonicalViiperDiagnosticLog.Callback))
            {
                LogInitFailure("NewUSBServerFailed");
                return null; // nothing acquired -- proven clean, nothing to discard.
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeCoTaskMem(address);
        }

        // Construct the owner as soon as the server handle exists: from this point on, any staged
        // failure below threads THIS object through cleanup rather than discarding native
        // ownership into locals that get GC'd.
        var runtime = new CanonicalViiperRuntime(native, serverHandle);

        var busId = 0u;
        if (!native.CreateUSBBus(serverHandle, ref busId))
        {
            LogInitFailure("CreateUSBBusFailed");
            return FinishUnwind(runtime);
        }
        runtime.BusId = busId;
        runtime._busCreated = true;

        // Zero VID/PID -> VIIPER's own canonical Steam Deck default identity (28DE:1205), matching
        // the existing route-session convention.
        if (!native.CreateSteamDeckDevice(serverHandle, out var deckHandle, busId, autoAttachLocalhost: false, idVendor: 0, idProduct: 0))
        {
            LogInitFailure("CreateSteamDeckDeviceFailed");
            return FinishUnwind(runtime);
        }
        runtime.DeckDeviceHandle = deckHandle;
        runtime._deckCreated = true;

        if (!native.GetUSBDeviceIdentity(deckHandle, out var deckIdentityBusId, out var deckLogicalDeviceId) || deckIdentityBusId != busId)
        {
            LogInitFailure("SteamDeckIdentityFailed");
            return UnwindDeckThenBusServer(runtime);
        }
        runtime.DeckLogicalDeviceId = deckLogicalDeviceId;

        if (!native.GetUSBDeviceAttachmentState(deckHandle, out var deckAttachmentState))
        {
            // Query failure: no evidence either way about live device state -- do not guess or
            // attempt any destructive call. Fail closed exactly like OutcomeUnknown.
            LogInitFailure("SteamDeckAttachmentStateQueryFailed");
            runtime.MarkUnsafe("SteamDeckAttachmentStateQueryFailed");
            return runtime;
        }
        switch (deckAttachmentState)
        {
            case USBDeviceAttachmentState.Detached:
                break; // known-safe: continue initialization below.
            case USBDeviceAttachmentState.Attached:
                // Known Attached: neutral-write, then classified-detach, before removal continues.
                LogInitFailure("SteamDeckInitialAttachmentStateNotDetached");
                if (!runtime.TryDetachDeckIfAttached(deckAttachmentState)) return runtime;
                return UnwindDeckThenBusServer(runtime);
            case USBDeviceAttachmentState.OutcomeUnknown:
            default:
                // Hard fail-closed boundary: OutcomeUnknown -- and any unrecognized/out-of-range
                // classification -- must never be followed by any destructive call (no remove, no
                // detach, no bus/server teardown). An unrecognized value is never treated as
                // safely detached.
                LogInitFailure($"SteamDeckInitialAttachmentState{(int)deckAttachmentState}");
                runtime.MarkUnsafe($"SteamDeckInitialAttachmentState{(int)deckAttachmentState}");
                return runtime;
        }

        if (!native.CreateXbox360Device(serverHandle, out var xbox360Handle, busId, autoAttachLocalhost: false,
                idVendor: 0, idProduct: 0, xinputSubType: 0))
        {
            LogInitFailure("CreateXbox360DeviceFailed");
            return UnwindDeckThenBusServer(runtime);
        }
        runtime.Xbox360DeviceHandle = xbox360Handle;
        runtime._xbox360Created = true;

        if (!native.GetUSBDeviceIdentity(xbox360Handle, out var xbox360IdentityBusId, out var xbox360LogicalDeviceId) || xbox360IdentityBusId != busId)
        {
            LogInitFailure("Xbox360IdentityFailed");
            return UnwindXbox360ThenDeckThenBusServer(runtime);
        }
        runtime.Xbox360LogicalDeviceId = xbox360LogicalDeviceId;

        if (!native.GetUSBDeviceAttachmentState(xbox360Handle, out var xbox360AttachmentState))
        {
            LogInitFailure("Xbox360AttachmentStateQueryFailed");
            runtime.MarkUnsafe("Xbox360AttachmentStateQueryFailed");
            return runtime;
        }
        switch (xbox360AttachmentState)
        {
            case USBDeviceAttachmentState.Detached:
                break;
            case USBDeviceAttachmentState.Attached:
                LogInitFailure("Xbox360InitialAttachmentStateNotDetached");
                if (!runtime.TryDetachXbox360IfAttached(xbox360AttachmentState)) return runtime;
                return UnwindXbox360ThenDeckThenBusServer(runtime);
            case USBDeviceAttachmentState.OutcomeUnknown:
            default:
                LogInitFailure($"Xbox360InitialAttachmentState{(int)xbox360AttachmentState}");
                runtime.MarkUnsafe($"Xbox360InitialAttachmentState{(int)xbox360AttachmentState}");
                return runtime;
        }

        runtime.State = CanonicalViiperRuntimeState.Ready;
        AppLog.Debug("SteamOutput", "Persistent canonical VIIPER runtime ready.",
            ("ServerHandleOwned", true), ("BusId", busId), ("DeckLogicalDeviceId", deckLogicalDeviceId), ("Xbox360LogicalDeviceId", xbox360LogicalDeviceId));
        return runtime;
    }

    // ---- Staged-init unwind orchestration (reverse of acquisition order) ----

    private static CanonicalViiperRuntime? UnwindXbox360ThenDeckThenBusServer(CanonicalViiperRuntime runtime)
    {
        if (runtime._xbox360Created && !runtime.TryRemoveXbox360()) return runtime;
        return UnwindDeckThenBusServer(runtime);
    }

    private static CanonicalViiperRuntime? UnwindDeckThenBusServer(CanonicalViiperRuntime runtime)
    {
        if (runtime._deckCreated && !runtime.TryRemoveDeck()) return runtime;
        return FinishUnwind(runtime);
    }

    private static CanonicalViiperRuntime? FinishUnwind(CanonicalViiperRuntime runtime)
    {
        if (runtime.State == CanonicalViiperRuntimeState.Unsafe) return runtime;
        if (!runtime.TryRemoveBusAndServer()) return runtime;
        runtime.TeardownPhase = CanonicalViiperRuntimeTeardownPhase.None;
        runtime.State = CanonicalViiperRuntimeState.Closed;
        return null; // fully unwound -- confirmed nothing owned.
    }

    private static void LogInitFailure(string reason) =>
        AppLog.Error("SteamOutput", "Persistent canonical VIIPER runtime initialization failed.",
            new InvalidOperationException("CanonicalViiperRuntime.TryInitialize staged failure."), ("Reason", reason));

    /// <summary>Marks the persistent runtime unsafe -- a hard fail-closed boundary. No further
    /// native attach/detach/remove/bus/server call is issued by this owner again.</summary>
    internal void MarkUnsafe(string reason)
    {
        if (State == CanonicalViiperRuntimeState.Unsafe || State == CanonicalViiperRuntimeState.Closed) return;
        State = CanonicalViiperRuntimeState.Unsafe;
        TeardownPhase = CanonicalViiperRuntimeTeardownPhase.None;
        AppLog.Error("SteamOutput", "Persistent canonical VIIPER runtime marked unsafe; no further native mutation will be attempted.",
            new InvalidOperationException("CanonicalViiperRuntime fail-closed boundary."), ("Reason", reason));
    }

    /// <summary>
    /// Final Runtime teardown (work order sections 19-21): only called after routing shutdown has
    /// completed. Detaches whichever logical device is (or might be) still attached, then removes
    /// both logical devices, then the bus, then closes the server -- stopping immediately at any
    /// classified result that is not <c>Success</c>. Idempotent and resumable: a retryable failure
    /// leaves <see cref="State"/> at <see cref="CanonicalViiperRuntimeState.CleanupPending"/> and
    /// <see cref="TeardownPhase"/> pointing at the step to retry; calling this again resumes from
    /// there -- this also resumes any staged-initialization unwind left CleanupPending by
    /// <see cref="TryInitialize"/>. Never called again once <see cref="State"/> is
    /// <see cref="CanonicalViiperRuntimeState.Unsafe"/> or already <see cref="CanonicalViiperRuntimeState.Closed"/>.
    /// </summary>
    internal async Task<bool> TeardownAsync()
    {
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State == CanonicalViiperRuntimeState.Closed) return true;
            if (State == CanonicalViiperRuntimeState.Unsafe) return false;

            if (TeardownPhase == CanonicalViiperRuntimeTeardownPhase.None)
                TeardownPhase = CanonicalViiperRuntimeTeardownPhase.DeckDetach;
            State = CanonicalViiperRuntimeState.CleanupPending;

            if (TeardownPhase == CanonicalViiperRuntimeTeardownPhase.DeckDetach)
            {
                if (_deckCreated)
                {
                    if (!TryGetAttachmentState(DeckDeviceHandle, out var deckState)) return false;
                    switch (deckState)
                    {
                        case USBDeviceAttachmentState.Detached:
                            break;
                        case USBDeviceAttachmentState.Attached:
                            if (!TryDetachDeckIfAttached(deckState)) return false;
                            break;
                        case USBDeviceAttachmentState.OutcomeUnknown:
                        default:
                            // Fail closed: an unrecognized/out-of-range classification is never
                            // treated as safely detached.
                            MarkUnsafe($"TeardownDeckAttachmentState{(int)deckState}");
                            return false;
                    }
                }
                TeardownPhase = CanonicalViiperRuntimeTeardownPhase.Xbox360Detach;
            }

            if (TeardownPhase == CanonicalViiperRuntimeTeardownPhase.Xbox360Detach)
            {
                if (_xbox360Created)
                {
                    if (!TryGetAttachmentState(Xbox360DeviceHandle, out var xboxState)) return false;
                    switch (xboxState)
                    {
                        case USBDeviceAttachmentState.Detached:
                            break;
                        case USBDeviceAttachmentState.Attached:
                            if (!TryDetachXbox360IfAttached(xboxState)) return false;
                            break;
                        case USBDeviceAttachmentState.OutcomeUnknown:
                        default:
                            MarkUnsafe($"TeardownXbox360AttachmentState{(int)xboxState}");
                            return false;
                    }
                }
                TeardownPhase = CanonicalViiperRuntimeTeardownPhase.DeckRemove;
            }

            if (TeardownPhase == CanonicalViiperRuntimeTeardownPhase.DeckRemove)
            {
                if (_deckCreated && !TryRemoveDeck()) return false;
                TeardownPhase = CanonicalViiperRuntimeTeardownPhase.Xbox360Remove;
            }

            if (TeardownPhase == CanonicalViiperRuntimeTeardownPhase.Xbox360Remove)
            {
                if (_xbox360Created && !TryRemoveXbox360()) return false;

                // Reachable directly (without going through DeckRemove first) when resuming an
                // init-time unwind that failed at the Xbox360 stage: the Deck was created before
                // Xbox360 in that ordering, so it can still be owned here. Remove it before
                // advancing to the bus rather than letting bus removal silently take it with it.
                if (_deckCreated)
                {
                    TeardownPhase = CanonicalViiperRuntimeTeardownPhase.DeckRemove;
                    if (!TryRemoveDeck()) return false;
                }

                TeardownPhase = CanonicalViiperRuntimeTeardownPhase.BusRemoval;
            }

            if (TeardownPhase is CanonicalViiperRuntimeTeardownPhase.BusRemoval or CanonicalViiperRuntimeTeardownPhase.ServerClose)
            {
                if (!TryRemoveBusAndServer()) return false;
                TeardownPhase = CanonicalViiperRuntimeTeardownPhase.None;
                State = CanonicalViiperRuntimeState.Closed;
                AppLog.Debug("SteamOutput", "Persistent canonical VIIPER runtime final teardown complete.");
                return true;
            }

            return false;
        }
        finally
        {
            _serial.Release();
        }
    }

    // ---- Shared classified-mutation helpers (used by both staged-init unwind and final teardown) ----

    /// <summary>Neutral-writes and classified-detaches the Deck handle when <paramref name="observedState"/>
    /// is <see cref="USBDeviceAttachmentState.Attached"/>; a no-op returning <c>true</c> for any other
    /// (already-safe) observed state. A rejected neutral write is NOT treated as clean -- it stops
    /// here (CleanupPending, retry later) rather than proceeding to detach with possibly-live input
    /// state still latched on the device.</summary>
    private bool TryDetachDeckIfAttached(USBDeviceAttachmentState observedState)
    {
        if (observedState != USBDeviceAttachmentState.Attached) return true;

        if (!_native.SetSteamDeckDeviceState(DeckDeviceHandle, default))
        {
            TeardownPhase = CanonicalViiperRuntimeTeardownPhase.DeckDetach;
            return false;
        }

        var result = _native.DetachUSBDeviceEx(DeckDeviceHandle);
        if (result == USBDeviceDetachResult.Success) return true;
        if (result == USBDeviceDetachResult.RetryableFailure)
        {
            TeardownPhase = CanonicalViiperRuntimeTeardownPhase.DeckDetach;
            return false;
        }
        MarkUnsafe($"DeckDetach{result}");
        return false;
    }

    private bool TryDetachXbox360IfAttached(USBDeviceAttachmentState observedState)
    {
        if (observedState != USBDeviceAttachmentState.Attached) return true;

        if (!_native.SetXbox360DeviceState(Xbox360DeviceHandle, default))
        {
            TeardownPhase = CanonicalViiperRuntimeTeardownPhase.Xbox360Detach;
            return false;
        }

        var result = _native.DetachUSBDeviceEx(Xbox360DeviceHandle);
        if (result == USBDeviceDetachResult.Success) return true;
        if (result == USBDeviceDetachResult.RetryableFailure)
        {
            TeardownPhase = CanonicalViiperRuntimeTeardownPhase.Xbox360Detach;
            return false;
        }
        MarkUnsafe($"Xbox360Detach{result}");
        return false;
    }

    private bool TryRemoveDeck()
    {
        var result = _native.RemoveSteamDeckDeviceEx(DeckDeviceHandle);
        if (result == SteamDeckDeviceRemoveResult.Success)
        {
            // Retire the capability at the exact successful boundary -- the fork API requires
            // callers to stop using a typed handle after successful removal, and a stale non-zero
            // handle alongside _deckCreated == false would be two representations of ownership.
            _deckCreated = false;
            DeckDeviceHandle = 0;
            DeckLogicalDeviceId = 0;
            return true;
        }
        if (result == SteamDeckDeviceRemoveResult.RetryableFailure)
        {
            TeardownPhase = CanonicalViiperRuntimeTeardownPhase.DeckRemove;
            return false;
        }
        // UnsafeOutcomeUnknown or Invalid: both fail closed identically -- no retry, mark unsafe.
        MarkUnsafe($"DeckRemove{result}");
        return false;
    }

    private bool TryRemoveXbox360()
    {
        var result = _native.RemoveXbox360DeviceEx(Xbox360DeviceHandle);
        if (result == Xbox360DeviceRemoveResult.Success)
        {
            _xbox360Created = false;
            Xbox360DeviceHandle = 0;
            Xbox360LogicalDeviceId = 0;
            return true;
        }
        if (result == Xbox360DeviceRemoveResult.RetryableFailure)
        {
            TeardownPhase = CanonicalViiperRuntimeTeardownPhase.Xbox360Remove;
            return false;
        }
        MarkUnsafe($"Xbox360Remove{result}");
        return false;
    }

    private bool TryRemoveBusAndServer()
    {
        if (_busCreated)
        {
            if (!_native.RemoveUSBBus(ServerHandle, BusId))
            {
                TeardownPhase = CanonicalViiperRuntimeTeardownPhase.BusRemoval;
                return false;
            }
            _busCreated = false;
            BusId = 0;
        }
        if (!_native.CloseUSBServer(ServerHandle))
        {
            TeardownPhase = CanonicalViiperRuntimeTeardownPhase.ServerClose;
            return false;
        }
        ServerHandle = 0;
        return true;
    }

    private bool TryGetAttachmentState(nuint deviceHandle, out USBDeviceAttachmentState state)
    {
        if (_native.GetUSBDeviceAttachmentState(deviceHandle, out state)) return true;
        // Query failure during teardown: no evidence either way -- do not guess. Stay
        // CleanupPending (not Unsafe: this is a query failure, not a classified mutation result) so
        // an explicit retry can be attempted again later.
        return false;
    }
}
