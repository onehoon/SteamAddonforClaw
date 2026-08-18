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

    internal MsiClawRumbleSink(IMsiClawPhysicalInputIdentityProvider identityProvider, IMsiClawRumbleTransport transport, IMsiClawRumbleEndpointResolver? endpointResolver = null)
    {
        _identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _endpointResolver = endpointResolver ?? new MsiClawRumbleEndpointResolver();
    }

    public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
    {
        if (Volatile.Read(ref _disposed) != 0) return new(PhysicalRumbleWriteStatus.Disposed, "Disposed");
        lock (_sync)
        {
            if (!_admissionOpen) return new(PhysicalRumbleWriteStatus.Unavailable, "PhysicalSessionRetiring");
            var generation = _identityProvider.CurrentSessionGeneration;
            var identity = _identityProvider.CurrentIdentity;
            if (identity is null || string.IsNullOrWhiteSpace(identity.PhysicalIdentity))
                return new(PhysicalRumbleWriteStatus.Unavailable, "PhysicalIdentityUnavailable");
            var endpoint = _endpointResolver.Resolve(identity);
            if (!endpoint.IsAvailable) return new(PhysicalRumbleWriteStatus.Unavailable, endpoint.Reason);
            var current = _identityProvider.CurrentIdentity;
            if (_identityProvider.CurrentSessionGeneration != generation || !SameIdentity(identity, current))
                return new(PhysicalRumbleWriteStatus.Unavailable, "StalePhysicalSession");

            var large8 = MsiClawRumblePacketBuilder.ToPhysicalByte(rumble.LargeMotor);
            var small8 = MsiClawRumblePacketBuilder.ToPhysicalByte(rumble.SmallMotor);
            try
            {
                var result = _transport.Write(endpoint.DevicePath!, MsiClawRumblePacketBuilder.Build(rumble));
                if (result.Succeeded)
            {
                AppLog.Debug("Rumble", "Rumble TX", ("Device", "MSIClaw"), ("PID", 1902), ("Large16", rumble.LargeMotor), ("Small16", rumble.SmallMotor), ("Large8", large8), ("Small8", small8), ("Result", "OK"), ("WriteMs", result.WriteMs));
                return new(PhysicalRumbleWriteStatus.Succeeded, "OK");
            }

                AppLog.Warn("Rumble", "MSI rumble write failed", null, ("PID", 1902), ("Large8", large8), ("Small8", small8), ("Operation", result.Reason.StartsWith("Open", StringComparison.Ordinal) ? "Open" : "Write"), ("Reason", result.Reason), ("Win32Error", result.Win32Error));
                return new(PhysicalRumbleWriteStatus.Failed, result.Reason);
            }
            catch (Exception exception)
            {
                AppLog.Warn("Rumble", "MSI rumble write failed", exception, ("PID", 1902), ("Large8", large8), ("Small8", small8), ("Reason", "TransportException"));
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
        lock (_sync) _admissionOpen = false;
    }

    internal void BeginPhysicalSession()
    {
        lock (_sync) _admissionOpen = true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _transport.Dispose();
    }
}
