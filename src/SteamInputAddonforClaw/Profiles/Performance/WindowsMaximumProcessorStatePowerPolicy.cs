using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Profiles.Performance;

/// <summary>Direct PowrProf access for PROCTHROTTLEMAX in the current active scheme. This feature
/// intentionally does not touch PROCTHROTTLEMAX1 or use powercfg, registry, WMI, or PowerShell.</summary>
internal sealed class WindowsMaximumProcessorStatePowerPolicy : IMaximumProcessorStatePowerPolicy
{
    private static readonly Guid SubProcessorGuid = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid MaximumProcessorStateGuid = new("bc5038f7-23e0-4960-96da-33abaf5935ec");

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(nint userRootPowerKey, out nint activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(nint userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(nint rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(nint rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(nint rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(nint rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, uint value);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint handle);

    public MaximumProcessorStateSystemState Read()
    {
        if (!TryGetActiveScheme(out var scheme, out var failure))
            return MaximumProcessorStateSystemState.Failure(failure!);

        var ac = ReadSide(scheme, isAc: true);
        var dc = ReadSide(scheme, isAc: false);
        var failures = new List<string>(2);
        if (ac.Status == MaximumProcessorStateReadStatus.Unavailable) failures.Add("AC");
        if (dc.Status == MaximumProcessorStateReadStatus.Unavailable) failures.Add("DC");
        return new MaximumProcessorStateSystemState(ac, dc,
            failures.Count == 0 ? null : $"The Windows maximum processor state could not be read for {string.Join(" and ", failures)}.");
    }

    public MaximumProcessorStateApplyResult Apply(int? ac, int? dc)
    {
        if (ac is null && dc is null) return MaximumProcessorStateApplyResult.NoOp;
        if ((ac is not null && !IsAllowed(ac.Value)) || (dc is not null && !IsAllowed(dc.Value)))
            return new MaximumProcessorStateApplyResult(ac is null, dc is null, "Only 99% and 100% are supported.");

        if (!TryGetActiveScheme(out var scheme, out var failure))
            return new MaximumProcessorStateApplyResult(ac is null, dc is null, failure);

        uint? acResult = null;
        if (ac is { } acPercent)
        {
            var schemeForCall = scheme;
            var subGroup = SubProcessorGuid;
            var setting = MaximumProcessorStateGuid;
            acResult = PowerWriteACValueIndex(0, ref schemeForCall, ref subGroup, ref setting, (uint)acPercent);
            if (acResult != 0)
                AppLog.Warn("Profiles.MaximumProcessorState", "Maximum processor state AC write failed.", null, ("Win32Error", acResult));
        }

        uint? dcResult = null;
        if (dc is { } dcPercent)
        {
            var schemeForCall = scheme;
            var subGroup = SubProcessorGuid;
            var setting = MaximumProcessorStateGuid;
            dcResult = PowerWriteDCValueIndex(0, ref schemeForCall, ref subGroup, ref setting, (uint)dcPercent);
            if (dcResult != 0)
                AppLog.Warn("Profiles.MaximumProcessorState", "Maximum processor state DC write failed.", null, ("Win32Error", dcResult));
        }

        uint ActivateScheme()
        {
            var schemeForActivate = scheme;
            var result = PowerSetActiveScheme(0, ref schemeForActivate);
            if (result != 0)
                AppLog.Warn("Profiles.MaximumProcessorState", "Maximum processor state active-scheme re-activation failed.", null, ("Win32Error", result));
            return result;
        }

        return ResolveApplyResult(ac, acResult, dc, dcResult, ActivateScheme);
    }

    internal static bool IsAllowed(int percent) => percent is 99 or 100;

    internal static MaximumProcessorStateApplyResult ResolveApplyResult(
        int? ac, uint? acWriteResult, int? dc, uint? dcWriteResult, Func<uint> activateScheme)
    {
        var acSucceeded = ac is null || acWriteResult == 0;
        var dcSucceeded = dc is null || dcWriteResult == 0;
        var anyRequestedWriteSucceeded = (ac is not null && acSucceeded) || (dc is not null && dcSucceeded);

        string? failure = null;
        if (ac is not null && !acSucceeded) failure = $"PowerWriteACValueIndex failed (Win32 error {acWriteResult}).";
        if (dc is not null && !dcSucceeded) failure ??= $"PowerWriteDCValueIndex failed (Win32 error {dcWriteResult}).";

        if (anyRequestedWriteSucceeded)
        {
            var activateResult = activateScheme();
            if (activateResult != 0)
            {
                failure ??= $"PowerSetActiveScheme failed (Win32 error {activateResult}).";
                if (ac is not null) acSucceeded = false;
                if (dc is not null) dcSucceeded = false;
            }
        }

        return new MaximumProcessorStateApplyResult(acSucceeded, dcSucceeded, failure);
    }

    private static bool TryGetActiveScheme(out Guid scheme, out string? failure)
    {
        var result = PowerGetActiveScheme(0, out var schemePointer);
        if (result != 0 || schemePointer == 0)
        {
            scheme = default;
            failure = $"PowerGetActiveScheme failed (Win32 error {result}).";
            AppLog.Warn("Profiles.MaximumProcessorState", "PowerGetActiveScheme failed.", null, ("Win32Error", result));
            return false;
        }

        try
        {
            scheme = Marshal.PtrToStructure<Guid>(schemePointer);
            failure = null;
            return true;
        }
        finally
        {
            LocalFree(schemePointer);
        }
    }

    private static MaximumProcessorStateSideReading ReadSide(Guid scheme, bool isAc)
    {
        var schemeForCall = scheme;
        var subGroup = SubProcessorGuid;
        var setting = MaximumProcessorStateGuid;
        uint value;
        var result = isAc
            ? PowerReadACValueIndex(0, ref schemeForCall, ref subGroup, ref setting, out value)
            : PowerReadDCValueIndex(0, ref schemeForCall, ref subGroup, ref setting, out value);

        if (result != 0)
        {
            AppLog.Warn("Profiles.MaximumProcessorState", "Maximum processor state current-value read failed.", null,
                ("Side", isAc ? "AC" : "DC"), ("Win32Error", result));
            return MaximumProcessorStateSideReading.Unavailable;
        }

        return MaximumProcessorStateSideReading.Known(value);
    }
}
