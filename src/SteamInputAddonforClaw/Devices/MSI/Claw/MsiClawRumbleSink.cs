using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Feedback;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class MsiClawRumbleSink : IPhysicalRumbleSink, IDisposable
{
    private readonly IMsiClawPhysicalInputIdentityProvider _identityProvider;
    private readonly IMsiClawRumbleEndpointResolver _endpointResolver;
    private readonly IMsiClawRumbleTransport _transport;
    private readonly Lock _sync = new();
    private int _disposed;
    private bool _admissionOpen = true;
    private bool _failureWarningEmitted;
    private long? _endpointGeneration;
    private MsiClawRumbleEndpointResolution _cachedEndpoint;
    private long? _lastWrittenGeneration;
    private TwoMotorRumble? _lastWrittenRumble;

    internal MsiClawRumbleSink(IMsiClawPhysicalInputIdentityProvider identityProvider, IMsiClawRumbleTransport transport, IMsiClawRumbleEndpointResolver? endpointResolver = null)
    {
        _identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _endpointResolver = endpointResolver ?? new MsiClawRumbleEndpointResolver();
    }

    public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
    {
        try { return SetRumbleCore(rumble); }
        catch (Exception exception)
        {
            try { AppLog.Debug("Rumble", "MSI rumble sink failure was contained.", ("Reason", exception.GetType().Name)); }
            catch { }
            return new(PhysicalRumbleWriteStatus.Failed, "SinkException");
        }
    }

    private PhysicalRumbleWriteResult SetRumbleCore(TwoMotorRumble rumble)
    {
        if (Volatile.Read(ref _disposed) != 0) return new(PhysicalRumbleWriteStatus.Disposed, "Disposed");
        MsiClawRumbleEndpointResolution endpoint;
        MsiClawPhysicalInputIdentity identity;
        long generation;
        bool needsResolve;
        lock (_sync)
        {
            if (!_admissionOpen && !rumble.Equals(TwoMotorRumble.Stopped)) return new(PhysicalRumbleWriteStatus.Unavailable, "PhysicalSessionRetiring");
            generation = _identityProvider.CurrentSessionGeneration;
            var currentIdentity = _identityProvider.CurrentIdentity;
            if (currentIdentity is null || string.IsNullOrWhiteSpace(currentIdentity.PhysicalIdentity))
                return new(PhysicalRumbleWriteStatus.Unavailable, "PhysicalIdentityUnavailable");
            identity = currentIdentity;
            needsResolve = _endpointGeneration != generation || !_cachedEndpoint.IsAvailable;
            endpoint = needsResolve ? default : _cachedEndpoint;
            if (!needsResolve && _lastWrittenGeneration == generation && _lastWrittenRumble is { } previous && previous.Equals(rumble))
                return new(PhysicalRumbleWriteStatus.Succeeded, "Unchanged");
        }

        if (needsResolve)
        {
            try { endpoint = _endpointResolver.Resolve(identity); }
            catch (Exception exception)
            {
                AppLog.Debug("Rumble", "MSI rumble endpoint resolution failed.", ("PID", 1902), ("PhysicalGeneration", generation), ("Reason", "EndpointResolutionException"), ("Exception", exception.GetType().Name));
                return new(PhysicalRumbleWriteStatus.Failed, "EndpointResolutionException");
            }
        }

        lock (_sync)
        {
            var current = _identityProvider.CurrentIdentity;
            if (_identityProvider.CurrentSessionGeneration != generation || !SameIdentity(identity, current))
                return new(PhysicalRumbleWriteStatus.Unavailable, "StalePhysicalSession");
            if (!_admissionOpen && !rumble.Equals(TwoMotorRumble.Stopped))
                return new(PhysicalRumbleWriteStatus.Unavailable, "PhysicalSessionRetiring");
            if (!endpoint.IsAvailable)
                return new(PhysicalRumbleWriteStatus.Unavailable, endpoint.Reason);
            if (needsResolve)
            {
                _cachedEndpoint = endpoint;
                _endpointGeneration = generation;
            }
            if (_lastWrittenGeneration == generation && _lastWrittenRumble is { } previous && previous.Equals(rumble))
                return new(PhysicalRumbleWriteStatus.Succeeded, "Unchanged");
            ResetLastWritten();
        }

        var large8 = MsiClawRumblePacketBuilder.ToPhysicalByte(rumble.LargeMotor);
        var small8 = MsiClawRumblePacketBuilder.ToPhysicalByte(rumble.SmallMotor);
        try
        {
            var result = _transport.Write(endpoint.DevicePath!, MsiClawRumblePacketBuilder.Build(rumble), endpoint.OutputReportLength);
            if (!result.Succeeded)
            {
                LogFailureOnce(result.Reason, large8, small8, result.Win32Error);
                return new(PhysicalRumbleWriteStatus.Failed, result.Reason);
            }
            lock (_sync)
            {
                if (_identityProvider.CurrentSessionGeneration != generation || !_admissionOpen && !rumble.Equals(TwoMotorRumble.Stopped))
                    return new(PhysicalRumbleWriteStatus.Unavailable, "StalePhysicalSession");
                _lastWrittenGeneration = generation;
                _lastWrittenRumble = rumble;
            }
            AppLog.Debug("Rumble", "Rumble TX", ("Device", "MSIClaw"), ("PID", 1902), ("Large16", rumble.LargeMotor), ("Small16", rumble.SmallMotor), ("Large8", large8), ("Small8", small8), ("Result", "OK"), ("WriteMs", result.WriteMs));
            return new(PhysicalRumbleWriteStatus.Succeeded, "OK");
        }
        catch (Exception exception)
        {
            LogFailureOnce("TransportException", large8, small8, 0, exception);
            return new(PhysicalRumbleWriteStatus.Failed, "TransportException");
        }
    }

    private static bool SameIdentity(MsiClawPhysicalInputIdentity expected, MsiClawPhysicalInputIdentity? actual) =>
        actual is not null && expected.InstanceGuid == actual.InstanceGuid &&
        string.Equals(expected.DevicePath, actual.DevicePath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.PnpInstanceId, actual.PnpInstanceId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.PhysicalIdentity, actual.PhysicalIdentity, StringComparison.OrdinalIgnoreCase);

    internal void InvalidatePhysicalSession()
    {
        try { _transport.InvalidatePhysicalSession(); }
        catch (Exception exception)
        {
            try { AppLog.Debug("Rumble", "MSI rumble invalidation failure was contained.", ("Reason", exception.GetType().Name)); }
            catch { }
        }
    }

    public void CancelPendingWrite()
    {
        try { _transport.CancelPendingWrite(); }
        catch (Exception exception)
        {
            try { AppLog.Debug("Rumble", "MSI rumble cancellation failure was contained.", ("Reason", exception.GetType().Name)); }
            catch { }
        }
    }

    internal void BeginPhysicalSessionRetirement()
    {
        Volatile.Write(ref _admissionOpen, false);
    }

    internal void BeginPhysicalSession()
    {
        lock (_sync)
        {
            _admissionOpen = true;
            ResetLastWritten();
            _failureWarningEmitted = false;
            _endpointGeneration = null;
            _cachedEndpoint = default;
        }
    }

    private void ResetLastWritten()
    {
        _lastWrittenGeneration = null;
        _lastWrittenRumble = null;
    }

    private void LogFailureOnce(string reason, byte large8, byte small8, int win32Error, Exception? exception = null)
    {
        if (_failureWarningEmitted) return;
        _failureWarningEmitted = true;
        AppLog.Warn("Rumble", "MSI rumble write failed", exception, ("PID", 1902), ("Large8", large8), ("Small8", small8), ("Operation", reason.StartsWith("Open", StringComparison.Ordinal) ? "Open" : "Write"), ("Reason", reason), ("Win32Error", win32Error));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _transport.Dispose();
    }
}
