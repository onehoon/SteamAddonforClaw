using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Frontend;

/// <summary>Shared Quick Settings Device mutation adapter (Shared Frontend V2, SF-V2-03 section 24):
/// validates a closed <see cref="QuickSettingsMutationIntent"/> and dispatches it onto exactly one of
/// the eight existing typed <see cref="IAddonFrontendControl"/> Device mutation methods via an
/// explicit switch -- never reflection. A malformed intent invokes zero typed mutations. Every valid
/// attempt returns a freshly re-projected Device page (section 28); the underlying typed operation
/// remains the final validity/hardware authority and keeps sole ownership of
/// <see cref="IAddonFrontendControl.StateInvalidated"/> (section 30).</summary>
internal static class QuickSettingsMutationAdapter
{
    internal static async Task<QuickSettingsMutationResult> MutateAsync(IAddonFrontendControl control, QuickSettingsMutationIntent intent, CancellationToken cancellationToken)
    {
        // Section 23/26.1: Profile projection/mutation is not implemented in this PR -- fail closed
        // without touching Device state, and without fabricating a Device page for a Profile intent.
        if (intent.PageId != QuickSettingsPageId.Device)
            return new QuickSettingsMutationResult(false, "Quick Settings mutation for this page is not available yet.",
                QuickSettingsPageSnapshot.Unavailable(intent.PageId, intent.AppId));

        // Section 26.1: a Device intent must not carry a game AppId.
        if (intent.AppId is not null)
            return await FailWithoutMutatingAsync(control, "A Device Quick Settings intent must not carry a game context.", cancellationToken).ConfigureAwait(false);

        switch (intent.EditedRowId)
        {
            case QuickSettingsRowId.DeviceCpuBoostEnabled:
            {
                if (!TryGetSingleBoolean(intent, QuickSettingsRowId.DeviceCpuBoostEnabled, out var enabled))
                    return await FailWithoutMutatingAsync(control, "Malformed CPU Boost toggle intent.", cancellationToken).ConfigureAwait(false);
                var result = await control.SetDeviceCpuBoostEnabledAsync(enabled, cancellationToken).ConfigureAwait(false);
                return await FinishAsync(control, result.Succeeded, result.FailureMessage, cancellationToken).ConfigureAwait(false);
            }
            case QuickSettingsRowId.DeviceCpuBoostAc:
            {
                if (!TryGetSingleEnum<CpuBoostMode>(intent, QuickSettingsRowId.DeviceCpuBoostAc, out var mode))
                    return await FailWithoutMutatingAsync(control, "Malformed CPU Boost value.", cancellationToken).ConfigureAwait(false);
                var result = await control.SetDeviceCpuBoostAcAsync(mode, cancellationToken).ConfigureAwait(false);
                return await FinishAsync(control, result.Succeeded, result.FailureMessage, cancellationToken).ConfigureAwait(false);
            }
            case QuickSettingsRowId.DeviceCpuBoostDc:
            {
                if (!TryGetSingleEnum<CpuBoostMode>(intent, QuickSettingsRowId.DeviceCpuBoostDc, out var mode))
                    return await FailWithoutMutatingAsync(control, "Malformed CPU Boost value.", cancellationToken).ConfigureAwait(false);
                var result = await control.SetDeviceCpuBoostDcAsync(mode, cancellationToken).ConfigureAwait(false);
                return await FinishAsync(control, result.Succeeded, result.FailureMessage, cancellationToken).ConfigureAwait(false);
            }
            case QuickSettingsRowId.DevicePowerModeEnabled:
            {
                if (!TryGetSingleBoolean(intent, QuickSettingsRowId.DevicePowerModeEnabled, out var enabled))
                    return await FailWithoutMutatingAsync(control, "Malformed Power Mode toggle intent.", cancellationToken).ConfigureAwait(false);
                var result = await control.SetDevicePowerModeEnabledAsync(enabled, cancellationToken).ConfigureAwait(false);
                return await FinishAsync(control, result.Succeeded, result.FailureMessage, cancellationToken).ConfigureAwait(false);
            }
            case QuickSettingsRowId.DevicePowerModeAc:
            {
                if (!TryGetSingleEnum<WindowsPowerMode>(intent, QuickSettingsRowId.DevicePowerModeAc, out var mode))
                    return await FailWithoutMutatingAsync(control, "Malformed Power Mode value.", cancellationToken).ConfigureAwait(false);
                var result = await control.SetDevicePowerModeAcAsync(mode, cancellationToken).ConfigureAwait(false);
                return await FinishAsync(control, result.Succeeded, result.FailureMessage, cancellationToken).ConfigureAwait(false);
            }
            case QuickSettingsRowId.DevicePowerModeDc:
            {
                if (!TryGetSingleEnum<WindowsPowerMode>(intent, QuickSettingsRowId.DevicePowerModeDc, out var mode))
                    return await FailWithoutMutatingAsync(control, "Malformed Power Mode value.", cancellationToken).ConfigureAwait(false);
                var result = await control.SetDevicePowerModeDcAsync(mode, cancellationToken).ConfigureAwait(false);
                return await FinishAsync(control, result.Succeeded, result.FailureMessage, cancellationToken).ConfigureAwait(false);
            }
            case QuickSettingsRowId.DeviceTdpEnabled:
            {
                if (!TryGetSingleBoolean(intent, QuickSettingsRowId.DeviceTdpEnabled, out var enabled))
                    return await FailWithoutMutatingAsync(control, "Malformed TDP toggle intent.", cancellationToken).ConfigureAwait(false);
                var result = await control.SetDeviceTdpEnabledAsync(enabled, cancellationToken).ConfigureAwait(false);
                return await FinishAsync(control, result.Succeeded, result.FailureMessage, cancellationToken).ConfigureAwait(false);
            }
            case QuickSettingsRowId.DeviceTdpAcPl1:
            case QuickSettingsRowId.DeviceTdpAcPl2:
            case QuickSettingsRowId.DeviceTdpDcPl1:
            case QuickSettingsRowId.DeviceTdpDcPl2:
            {
                if (!TryGetTdpGroup(intent, out var configuration))
                    return await FailWithoutMutatingAsync(control, "Malformed TDP slider group intent.", cancellationToken).ConfigureAwait(false);
                var result = await control.SetDeviceTdpAsync(configuration, cancellationToken).ConfigureAwait(false);
                return await FinishAsync(control, result.Succeeded, result.FailureMessage, cancellationToken).ConfigureAwait(false);
            }
            default:
                return await FailWithoutMutatingAsync(control, "This row is not editable.", cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool TryGetSingleBoolean(QuickSettingsMutationIntent intent, QuickSettingsRowId rowId, out bool value)
    {
        value = false;
        if (intent.EditedRowId != rowId || intent.Values.Count != 1) return false;
        var entry = intent.Values[0];
        if (entry.RowId != rowId || entry.Value.Kind != QuickSettingsValueKind.Boolean || !entry.Value.IsStructurallyValid) return false;
        value = entry.Value.BooleanValue!.Value;
        return true;
    }

    private static bool TryGetSingleEnum<TEnum>(QuickSettingsMutationIntent intent, QuickSettingsRowId rowId, out TEnum value) where TEnum : struct, Enum
    {
        value = default;
        if (intent.EditedRowId != rowId || intent.Values.Count != 1) return false;
        var entry = intent.Values[0];
        if (entry.RowId != rowId || entry.Value.Kind != QuickSettingsValueKind.Integer || !entry.Value.IsStructurallyValid) return false;
        var raw = entry.Value.IntegerValue!.Value;
        if (!Enum.IsDefined(typeof(TEnum), raw)) return false;
        value = (TEnum)(object)raw;
        return true;
    }

    private static bool IsTdpGroupRowId(QuickSettingsRowId rowId) =>
        rowId is QuickSettingsRowId.DeviceTdpAcPl1 or QuickSettingsRowId.DeviceTdpAcPl2 or QuickSettingsRowId.DeviceTdpDcPl1 or QuickSettingsRowId.DeviceTdpDcPl2;

    /// <summary>Section 27: the intent's whole draft must contain exactly one each of the Enabled
    /// toggle plus all four numeric rows, Enabled must be <see langword="true"/> for a slider-group
    /// commit, and every numeric value must be a structurally valid Integer -- no duplicates, no
    /// missing members, no unrelated row values.</summary>
    private static bool TryGetTdpGroup(QuickSettingsMutationIntent intent, out FrontendTdpConfiguration configuration)
    {
        configuration = null!;
        if (!IsTdpGroupRowId(intent.EditedRowId) || intent.Values.Count != 5) return false;

        bool? enabled = null;
        int? acPl1 = null, acPl2 = null, dcPl1 = null, dcPl2 = null;
        var seen = new HashSet<QuickSettingsRowId>();

        foreach (var entry in intent.Values)
        {
            if (!seen.Add(entry.RowId)) return false;

            switch (entry.RowId)
            {
                case QuickSettingsRowId.DeviceTdpEnabled:
                    if (entry.Value.Kind != QuickSettingsValueKind.Boolean || !entry.Value.IsStructurallyValid) return false;
                    enabled = entry.Value.BooleanValue!.Value;
                    break;
                case QuickSettingsRowId.DeviceTdpAcPl1:
                    if (!TryGetInteger(entry.Value, out var acPl1Value)) return false;
                    acPl1 = acPl1Value;
                    break;
                case QuickSettingsRowId.DeviceTdpAcPl2:
                    if (!TryGetInteger(entry.Value, out var acPl2Value)) return false;
                    acPl2 = acPl2Value;
                    break;
                case QuickSettingsRowId.DeviceTdpDcPl1:
                    if (!TryGetInteger(entry.Value, out var dcPl1Value)) return false;
                    dcPl1 = dcPl1Value;
                    break;
                case QuickSettingsRowId.DeviceTdpDcPl2:
                    if (!TryGetInteger(entry.Value, out var dcPl2Value)) return false;
                    dcPl2 = dcPl2Value;
                    break;
                default:
                    return false;
            }
        }

        if (enabled is not true || acPl1 is null || acPl2 is null || dcPl1 is null || dcPl2 is null) return false;

        configuration = new FrontendTdpConfiguration(true, new FrontendTdpPowerPair(acPl1.Value, acPl2.Value), new FrontendTdpPowerPair(dcPl1.Value, dcPl2.Value));
        return true;
    }

    private static bool TryGetInteger(QuickSettingsValue value, out int result)
    {
        result = 0;
        if (value.Kind != QuickSettingsValueKind.Integer || !value.IsStructurallyValid) return false;
        result = value.IntegerValue!.Value;
        return true;
    }

    private static async Task<QuickSettingsMutationResult> FinishAsync(IAddonFrontendControl control, bool succeeded, string? failureMessage, CancellationToken cancellationToken)
    {
        var page = await CaptureDevicePageAsync(control, cancellationToken).ConfigureAwait(false);
        return new QuickSettingsMutationResult(succeeded, failureMessage, page);
    }

    private static async Task<QuickSettingsMutationResult> FailWithoutMutatingAsync(IAddonFrontendControl control, string failureMessage, CancellationToken cancellationToken)
    {
        var page = await CaptureDevicePageAsync(control, cancellationToken).ConfigureAwait(false);
        return new QuickSettingsMutationResult(false, failureMessage, page);
    }

    private static async Task<QuickSettingsPageSnapshot> CaptureDevicePageAsync(IAddonFrontendControl control, CancellationToken cancellationToken)
    {
        var snapshot = await control.CaptureDeviceQuickSettingsAsync(cancellationToken).ConfigureAwait(false);
        return QuickSettingsPresentation.BuildDevice(snapshot);
    }
}
