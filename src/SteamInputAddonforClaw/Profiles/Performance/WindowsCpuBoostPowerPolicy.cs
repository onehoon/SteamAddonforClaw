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

        uint? acWriteResult = null;
        if (ac is { } acMode)
        {
            var subGroup = SubProcessorGuid;
            var setting = PerfBoostModeGuid;
            var schemeForCall = scheme;
            acWriteResult = PowerWriteACValueIndex(0, ref schemeForCall, ref subGroup, ref setting, (uint)acMode);
            if (acWriteResult != 0)
                AppLog.Warn("Profiles.CpuBoost", "CPU Boost AC write failed.", null, ("Win32Error", acWriteResult));
        }

        uint? dcWriteResult = null;
        if (dc is { } dcMode)
        {
            var subGroup = SubProcessorGuid;
            var setting = PerfBoostModeGuid;
            var schemeForCall = scheme;
            dcWriteResult = PowerWriteDCValueIndex(0, ref schemeForCall, ref subGroup, ref setting, (uint)dcMode);
            if (dcWriteResult != 0)
                AppLog.Warn("Profiles.CpuBoost", "CPU Boost DC write failed.", null, ("Win32Error", dcWriteResult));
        }

        // Re-activate the same scheme so Windows applies the newly written value(s) -- writing
        // alone does not take effect (work order section 3/16). Microsoft documents that changes
        // to an active scheme have no effect until PowerSetActiveScheme is called, so a failure
        // here must fail the apply for the requested side(s) that were just written -- it must
        // never be reported as a successful write. Only invoked when at least one REQUESTED
        // side's write actually succeeded (see ResolveApplyResult).
        uint ActivateScheme()
        {
            var schemeForActivate = scheme;
            var activateResult = PowerSetActiveScheme(0, ref schemeForActivate);
            if (activateResult != 0)
                AppLog.Warn("Profiles.CpuBoost", "CPU Boost active-scheme re-activation failed.", null, ("Win32Error", activateResult));
            return activateResult;
        }

        return ResolveApplyResult(ac, acWriteResult, dc, dcWriteResult, ActivateScheme);
    }

    /// <summary>
    /// The pure decision logic for <see cref="Apply"/>, extracted so it is unit-testable without
    /// any real PowrProf call: given the raw Win32 result codes for whichever side(s) were
    /// requested (<see langword="null"/> means "not requested"), decides success per side and
    /// whether/how to invoke <paramref name="activateScheme"/>.
    ///
    /// Unrequested sides start "succeeded" trivially (there was nothing to fail), but that must
    /// never be conflated with "a write succeeded, so activate the scheme" -- only a REQUESTED
    /// side's success counts toward <c>anyRequestedWriteSucceeded</c>. A
    /// <see cref="PowerSetActiveScheme"/> failure fails every REQUESTED side that had otherwise
    /// succeeded, because Microsoft documents that a written value has no effect until the scheme
    /// is re-activated -- an activation failure must never be reported as <c>Succeeded</c>.
    /// </summary>
    internal static CpuBoostApplyResult ResolveApplyResult(CpuBoostMode? ac, uint? acWriteResult, CpuBoostMode? dc, uint? dcWriteResult, Func<uint> activateScheme)
    {
        var acSucceeded = ac is null || acWriteResult == 0;
        var dcSucceeded = dc is null || dcWriteResult == 0;
        var anyRequestedWriteSucceeded = (ac is not null && acSucceeded) || (dc is not null && dcSucceeded);

        string? failureMessage = null;
        if (ac is not null && !acSucceeded) failureMessage = $"PowerWriteACValueIndex failed (Win32 error {acWriteResult}).";
        if (dc is not null && !dcSucceeded) failureMessage ??= $"PowerWriteDCValueIndex failed (Win32 error {dcWriteResult}).";

        if (anyRequestedWriteSucceeded)
        {
            var activateResult = activateScheme();
            if (activateResult != 0)
            {
                failureMessage ??= $"PowerSetActiveScheme failed (Win32 error {activateResult}).";
                if (ac is not null) acSucceeded = false;
                if (dc is not null) dcSucceeded = false;
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
