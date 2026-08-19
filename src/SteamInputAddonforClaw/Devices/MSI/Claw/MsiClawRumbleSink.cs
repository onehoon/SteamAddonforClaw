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
        lock (_sync)
        {
            if (!_admissionOpen) return new(PhysicalRumbleWriteStatus.Unavailable, "PhysicalSessionRetiring");
            var generation = _identityProvider.CurrentSessionGeneration;
            var identity = _identityProvider.CurrentIdentity;
            if (identity is null || string.IsNullOrWhiteSpace(identity.PhysicalIdentity))
                return new(PhysicalRumbleWriteStatus.Unavailable, "PhysicalIdentityUnavailable");
            var generationEndpoint = _identityProvider.CurrentSessionGeneration;
            MsiClawRumbleEndpointResolution endpoint;
            try
            {
                endpoint = _endpointGeneration == generationEndpoint && _cachedEndpoint.IsAvailable
                    ? _cachedEndpoint
                    : _endpointResolver.Resolve(identity);
            }
            catch (Exception exception)
            {
                AppLog.Debug("Rumble", "MSI rumble endpoint resolution failed.", ("PID", 1902), ("PhysicalGeneration", generationEndpoint), ("Reason", "EndpointResolutionException"), ("Exception", exception.GetType().Name), ("HResult", exception.HResult), ("Message", exception.Message));
                return new(PhysicalRumbleWriteStatus.Failed, "EndpointResolutionException");
            }
            if (endpoint.IsAvailable)
            {
                if (!_cachedEndpoint.Equals(endpoint))
                {
                    AppLog.Info("Rumble", "Rumble endpoint selected", ("PID", 1902), ("OutputReportLength", endpoint.OutputReportLength));
                }
                _cachedEndpoint = endpoint;
                _endpointGeneration = generationEndpoint;
            }
            if (!endpoint.IsAvailable)
            {
                AppLog.Debug("Rumble", "MSI rumble endpoint unavailable.", ("PID", 1902), ("PhysicalGeneration", generationEndpoint), ("Reason", endpoint.Reason));
                return new(PhysicalRumbleWriteStatus.Unavailable, endpoint.Reason);
            }
            var current = _identityProvider.CurrentIdentity;
            if (_identityProvider.CurrentSessionGeneration != generation || !SameIdentity(identity, current))
                return new(PhysicalRumbleWriteStatus.Unavailable, "StalePhysicalSession");

            var large8 = MsiClawRumblePacketBuilder.ToPhysicalByte(rumble.LargeMotor);
            var small8 = MsiClawRumblePacketBuilder.ToPhysicalByte(rumble.SmallMotor);
            try
            {
                var result = _transport.Write(endpoint.DevicePath!, MsiClawRumblePacketBuilder.Build(rumble), endpoint.OutputReportLength);
                if (result.Succeeded)
            {
                AppLog.Debug("Rumble", "Rumble TX", ("Device", "MSIClaw"), ("PID", 1902), ("Large16", rumble.LargeMotor), ("Small16", rumble.SmallMotor), ("Large8", large8), ("Small8", small8), ("Result", "OK"), ("WriteMs", result.WriteMs));
                return new(PhysicalRumbleWriteStatus.Succeeded, "OK");
            }

                LogFailureOnce(result.Reason, large8, small8, result.Win32Error);
                return new(PhysicalRumbleWriteStatus.Failed, result.Reason);
            }
            catch (Exception exception)
            {
                LogFailureOnce("TransportException", large8, small8, 0, exception);
                return new(PhysicalRumbleWriteStatus.Failed, "TransportException");
            }
        }
    }

    private static bool SameIdentity(MsiClawPhysicalInputIdentity expected, MsiClawPhysicalInputIdentity? actual) =>
        actual is not null && expected.InstanceGuid == actual.InstanceGuid &&
        string.Equals(expected.DevicePath, actual.DevicePath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.PnpInstanceId, actual.PnpInstanceId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.PhysicalIdentity, actual.PhysicalIdentity, StringComparison.OrdinalIgnoreCase);

    internal void InvalidatePhysicalSession()
    {
        lock (_sync) _transport.InvalidatePhysicalSession();
    }

    internal void BeginPhysicalSessionRetirement()
    {
        lock (_sync)
        {
            _admissionOpen = false;
            _failureWarningEmitted = false;
            _endpointGeneration = null;
            _cachedEndpoint = default;
            _transport.InvalidatePhysicalSession();
        }
    }

    internal void BeginPhysicalSession()
    {
        lock (_sync)
        {
            _admissionOpen = true;
            _failureWarningEmitted = false;
            _endpointGeneration = null;
            _cachedEndpoint = default;
        }
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
