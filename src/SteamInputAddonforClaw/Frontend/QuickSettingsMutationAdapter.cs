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
    internal static Task<QuickSettingsMutationResult> MutateAsync(IAddonFrontendControl control, QuickSettingsMutationIntent intent, CancellationToken cancellationToken) => intent.PageId switch
    {
        QuickSettingsPageId.Device => MutateDeviceAsync(control, intent, cancellationToken),
        QuickSettingsPageId.Profile => MutateProfileAsync(control, intent, cancellationToken),
        _ => Task.FromResult(new QuickSettingsMutationResult(false, "Quick Settings mutation for this page is not available yet.",
            QuickSettingsPageSnapshot.Unavailable(intent.PageId, intent.AppId))),
    };

    private static async Task<QuickSettingsMutationResult> MutateDeviceAsync(IAddonFrontendControl control, QuickSettingsMutationIntent intent, CancellationToken cancellationToken)
    {
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

    /// <summary>SF-V2-08 section 8: validates current-target identity and current projected
    /// writability before dispatching any Profile mutation onto the existing typed Game Profile
    /// methods -- a malformed, stale, or currently-non-writable intent invokes zero typed mutations.</summary>
    private static async Task<QuickSettingsMutationResult> MutateProfileAsync(IAddonFrontendControl control, QuickSettingsMutationIntent intent, CancellationToken cancellationToken)
    {
        // Section 8.1 step 1: a Profile intent must carry a real game AppId.
        if (intent.AppId is not (> 0)) return new QuickSettingsMutationResult(false, "A Profile Quick Settings intent must carry a game context.", QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, intent.AppId));
        var appId = intent.AppId.Value;

        // Section 8.1 steps 2-3: the current active Profile target is the only validity authority --
        // a mismatch (including no active game) fails closed without any typed mutation.
        var active = await control.CaptureActiveGameProfileAsync(cancellationToken).ConfigureAwait(false);
        if (active.AppId != appId)
            return new QuickSettingsMutationResult(false, "The active game changed; this Profile is no longer current.", QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, appId));

        // Section 8.2: require the intent's edited row to be currently Available/Writable in a fresh
        // projection -- this is what fails a stale child draft closed once the Profile (or one of its
        // features) was disabled after the draft was seeded.
        var currentPage = QuickSettingsPresentation.BuildProfile(active);
        var editedRow = currentPage.Sections.SelectMany(s => s.Rows).FirstOrDefault(r => r.RowId == intent.EditedRowId);
        if (editedRow is not { Available: true, Writable: true })
            return new QuickSettingsMutationResult(false, "This row is not editable.", currentPage);

        switch (intent.EditedRowId)
        {
            case QuickSettingsRowId.ProfileEnabled:
            {
                if (!TryGetSingleBoolean(intent, QuickSettingsRowId.ProfileEnabled, out var enabled))
                    return new QuickSettingsMutationResult(false, "Malformed Profile toggle intent.", currentPage);
                // Section 8.4: the display name comes from the already-validated active snapshot --
                // the generic intent deliberately carries no duplicated display-name field.
                var result = await control.SetGameProfileEnabledAsync(appId, enabled, active.DisplayName, cancellationToken).ConfigureAwait(false);
                return FinishProfile(result);
            }
            case QuickSettingsRowId.ProfileTdpEnabled:
            {
                if (!TryGetSingleBoolean(intent, QuickSettingsRowId.ProfileTdpEnabled, out var enabled))
                    return new QuickSettingsMutationResult(false, "Malformed TDP toggle intent.", currentPage);
                var result = await control.SetGameProfileTdpEnabledAsync(appId, enabled, cancellationToken).ConfigureAwait(false);
                return FinishProfile(result);
            }
            case QuickSettingsRowId.ProfileTdpAcPl1:
            case QuickSettingsRowId.ProfileTdpAcPl2:
            case QuickSettingsRowId.ProfileTdpDcPl1:
            case QuickSettingsRowId.ProfileTdpDcPl2:
            {
                if (!TryGetProfileTdpGroup(intent, out var configuration))
                    return new QuickSettingsMutationResult(false, "Malformed TDP slider group intent.", currentPage);
                var result = await control.SetGameProfileTdpAsync(appId, configuration, cancellationToken).ConfigureAwait(false);
                return FinishProfile(result);
            }
            case QuickSettingsRowId.ProfileCpuBoostEnabled:
            {
                if (!TryGetSingleBoolean(intent, QuickSettingsRowId.ProfileCpuBoostEnabled, out var enabled))
                    return new QuickSettingsMutationResult(false, "Malformed CPU Boost toggle intent.", currentPage);
                var result = await control.SetGameProfileCpuBoostEnabledAsync(appId, enabled, cancellationToken).ConfigureAwait(false);
                return FinishProfile(result);
            }
            case QuickSettingsRowId.ProfileCpuBoostAc:
            {
                if (!TryGetSingleEnum<CpuBoostMode>(intent, QuickSettingsRowId.ProfileCpuBoostAc, out var mode))
                    return new QuickSettingsMutationResult(false, "Malformed CPU Boost value.", currentPage);
                var result = await control.SetGameProfileCpuBoostAcAsync(appId, mode, cancellationToken).ConfigureAwait(false);
                return FinishProfile(result);
            }
            case QuickSettingsRowId.ProfileCpuBoostDc:
            {
                if (!TryGetSingleEnum<CpuBoostMode>(intent, QuickSettingsRowId.ProfileCpuBoostDc, out var mode))
                    return new QuickSettingsMutationResult(false, "Malformed CPU Boost value.", currentPage);
                var result = await control.SetGameProfileCpuBoostDcAsync(appId, mode, cancellationToken).ConfigureAwait(false);
                return FinishProfile(result);
            }
            case QuickSettingsRowId.ProfilePowerModeEnabled:
            {
                if (!TryGetSingleBoolean(intent, QuickSettingsRowId.ProfilePowerModeEnabled, out var enabled))
                    return new QuickSettingsMutationResult(false, "Malformed Power Mode toggle intent.", currentPage);
                var result = await control.SetGameProfilePowerModeEnabledAsync(appId, enabled, cancellationToken).ConfigureAwait(false);
                return FinishProfile(result);
            }
            case QuickSettingsRowId.ProfilePowerModeAc:
            {
                if (!TryGetSingleEnum<WindowsPowerMode>(intent, QuickSettingsRowId.ProfilePowerModeAc, out var mode))
                    return new QuickSettingsMutationResult(false, "Malformed Power Mode value.", currentPage);
                var result = await control.SetGameProfilePowerModeAcAsync(appId, mode, cancellationToken).ConfigureAwait(false);
                return FinishProfile(result);
            }
            case QuickSettingsRowId.ProfilePowerModeDc:
            {
                if (!TryGetSingleEnum<WindowsPowerMode>(intent, QuickSettingsRowId.ProfilePowerModeDc, out var mode))
                    return new QuickSettingsMutationResult(false, "Malformed Power Mode value.", currentPage);
                var result = await control.SetGameProfilePowerModeDcAsync(appId, mode, cancellationToken).ConfigureAwait(false);
                return FinishProfile(result);
            }
            default:
                return new QuickSettingsMutationResult(false, "This row is not editable.", currentPage);
        }
    }

    // Section 8.7: the typed operation's own returned snapshot is always the authoritative page --
    // never the submitted draft -- for both success and a typed feature failure.
    private static QuickSettingsMutationResult FinishProfile(FrontendGameProfileMutationResult result) =>
        new(result.Succeeded, result.FailureMessage, QuickSettingsPresentation.BuildProfile(result.Snapshot));

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

    /// <summary>Section 27 (device) / SF-V2-08 section 8.6 (profile), shared: the intent's whole draft
    /// must contain exactly one each of the given Enabled toggle row id plus the four given numeric
    /// slider row ids, Enabled must be <see langword="true"/> for a slider-group commit, and every
    /// numeric value must be a structurally valid Integer -- no duplicates, no missing members, no
    /// unrelated row values. Parameterized by row id rather than duplicated per page (work order
    /// section 8.6 "a small parameterized TDP-group validation helper shared with Device").</summary>
    private static bool TryGetTdpGroupValues(QuickSettingsMutationIntent intent,
        QuickSettingsRowId enabledRowId, QuickSettingsRowId acPl1RowId, QuickSettingsRowId acPl2RowId, QuickSettingsRowId dcPl1RowId, QuickSettingsRowId dcPl2RowId,
        out int acPl1, out int acPl2, out int dcPl1, out int dcPl2)
    {
        acPl1 = acPl2 = dcPl1 = dcPl2 = 0;
        var groupRowIds = new HashSet<QuickSettingsRowId> { acPl1RowId, acPl2RowId, dcPl1RowId, dcPl2RowId };
        if (!groupRowIds.Contains(intent.EditedRowId) || intent.Values.Count != 5) return false;

        bool? enabled = null;
        int? ac1 = null, ac2 = null, dc1 = null, dc2 = null;
        var seen = new HashSet<QuickSettingsRowId>();

        foreach (var entry in intent.Values)
        {
            if (!seen.Add(entry.RowId)) return false;

            if (entry.RowId == enabledRowId)
            {
                if (entry.Value.Kind != QuickSettingsValueKind.Boolean || !entry.Value.IsStructurallyValid) return false;
                enabled = entry.Value.BooleanValue!.Value;
            }
            else if (entry.RowId == acPl1RowId) { if (!TryGetInteger(entry.Value, out var value)) return false; ac1 = value; }
            else if (entry.RowId == acPl2RowId) { if (!TryGetInteger(entry.Value, out var value)) return false; ac2 = value; }
            else if (entry.RowId == dcPl1RowId) { if (!TryGetInteger(entry.Value, out var value)) return false; dc1 = value; }
            else if (entry.RowId == dcPl2RowId) { if (!TryGetInteger(entry.Value, out var value)) return false; dc2 = value; }
            else return false;
        }

        if (enabled is not true || ac1 is null || ac2 is null || dc1 is null || dc2 is null) return false;

        acPl1 = ac1.Value; acPl2 = ac2.Value; dcPl1 = dc1.Value; dcPl2 = dc2.Value;
        return true;
    }

    private static bool TryGetTdpGroup(QuickSettingsMutationIntent intent, out FrontendTdpConfiguration configuration)
    {
        configuration = null!;
        if (!TryGetTdpGroupValues(intent, QuickSettingsRowId.DeviceTdpEnabled, QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsRowId.DeviceTdpAcPl2, QuickSettingsRowId.DeviceTdpDcPl1, QuickSettingsRowId.DeviceTdpDcPl2,
                out var acPl1, out var acPl2, out var dcPl1, out var dcPl2)) return false;
        configuration = new FrontendTdpConfiguration(true, new FrontendTdpPowerPair(acPl1, acPl2), new FrontendTdpPowerPair(dcPl1, dcPl2));
        return true;
    }

    private static bool TryGetProfileTdpGroup(QuickSettingsMutationIntent intent, out FrontendGameTdpConfiguration configuration)
    {
        configuration = null!;
        if (!TryGetTdpGroupValues(intent, QuickSettingsRowId.ProfileTdpEnabled, QuickSettingsRowId.ProfileTdpAcPl1, QuickSettingsRowId.ProfileTdpAcPl2, QuickSettingsRowId.ProfileTdpDcPl1, QuickSettingsRowId.ProfileTdpDcPl2,
                out var acPl1, out var acPl2, out var dcPl1, out var dcPl2)) return false;
        configuration = new FrontendGameTdpConfiguration(true, new FrontendTdpPowerPair(acPl1, acPl2), new FrontendTdpPowerPair(dcPl1, dcPl2));
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
