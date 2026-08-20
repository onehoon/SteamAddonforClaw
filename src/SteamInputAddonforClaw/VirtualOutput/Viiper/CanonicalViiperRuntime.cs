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
    Uninitialized,
    Ready,
    CleanupPending,
    Unsafe,
    Closed
}

/// <summary>Which native resource final teardown is currently retrying, when a retryable failure
/// leaves teardown incomplete (see <see cref="CanonicalViiperRuntime.TeardownAsync"/>).</summary>
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
/// <see cref="UnsafeOutcomeUnknown"/> classifications from the native ABI are a hard fail-closed
/// boundary: once <see cref="State"/> becomes <see cref="CanonicalViiperRuntimeState.Unsafe"/>, no
/// further native attach/detach/remove/bus/server call is ever made against this owner again --
/// evidence is preserved instead of guessing at recovery.
/// </summary>
internal sealed class CanonicalViiperRuntime
{
    // Xbox360 canonical identity: the standard wired Xbox 360 controller VID/PID (already used by
    // the existing managed ABI test preparation in CanonicalViiperNativeAbiTests), with XInput
    // subtype 1 (gamepad) -- the header's own documented default. Unlike the Steam Deck path
    // (which passes zero VID/PID to let VIIPER apply its own canonical default), the generated
    // header does not document an equivalent zero-VID/PID default for CreateXbox360Device, so an
    // explicit, already-established identity is used rather than inventing one.
    private const ushort Xbox360VendorId = 0x045E;
    private const ushort Xbox360ProductId = 0x028E;
    private const byte Xbox360XInputSubType = 1;

    private readonly ICanonicalViiperNativeApi _native;
    private readonly SemaphoreSlim _serial = new(1, 1);

    private CanonicalViiperRuntime(ICanonicalViiperNativeApi native, nuint serverHandle, uint busId,
        nuint deckHandle, uint deckLogicalDeviceId, nuint xbox360Handle, uint xbox360LogicalDeviceId)
    {
        _native = native;
        ServerHandle = serverHandle;
        BusId = busId;
        DeckDeviceHandle = deckHandle;
        DeckLogicalDeviceId = deckLogicalDeviceId;
        Xbox360DeviceHandle = xbox360Handle;
        Xbox360LogicalDeviceId = xbox360LogicalDeviceId;
        State = CanonicalViiperRuntimeState.Ready;
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
    /// moment a native result is not known-safe (no destructive cleanup past that point). Returns
    /// null on any failure -- callers must fail routing closed rather than fall back to a
    /// second/legacy ownership path (work order section 27).
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
                return null;
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeCoTaskMem(address);
        }

        var busId = 0u;
        if (!native.CreateUSBBus(serverHandle, ref busId))
        {
            LogInitFailure("CreateUSBBusFailed");
            SafeCloseServer(native, serverHandle);
            return null;
        }

        // Zero VID/PID -> VIIPER's own canonical Steam Deck default identity (28DE:1205), matching
        // the existing route-session convention.
        if (!native.CreateSteamDeckDevice(serverHandle, out var deckHandle, busId, autoAttachLocalhost: false, idVendor: 0, idProduct: 0))
        {
            LogInitFailure("CreateSteamDeckDeviceFailed");
            SafeRemoveBusAndServer(native, serverHandle, busId);
            return null;
        }

        if (!native.GetUSBDeviceIdentity(deckHandle, out var deckIdentityBusId, out var deckLogicalDeviceId) || deckIdentityBusId != busId)
        {
            LogInitFailure("SteamDeckIdentityFailed");
            if (!SafeRemoveDeckDevice(native, deckHandle)) return null;
            SafeRemoveBusAndServer(native, serverHandle, busId);
            return null;
        }

        if (!native.GetUSBDeviceAttachmentState(deckHandle, out var deckAttachmentState) || deckAttachmentState != USBDeviceAttachmentState.Detached)
        {
            LogInitFailure("SteamDeckInitialAttachmentStateNotDetached");
            if (!SafeRemoveDeckDevice(native, deckHandle)) return null;
            SafeRemoveBusAndServer(native, serverHandle, busId);
            return null;
        }

        if (!native.CreateXbox360Device(serverHandle, out var xbox360Handle, busId, autoAttachLocalhost: false,
                Xbox360VendorId, Xbox360ProductId, Xbox360XInputSubType))
        {
            LogInitFailure("CreateXbox360DeviceFailed");
            if (!SafeRemoveDeckDevice(native, deckHandle)) return null;
            SafeRemoveBusAndServer(native, serverHandle, busId);
            return null;
        }

        if (!native.GetUSBDeviceIdentity(xbox360Handle, out var xbox360IdentityBusId, out var xbox360LogicalDeviceId) || xbox360IdentityBusId != busId)
        {
            LogInitFailure("Xbox360IdentityFailed");
            if (!SafeRemoveXbox360Device(native, xbox360Handle)) return null;
            if (!SafeRemoveDeckDevice(native, deckHandle)) return null;
            SafeRemoveBusAndServer(native, serverHandle, busId);
            return null;
        }

        if (!native.GetUSBDeviceAttachmentState(xbox360Handle, out var xbox360AttachmentState) || xbox360AttachmentState != USBDeviceAttachmentState.Detached)
        {
            LogInitFailure("Xbox360InitialAttachmentStateNotDetached");
            if (!SafeRemoveXbox360Device(native, xbox360Handle)) return null;
            if (!SafeRemoveDeckDevice(native, deckHandle)) return null;
            SafeRemoveBusAndServer(native, serverHandle, busId);
            return null;
        }

        AppLog.Debug("SteamOutput", "Persistent canonical VIIPER runtime ready.",
            ("ServerHandleOwned", true), ("BusId", busId), ("DeckLogicalDeviceId", deckLogicalDeviceId), ("Xbox360LogicalDeviceId", xbox360LogicalDeviceId));
        return new CanonicalViiperRuntime(native, serverHandle, busId, deckHandle, deckLogicalDeviceId, xbox360Handle, xbox360LogicalDeviceId);
    }

    // Best-known-safe reverse cleanup helper for the Deck device during staged init failure. Only
    // ever reached BEFORE the runtime object exists, so there is no "mark runtime unsafe" state to
    // set -- an unsafe/unknown/invalid removal result here simply means TryInitialize must return
    // null and leak the evidence (matching the no-blind-recovery rule); the process never had a
    // Ready runtime to begin with.
    private static bool SafeRemoveDeckDevice(ICanonicalViiperNativeApi native, nuint deckHandle)
    {
        var result = native.RemoveSteamDeckDeviceEx(deckHandle);
        if (result == SteamDeckDeviceRemoveResult.Success) return true;
        AppLog.Error("SteamOutput", "Persistent VIIPER runtime init failed and the unattached Steam Deck logical device could not be safely removed.",
            new InvalidOperationException("RemoveSteamDeckDeviceEx classified failure during init unwind."), ("Result", result));
        return false;
    }

    private static bool SafeRemoveXbox360Device(ICanonicalViiperNativeApi native, nuint xbox360Handle)
    {
        var result = native.RemoveXbox360DeviceEx(xbox360Handle);
        if (result == Xbox360DeviceRemoveResult.Success) return true;
        AppLog.Error("SteamOutput", "Persistent VIIPER runtime init failed and the unattached Xbox360 logical device could not be safely removed.",
            new InvalidOperationException("RemoveXbox360DeviceEx classified failure during init unwind."), ("Result", result));
        return false;
    }

    private static void SafeRemoveBusAndServer(ICanonicalViiperNativeApi native, nuint serverHandle, uint busId)
    {
        if (!native.RemoveUSBBus(serverHandle, busId))
        {
            AppLog.Error("SteamOutput", "Persistent VIIPER runtime init failed and the bus it opened could not be removed.",
                new InvalidOperationException("RemoveUSBBus failed during init unwind."));
            return;
        }
        SafeCloseServer(native, serverHandle);
    }

    private static void SafeCloseServer(ICanonicalViiperNativeApi native, nuint serverHandle)
    {
        if (!native.CloseUSBServer(serverHandle))
            AppLog.Error("SteamOutput", "Persistent VIIPER runtime init failed and the server it opened could not be closed.",
                new InvalidOperationException("CloseUSBServer failed during init unwind."));
    }

    private static void LogInitFailure(string reason) =>
        AppLog.Error("SteamOutput", "Persistent canonical VIIPER runtime initialization failed.",
            new InvalidOperationException("CanonicalViiperRuntime.TryInitialize staged failure."), ("Reason", reason));

    /// <summary>Route entry: classified attach of the persistent Deck handle (work order section 7).
    /// Never called when <see cref="State"/> is not <see cref="CanonicalViiperRuntimeState.Ready"/>.</summary>
    internal USBDeviceAttachResult AttachDeck()
    {
        if (State != CanonicalViiperRuntimeState.Ready) return USBDeviceAttachResult.Invalid;
        var result = _native.AttachUSBDeviceEx(DeckDeviceHandle);
        if (result == USBDeviceAttachResult.UnsafeOutcomeUnknown) MarkUnsafe("DeckAttachUnsafeOutcomeUnknown");
        return result;
    }

    /// <summary>Route exit: classified detach of the persistent Deck handle (work order section 10).</summary>
    internal USBDeviceDetachResult DetachDeck()
    {
        if (State != CanonicalViiperRuntimeState.Ready) return USBDeviceDetachResult.Invalid;
        var result = _native.DetachUSBDeviceEx(DeckDeviceHandle);
        if (result == USBDeviceDetachResult.UnsafeOutcomeUnknown) MarkUnsafe("DeckDetachUnsafeOutcomeUnknown");
        return result;
    }

    internal bool TryGetDeckAttachmentState(out USBDeviceAttachmentState state)
    {
        if (State != CanonicalViiperRuntimeState.Ready) { state = default; return false; }
        return _native.GetUSBDeviceAttachmentState(DeckDeviceHandle, out state);
    }

    internal bool SetDeckState(SteamDeckDeviceState value) =>
        State == CanonicalViiperRuntimeState.Ready && _native.SetSteamDeckDeviceState(DeckDeviceHandle, value);

    internal bool SetDeckOutputCallback(SteamDeckOutputCallback? callback) =>
        State == CanonicalViiperRuntimeState.Ready && _native.SetSteamDeckOutputCallback(DeckDeviceHandle, callback);

    /// <summary>Marks the persistent runtime unsafe -- a hard fail-closed boundary. No further
    /// native attach/detach/remove/bus/server call is issued by this owner again. Called both from
    /// this owner's own classified calls above and, per work order section 7, when a
    /// route-time attach borrowing this handle observes <see cref="USBDeviceAttachResult.UnsafeOutcomeUnknown"/>
    /// or <see cref="USBDeviceAttachResult.Invalid"/> (and the detach equivalents) -- an uncertain
    /// or invalid native result about THIS handle poisons the one authority that owns it, not just
    /// the calling route.</summary>
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
    /// there. Never called again once <see cref="State"/> is <see cref="CanonicalViiperRuntimeState.Unsafe"/>
    /// or already <see cref="CanonicalViiperRuntimeState.Closed"/>.
    /// </summary>
    internal async Task<bool> TeardownAsync()
    {
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State == CanonicalViiperRuntimeState.Closed) return true;
            if (State == CanonicalViiperRuntimeState.Unsafe) return false;
            if (State == CanonicalViiperRuntimeState.Uninitialized) return false;

            if (TeardownPhase == CanonicalViiperRuntimeTeardownPhase.None)
                TeardownPhase = CanonicalViiperRuntimeTeardownPhase.DeckDetach;
            State = CanonicalViiperRuntimeState.CleanupPending;

            if (TeardownPhase == CanonicalViiperRuntimeTeardownPhase.DeckDetach)
            {
                if (!TryGetDeckAttachmentStateDuringTeardown(DeckDeviceHandle, out var deckState)) return false;
                if (deckState == USBDeviceAttachmentState.OutcomeUnknown) { MarkUnsafe("TeardownDeckAttachmentStateOutcomeUnknown"); return false; }
                if (deckState == USBDeviceAttachmentState.Attached)
                {
                    _native.SetSteamDeckDeviceState(DeckDeviceHandle, default);
                    var detach = _native.DetachUSBDeviceEx(DeckDeviceHandle);
                    if (detach == USBDeviceDetachResult.UnsafeOutcomeUnknown) { MarkUnsafe("TeardownDeckDetachUnsafeOutcomeUnknown"); return false; }
                    if (detach != USBDeviceDetachResult.Success) return false; // RetryableFailure/Invalid: stay CleanupPending, retry later
                }
                TeardownPhase = CanonicalViiperRuntimeTeardownPhase.Xbox360Detach;
            }

            if (TeardownPhase == CanonicalViiperRuntimeTeardownPhase.Xbox360Detach)
            {
                if (!TryGetDeckAttachmentStateDuringTeardown(Xbox360DeviceHandle, out var xboxState)) return false;
                if (xboxState == USBDeviceAttachmentState.OutcomeUnknown) { MarkUnsafe("TeardownXbox360AttachmentStateOutcomeUnknown"); return false; }
                if (xboxState == USBDeviceAttachmentState.Attached)
                {
                    _native.SetXbox360DeviceState(Xbox360DeviceHandle, default);
                    var detach = _native.DetachUSBDeviceEx(Xbox360DeviceHandle);
                    if (detach == USBDeviceDetachResult.UnsafeOutcomeUnknown) { MarkUnsafe("TeardownXbox360DetachUnsafeOutcomeUnknown"); return false; }
                    if (detach != USBDeviceDetachResult.Success) return false;
                }
                TeardownPhase = CanonicalViiperRuntimeTeardownPhase.DeckRemove;
            }

            if (TeardownPhase == CanonicalViiperRuntimeTeardownPhase.DeckRemove)
            {
                var result = _native.RemoveSteamDeckDeviceEx(DeckDeviceHandle);
                if (result == SteamDeckDeviceRemoveResult.UnsafeOutcomeUnknown) { MarkUnsafe("TeardownDeckRemoveUnsafeOutcomeUnknown"); return false; }
                if (result != SteamDeckDeviceRemoveResult.Success) return false; // RetryableFailure/Invalid: retry later, bus/server retained
                TeardownPhase = CanonicalViiperRuntimeTeardownPhase.Xbox360Remove;
            }

            if (TeardownPhase == CanonicalViiperRuntimeTeardownPhase.Xbox360Remove)
            {
                var result = _native.RemoveXbox360DeviceEx(Xbox360DeviceHandle);
                if (result == Xbox360DeviceRemoveResult.UnsafeOutcomeUnknown) { MarkUnsafe("TeardownXbox360RemoveUnsafeOutcomeUnknown"); return false; }
                if (result != Xbox360DeviceRemoveResult.Success) return false;
                TeardownPhase = CanonicalViiperRuntimeTeardownPhase.BusRemoval;
            }

            if (TeardownPhase == CanonicalViiperRuntimeTeardownPhase.BusRemoval)
            {
                if (!_native.RemoveUSBBus(ServerHandle, BusId)) return false; // retain bus/server ownership, retry later
                TeardownPhase = CanonicalViiperRuntimeTeardownPhase.ServerClose;
            }

            if (TeardownPhase == CanonicalViiperRuntimeTeardownPhase.ServerClose)
            {
                if (!_native.CloseUSBServer(ServerHandle)) return false; // retain server ownership, retry later
            }

            TeardownPhase = CanonicalViiperRuntimeTeardownPhase.None;
            State = CanonicalViiperRuntimeState.Closed;
            AppLog.Debug("SteamOutput", "Persistent canonical VIIPER runtime final teardown complete.");
            return true;
        }
        finally
        {
            _serial.Release();
        }
    }

    private bool TryGetDeckAttachmentStateDuringTeardown(nuint deviceHandle, out USBDeviceAttachmentState state)
    {
        if (_native.GetUSBDeviceAttachmentState(deviceHandle, out state)) return true;
        // Query failure during teardown: no evidence either way -- do not guess. Stay
        // CleanupPending (not Unsafe: this is a query failure, not a classified mutation result) so
        // an explicit retry can be attempted again later.
        return false;
    }
}
