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
}

internal sealed class WindowsMsiClawNativeHidApi : IMsiClawNativeHidApi
{
    public int LastError { get; private set; }

    public SafeFileHandle Open(string devicePath, uint desiredAccess, uint shareMode, uint creationDisposition)
    {
        var handle = CreateFileW(devicePath, desiredAccess, shareMode, IntPtr.Zero, creationDisposition, 0, IntPtr.Zero);
        LastError = handle.IsInvalid ? Marshal.GetLastWin32Error() : 0;
        return handle;
    }

    public bool Write(SafeFileHandle handle, byte[] buffer, out uint bytesWritten)
    {
        var result = WriteFile(handle, buffer, (uint)buffer.Length, out bytesWritten, IntPtr.Zero);
        LastError = result ? 0 : Marshal.GetLastWin32Error();
        return result;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(SafeFileHandle file, byte[] buffer, uint numberOfBytesToWrite, out uint numberOfBytesWritten, IntPtr overlapped);
}
