using SteamInputAddonforClaw.Contracts.DeviceProfiles;

namespace SteamInputAddonforClaw.Profiles.Performance;

public enum PowerModeReadStatus { Known, Unknown, Unavailable }
internal readonly record struct PowerModeSideReading(PowerModeReadStatus Status, WindowsPowerMode? Mode)
{
    public static PowerModeSideReading Unavailable => new(PowerModeReadStatus.Unavailable, null);
}
internal readonly record struct PowerModeSystemState(bool Succeeded, PowerModeSideReading Ac, PowerModeSideReading Dc, string? FailureMessage);
internal readonly record struct PowerModeApplyResult(bool AcSucceeded, bool DcSucceeded, string? FailureMessage)
{
    public static PowerModeApplyResult NoOp => new(true, true, null);
    public bool Succeeded => AcSucceeded && DcSucceeded;
}
internal interface IPowerModePolicy
{
    PowerModeSystemState Read();
    PowerModeApplyResult Apply(WindowsPowerMode? ac, WindowsPowerMode? dc);
}
