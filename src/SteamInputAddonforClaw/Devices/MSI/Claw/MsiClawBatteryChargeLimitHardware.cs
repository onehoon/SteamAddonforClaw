using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal readonly record struct MsiBatteryChargeLimitState(
    bool Enabled,
    int LimitPercent,
    byte RawValue,
    bool IsProductValue);

internal readonly record struct MsiBatteryChargeLimitReadResult(
    bool Succeeded,
    MsiBatteryChargeLimitState? State,
    string? FailureMessage);

internal enum MsiBatteryChargeLimitMutationOutcome
{
    Succeeded,
    InvalidTarget,
    ReadFailed,
    WriteFailed,
    VerificationFailed
}

internal readonly record struct MsiBatteryChargeLimitMutationResult(
    MsiBatteryChargeLimitMutationOutcome Outcome,
    MsiBatteryChargeLimitState? State,
    string? FailureMessage)
{
    internal bool Succeeded => Outcome == MsiBatteryChargeLimitMutationOutcome.Succeeded;
}

internal sealed class MsiClawBatteryChargeLimitHardware
{
    internal const int Block = 215;
    private const byte EnableMask = 0x80;
    private const byte LimitMask = 0x7F;
    private readonly IMsiClawTdpTransport _transport;
    private readonly object _gate = new();

    internal MsiClawBatteryChargeLimitHardware(IMsiClawTdpTransport transport) =>
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    internal MsiBatteryChargeLimitReadResult Read()
    {
        lock (_gate)
        {
            if (TryRead(out var state)) return new(true, state, null);
            AppLog.Debug("MsiBatteryChargeLimit", "BatteryLimit read failed");
            return new(false, null, "BatteryLimit read failed.");
        }
    }

    internal MsiBatteryChargeLimitMutationResult SetPercent(int percent)
    {
        if (!IsProductValue(percent))
        {
            AppLog.Debug("MsiBatteryChargeLimit", "BatteryLimit write rejected: invalid product target", ("Percent", percent));
            return new(MsiBatteryChargeLimitMutationOutcome.InvalidTarget, null, "The battery limit must be 60% to 100% in 5% steps.");
        }

        lock (_gate)
        {
            if (!TryRead(out var current))
                return new(MsiBatteryChargeLimitMutationOutcome.ReadFailed, null, "BatteryLimit read failed.");

            var target = (byte)((current.RawValue & EnableMask) | percent);
            return Mutate(current, target, percent, current.Enabled);
        }
    }

    internal MsiBatteryChargeLimitMutationResult SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (!TryRead(out var current))
                return new(MsiBatteryChargeLimitMutationOutcome.ReadFailed, null, "BatteryLimit read failed.");

            if (enabled && !current.IsProductValue)
            {
                AppLog.Debug("MsiBatteryChargeLimit", "BatteryLimit write rejected: invalid remembered product target",
                    ("RawValue", $"0x{current.RawValue:X2}"), ("Percent", current.LimitPercent));
                return new(MsiBatteryChargeLimitMutationOutcome.InvalidTarget, current,
                    "The observed remembered limit is outside the Addon product range. Apply a valid limit before enabling.");
            }

            var target = enabled ? (byte)(current.RawValue | EnableMask) : (byte)(current.RawValue & LimitMask);
            return Mutate(current, target, current.LimitPercent, enabled);
        }
    }

    internal static bool IsProductValue(int percent) => percent is >= 60 and <= 100 && (percent - 60) % 5 == 0;

    internal static MsiBatteryChargeLimitState Decode(byte rawValue) =>
        new((rawValue & EnableMask) != 0, rawValue & LimitMask, rawValue, IsProductValue(rawValue & LimitMask));

    private MsiBatteryChargeLimitMutationResult Mutate(
        MsiBatteryChargeLimitState current,
        byte target,
        int expectedPercent,
        bool expectedEnabled)
    {
        try
        {
            if (!_transport.TrySetData(Block, target))
            {
                AppLog.Debug("MsiBatteryChargeLimit", "BatteryLimit Set_Data failed", ("Value", $"0x{target:X2}"));
                return new(MsiBatteryChargeLimitMutationOutcome.WriteFailed, current, "BatteryLimit Set_Data failed.");
            }
        }
        catch (Exception exception)
        {
            AppLog.Debug("MsiBatteryChargeLimit", "BatteryLimit Set_Data failed", ("Exception", exception.GetType().Name));
            return new(MsiBatteryChargeLimitMutationOutcome.WriteFailed, current, "BatteryLimit Set_Data failed.");
        }

        if (!TryRead(out var readback))
        {
            AppLog.Debug("MsiBatteryChargeLimit", "BatteryLimit readback failed");
            return new(MsiBatteryChargeLimitMutationOutcome.VerificationFailed, null, "BatteryLimit readback failed.");
        }

        if (readback.Enabled != expectedEnabled || readback.LimitPercent != expectedPercent)
        {
            AppLog.Debug("MsiBatteryChargeLimit", "BatteryLimit verification mismatch",
                ("Expected", $"0x{target:X2}"), ("Actual", $"0x{readback.RawValue:X2}"));
            return new(MsiBatteryChargeLimitMutationOutcome.VerificationFailed, readback, "BatteryLimit readback did not match the requested value.");
        }

        AppLog.Debug("MsiBatteryChargeLimit", "BatteryLimit mutation succeeded",
            ("RawBefore", $"0x{current.RawValue:X2}"), ("RawAfter", $"0x{readback.RawValue:X2}"));
        return new(MsiBatteryChargeLimitMutationOutcome.Succeeded, readback, null);
    }

    private bool TryRead(out MsiBatteryChargeLimitState state)
    {
        state = default;
        try
        {
            if (!_transport.TryGetData(Block, out var payload) || payload is not { Length: > 0 })
                return false;

            state = Decode(payload[0]);
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Debug("MsiBatteryChargeLimit", "BatteryLimit read failed", ("Exception", exception.GetType().Name));
            return false;
        }
    }
}
