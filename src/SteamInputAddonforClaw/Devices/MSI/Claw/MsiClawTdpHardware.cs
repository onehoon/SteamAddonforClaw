using SteamInputAddonforClaw.Devices.Abstractions;
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
    private const int ShiftPayloadOffset = 0;
    private readonly IMsiClawTdpTransport _transport;
    private int? _cachedPl1;
    private int? _cachedPl2;

    internal MsiClawTdpHardware(IMsiClawTdpTransport transport) => _transport = transport;

    internal void InvalidateCachedPowerLimits() => (_cachedPl1, _cachedPl2) = (null, null);

    internal MsiClawTdpApplyResult Apply(HandheldDeviceModelId modelId, TdpPowerPair target)
    {
        if (!MsiClawTdpPolicy.TryResolve(modelId, out var policy))
            return new(false, MsiClawTdpFailureStage.UnsupportedModel);
        if (!policy.IsValid(target))
            return new(false, MsiClawTdpFailureStage.InvalidTarget);

        if (!_transport.TryGetAp(0, out var ap) || ap.Length <= ShiftPayloadOffset)
        {
            InvalidateCachedPowerLimits();
            return new(false, MsiClawTdpFailureStage.ShiftRead);
        }

        var currentShift = ap[ShiftPayloadOffset];
        var desiredShift = EncodeShift(currentShift, policy.ManualCompatibleShiftSelector);
        if (currentShift != desiredShift && !_transport.TrySetData(ShiftBlock, desiredShift))
        {
            InvalidateCachedPowerLimits();
            return new(false, MsiClawTdpFailureStage.ShiftWrite);
        }

        var pl1Changed = _cachedPl1 != target.Pl1Watts;
        var pl2Changed = _cachedPl2 != target.Pl2Watts;
        if (!pl1Changed && !pl2Changed)
            return new(true);

        if (pl1Changed && !_transport.TrySetData(Pl1Block, 8))
            return Fail(MsiClawTdpFailureStage.Pl1Floor, target, recoveryAttempted: false);

        if (pl2Changed && !_transport.TrySetData(Pl2Block, ToByte(target.Pl2Watts)))
            return RecoverAfterFailure(MsiClawTdpFailureStage.Pl2, pl1Changed, target);

        if (pl1Changed && !_transport.TrySetData(Pl1Block, ToByte(target.Pl1Watts)))
            return RecoverAfterFailure(MsiClawTdpFailureStage.Pl1Final, pl1Changed, target);

        _cachedPl1 = target.Pl1Watts;
        _cachedPl2 = target.Pl2Watts;
        return new(true);
    }

    internal static byte EncodeShift(byte current, int selector)
    {
        var offset = selector switch { 0 => 4, 6 => 6, _ => throw new ArgumentOutOfRangeException(nameof(selector)) };
        return (byte)((current & 0xC3 | 0xC0) & 0xFC | offset);
    }

    private MsiClawTdpApplyResult RecoverAfterFailure(MsiClawTdpFailureStage stage, bool floorWritten, TdpPowerPair target)
    {
        if (!floorWritten)
            return Fail(stage, target, recoveryAttempted: false);
        var recovered = _transport.TrySetData(Pl1Block, ToByte(target.Pl1Watts));
        InvalidateCachedPowerLimits();
        return new(false, stage, RecoveryAttempted: true, RecoverySucceeded: recovered);
    }

    private MsiClawTdpApplyResult Fail(MsiClawTdpFailureStage stage, TdpPowerPair target, bool recoveryAttempted)
    {
        InvalidateCachedPowerLimits();
        return new(false, stage, recoveryAttempted);
    }

    private static byte ToByte(int watts) => checked((byte)watts);
}
