using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

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
        "RemoveSteamControllerDevice"
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
    private readonly object _callbackGate = new();
    private readonly Dictionary<nuint, SteamControllerOutputCallback> _outputCallbacks = [];
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
        if (succeeded)
        {
            lock (_callbackGate)
            {
                _outputCallbacks.Remove(deviceHandle);
                _deviceOwnership.Remove(deviceHandle);
            }
        }
        return succeeded;
    }

    private void ReleaseOutputCallbacksLocked(Func<(nuint ServerHandle, uint BusId), bool> predicate)
    {
        foreach (var (deviceHandle, ownership) in _deviceOwnership.ToArray())
        {
            if (!predicate(ownership)) continue;

            _outputCallbacks.Remove(deviceHandle);
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
}
