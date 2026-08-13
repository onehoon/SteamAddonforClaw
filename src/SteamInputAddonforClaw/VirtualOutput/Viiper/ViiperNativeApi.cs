using System.Runtime.InteropServices;

namespace SteamInputAddonforClaw.VirtualOutput.Viiper;

internal interface IViiperNativeApi : IDisposable
{
    int Initialize(string listenAddress);
    void Shutdown();
    int CreateBus(uint busId);
    int RemoveBus(uint busId);
    int AddDevice(uint busId, string typeName, ushort vendorId, ushort productId, out uint deviceId);
    int RemoveDevice(uint busId, uint deviceId);
    int SetInput(uint busId, uint deviceId, byte[] report);
    int SetFeedbackCallback(uint busId, uint deviceId, Action<ReadOnlyMemory<byte>> callback);
    string[] GetDeviceTypes();
    string? GetLastError();
}

internal sealed class ViiperNativeApi : IViiperNativeApi
{
    private readonly nint _library;
    private readonly Init _initialize;
    private readonly ShutdownDelegate _shutdown;
    private readonly Bus _createBus;
    private readonly Bus _removeBus;
    private readonly AddDeviceEx _addDevice;
    private readonly RemoveDeviceDelegate _removeDevice;
    private readonly SetInputDelegate _setInput;
    private readonly SetFeedbackCallbackDelegate _setFeedbackCallback;
    private readonly GetString _getTypes;
    private readonly GetString _getLastError;
    private readonly FreeString _freeString;
    private bool _disposed;
    private FeedbackCallbackDelegate? _feedbackCallback;

    private ViiperNativeApi(nint library)
    {
        _library = library;
        _initialize = Bind<Init>("viiper_init");
        _shutdown = Bind<ShutdownDelegate>("viiper_shutdown");
        _createBus = Bind<Bus>("viiper_bus_create");
        _removeBus = Bind<Bus>("viiper_bus_remove");
        _addDevice = Bind<AddDeviceEx>("viiper_device_add_ex");
        _removeDevice = Bind<RemoveDeviceDelegate>("viiper_device_remove");
        _setInput = Bind<SetInputDelegate>("viiper_device_set_input");
        _setFeedbackCallback = Bind<SetFeedbackCallbackDelegate>("viiper_device_set_feedback_callback");
        _getTypes = Bind<GetString>("viiper_list_device_types");
        _getLastError = Bind<GetString>("viiper_last_error");
        _freeString = Bind<FreeString>("viiper_free_string");
    }

    internal static ViiperNativeApi Load(string absolutePath) => new(ViiperNativeModuleCache.GetOrLoad(absolutePath));

    public int Initialize(string listenAddress) => WithUtf8(listenAddress, _initialize);
    public void Shutdown() => _shutdown();
    public int CreateBus(uint busId) => _createBus(busId);
    public int RemoveBus(uint busId) => _removeBus(busId);
    public int AddDevice(uint busId, string typeName, ushort vendorId, ushort productId, out uint deviceId)
    {
        var value = Marshal.StringToCoTaskMemUTF8(typeName);
        try { return _addDevice(busId, value, vendorId, productId, out deviceId); }
        finally { Marshal.FreeCoTaskMem(value); }
    }
    public int RemoveDevice(uint busId, uint deviceId) => _removeDevice(busId, deviceId);
    public int SetInput(uint busId, uint deviceId, byte[] report) => _setInput(busId, deviceId, report, report.Length);
    public int SetFeedbackCallback(uint busId, uint deviceId, Action<ReadOnlyMemory<byte>> callback)
    {
        _feedbackCallback = (_, _, data, length, _) =>
        {
            if (data == IntPtr.Zero || length <= 0) { callback(ReadOnlyMemory<byte>.Empty); return; }
            var copy = new byte[length];
            Marshal.Copy(data, copy, 0, length);
            callback(copy);
        };
        return _setFeedbackCallback(busId, deviceId, _feedbackCallback, IntPtr.Zero);
    }
    public string[] GetDeviceTypes() => ReadOwnedString(_getTypes) is { Length: > 0 } json ? System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? [] : [];
    public string? GetLastError() => ReadOwnedString(_getLastError);
    // The native module itself is process-lifetime and is never unloaded here; see ViiperNativeModuleCache.
    public void Dispose() { if (!_disposed) { _feedbackCallback = null; _disposed = true; } }

    private T Bind<T>(string export) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, export));
    private int WithUtf8(string value, Init action) { var pointer = Marshal.StringToCoTaskMemUTF8(value); try { return action(pointer); } finally { Marshal.FreeCoTaskMem(pointer); } }
    private string? ReadOwnedString(GetString action) { var pointer = action(); if (pointer == IntPtr.Zero) return null; try { return Marshal.PtrToStringUTF8(pointer); } finally { _freeString(pointer); } }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Init(nint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ShutdownDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Bus(uint busId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AddDeviceEx(uint busId, nint typeName, ushort vendorId, ushort productId, out uint deviceId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RemoveDeviceDelegate(uint busId, uint deviceId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetInputDelegate(uint busId, uint deviceId, [In] byte[] data, int length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetFeedbackCallbackDelegate(uint busId, uint deviceId, FeedbackCallbackDelegate callback, IntPtr userData);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FeedbackCallbackDelegate(uint busId, uint deviceId, IntPtr data, int length, IntPtr userData);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetString();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FreeString(nint value);
}
