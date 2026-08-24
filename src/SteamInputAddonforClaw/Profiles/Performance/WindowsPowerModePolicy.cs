using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Profiles.Performance;

internal sealed class WindowsPowerModePolicy : IPowerModePolicy
{
    private static readonly Guid Efficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid Performance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    [DllImport("powrprof.dll")] private static extern uint PowerGetUserConfiguredACPowerMode(out Guid mode);
    [DllImport("powrprof.dll")] private static extern uint PowerSetUserConfiguredACPowerMode(ref Guid mode);
    [DllImport("powrprof.dll")] private static extern uint PowerGetUserConfiguredDCPowerMode(out Guid mode);
    [DllImport("powrprof.dll")] private static extern uint PowerSetUserConfiguredDCPowerMode(ref Guid mode);

    public PowerModeSystemState Read()
    {
        var acResult = PowerGetUserConfiguredACPowerMode(out var acGuid);
        var dcResult = PowerGetUserConfiguredDCPowerMode(out var dcGuid);
        if (acResult != 0) LogReadFailure("AC", acResult);
        if (dcResult != 0) LogReadFailure("DC", dcResult);
        var ac = acResult == 0 ? Map(acGuid, "AC") : PowerModeSideReading.Unavailable;
        var dc = dcResult == 0 ? Map(dcGuid, "DC") : PowerModeSideReading.Unavailable;
        if (acResult != 0 || dcResult != 0)
            return new(false, ac, dc, $"Power Mode read failed (AC={acResult}, DC={dcResult}).");
        return new(true, ac, dc, null);
    }

    public PowerModeApplyResult Apply(WindowsPowerMode? ac, WindowsPowerMode? dc)
    {
        uint? acResult = ac is { } a ? SetAc(a) : null;
        uint? dcResult = dc is { } d ? SetDc(d) : null;
        if (ac is not null && acResult != 0) LogWriteFailure("AC", acResult.Value);
        if (dc is not null && dcResult != 0) LogWriteFailure("DC", dcResult.Value);
        var acOk = ac is null || acResult == 0;
        var dcOk = dc is null || dcResult == 0;
        return new(acOk, dcOk, !acOk ? $"PowerSetUserConfiguredACPowerMode failed (Win32 error {acResult})." : !dcOk ? $"PowerSetUserConfiguredDCPowerMode failed (Win32 error {dcResult})." : null);
    }

    internal static PowerModeSideReading Map(Guid guid, string side)
    {
        if (guid == Guid.Empty) return new(PowerModeReadStatus.Known, WindowsPowerMode.Balanced);
        if (guid == Efficiency) return new(PowerModeReadStatus.Known, WindowsPowerMode.BestPowerEfficiency);
        if (guid == Performance) return new(PowerModeReadStatus.Known, WindowsPowerMode.BestPerformance);
        AppLog.Warn("Profiles.PowerMode", "Unknown Windows Power Mode GUID.", null, ("Side", side), ("Guid", guid));
        return new(PowerModeReadStatus.Unknown, null);
    }

    internal static Guid ToGuid(WindowsPowerMode mode) => mode switch
    {
        WindowsPowerMode.BestPowerEfficiency => Efficiency,
        WindowsPowerMode.Balanced => Guid.Empty,
        WindowsPowerMode.BestPerformance => Performance,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
    internal static void LogReadFailure(string side, uint error) =>
        AppLog.Warn("Profiles.PowerMode", $"Power Mode {side} read failed.", null, ("Side", side), ("Win32Error", error));
    internal static void LogWriteFailure(string side, uint error) =>
        AppLog.Warn("Profiles.PowerMode", $"Power Mode {side} write failed.", null, ("Side", side), ("Win32Error", error));
    private static uint SetAc(WindowsPowerMode mode) { var guid = ToGuid(mode); return PowerSetUserConfiguredACPowerMode(ref guid); }
    private static uint SetDc(WindowsPowerMode mode) { var guid = ToGuid(mode); return PowerSetUserConfiguredDCPowerMode(ref guid); }
}
