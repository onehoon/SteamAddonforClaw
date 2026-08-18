namespace SteamInputAddonforClaw.Feedback;

internal readonly record struct TwoMotorRumble(ushort LargeMotor, ushort SmallMotor)
{
    internal static TwoMotorRumble Stopped => default;
}

internal interface IPhysicalRumbleSink
{
    void SetRumble(TwoMotorRumble rumble);
}
