using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal interface IMsiClawRawHidTransport
{
    Task<bool> WriteAsync(string devicePath, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);
    Task<byte[]?> ReadAsync(string devicePath, int reportLength, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
    Task<IReadOnlyList<byte[]>?> WriteAndReadAsync(
        string devicePath,
        ReadOnlyMemory<byte> bytes,
        int reportLength,
        int maxReports,
        TimeSpan timeout,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<byte[]>?>(null);
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

    public async Task<byte[]?> ReadAsync(string devicePath, int reportLength, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(devicePath) || reportLength <= 0 || reportLength > 4096)
            return null;

        using var handle = _api.Open(devicePath, GenericRead, ShareRead | ShareWrite, OpenExisting);
        if (handle.IsInvalid)
            return null;

        return await ReadOneBoundedAsync(handle, reportLength, timeout, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<byte[]>?> WriteAndReadAsync(
        string devicePath,
        ReadOnlyMemory<byte> bytes,
        int reportLength,
        int maxReports,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(devicePath)
            || bytes.Length != 64
            || reportLength <= 0
            || reportLength > 4096
            || maxReports <= 0
            || maxReports > 4
            || timeout <= TimeSpan.Zero)
            return null;

        using var handle = _api.Open(devicePath, GenericRead | GenericWrite, ShareRead | ShareWrite, OpenExisting);
        if (handle.IsInvalid)
            return null;

        var request = bytes.ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        if (!_api.Write(handle, request, out var bytesWritten) || bytesWritten != request.Length)
            return null;

        var reports = new List<byte[]>(maxReports);
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < maxReports; i++)
        {
            var remaining = timeout - Stopwatch.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
                break;

            var report = await ReadOneBoundedAsync(handle, reportLength, remaining, cancellationToken).ConfigureAwait(false);
            if (report is null)
                break;
            reports.Add(report);
        }

        return reports;
    }

    private async Task<byte[]?> ReadOneBoundedAsync(
        SafeFileHandle handle,
        int reportLength,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[reportLength];
        var readTask = Task.Run(() =>
        {
            var ok = _api.Read(handle, buffer, out var bytesRead);
            return (ok, bytesRead);
        });
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            var (succeeded, bytesRead) = await readTask.WaitAsync(timeoutSource.Token).ConfigureAwait(false);
            if (!succeeded || bytesRead != reportLength)
                return null;
            return buffer;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _api.CancelRead(handle);
            try { await readTask.ConfigureAwait(false); }
            catch { /* the bounded read already failed closed */ }
            return null;
        }
        catch (OperationCanceledException)
        {
            _api.CancelRead(handle);
            try { await readTask.ConfigureAwait(false); }
            catch { /* cancellation is propagated below */ }
            throw;
        }
    }

}

internal interface IMsiClawNativeHidApi
{
    int LastError { get; }
    SafeFileHandle Open(string devicePath, uint desiredAccess, uint shareMode, uint creationDisposition);
    bool Write(SafeFileHandle handle, byte[] buffer, out uint bytesWritten);
    void CancelWrite(SafeFileHandle handle) { }
    bool Read(SafeFileHandle handle, byte[] buffer, out uint bytesRead) { bytesRead = 0; return false; }
    void CancelRead(SafeFileHandle handle) { }

    /// <summary>
    /// Reads the true input/output report byte lengths and HID Usage/UsagePage for an opened HID
    /// interface via HidD_GetPreparsedData + HidP_GetCaps. This is the authoritative source for
    /// report lengths and collection usage -- DeviceInformation has no valid property keys for
    /// either.
    /// </summary>
    bool TryGetReportLengths(SafeFileHandle handle, out int inputReportLength, out int outputReportLength, out ushort usagePage, out ushort usage, out int hidStatus);
}

internal sealed class WindowsMsiClawNativeHidApi : IMsiClawNativeHidApi
{
    public int LastError { get; private set; }
    private int _writeThreadId;
    private readonly object _writeCancellationGate = new();
    private SafeFileHandle? _activeWriteHandle;
    private int _readThreadId;
    private readonly object _readCancellationGate = new();
    private SafeFileHandle? _activeReadHandle;

    public SafeFileHandle Open(string devicePath, uint desiredAccess, uint shareMode, uint creationDisposition)
    {
        var handle = CreateFileW(devicePath, desiredAccess, shareMode, IntPtr.Zero, creationDisposition, 0, IntPtr.Zero);
        LastError = handle.IsInvalid ? Marshal.GetLastWin32Error() : 0;
        return handle;
    }

    public bool Write(SafeFileHandle handle, byte[] buffer, out uint bytesWritten)
    {
        lock (_writeCancellationGate)
        {
            _writeThreadId = unchecked((int)GetCurrentThreadId());
            _activeWriteHandle = handle;
        }
        bool result;
        try { result = WriteFile(handle, buffer, (uint)buffer.Length, out bytesWritten, IntPtr.Zero); }
        finally
        {
            lock (_writeCancellationGate)
            {
                _writeThreadId = 0;
                _activeWriteHandle = null;
            }
        }
        LastError = result ? 0 : Marshal.GetLastWin32Error();
        return result;
    }

    public void CancelWrite(SafeFileHandle handle)
    {
        lock (_writeCancellationGate)
        {
            if (_writeThreadId == 0 || !ReferenceEquals(_activeWriteHandle, handle)) return;
            using var thread = OpenThread(ThreadTerminate, false, unchecked((uint)_writeThreadId));
            if (!thread.IsInvalid && !CancelSynchronousIo(thread))
                LastError = Marshal.GetLastWin32Error();
        }
    }

    public bool Read(SafeFileHandle handle, byte[] buffer, out uint bytesRead)
    {
        lock (_readCancellationGate)
        {
            _readThreadId = unchecked((int)GetCurrentThreadId());
            _activeReadHandle = handle;
        }
        bool result;
        try { result = ReadFile(handle, buffer, (uint)buffer.Length, out bytesRead, IntPtr.Zero); }
        finally
        {
            lock (_readCancellationGate)
            {
                _readThreadId = 0;
                _activeReadHandle = null;
            }
        }
        LastError = result ? 0 : Marshal.GetLastWin32Error();
        return result;
    }

    public void CancelRead(SafeFileHandle handle)
    {
        lock (_readCancellationGate)
        {
            if (_readThreadId == 0 || !ReferenceEquals(_activeReadHandle, handle)) return;
            using var thread = OpenThread(ThreadTerminate, false, unchecked((uint)_readThreadId));
            if (!thread.IsInvalid && !CancelSynchronousIo(thread))
                LastError = Marshal.GetLastWin32Error();
        }
    }

    public bool TryGetReportLengths(SafeFileHandle handle, out int inputReportLength, out int outputReportLength, out ushort usagePage, out ushort usage, out int hidStatus)
    {
        inputReportLength = 0;
        outputReportLength = 0;
        usagePage = 0;
        usage = 0;
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
            usagePage = caps.UsagePage;
            usage = caps.Usage;
            LastError = 0;
            return true;
        }
        finally
        {
            HidD_FreePreparsedData(preparsedData);
        }
    }

    private const int HidpStatusSuccess = 0x00110000;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(SafeFileHandle file, byte[] buffer, uint numberOfBytesToWrite, out uint numberOfBytesWritten, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(SafeFileHandle file, byte[] buffer, uint numberOfBytesToRead, out uint numberOfBytesRead, IntPtr overlapped);
    private const uint ThreadTerminate = 0x0001;
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle OpenThread(uint desiredAccess, bool inheritHandle, uint threadId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelSynchronousIo(SafeFileHandle threadHandle);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

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
