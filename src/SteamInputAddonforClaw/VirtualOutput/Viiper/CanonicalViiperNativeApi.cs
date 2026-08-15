using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal enum SteamControllerDeviceRemoveResult : int
{
    Success = 0,
    RetryableFailure = 1,
    UnsafeOutcomeUnknown = 2,
    Invalid = 3
}

// Mirrors VIIPER's SteamDeckDeviceRemoveResult enum (dist/libVIIPER/libVIIPER.h,
// 0b3627317d2008065d8ec231f94bf31af7527bbd). Distinct managed type from
// SteamControllerDeviceRemoveResult even though the values are identical -- Gordon and Steam Deck
// are separate typed devices in VIIPER with their own enums, and keeping them separate here avoids
// silently coupling the two typed lifecycles together.
internal enum SteamDeckDeviceRemoveResult : int
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
    bool CreateSteamControllerDevice(nuint serverHandle, out nuint deviceHandle, uint busId, bool autoAttachLocalhost, ushort idVendor, ushort idProduct);
    bool SetSteamControllerDeviceState(nuint deviceHandle, SteamControllerDeviceState state);
    bool SetSteamControllerOutputCallback(nuint deviceHandle, SteamControllerOutputCallback? callback);
    bool RemoveSteamControllerDevice(nuint deviceHandle);
    SteamControllerDeviceRemoveResult RemoveSteamControllerDeviceEx(nuint deviceHandle);

    // Steam Deck typed surface (VIIPER main@0b3627317d2008065d8ec231f94bf31af7527bbd).
    bool CreateSteamDeckDevice(nuint serverHandle, out nuint deviceHandle, uint busId, bool autoAttachLocalhost, ushort idVendor, ushort idProduct);
    bool SetSteamDeckDeviceState(nuint deviceHandle, SteamDeckDeviceState state);
    bool SetSteamDeckOutputCallback(nuint deviceHandle, SteamDeckOutputCallback? callback);
    bool RemoveSteamDeckDevice(nuint deviceHandle);
    SteamDeckDeviceRemoveResult RemoveSteamDeckDeviceEx(nuint deviceHandle);
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
        "CreateSteamControllerDevice",
        "SetSteamControllerDeviceState",
        "SetSteamControllerOutputCallback",
        "RemoveSteamControllerDevice",
        "RemoveSteamControllerDeviceEx",
        "CreateSteamDeckDevice",
        "SetSteamDeckDeviceState",
        "SetSteamDeckOutputCallback",
        "RemoveSteamDeckDevice",
        "RemoveSteamDeckDeviceEx"
    ];

    private readonly NewUsbServerDelegate _newUsbServer;
    private readonly CloseUsbServerDelegate _closeUsbServer;
    private readonly CreateUsbBusDelegate _createUsbBus;
    private readonly RemoveUsbBusDelegate _removeUsbBus;
    private readonly GetUsbDeviceIdentityDelegate _getUsbDeviceIdentity;
    private readonly AttachUsbDeviceDelegate _attachUsbDevice;
    private readonly DetachUsbDeviceDelegate _detachUsbDevice;
    private readonly CreateSteamControllerDeviceDelegate _createSteamControllerDevice;
    private readonly SetSteamControllerDeviceStateDelegate _setSteamControllerDeviceState;
    private readonly SetSteamControllerOutputCallbackDelegate _setSteamControllerOutputCallback;
    private readonly RemoveSteamControllerDeviceDelegate _removeSteamControllerDevice;
    private readonly RemoveSteamControllerDeviceExDelegate _removeSteamControllerDeviceEx;
    private readonly CreateSteamDeckDeviceDelegate _createSteamDeckDevice;
    private readonly SetSteamDeckDeviceStateDelegate _setSteamDeckDeviceState;
    private readonly SetSteamDeckOutputCallbackDelegate _setSteamDeckOutputCallback;
    private readonly RemoveSteamDeckDeviceDelegate _removeSteamDeckDevice;
    private readonly RemoveSteamDeckDeviceExDelegate _removeSteamDeckDeviceEx;
    private readonly object _callbackGate = new();
    private readonly Dictionary<nuint, SteamControllerOutputCallback> _outputCallbacks = [];
    // Kept separate from _outputCallbacks (SteamControllerOutputCallback-typed) even though both
    // dictionaries key on device handle: Steam Deck and Steam Controller callbacks are distinct
    // managed delegate types, and a shared dictionary would force one of them to be stored as
    // object/erase the delegate type, losing the compile-time guarantee that a Deck handle can
    // never be invoked through a Controller-shaped callback or vice versa.
    private readonly Dictionary<nuint, SteamDeckOutputCallback> _steamDeckOutputCallbacks = [];
    private readonly Dictionary<nuint, ViiperLogCallback> _logCallbacks = [];
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
        _createSteamControllerDevice = Bind<CreateSteamControllerDeviceDelegate>(library, resolve, "CreateSteamControllerDevice");
        _setSteamControllerDeviceState = Bind<SetSteamControllerDeviceStateDelegate>(library, resolve, "SetSteamControllerDeviceState");
        _setSteamControllerOutputCallback = Bind<SetSteamControllerOutputCallbackDelegate>(library, resolve, "SetSteamControllerOutputCallback");
        _removeSteamControllerDevice = Bind<RemoveSteamControllerDeviceDelegate>(library, resolve, "RemoveSteamControllerDevice");
        _removeSteamControllerDeviceEx = Bind<RemoveSteamControllerDeviceExDelegate>(library, resolve, "RemoveSteamControllerDeviceEx");
        _createSteamDeckDevice = Bind<CreateSteamDeckDeviceDelegate>(library, resolve, "CreateSteamDeckDevice");
        _setSteamDeckDeviceState = Bind<SetSteamDeckDeviceStateDelegate>(library, resolve, "SetSteamDeckDeviceState");
        _setSteamDeckOutputCallback = Bind<SetSteamDeckOutputCallbackDelegate>(library, resolve, "SetSteamDeckOutputCallback");
        _removeSteamDeckDevice = Bind<RemoveSteamDeckDeviceDelegate>(library, resolve, "RemoveSteamDeckDevice");
        _removeSteamDeckDeviceEx = Bind<RemoveSteamDeckDeviceExDelegate>(library, resolve, "RemoveSteamDeckDeviceEx");
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

    public bool CreateSteamControllerDevice(
        nuint serverHandle,
        out nuint deviceHandle,
        uint busId,
        bool autoAttachLocalhost,
        ushort idVendor,
        ushort idProduct)
    {
        var succeeded = Succeeded(_createSteamControllerDevice(
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

    public bool SetSteamControllerDeviceState(nuint deviceHandle, SteamControllerDeviceState state)
        => Succeeded(_setSteamControllerDeviceState(deviceHandle, state));

    public bool SetSteamControllerOutputCallback(nuint deviceHandle, SteamControllerOutputCallback? callback)
    {
        var succeeded = Succeeded(_setSteamControllerOutputCallback(deviceHandle, callback));
        if (succeeded)
        {
            lock (_callbackGate)
            {
                if (callback is null) _outputCallbacks.Remove(deviceHandle);
                else _outputCallbacks[deviceHandle] = callback;
            }
        }
        return succeeded;
    }

    public bool RemoveSteamControllerDevice(nuint deviceHandle)
    {
        var succeeded = Succeeded(_removeSteamControllerDevice(deviceHandle));
        if (succeeded) ReleaseDeviceOwnership(deviceHandle);
        return succeeded;
    }

    public SteamControllerDeviceRemoveResult RemoveSteamControllerDeviceEx(nuint deviceHandle)
    {
        var result = _removeSteamControllerDeviceEx(deviceHandle);
        if (result == SteamControllerDeviceRemoveResult.Success)
            ReleaseDeviceOwnership(deviceHandle);
        return result;
    }

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
            _outputCallbacks.Remove(deviceHandle);
            _steamDeckOutputCallbacks.Remove(deviceHandle);
            _deviceOwnership.Remove(deviceHandle);
        }
    }

    private void ReleaseOutputCallbacksLocked(Func<(nuint ServerHandle, uint BusId), bool> predicate)
    {
        foreach (var (deviceHandle, ownership) in _deviceOwnership.ToArray())
        {
            if (!predicate(ownership)) continue;

            _outputCallbacks.Remove(deviceHandle);
            _steamDeckOutputCallbacks.Remove(deviceHandle);
            _deviceOwnership.Remove(deviceHandle);
        }
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
    internal delegate byte CreateSteamControllerDeviceDelegate(
        nuint serverHandle,
        out nuint outDeviceHandle,
        uint busId,
        byte autoAttachLocalhost,
        ushort idVendor,
        ushort idProduct);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte SetSteamControllerDeviceStateDelegate(nuint handle, SteamControllerDeviceState state);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte SetSteamControllerOutputCallbackDelegate(nuint handle, SteamControllerOutputCallback? callback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte RemoveSteamControllerDeviceDelegate(nuint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate SteamControllerDeviceRemoveResult RemoveSteamControllerDeviceExDelegate(nuint handle);

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
}
