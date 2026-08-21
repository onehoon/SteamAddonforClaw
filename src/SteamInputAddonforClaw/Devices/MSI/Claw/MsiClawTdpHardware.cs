using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Profiles;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal enum MsiClawTdpFailureStage
{
    None,
    UnsupportedModel,
    InvalidTarget,
    ShiftRead,
    ShiftWrite,
    Pl1Floor,
    Pl2,
    Pl1Final
}

internal sealed record MsiClawTdpApplyResult(
    bool Succeeded,
    MsiClawTdpFailureStage FailureStage = MsiClawTdpFailureStage.None,
    bool RecoveryAttempted = false,
    bool RecoverySucceeded = false);

internal sealed class MsiClawTdpHardware
{
    private const int ShiftBlock = 210;
    private const int Pl1Block = 80;
    private const int Pl2Block = 81;
    private const int ShiftPayloadOffset = 2;
    private readonly IMsiClawTdpTransport _transport;
    private int? _cachedPl1;
    private int? _cachedPl2;

    internal MsiClawTdpHardware(IMsiClawTdpTransport transport) => _transport = transport;

    internal void InvalidateCachedPowerLimits(string reason = "Unknown")
    {
        (_cachedPl1, _cachedPl2) = (null, null);
        AppLog.Debug("Profiles.Tdp.Hardware", "Power-limit cache invalidated", ("Reason", reason));
    }

    internal MsiClawTdpApplyResult Apply(HandheldDeviceModelId modelId, TdpPowerPair target)
    {
        if (!MsiClawTdpPolicy.TryResolve(modelId, out var policy))
            return new(false, MsiClawTdpFailureStage.UnsupportedModel);
        if (!policy.IsValid(target))
            return new(false, MsiClawTdpFailureStage.InvalidTarget);

        if (!_transport.TryGetAp(0, out var ap))
        {
            AppLog.Warn("Profiles.Tdp.Hardware", "MSI_ACPI Get_AP failed", null, ("Index", 0));
            InvalidateCachedPowerLimits("HardwareFailure");
            return new(false, MsiClawTdpFailureStage.ShiftRead);
        }
        if (ap.Length <= ShiftPayloadOffset)
        {
            AppLog.Warn("Profiles.Tdp.Hardware", "Shift response is invalid", null, ("PayloadLength", ap.Length), ("RequiredOffset", ShiftPayloadOffset));
            InvalidateCachedPowerLimits("HardwareFailure");
            return new(false, MsiClawTdpFailureStage.ShiftRead);
        }

        var currentShift = ap[ShiftPayloadOffset];
        if ((currentShift & 0x80) == 0)
        {
            AppLog.Warn("Profiles.Tdp.Hardware", "Shift state is unsupported", null, ("Current", $"0x{currentShift:X2}"));
            InvalidateCachedPowerLimits("HardwareFailure");
            return new(false, MsiClawTdpFailureStage.ShiftRead);
        }

        var desiredShift = EncodeShift(currentShift, policy.ManualCompatibleShiftSelector);
        AppLog.Debug("Profiles.Tdp.Hardware", "Shift state read", ("Current", $"0x{currentShift:X2}"), ("Desired", $"0x{desiredShift:X2}"), ("Action", currentShift == desiredShift ? "Keep" : "Rewrite"));
        if (currentShift != desiredShift)
        {
            // A real mismatch proves that an external owner changed hardware after the
            // cached PL pair was recorded, so the PL cache cannot be trusted.
            InvalidateCachedPowerLimits("ShiftMismatch");
            if (!SetData(ShiftBlock, desiredShift))
            {
                AppLog.Warn("Profiles.Tdp.Hardware", "TDP write failed", null, ("Stage", MsiClawTdpFailureStage.ShiftWrite), ("TargetPL1", target.Pl1Watts), ("TargetPL2", target.Pl2Watts), ("RecoveryAttempted", false));
                return new(false, MsiClawTdpFailureStage.ShiftWrite);
            }
        }

        var pl1Changed = _cachedPl1 != target.Pl1Watts;
        var pl2Changed = _cachedPl2 != target.Pl2Watts;
        if (!pl1Changed && !pl2Changed)
            return new(true);

        if (pl1Changed && !SetData(Pl1Block, 8))
            return Fail(MsiClawTdpFailureStage.Pl1Floor, target, recoveryAttempted: false);

        if (pl2Changed && !SetData(Pl2Block, ToByte(target.Pl2Watts)))
            return RecoverAfterFailure(MsiClawTdpFailureStage.Pl2, pl1Changed, target);

        if (pl1Changed && !SetData(Pl1Block, ToByte(target.Pl1Watts)))
            return RecoverAfterFailure(MsiClawTdpFailureStage.Pl1Final, pl1Changed, target);

        _cachedPl1 = target.Pl1Watts;
        _cachedPl2 = target.Pl2Watts;
        return new(true);
    }

    internal static byte EncodeShift(byte current, int selector)
    {
        var mode = selector switch { 0 => 4, 6 => 6, _ => throw new ArgumentOutOfRangeException(nameof(selector)) };
        // Preserve AP upper state bits, force the active bit, and replace only mode bits.
        return (byte)((current & 0xC0) | 0x40 | mode);
    }

    private MsiClawTdpApplyResult RecoverAfterFailure(MsiClawTdpFailureStage stage, bool floorWritten, TdpPowerPair target)
    {
        if (!floorWritten)
            return Fail(stage, target, recoveryAttempted: false);
        var recovered = _transport.TrySetData(Pl1Block, ToByte(target.Pl1Watts));
        InvalidateCachedPowerLimits("HardwareFailure");
        AppLog.Debug("Profiles.Tdp.Hardware", "PL1 recovery write", ("Block", Pl1Block), ("Value", target.Pl1Watts), ("Succeeded", recovered));
        AppLog.Warn("Profiles.Tdp.Hardware", "TDP write failed", null, ("Stage", stage), ("TargetPL1", target.Pl1Watts), ("TargetPL2", target.Pl2Watts), ("RecoveryAttempted", true), ("RecoverySucceeded", recovered));
        return new(false, stage, RecoveryAttempted: true, RecoverySucceeded: recovered);
    }

    private MsiClawTdpApplyResult Fail(MsiClawTdpFailureStage stage, TdpPowerPair target, bool recoveryAttempted)
    {
        InvalidateCachedPowerLimits("HardwareFailure");
        AppLog.Warn("Profiles.Tdp.Hardware", "TDP write failed", null, ("Stage", stage), ("TargetPL1", target.Pl1Watts), ("TargetPL2", target.Pl2Watts), ("RecoveryAttempted", recoveryAttempted));
        return new(false, stage, recoveryAttempted);
    }

    private bool SetData(int block, byte value)
    {
        var succeeded = _transport.TrySetData(block, value);
        AppLog.Debug("Profiles.Tdp.Hardware", "MSI_ACPI Set_Data", ("Block", block), ("Value", value), ("Succeeded", succeeded));
        return succeeded;
    }

    private static byte ToByte(int watts) => checked((byte)watts);
}
