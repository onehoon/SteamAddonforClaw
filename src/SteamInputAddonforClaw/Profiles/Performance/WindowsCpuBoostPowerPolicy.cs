using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Profiles.Performance;

/// <summary>
/// Direct <c>PowrProf.dll</c> interop for the Windows "Processor Performance Boost Mode"
/// (<c>PERFBOOSTMODE</c>) setting under the <c>SUB_PROCESSOR</c> subgroup, for the current active
/// power scheme. This is the only type in the CPU Boost feature that talks to PowrProf; everything
/// above it (<see cref="CpuBoostRuntime"/>) goes through <see cref="ICpuBoostPowerPolicy"/>.
///
/// Uses classic <c>[DllImport]</c> P/Invoke to match the existing PowrProf interop in
/// <see cref="SteamInputAddonforClaw.Power.WindowsSuspendResumeNotificationSource"/>.
/// Never uses <c>powercfg.exe</c>, the registry, WMI, or PowerShell (work order section 3) and
/// never modifies the setting's hidden-attribute registry flag (section 18).
/// </summary>
internal sealed class WindowsCpuBoostPowerPolicy : ICpuBoostPowerPolicy
{
    // SUB_PROCESSOR: processor power management subgroup.
    private static readonly Guid SubProcessorGuid = new("54533251-82be-4824-96c1-47b60b740d00");

    // PERFBOOSTMODE: Processor Performance Boost Mode.
    private static readonly Guid PerfBoostModeGuid = new("be337238-0d82-4146-a960-4f3749d470c7");

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

    public CpuBoostSystemState Read()
    {
        if (!TryGetActiveScheme(out var scheme, out var failure))
            return CpuBoostSystemState.Failure(failure!);

        var ac = ReadSide(scheme, isAc: true);
        var dc = ReadSide(scheme, isAc: false);
        return new CpuBoostSystemState(true, ac, dc, null);
    }

    public CpuBoostApplyResult Apply(CpuBoostMode? ac, CpuBoostMode? dc)
    {
        if (ac is null && dc is null) return CpuBoostApplyResult.NoOp;

        if (!TryGetActiveScheme(out var scheme, out var failure))
            return new CpuBoostApplyResult(ac is null, dc is null, failure);

        var acSucceeded = true;
        var dcSucceeded = true;
        string? failureMessage = null;

        if (ac is { } acMode)
        {
            var subGroup = SubProcessorGuid;
            var setting = PerfBoostModeGuid;
            var schemeForCall = scheme;
            var result = PowerWriteACValueIndex(0, ref schemeForCall, ref subGroup, ref setting, (uint)acMode);
            acSucceeded = result == 0;
            if (!acSucceeded)
            {
                failureMessage = $"PowerWriteACValueIndex failed (Win32 error {result}).";
                AppLog.Warn("Profiles.CpuBoost", "CPU Boost AC write failed.", null, ("Win32Error", result));
            }
        }

        if (dc is { } dcMode)
        {
            var subGroup = SubProcessorGuid;
            var setting = PerfBoostModeGuid;
            var schemeForCall = scheme;
            var result = PowerWriteDCValueIndex(0, ref schemeForCall, ref subGroup, ref setting, (uint)dcMode);
            dcSucceeded = result == 0;
            if (!dcSucceeded)
            {
                failureMessage ??= $"PowerWriteDCValueIndex failed (Win32 error {result}).";
                AppLog.Warn("Profiles.CpuBoost", "CPU Boost DC write failed.", null, ("Win32Error", result));
            }
        }

        // Re-activate the same scheme so Windows applies the newly written value(s) -- writing
        // alone does not take effect (work order section 3/16). Only meaningful if at least one
        // write actually succeeded; still safe/idempotent to call otherwise.
        if (acSucceeded || dcSucceeded)
        {
            var schemeForActivate = scheme;
            var activateResult = PowerSetActiveScheme(0, ref schemeForActivate);
            if (activateResult != 0)
            {
                failureMessage ??= $"PowerSetActiveScheme failed (Win32 error {activateResult}).";
                AppLog.Warn("Profiles.CpuBoost", "CPU Boost active-scheme re-activation failed.", null, ("Win32Error", activateResult));
            }
        }

        return new CpuBoostApplyResult(acSucceeded, dcSucceeded, failureMessage);
    }

    private static bool TryGetActiveScheme(out Guid scheme, out string? failure)
    {
        var result = PowerGetActiveScheme(0, out var schemePointer);
        if (result != 0 || schemePointer == 0)
        {
            scheme = default;
            failure = $"PowerGetActiveScheme failed (Win32 error {result}).";
            AppLog.Warn("Profiles.CpuBoost", "PowerGetActiveScheme failed.", null, ("Win32Error", result));
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
            // PowerGetActiveScheme allocates the returned buffer; the caller owns freeing it with
            // LocalFree (work order section 3).
            LocalFree(schemePointer);
        }
    }

    private static CpuBoostSideReading ReadSide(Guid scheme, bool isAc)
    {
        var schemeForCall = scheme;
        var subGroup = SubProcessorGuid;
        var setting = PerfBoostModeGuid;
        var result = isAc
            ? PowerReadACValueIndex(0, ref schemeForCall, ref subGroup, ref setting, out var value)
            : PowerReadDCValueIndex(0, ref schemeForCall, ref subGroup, ref setting, out value);

        if (result != 0)
        {
            AppLog.Warn("Profiles.CpuBoost", "CPU Boost current-state read failed.", null, ("Side", isAc ? "AC" : "DC"), ("Win32Error", result));
            return CpuBoostSideReading.Unavailable;
        }

        // Never normalize an unmapped raw value onto a known mode (work order section 17): the
        // known/supported set is exactly 0..6.
        return value <= 6
            ? CpuBoostSideReading.Known((CpuBoostMode)value)
            : CpuBoostSideReading.UnknownValue();
    }
}
