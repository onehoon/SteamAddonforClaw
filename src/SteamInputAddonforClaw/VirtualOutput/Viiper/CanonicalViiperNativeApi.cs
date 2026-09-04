using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

// Mirrors VIIPER's SteamDeckDeviceRemoveResult enum (dist/libVIIPER/libVIIPER.h,
// 0b3627317d2008065d8ec231f94bf31af7527bbd).
internal enum SteamDeckDeviceRemoveResult : int
{
    Success = 0,
    RetryableFailure = 1,
    UnsafeOutcomeUnknown = 2,
    Invalid = 3
}

// Mirrors VIIPER's Xbox360DeviceRemoveResult enum (dist/libVIIPER/libVIIPER.h, VIIPER
// main@a6bb749199aa797da690c611d2f18edc5e770c1e -- see
// src/SteamInputAddonforClaw/Dependencies/Viiper/PROVENANCE.md for the pinned commit).
internal enum Xbox360DeviceRemoveResult : int
{
    Success = 0,
    RetryableFailure = 1,
    UnsafeOutcomeUnknown = 2,
    Invalid = 3
}

internal interface ICanonicalViiperNativeApi
{
    bool NewUSBServer(ref USBServerConfig config, out nuint serverHandle, ViiperLogCallback? logCallback = null);
    bool CloseUSBServer(nuint serverHandle);
    bool CreateUSBBus(nuint serverHandle, ref uint busId);
    bool RemoveUSBBus(nuint serverHandle, uint busId);
    bool GetUSBDeviceIdentity(nuint deviceHandle, out uint busId, out uint deviceId);
    bool AttachUSBDevice(nuint deviceHandle);
    bool DetachUSBDevice(nuint deviceHandle);

    // Classified attachment surface: mirrors the native classification unchanged (never
    // translated into policy, never automatically retried -- see docs/VIIPER_INTEGRATION.md). The
    // Production uses this classified surface for persistent Steam Deck route attach/detach;
    // retryable results are retried only at explicit route lifecycle boundaries.
    USBDeviceAttachResult AttachUSBDeviceEx(nuint deviceHandle);
    USBDeviceDetachResult DetachUSBDeviceEx(nuint deviceHandle);
    bool GetUSBDeviceAttachmentState(nuint deviceHandle, out USBDeviceAttachmentState state);

    // Canonical typed Steam Deck surface consumed by the Addon.
    bool CreateSteamDeckDevice(nuint serverHandle, out nuint deviceHandle, uint busId, bool autoAttachLocalhost, ushort idVendor, ushort idProduct);
    bool SetSteamDeckDeviceState(nuint deviceHandle, SteamDeckDeviceState state);
    bool SetSteamDeckOutputCallback(nuint deviceHandle, SteamDeckOutputCallback? callback);
    bool RemoveSteamDeckDevice(nuint deviceHandle);
    SteamDeckDeviceRemoveResult RemoveSteamDeckDeviceEx(nuint deviceHandle);

    // Canonical typed Xbox360 surface. The Full1902 process-lifetime runtime creates one
    // detached-ready logical handle; MsiClawAddonPresentation classified-attaches it for the Xbox360
    // virtual presentation. This interface binds the ABI surface only -- presentation policy stays in
    // MsiClawAddonPresentation. Buttons/D-pad/sticks/triggers are supported here; the host's rumble
    // is delivered through SetXbox360RumbleCallback, whose managed delegate is rooted like the Steam
    // Deck output callback.
    bool CreateXbox360Device(nuint serverHandle, out nuint deviceHandle, uint busId, bool autoAttachLocalhost, ushort idVendor, ushort idProduct, byte xinputSubType);
    bool SetXbox360DeviceState(nuint deviceHandle, Xbox360DeviceState state);
    bool SetXbox360RumbleCallback(nuint deviceHandle, Xbox360RumbleCallback? callback);
    bool RemoveXbox360Device(nuint deviceHandle);
    Xbox360DeviceRemoveResult RemoveXbox360DeviceEx(nuint deviceHandle);
}

internal sealed class CanonicalViiperNativeApi : ICanonicalViiperNativeApi
{
    internal static readonly IReadOnlyList<string> RequiredExports =
    [
        "NewUSBServer",
        "CloseUSBServer",
        "CreateUSBBus",
        "RemoveUSBBus",
        "GetUSBDeviceIdentity",
        "AttachUSBDevice",
        "DetachUSBDevice",
        "AttachUSBDeviceEx",
        "DetachUSBDeviceEx",
        "GetUSBDeviceAttachmentState",
        "CreateSteamDeckDevice",
        "SetSteamDeckDeviceState",
        "SetSteamDeckOutputCallback",
        "RemoveSteamDeckDevice",
        "RemoveSteamDeckDeviceEx",
        "CreateXbox360Device",
        "SetXbox360DeviceState",
        "SetXbox360RumbleCallback",
        "RemoveXbox360Device",
        "RemoveXbox360DeviceEx"
    ];

    private readonly NewUsbServerDelegate _newUsbServer;
    private readonly CloseUsbServerDelegate _closeUsbServer;
    private readonly CreateUsbBusDelegate _createUsbBus;
    private readonly RemoveUsbBusDelegate _removeUsbBus;
    private readonly GetUsbDeviceIdentityDelegate _getUsbDeviceIdentity;
    private readonly AttachUsbDeviceDelegate _attachUsbDevice;
    private readonly DetachUsbDeviceDelegate _detachUsbDevice;
    private readonly AttachUsbDeviceExDelegate _attachUsbDeviceEx;
    private readonly DetachUsbDeviceExDelegate _detachUsbDeviceEx;
    private readonly GetUsbDeviceAttachmentStateDelegate _getUsbDeviceAttachmentState;
    private readonly CreateSteamDeckDeviceDelegate _createSteamDeckDevice;
    private readonly SetSteamDeckDeviceStateDelegate _setSteamDeckDeviceState;
    private readonly SetSteamDeckOutputCallbackDelegate _setSteamDeckOutputCallback;
    private readonly RemoveSteamDeckDeviceDelegate _removeSteamDeckDevice;
    private readonly RemoveSteamDeckDeviceExDelegate _removeSteamDeckDeviceEx;
    private readonly CreateXbox360DeviceDelegate _createXbox360Device;
    private readonly SetXbox360DeviceStateDelegate _setXbox360DeviceState;
    private readonly SetXbox360RumbleCallbackDelegate _setXbox360RumbleCallback;
    private readonly RemoveXbox360DeviceDelegate _removeXbox360Device;
    private readonly RemoveXbox360DeviceExDelegate _removeXbox360DeviceEx;
    private readonly object _callbackGate = new();
    private readonly Dictionary<nuint, SteamDeckOutputCallback> _steamDeckOutputCallbacks = [];
    private readonly Dictionary<nuint, Xbox360RumbleCallback> _xbox360RumbleCallbacks = [];
    private readonly Dictionary<nuint, ViiperLogCallback> _logCallbacks = [];
    // Shared logical-device ownership map for both Steam Deck and Xbox360 handles. Safe to share:
    // VIIPER's canonical implementation allocates every typed device handle from the same
    // process-global cgo.Handle space and records all families in the same global
    // deviceHandleRecords table, so a Deck handle and an Xbox360 handle are never independently
    // allocated and can never collide. Releasing a Steam Deck output callback root for an Xbox360
    // handle (which never had one) is a harmless no-op.
    private readonly Dictionary<nuint, (nuint ServerHandle, uint BusId)> _deviceOwnership = [];

    internal CanonicalViiperNativeApi(nint library, Func<nint, string, nint>? exportResolver = null)
    {
        var resolve = exportResolver ?? NativeLibrary.GetExport;
        _newUsbServer = Bind<NewUsbServerDelegate>(library, resolve, "NewUSBServer");
        _closeUsbServer = Bind<CloseUsbServerDelegate>(library, resolve, "CloseUSBServer");
        _createUsbBus = Bind<CreateUsbBusDelegate>(library, resolve, "CreateUSBBus");
        _removeUsbBus = Bind<RemoveUsbBusDelegate>(library, resolve, "RemoveUSBBus");
        _getUsbDeviceIdentity = Bind<GetUsbDeviceIdentityDelegate>(library, resolve, "GetUSBDeviceIdentity");
        _attachUsbDevice = Bind<AttachUsbDeviceDelegate>(library, resolve, "AttachUSBDevice");
        _detachUsbDevice = Bind<DetachUsbDeviceDelegate>(library, resolve, "DetachUSBDevice");
        _attachUsbDeviceEx = Bind<AttachUsbDeviceExDelegate>(library, resolve, "AttachUSBDeviceEx");
        _detachUsbDeviceEx = Bind<DetachUsbDeviceExDelegate>(library, resolve, "DetachUSBDeviceEx");
        _getUsbDeviceAttachmentState = Bind<GetUsbDeviceAttachmentStateDelegate>(library, resolve, "GetUSBDeviceAttachmentState");
        _createSteamDeckDevice = Bind<CreateSteamDeckDeviceDelegate>(library, resolve, "CreateSteamDeckDevice");
        _setSteamDeckDeviceState = Bind<SetSteamDeckDeviceStateDelegate>(library, resolve, "SetSteamDeckDeviceState");
        _setSteamDeckOutputCallback = Bind<SetSteamDeckOutputCallbackDelegate>(library, resolve, "SetSteamDeckOutputCallback");
        _removeSteamDeckDevice = Bind<RemoveSteamDeckDeviceDelegate>(library, resolve, "RemoveSteamDeckDevice");
        _removeSteamDeckDeviceEx = Bind<RemoveSteamDeckDeviceExDelegate>(library, resolve, "RemoveSteamDeckDeviceEx");
        _createXbox360Device = Bind<CreateXbox360DeviceDelegate>(library, resolve, "CreateXbox360Device");
        _setXbox360DeviceState = Bind<SetXbox360DeviceStateDelegate>(library, resolve, "SetXbox360DeviceState");
        _setXbox360RumbleCallback = Bind<SetXbox360RumbleCallbackDelegate>(library, resolve, "SetXbox360RumbleCallback");
        _removeXbox360Device = Bind<RemoveXbox360DeviceDelegate>(library, resolve, "RemoveXbox360Device");
        _removeXbox360DeviceEx = Bind<RemoveXbox360DeviceExDelegate>(library, resolve, "RemoveXbox360DeviceEx");
    }

    internal static CanonicalViiperNativeApi Load(string absolutePath)
        => new(ViiperNativeModuleCache.GetOrLoad(absolutePath));

    public bool NewUSBServer(ref USBServerConfig config, out nuint serverHandle, ViiperLogCallback? logCallback = null)
    {
        var succeeded = Succeeded(_newUsbServer(ref config, out serverHandle, logCallback));
        if (succeeded && logCallback is not null)
        {
            lock (_callbackGate) _logCallbacks[serverHandle] = logCallback;
        }
        return succeeded;
    }

    public bool CloseUSBServer(nuint serverHandle)
    {
        var succeeded = Succeeded(_closeUsbServer(serverHandle));
        if (succeeded)
        {
            lock (_callbackGate)
            {
                ReleaseOutputCallbacksLocked(ownership => ownership.ServerHandle == serverHandle);
                _logCallbacks.Remove(serverHandle);
            }
        }
        return succeeded;
    }

    public bool CreateUSBBus(nuint serverHandle, ref uint busId)
        => Succeeded(_createUsbBus(serverHandle, ref busId));

    public bool RemoveUSBBus(nuint serverHandle, uint busId)
    {
        var succeeded = Succeeded(_removeUsbBus(serverHandle, busId));
        if (succeeded)
        {
            lock (_callbackGate)
                ReleaseOutputCallbacksLocked(ownership => ownership.ServerHandle == serverHandle && ownership.BusId == busId);
        }
        return succeeded;
    }

    public bool GetUSBDeviceIdentity(nuint deviceHandle, out uint busId, out uint deviceId)
        => Succeeded(_getUsbDeviceIdentity(deviceHandle, out busId, out deviceId));

    public bool AttachUSBDevice(nuint deviceHandle)
        => Succeeded(_attachUsbDevice(deviceHandle));

    public bool DetachUSBDevice(nuint deviceHandle)
        => Succeeded(_detachUsbDevice(deviceHandle));

    // Classified attachment surface: the native classification is returned unchanged. No policy
    // translation, no automatic retry, no inference about PnP/Windows-side state belongs here --
    // see docs/VIIPER_INTEGRATION.md.
    public USBDeviceAttachResult AttachUSBDeviceEx(nuint deviceHandle)
        => _attachUsbDeviceEx(deviceHandle);

    public USBDeviceDetachResult DetachUSBDeviceEx(nuint deviceHandle)
        => _detachUsbDeviceEx(deviceHandle);

    public bool GetUSBDeviceAttachmentState(nuint deviceHandle, out USBDeviceAttachmentState state)
        => Succeeded(_getUsbDeviceAttachmentState(deviceHandle, out state));

    public bool CreateSteamDeckDevice(
        nuint serverHandle,
        out nuint deviceHandle,
        uint busId,
        bool autoAttachLocalhost,
        ushort idVendor,
        ushort idProduct)
    {
        var succeeded = Succeeded(_createSteamDeckDevice(
            serverHandle,
            out deviceHandle,
            busId,
            autoAttachLocalhost ? (byte)1 : (byte)0,
            idVendor,
            idProduct));
        if (succeeded)
        {
            lock (_callbackGate) _deviceOwnership[deviceHandle] = (serverHandle, busId);
        }
        return succeeded;
    }

    public bool SetSteamDeckDeviceState(nuint deviceHandle, SteamDeckDeviceState state)
        => Succeeded(_setSteamDeckDeviceState(deviceHandle, state));

    public bool SetSteamDeckOutputCallback(nuint deviceHandle, SteamDeckOutputCallback? callback)
    {
        // The native call and the managed root mutation must be one serialized operation, not two
        // independent steps: two threads racing SetSteamDeckOutputCallback on the same handle could
        // otherwise interleave as native<-cb1, native<-cb2, root<-cb2, root<-cb1, leaving native
        // holding cb2's function pointer while the managed root only keeps cb1 alive -- cb2 becomes
        // GC-eligible while VIIPER can still invoke it. Holding _callbackGate across both the native
        // call and the corresponding dictionary mutation makes the pair atomic with respect to any
        // other Set/Clear on this API instance, as well as the teardown-driven root release paths
        // below, which already run under the same lock.
        lock (_callbackGate)
        {
            var succeeded = Succeeded(_setSteamDeckOutputCallback(deviceHandle, callback));
            if (succeeded)
            {
                if (callback is null) _steamDeckOutputCallbacks.Remove(deviceHandle);
                else _steamDeckOutputCallbacks[deviceHandle] = callback;
            }
            return succeeded;
        }
    }

    public bool RemoveSteamDeckDevice(nuint deviceHandle)
    {
        var succeeded = Succeeded(_removeSteamDeckDevice(deviceHandle));
        if (succeeded) ReleaseDeviceOwnership(deviceHandle);
        return succeeded;
    }

    public SteamDeckDeviceRemoveResult RemoveSteamDeckDeviceEx(nuint deviceHandle)
    {
        var result = _removeSteamDeckDeviceEx(deviceHandle);
        if (result == SteamDeckDeviceRemoveResult.Success)
            ReleaseDeviceOwnership(deviceHandle);
        return result;
    }

    private void ReleaseDeviceOwnership(nuint deviceHandle)
    {
        lock (_callbackGate)
        {
            _steamDeckOutputCallbacks.Remove(deviceHandle);
            _xbox360RumbleCallbacks.Remove(deviceHandle);
            _deviceOwnership.Remove(deviceHandle);
        }
    }

    private void ReleaseOutputCallbacksLocked(Func<(nuint ServerHandle, uint BusId), bool> predicate)
    {
        foreach (var (deviceHandle, ownership) in _deviceOwnership.ToArray())
        {
            if (!predicate(ownership)) continue;

            _steamDeckOutputCallbacks.Remove(deviceHandle);
            _xbox360RumbleCallbacks.Remove(deviceHandle);
            _deviceOwnership.Remove(deviceHandle);
        }
    }

    // ---- Canonical typed Xbox360 surface. The Full1902 process-lifetime runtime creates one
    // detached-ready logical handle; MsiClawAddonPresentation classified-attaches it for the Xbox360
    // virtual presentation. This API layer owns only ABI binding/managed callback rooting;
    // presentation policy remains in MsiClawAddonPresentation. Buttons/D-pad/sticks/triggers are
    // mapped here, and the host's rumble is delivered through the rooted SetXbox360RumbleCallback.
    // Callback/ownership state is tracked in the shared _xbox360RumbleCallbacks / _deviceOwnership
    // maps; RemoveXbox360Device(Ex)/RemoveUSBBus/CloseUSBServer release it through the existing
    // ReleaseDeviceOwnership / ReleaseOutputCallbacksLocked paths. ----

    public bool CreateXbox360Device(
        nuint serverHandle,
        out nuint deviceHandle,
        uint busId,
        bool autoAttachLocalhost,
        ushort idVendor,
        ushort idProduct,
        byte xinputSubType)
    {
        var succeeded = Succeeded(_createXbox360Device(
            serverHandle,
            out deviceHandle,
            busId,
            autoAttachLocalhost ? (byte)1 : (byte)0,
            idVendor,
            idProduct,
            xinputSubType));
        if (succeeded)
        {
            lock (_callbackGate) _deviceOwnership[deviceHandle] = (serverHandle, busId);
        }
        return succeeded;
    }

    public bool SetXbox360DeviceState(nuint deviceHandle, Xbox360DeviceState state)
        => Succeeded(_setXbox360DeviceState(deviceHandle, state));

    public bool SetXbox360RumbleCallback(nuint deviceHandle, Xbox360RumbleCallback? callback)
    {
        // Same atomicity requirement as SetSteamDeckOutputCallback: hold _callbackGate across the
        // native call and the managed-root mutation so native can never end up holding a function
        // pointer whose managed delegate the root dictionary no longer keeps alive, and so the
        // teardown-driven release paths cannot interleave.
        lock (_callbackGate)
        {
            var succeeded = Succeeded(_setXbox360RumbleCallback(deviceHandle, callback));
            if (succeeded)
            {
                if (callback is null) _xbox360RumbleCallbacks.Remove(deviceHandle);
                else _xbox360RumbleCallbacks[deviceHandle] = callback;
            }
            return succeeded;
        }
    }

    public bool RemoveXbox360Device(nuint deviceHandle)
    {
        var succeeded = Succeeded(_removeXbox360Device(deviceHandle));
        if (succeeded) ReleaseDeviceOwnership(deviceHandle);
        return succeeded;
    }

    public Xbox360DeviceRemoveResult RemoveXbox360DeviceEx(nuint deviceHandle)
    {
        var result = _removeXbox360DeviceEx(deviceHandle);
        if (result == Xbox360DeviceRemoveResult.Success)
            ReleaseDeviceOwnership(deviceHandle);
        return result;
    }

    private static T Bind<T>(nint library, Func<nint, string, nint> resolver, string export) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(resolver(library, export));

    private static bool Succeeded(byte value) => value != 0;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte NewUsbServerDelegate(ref USBServerConfig config, out nuint outHandle, ViiperLogCallback? logCallback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte CloseUsbServerDelegate(nuint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte CreateUsbBusDelegate(nuint handle, ref uint busId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte RemoveUsbBusDelegate(nuint handle, uint busId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte GetUsbDeviceIdentityDelegate(nuint handle, out uint busId, out uint deviceId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte AttachUsbDeviceDelegate(nuint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte DetachUsbDeviceDelegate(nuint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate USBDeviceAttachResult AttachUsbDeviceExDelegate(nuint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate USBDeviceDetachResult DetachUsbDeviceExDelegate(nuint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte GetUsbDeviceAttachmentStateDelegate(nuint handle, out USBDeviceAttachmentState state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte CreateSteamDeckDeviceDelegate(
        nuint serverHandle,
        out nuint outDeviceHandle,
        uint busId,
        byte autoAttachLocalhost,
        ushort idVendor,
        ushort idProduct);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte SetSteamDeckDeviceStateDelegate(nuint handle, SteamDeckDeviceState state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte SetSteamDeckOutputCallbackDelegate(nuint handle, SteamDeckOutputCallback? callback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte RemoveSteamDeckDeviceDelegate(nuint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate SteamDeckDeviceRemoveResult RemoveSteamDeckDeviceExDelegate(nuint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte CreateXbox360DeviceDelegate(
        nuint serverHandle,
        out nuint outDeviceHandle,
        uint busId,
        byte autoAttachLocalhost,
        ushort idVendor,
        ushort idProduct,
        byte xinputSubType);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte SetXbox360DeviceStateDelegate(nuint handle, Xbox360DeviceState state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte SetXbox360RumbleCallbackDelegate(nuint handle, Xbox360RumbleCallback? callback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte RemoveXbox360DeviceDelegate(nuint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate Xbox360DeviceRemoveResult RemoveXbox360DeviceExDelegate(nuint handle);
}
