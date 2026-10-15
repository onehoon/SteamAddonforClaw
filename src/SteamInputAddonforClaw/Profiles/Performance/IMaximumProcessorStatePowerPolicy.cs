namespace SteamInputAddonforClaw.Profiles.Performance;

internal enum MaximumProcessorStateReadStatus
{
    Known,
    Unavailable
}

internal readonly record struct MaximumProcessorStateSideReading(MaximumProcessorStateReadStatus Status, uint? Percent)
{
    public static readonly MaximumProcessorStateSideReading Unavailable = new(MaximumProcessorStateReadStatus.Unavailable, null);
    public static MaximumProcessorStateSideReading Known(uint percent) => new(MaximumProcessorStateReadStatus.Known, percent);
}

internal readonly record struct MaximumProcessorStateSystemState(
    MaximumProcessorStateSideReading Ac,
    MaximumProcessorStateSideReading Dc,
    string? FailureMessage)
{
    public static MaximumProcessorStateSystemState Failure(string message)
        => new(MaximumProcessorStateSideReading.Unavailable, MaximumProcessorStateSideReading.Unavailable, message);
}

internal readonly record struct MaximumProcessorStateApplyResult(bool AcSucceeded, bool DcSucceeded, string? FailureMessage)
{
    public static readonly MaximumProcessorStateApplyResult NoOp = new(true, true, null);
    public bool Succeeded => AcSucceeded && DcSucceeded;
}

/// <summary>Narrow boundary around the Windows maximum-processor-state power setting.</summary>
internal interface IMaximumProcessorStatePowerPolicy
{
    MaximumProcessorStateSystemState Read();
    MaximumProcessorStateApplyResult Apply(int? ac, int? dc);
}
