namespace SteamInputAddonforClaw.Feedback;

internal readonly record struct TwoMotorRumble(ushort LargeMotor, ushort SmallMotor)
{
    internal static TwoMotorRumble Stopped => default;
}

internal interface IPhysicalRumbleSink
{
    PhysicalRumbleWriteResult SetRumble(TwoMotorRumble rumble);

    // Best-effort cancellation of an in-flight physical write. Implementations that do not
    // have an underlying cancellable operation may safely leave this as a no-op.
    void CancelPendingWrite() { }
}
