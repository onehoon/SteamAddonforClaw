using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Feedback;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class MsiClawRumbleSink : IPhysicalRumbleSink, IDisposable
{
    private readonly IMsiClawPhysicalInputIdentityProvider _identityProvider;
    private readonly IMsiClawRumbleTransport _transport;
    private int _disposed;

    internal MsiClawRumbleSink(IMsiClawPhysicalInputIdentityProvider identityProvider, IMsiClawRumbleTransport transport)
    {
        _identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble)
    {
        if (Volatile.Read(ref _disposed) != 0) return new(PhysicalRumbleWriteStatus.Disposed, "Disposed");
        var identity = _identityProvider.CurrentIdentity;
        if (identity is null || string.IsNullOrWhiteSpace(identity.DevicePath) || string.IsNullOrWhiteSpace(identity.PhysicalIdentity))
            return new(PhysicalRumbleWriteStatus.Unavailable, "PhysicalIdentityUnavailable");

        var large8 = MsiClawRumblePacketBuilder.ToPhysicalByte(rumble.LargeMotor);
        var small8 = MsiClawRumblePacketBuilder.ToPhysicalByte(rumble.SmallMotor);
        try
        {
            var result = _transport.Write(identity.DevicePath, MsiClawRumblePacketBuilder.Build(rumble));
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _transport.Dispose();
    }
}
