using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal interface IMsiClawRawHidTransport
{
    Task<bool> WriteAsync(string devicePath, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);
}

internal sealed class WindowsMsiClawRawHidTransport : IMsiClawRawHidTransport
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareRead = 0x00000001;
    private const uint ShareWrite = 0x00000002;
    private const uint OpenExisting = 3;

    private readonly IMsiClawNativeHidApi _api;

    internal WindowsMsiClawRawHidTransport(IMsiClawNativeHidApi? api = null) => _api = api ?? new WindowsMsiClawNativeHidApi();

    public Task<bool> WriteAsync(string devicePath, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(devicePath) || bytes.Length != 64)
        {
            AppLog.Debug("NativeMode", "Raw MSI HID write rejected before native I/O.", ("Reason", string.IsNullOrWhiteSpace(devicePath) ? "EmptyDevicePath" : "InvalidLength"), ("RequestedLength", bytes.Length));
            return Task.FromResult(false);
        }

        using var handle = _api.Open(devicePath, GenericRead | GenericWrite, ShareRead | ShareWrite, OpenExisting);
        if (handle.IsInvalid)
        {
            var error = _api.LastError;
            AppLog.Debug("NativeMode", "Raw MSI HID open failed.", ("Operation", "Open"), ("Win32Error", error), ("RequestedLength", bytes.Length));
            return Task.FromResult(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var buffer = bytes.ToArray();
        if (!_api.Write(handle, buffer, out var written))
        {
            var error = _api.LastError;
            AppLog.Debug("NativeMode", "Raw MSI HID write failed.", ("Operation", "Write"), ("BytesWritten", written), ("Win32Error", error), ("RequestedLength", buffer.Length));
            return Task.FromResult(false);
        }

        if (written != buffer.Length)
        {
            AppLog.Debug("NativeMode", "Raw MSI HID partial write rejected.", ("Operation", "Write"), ("Reason", "PartialWrite"), ("BytesWritten", written), ("RequestedLength", buffer.Length));
            return Task.FromResult(false);
        }

        AppLog.Debug("NativeMode", "Raw MSI HID write succeeded.", ("Operation", "Write"), ("BytesWritten", written), ("RequestedLength", buffer.Length));
        return Task.FromResult(true);
    }

}

internal interface IMsiClawNativeHidApi
{
    int LastError { get; }
    SafeFileHandle Open(string devicePath, uint desiredAccess, uint shareMode, uint creationDisposition);
    bool Write(SafeFileHandle handle, byte[] buffer, out uint bytesWritten);

    /// <summary>
    /// Reads the true input/output report byte lengths for an opened HID interface via
    /// HidD_GetPreparsedData + HidP_GetCaps. This is the authoritative source for report
    /// lengths -- DeviceInformation has no valid property keys for HID report lengths.
    /// </summary>
    bool TryGetReportLengths(SafeFileHandle handle, out int inputReportLength, out int outputReportLength, out int hidStatus);
}

internal sealed class WindowsMsiClawNativeHidApi : IMsiClawNativeHidApi
{
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint WriteTimeoutMs = 1000;
    public int LastError { get; private set; }

    public SafeFileHandle Open(string devicePath, uint desiredAccess, uint shareMode, uint creationDisposition)
    {
        var handle = CreateFileW(devicePath, desiredAccess, shareMode, IntPtr.Zero, creationDisposition, FileFlagOverlapped, IntPtr.Zero);
        LastError = handle.IsInvalid ? Marshal.GetLastWin32Error() : 0;
        return handle;
    }

    public bool Write(SafeFileHandle handle, byte[] buffer, out uint bytesWritten)
    {
        bytesWritten = 0;
        var overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
        var eventHandle = CreateEventW(IntPtr.Zero, true, false, null);
        var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var state = new NativeOverlapped { EventHandle = eventHandle };
            Marshal.StructureToPtr(state, overlapped, false);
            var result = WriteFile(handle, pinned.AddrOfPinnedObject(), (uint)buffer.Length, IntPtr.Zero, overlapped);
            if (!result)
            {
                LastError = Marshal.GetLastWin32Error();
                if (LastError != ErrorIoPending)
                    return false;

                var wait = WaitForSingleObject(eventHandle, WriteTimeoutMs);
                if (wait != WaitObject0)
                {
                    var terminalError = wait == WaitTimeout ? ErrorTimeout : Marshal.GetLastWin32Error();
                    CancelAndDrain(handle, overlapped);
                    LastError = terminalError;
                    return false;
                }
            }

            result = GetOverlappedResult(handle, overlapped, out bytesWritten, false);
            LastError = result ? 0 : Marshal.GetLastWin32Error();
            return result;
        }
        finally
        {
            pinned.Free();
            if (eventHandle != IntPtr.Zero) CloseHandle(eventHandle);
            Marshal.FreeHGlobal(overlapped);
        }
    }

    private static void CancelAndDrain(SafeFileHandle handle, IntPtr overlapped)
    {
        _ = CancelIoEx(handle, overlapped);
        _ = GetOverlappedResult(handle, overlapped, out _, true);
    }

    public bool TryGetReportLengths(SafeFileHandle handle, out int inputReportLength, out int outputReportLength, out int hidStatus)
    {
        inputReportLength = 0;
        outputReportLength = 0;
        hidStatus = 0;
        if (!HidD_GetPreparsedData(handle, out var preparsedData) || preparsedData == IntPtr.Zero)
        {
            LastError = Marshal.GetLastWin32Error();
            return false;
        }
        try
        {
            hidStatus = HidP_GetCaps(preparsedData, out var caps);
            if (hidStatus != HidpStatusSuccess)
            {
                return false;
            }
            inputReportLength = caps.InputReportByteLength;
            outputReportLength = caps.OutputReportByteLength;
            LastError = 0;
            return true;
        }
        finally
        {
            HidD_FreePreparsedData(preparsedData);
        }
    }

    private const int HidpStatusSuccess = 0x00110000;
    private const int ErrorIoPending = 997;
    private const int ErrorTimeout = 1460;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateEventW(IntPtr attributes, bool manualReset, bool initialState, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(SafeFileHandle file, IntPtr buffer, uint numberOfBytesToWrite, IntPtr numberOfBytesWritten, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlapped, out uint numberOfBytesTransferred, bool wait);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeOverlapped
    {
        public UIntPtr Internal;
        public UIntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr EventHandle;
    }

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }
}
