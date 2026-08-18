using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal readonly record struct MsiClawRumbleTransportResult(bool Succeeded, string Reason, int Win32Error = 0, double WriteMs = 0);

internal interface IMsiClawRumbleTransport : IDisposable
{
    MsiClawRumbleTransportResult Write(string devicePath, ReadOnlySpan<byte> packet);
}

internal sealed class WindowsMsiClawRumbleTransport : IMsiClawRumbleTransport
{
    private readonly IMsiClawNativeHidApi _api;
    private readonly Lock _sync = new();
    private SafeFileHandle? _handle;
    private string? _devicePath;
    private bool _disposed;

    internal WindowsMsiClawRumbleTransport(IMsiClawNativeHidApi? api = null) => _api = api ?? new WindowsMsiClawNativeHidApi();

    public MsiClawRumbleTransportResult Write(string devicePath, ReadOnlySpan<byte> packet)
    {
        lock (_sync)
        {
            if (_disposed) return new(false, "Disposed");
            if (string.IsNullOrWhiteSpace(devicePath)) return new(false, "EmptyDevicePath");
            if (packet.Length != 11) return new(false, "InvalidLength");

            if (!string.Equals(_devicePath, devicePath, StringComparison.OrdinalIgnoreCase))
                CloseHandleLocked();

            var openStarted = Stopwatch.GetTimestamp();
            if (_handle is null)
            {
                _handle = _api.Open(devicePath, 0x80000000 | 0x40000000, 0x00000001 | 0x00000002, 3);
                if (_handle.IsInvalid)
                {
                    var error = _api.LastError;
                    CloseHandleLocked();
                    return new(false, "OpenFailed", error, Stopwatch.GetElapsedTime(openStarted).TotalMilliseconds);
                }
                _devicePath = devicePath;
            }

            var bytes = packet.ToArray();
            var writeStarted = Stopwatch.GetTimestamp();
            if (!_api.Write(_handle, bytes, out var written))
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
