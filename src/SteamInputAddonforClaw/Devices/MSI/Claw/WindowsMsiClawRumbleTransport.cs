using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal readonly record struct MsiClawRumbleTransportResult(bool Succeeded, string Reason, int Win32Error = 0, double WriteMs = 0);

internal interface IMsiClawRumbleTransport : IDisposable
{
    /// <param name="outputReportLength">The resolved endpoint's real HID OutputReportLength --
    /// the native write buffer is sized to exactly this, never a fixed constant (proven hardware
    /// reports 32 bytes for the real PID1902 gamepad collection, not 64).</param>
    MsiClawRumbleTransportResult Write(string devicePath, ReadOnlySpan<byte> semanticPacket, int outputReportLength);
    void InvalidatePhysicalSession();
    void CancelPendingWrite() { }
}

internal sealed class WindowsMsiClawRumbleTransport : IMsiClawRumbleTransport
{
    private static readonly TimeSpan PhysicalWriteTimeout = TimeSpan.FromMilliseconds(250);
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareReadWrite = 0x00000001 | 0x00000002;
    private const uint OpenExisting = 3;

    private readonly IMsiClawNativeHidApi _api;
    private readonly object _sync = new();
    private SafeFileHandle? _handle;
    private string? _devicePath;
    private bool _disposed;

    internal Action? WriteRequested { get; set; }
    internal Action? InvalidationRequested { get; set; }

    internal WindowsMsiClawRumbleTransport(IMsiClawNativeHidApi? api = null) => _api = api ?? new WindowsMsiClawNativeHidApi();

    public MsiClawRumbleTransportResult Write(string devicePath, ReadOnlySpan<byte> semanticPacket, int outputReportLength)
    {
        WriteRequested?.Invoke();
        lock (_sync)
        {
            if (_disposed) return new(false, "Disposed");
            if (string.IsNullOrWhiteSpace(devicePath)) return new(false, "EmptyDevicePath");
            if (semanticPacket.Length != 11) return new(false, "InvalidSemanticLength");
            if (outputReportLength <= 0 || outputReportLength < semanticPacket.Length) return new(false, "InvalidOutputReportLength");

            if (!string.Equals(_devicePath, devicePath, StringComparison.OrdinalIgnoreCase))
                CloseHandleLocked();

            var openStarted = Stopwatch.GetTimestamp();
            if (_handle is null)
            {
                // Normal synchronous HID Open/Write, matching the ClawTweaks-proven behavior --
                // no overlapped I/O, no event/wait/cancel machinery. Some MSI HID collections deny
                // GENERIC_READ while still allowing output writes (ClawButtonMonitor.SharedHidWrite
                // retries write-only for exactly this reason), so fall back to GENERIC_WRITE only
                // before giving up.
                _handle = _api.Open(devicePath, GenericRead | GenericWrite, ShareReadWrite, OpenExisting);
                if (_handle.IsInvalid)
                {
                    _handle.Dispose();
                    _handle = _api.Open(devicePath, GenericWrite, ShareReadWrite, OpenExisting);
                }
                if (_handle.IsInvalid)
                {
                    var error = _api.LastError;
                    CloseHandleLocked();
                    return new(false, "OpenFailed", error, Stopwatch.GetElapsedTime(openStarted).TotalMilliseconds);
                }
                _devicePath = devicePath;
            }

            // Buffer sized to the endpoint's actual OutputReportLength -- never a fixed constant.
            var bytes = new byte[outputReportLength];
            semanticPacket.CopyTo(bytes);
            var writeStarted = Stopwatch.GetTimestamp();
            // Do not hold the transport state lock while native I/O is pending. Retirement must
            // be able to reach the native cancellation seam while this call is blocked.
            var pendingHandle = _handle;
            Monitor.Exit(_sync);
            using var watchdogCancellation = new CancellationTokenSource();
            var watchdog = CancelAfterDeadlineAsync(pendingHandle, watchdogCancellation.Token);
            bool writeSucceeded;
            uint written;
            try { writeSucceeded = _api.Write(pendingHandle, bytes, out written); }
            finally
            {
                watchdogCancellation.Cancel();
                try { watchdog.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
                Monitor.Enter(_sync);
            }
            if (!writeSucceeded)
            {
                var error = _api.LastError;
                CloseHandleLocked();
                return new(false, "WriteFailed", error, Stopwatch.GetElapsedTime(writeStarted).TotalMilliseconds);
            }
            if (written != bytes.Length)
            {
                CloseHandleLocked();
                return new(false, "PartialWrite", 0, Stopwatch.GetElapsedTime(writeStarted).TotalMilliseconds);
            }

            return new(true, "OK", 0, Stopwatch.GetElapsedTime(writeStarted).TotalMilliseconds);
        }
    }

    public void InvalidatePhysicalSession()
    {
        InvalidationRequested?.Invoke();
        lock (_sync)
            CloseHandleLocked();
    }

    private async Task CancelAfterDeadlineAsync(SafeFileHandle handle, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(PhysicalWriteTimeout, cancellationToken).ConfigureAwait(false);
            _api.CancelWrite(handle);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public void CancelPendingWrite()
    {
        SafeFileHandle? handle;
        lock (_sync) handle = _handle;
        if (handle is not null && !handle.IsInvalid) _api.CancelWrite(handle);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            CloseHandleLocked();
        }
    }

    private void CloseHandleLocked()
    {
        _handle?.Dispose();
        _handle = null;
        _devicePath = null;
    }
}
