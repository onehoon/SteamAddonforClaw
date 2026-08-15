using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SteamInputAddonforClaw.Diagnostics.GordonDPad;

/// <summary>
/// Reads raw input reports from an already-resolved Gordon HID device path, read-only, sharing the
/// device with any other reader (Steam included) rather than requesting exclusive access. Abstracted
/// behind this interface so <see cref="GordonDPadDiagnosticSession"/> can be tested without a real HID
/// device present.
/// </summary>
internal interface IDirectHidReader : IAsyncDisposable
{
    /// <summary>True once <see cref="OpenAsync"/> has successfully opened the device.</summary>
    bool IsOpen { get; }

    /// <summary>
    /// Opens the device read-only, sharing read/write access with any other process (never requests
    /// exclusive access -- Steam may already have this device open). Returns false (does not throw) on
    /// any failure to open; this is a diagnostic probe, not a routing-critical operation.
    /// </summary>
    Task<bool> OpenAsync();

    /// <summary>
    /// Reads one report at a time in a loop, invoking <paramref name="onReport"/> for each, until
    /// <paramref name="cancellationToken"/> is canceled, the device disconnects, or a read fails. Never
    /// throws out of this method for an ordinary disconnect/cancellation; invokes
    /// <paramref name="onFault"/> once for any other unexpected failure so the caller can report status
    /// without this method itself throwing on a background thread.
    /// </summary>
    Task RunAsync(Action<byte[]> onReport, Action<Exception> onFault, CancellationToken cancellationToken);
}

internal sealed class Win32DirectHidReader(string devicePath, int reportLength) : IDirectHidReader
{
    private SafeFileHandle? _handle;
    private FileStream? _stream;

    public bool IsOpen => _stream is not null;

    public Task<bool> OpenAsync()
    {
        try
        {
            var handle = NativeMethods.CreateFile(devicePath, NativeConstants.GenericRead, FileShare.Read | FileShare.Write, IntPtr.Zero, FileMode.Open, NativeConstants.FileFlagOverlapped, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                return Task.FromResult(false);
            }
            _handle = handle;
            // isAsync: true over a FILE_FLAG_OVERLAPPED handle gives genuine IOCP-backed async reads via
            // ReadAsync -- no manual OVERLAPPED/GetOverlappedResult bookkeeping needed.
            _stream = new FileStream(handle, FileAccess.Read, Math.Max(reportLength, GordonHidReportParser.ExpectedLength), isAsync: true);
            return Task.FromResult(true);
        }
        catch
        {
            _handle?.Dispose();
            _handle = null;
            _stream = null;
            return Task.FromResult(false);
        }
    }

    public async Task RunAsync(Action<byte[]> onReport, Action<Exception> onFault, CancellationToken cancellationToken)
    {
        if (_stream is null) return;
        var buffer = new byte[Math.Max(reportLength, GordonHidReportParser.ExpectedLength)];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await _stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read <= 0) return; // 0-length read: device closed/disconnected.
                onReport(buffer.AsSpan(0, read).ToArray());
            }
        }
        catch (OperationCanceledException)
        {
            // Ordinary cancellation (diagnostic Stop, routing exit, shutdown) -- not a fault.
        }
        catch (ObjectDisposedException)
        {
            // The stream/handle was disposed concurrently (e.g. device removal handled elsewhere) -- not
            // a fault to report; the caller already knows the reader is going away.
        }
        catch (Exception exception)
        {
            try { onFault(exception); } catch { /* a bad fault handler must not crash the read loop teardown */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        var stream = _stream;
        _stream = null;
        if (stream is not null)
        {
            try { await stream.DisposeAsync().ConfigureAwait(false); }
            catch { /* best-effort close */ }
        }
        _handle?.Dispose();
        _handle = null;
    }

    private static class NativeConstants
    {
        internal const uint GenericRead = 0x80000000;
        internal const FileOptions FileFlagOverlapped = (FileOptions)0x40000000;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(string name, uint access, FileShare share, IntPtr security, FileMode disposition, FileOptions flags, IntPtr template);
    }
}
