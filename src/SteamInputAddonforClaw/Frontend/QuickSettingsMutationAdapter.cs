using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Frontend;

/// <summary>Shared Quick Settings Device mutation adapter (Shared Frontend V2, SF-V2-03 section 24):
/// validates a closed <see cref="QuickSettingsMutationIntent"/> and dispatches it onto exactly one of
/// existing typed <see cref="IAddonFrontendControl"/> Device mutation methods via an
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
            QuickSettingsPageSnapshot.Unavailable(intent.PageId, intent.ProfileTarget))),
    };

    private static async Task<QuickSettingsMutationResult> MutateDeviceAsync(IAddonFrontendControl control, QuickSettingsMutationIntent intent, CancellationToken cancellationToken)
    {
        // A Device intent must not carry a game target.
        if (intent.ProfileTarget is not null)
            return await FailWithoutMutatingAsync(control, "A Device Quick Settings intent must not carry a game context.", cancellationToken).ConfigureAwait(false);

        switch (intent.EditedRowId)
        {
            case QuickSettingsRowId.DeviceBatteryChargeLimitEnabled:
            {
                if (!TryGetSingleBoolean(intent, QuickSettingsRowId.DeviceBatteryChargeLimitEnabled, out var enabled))
                    return await FailWithoutMutatingAsync(control, "Malformed battery charge-limit toggle intent.", cancellationToken).ConfigureAwait(false);
                var result = await control.SetDeviceBatteryChargeLimitEnabledAsync(enabled, cancellationToken).ConfigureAwait(false);
                return await FinishAsync(control, result.Succeeded, result.FailureMessage, cancellationToken).ConfigureAwait(false);
            }
            case QuickSettingsRowId.DeviceBatteryChargeLimitPercent:
            {
                if (!TryGetSingleIntegerInRange(intent, QuickSettingsRowId.DeviceBatteryChargeLimitPercent, 60, 100, out var percent)
                    || (percent - 60) % 5 != 0)
                    return await FailWithoutMutatingAsync(control, "Malformed battery charge-limit value.", cancellationToken).ConfigureAwait(false);
                var result = await control.SetDeviceBatteryChargeLimitPercentAsync(percent, cancellationToken).ConfigureAwait(false);
                return await FinishAsync(control, result.Succeeded, result.FailureMessage, cancellationToken).ConfigureAwait(false);
            }
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

    /// <summary>Validates the Runtime-selected active target and current row writability before
    /// dispatching onto the existing typed Steam/XBOX profile mutation methods.</summary>
    private static async Task<QuickSettingsMutationResult> MutateProfileAsync(IAddonFrontendControl control, QuickSettingsMutationIntent intent, CancellationToken cancellationToken)
    {
        var profileTarget = intent.ProfileTarget;
        if (profileTarget is not { IsStructurallyValid: true })
            return new(false, "A Profile Quick Settings intent must carry a valid active-game target.", QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, profileTarget));

        // CaptureQuickSettingsPageAsync admits only an exact match to the Runtime's current target.
        var currentPage = await control.CaptureQuickSettingsPageAsync(QuickSettingsPageId.Profile, profileTarget, cancellationToken).ConfigureAwait(false);
        if (!currentPage.Available || currentPage.ProfileTarget != profileTarget)
            return new(false, "The active game changed; this Profile is no longer current.", currentPage);

        if (profileTarget.Kind == QuickSettingsProfileTargetKind.Steam
            && IsProfileBackButtonMappingRow(intent.EditedRowId))
            return new(false, "Per-game M1/M2 mapping is available only for XBOX profiles.", currentPage);

        var editedRow = currentPage.Sections.SelectMany(s => s.Rows).FirstOrDefault(r => r.RowId == intent.EditedRowId);
        if (editedRow is not { Available: true, Writable: true })
            return new(false, "This row is not editable.", currentPage);

        var appId = profileTarget.SteamAppId;
        var xboxKey = profileTarget.XboxGameKey;
        var displayName = currentPage.Sections.FirstOrDefault(section => section.SectionId == QuickSettingsSectionId.ProfileGeneral)?.Label;

        switch (intent.EditedRowId)
        {
            case QuickSettingsRowId.ProfileEnabled:
            {
                if (!TryGetSingleBoolean(intent, QuickSettingsRowId.ProfileEnabled, out var enabled))
                    return new QuickSettingsMutationResult(false, "Malformed Profile toggle intent.", currentPage);
                // Section 8.4: the display name comes from the already-validated active snapshot --
                // the generic intent deliberately carries no duplicated display-name field.
                return profileTarget.Kind == QuickSettingsProfileTargetKind.Steam
                    ? FinishProfile(await control.SetGameProfileEnabledAsync(appId!.Value, enabled, displayName, cancellationToken).ConfigureAwait(false))
                    : FinishProfile(await control.SetXboxGameProfileEnabledAsync(xboxKey!, enabled, displayName, cancellationToken).ConfigureAwait(false));
            }
            case QuickSettingsRowId.ProfileBackButtonUseGlobal:
            {
                if (profileTarget.Kind != QuickSettingsProfileTargetKind.Xbox
                    || !TryGetSingleBoolean(intent, QuickSettingsRowId.ProfileBackButtonUseGlobal, out var perGameEnabled))
                    return new QuickSettingsMutationResult(false, "Malformed XBOX M1/M2 mapping toggle intent.", currentPage);

                BackButtonMappingSettings? mapping = null;
                if (perGameEnabled && !TryGetCurrentProfileBackButtonMapping(currentPage, out mapping!))
                    return new QuickSettingsMutationResult(false, "The current M1/M2 mapping is invalid.", currentPage);

                return FinishProfile(await control.SetXboxGameProfileBackButtonMappingAsync(xboxKey!, mapping, cancellationToken).ConfigureAwait(false));
            }
            case QuickSettingsRowId.ProfileBackButtonM1:
            case QuickSettingsRowId.ProfileBackButtonM2:
            {
                if (profileTarget.Kind != QuickSettingsProfileTargetKind.Xbox
                    || !TryGetProfileBackButtonMappingGroup(intent, out var mapping))
                    return new QuickSettingsMutationResult(false, "Malformed XBOX M1/M2 mapping group intent.", currentPage);
                return FinishProfile(await control.SetXboxGameProfileBackButtonMappingAsync(xboxKey!, mapping, cancellationToken).ConfigureAwait(false));
            }
            case QuickSettingsRowId.ProfileTdpEnabled:
            {
                if (!TryGetSingleBoolean(intent, QuickSettingsRowId.ProfileTdpEnabled, out var enabled))
                    return new QuickSettingsMutationResult(false, "Malformed TDP toggle intent.", currentPage);
                return profileTarget.Kind == QuickSettingsProfileTargetKind.Steam
                    ? FinishProfile(await control.SetGameProfileTdpEnabledAsync(appId!.Value, enabled, cancellationToken).ConfigureAwait(false))
                    : FinishProfile(await control.SetXboxGameProfileTdpEnabledAsync(xboxKey!, enabled, cancellationToken).ConfigureAwait(false));
            }
            case QuickSettingsRowId.ProfileTdpAcPl1:
            case QuickSettingsRowId.ProfileTdpAcPl2:
            case QuickSettingsRowId.ProfileTdpDcPl1:
            case QuickSettingsRowId.ProfileTdpDcPl2:
            {
                if (!TryGetProfileTdpGroup(intent, out var configuration))
                    return new QuickSettingsMutationResult(false, "Malformed TDP slider group intent.", currentPage);
                return profileTarget.Kind == QuickSettingsProfileTargetKind.Steam
                    ? FinishProfile(await control.SetGameProfileTdpAsync(appId!.Value, configuration, cancellationToken).ConfigureAwait(false))
                    : FinishProfile(await control.SetXboxGameProfileTdpAsync(xboxKey!, configuration, cancellationToken).ConfigureAwait(false));
            }
            case QuickSettingsRowId.ProfileCpuBoostEnabled:
            {
                if (!TryGetSingleBoolean(intent, QuickSettingsRowId.ProfileCpuBoostEnabled, out var enabled))
                    return new QuickSettingsMutationResult(false, "Malformed CPU Boost toggle intent.", currentPage);
                return profileTarget.Kind == QuickSettingsProfileTargetKind.Steam
                    ? FinishProfile(await control.SetGameProfileCpuBoostEnabledAsync(appId!.Value, enabled, cancellationToken).ConfigureAwait(false))
                    : FinishProfile(await control.SetXboxGameProfileCpuBoostEnabledAsync(xboxKey!, enabled, cancellationToken).ConfigureAwait(false));
            }
            case QuickSettingsRowId.ProfileCpuBoostAc:
            {
                if (!TryGetSingleEnum<CpuBoostMode>(intent, QuickSettingsRowId.ProfileCpuBoostAc, out var mode))
                    return new QuickSettingsMutationResult(false, "Malformed CPU Boost value.", currentPage);
                return profileTarget.Kind == QuickSettingsProfileTargetKind.Steam
                    ? FinishProfile(await control.SetGameProfileCpuBoostAcAsync(appId!.Value, mode, cancellationToken).ConfigureAwait(false))
                    : FinishProfile(await control.SetXboxGameProfileCpuBoostAcAsync(xboxKey!, mode, cancellationToken).ConfigureAwait(false));
            }
            case QuickSettingsRowId.ProfileCpuBoostDc:
            {
                if (!TryGetSingleEnum<CpuBoostMode>(intent, QuickSettingsRowId.ProfileCpuBoostDc, out var mode))
                    return new QuickSettingsMutationResult(false, "Malformed CPU Boost value.", currentPage);
                return profileTarget.Kind == QuickSettingsProfileTargetKind.Steam
                    ? FinishProfile(await control.SetGameProfileCpuBoostDcAsync(appId!.Value, mode, cancellationToken).ConfigureAwait(false))
                    : FinishProfile(await control.SetXboxGameProfileCpuBoostDcAsync(xboxKey!, mode, cancellationToken).ConfigureAwait(false));
            }
            case QuickSettingsRowId.ProfilePowerModeEnabled:
            {
                if (!TryGetSingleBoolean(intent, QuickSettingsRowId.ProfilePowerModeEnabled, out var enabled))
                    return new QuickSettingsMutationResult(false, "Malformed Power Mode toggle intent.", currentPage);
                return profileTarget.Kind == QuickSettingsProfileTargetKind.Steam
                    ? FinishProfile(await control.SetGameProfilePowerModeEnabledAsync(appId!.Value, enabled, cancellationToken).ConfigureAwait(false))
                    : FinishProfile(await control.SetXboxGameProfilePowerModeEnabledAsync(xboxKey!, enabled, cancellationToken).ConfigureAwait(false));
            }
            case QuickSettingsRowId.ProfilePowerModeAc:
            {
                if (!TryGetSingleEnum<WindowsPowerMode>(intent, QuickSettingsRowId.ProfilePowerModeAc, out var mode))
                    return new QuickSettingsMutationResult(false, "Malformed Power Mode value.", currentPage);
                return profileTarget.Kind == QuickSettingsProfileTargetKind.Steam
                    ? FinishProfile(await control.SetGameProfilePowerModeAcAsync(appId!.Value, mode, cancellationToken).ConfigureAwait(false))
                    : FinishProfile(await control.SetXboxGameProfilePowerModeAcAsync(xboxKey!, mode, cancellationToken).ConfigureAwait(false));
            }
            case QuickSettingsRowId.ProfilePowerModeDc:
            {
                if (!TryGetSingleEnum<WindowsPowerMode>(intent, QuickSettingsRowId.ProfilePowerModeDc, out var mode))
                    return new QuickSettingsMutationResult(false, "Malformed Power Mode value.", currentPage);
                return profileTarget.Kind == QuickSettingsProfileTargetKind.Steam
                    ? FinishProfile(await control.SetGameProfilePowerModeDcAsync(appId!.Value, mode, cancellationToken).ConfigureAwait(false))
                    : FinishProfile(await control.SetXboxGameProfilePowerModeDcAsync(xboxKey!, mode, cancellationToken).ConfigureAwait(false));
            }
            case QuickSettingsRowId.ProfileFpsLimitEnabled:
            {
                if (!TryGetSingleBoolean(intent, QuickSettingsRowId.ProfileFpsLimitEnabled, out var enabled))
                    return new QuickSettingsMutationResult(false, "Malformed Intel FPS Limit toggle intent.", currentPage);
                return profileTarget.Kind == QuickSettingsProfileTargetKind.Steam
                    ? FinishProfile(await control.SetGameProfileFpsLimitEnabledAsync(appId!.Value, enabled, cancellationToken).ConfigureAwait(false))
                    : FinishProfile(await control.SetXboxGameProfileFpsLimitEnabledAsync(xboxKey!, enabled, cancellationToken).ConfigureAwait(false));
            }
            case QuickSettingsRowId.ProfileFpsLimitAc:
            case QuickSettingsRowId.ProfileFpsLimitDc:
            {
                if (!TryGetSingleIntegerInRange(intent, intent.EditedRowId, 40, 120, out var fps))
                    return new QuickSettingsMutationResult(false, "Malformed Intel FPS Limit value.", currentPage);
                if (profileTarget.Kind == QuickSettingsProfileTargetKind.Steam)
                    return FinishProfile(intent.EditedRowId == QuickSettingsRowId.ProfileFpsLimitAc
                        ? await control.SetGameProfileFpsLimitAcAsync(appId!.Value, fps, cancellationToken).ConfigureAwait(false)
                        : await control.SetGameProfileFpsLimitDcAsync(appId!.Value, fps, cancellationToken).ConfigureAwait(false));
                return FinishProfile(intent.EditedRowId == QuickSettingsRowId.ProfileFpsLimitAc
                    ? await control.SetXboxGameProfileFpsLimitAcAsync(xboxKey!, fps, cancellationToken).ConfigureAwait(false)
                    : await control.SetXboxGameProfileFpsLimitDcAsync(xboxKey!, fps, cancellationToken).ConfigureAwait(false));
            }
            case QuickSettingsRowId.ProfileGpuMinimumClockEnabled:
            {
                if (!TryGetSingleBoolean(intent, QuickSettingsRowId.ProfileGpuMinimumClockEnabled, out var enabled))
                    return new QuickSettingsMutationResult(false, "Malformed Minimum GPU Clock toggle intent.", currentPage);
                return profileTarget.Kind == QuickSettingsProfileTargetKind.Steam
                    ? FinishProfile(await control.SetGameProfileGpuMinimumClockEnabledAsync(appId!.Value, enabled, cancellationToken).ConfigureAwait(false))
                    : FinishProfile(await control.SetXboxGameProfileGpuMinimumClockEnabledAsync(xboxKey!, enabled, cancellationToken).ConfigureAwait(false));
            }
            case QuickSettingsRowId.ProfileGpuMinimumClockAc:
            case QuickSettingsRowId.ProfileGpuMinimumClockDc:
            {
                var gpuRow = currentPage.Sections.SelectMany(section => section.Rows)
                    .FirstOrDefault(row => row.RowId == intent.EditedRowId);
                var optionCount = gpuRow?.SliderSpec?.Options?.Count ?? 0;
                if (optionCount == 0 || !TryGetSingleIntegerInRange(intent, intent.EditedRowId, 0, optionCount - 1, out var index))
                    return new QuickSettingsMutationResult(false, "Malformed Minimum GPU Clock value.", currentPage);
                if (profileTarget.Kind == QuickSettingsProfileTargetKind.Steam)
                    return FinishProfile(intent.EditedRowId == QuickSettingsRowId.ProfileGpuMinimumClockAc
                        ? await control.SetGameProfileGpuMinimumClockAcAsync(appId!.Value, index, cancellationToken).ConfigureAwait(false)
                        : await control.SetGameProfileGpuMinimumClockDcAsync(appId!.Value, index, cancellationToken).ConfigureAwait(false));
                return FinishProfile(intent.EditedRowId == QuickSettingsRowId.ProfileGpuMinimumClockAc
                    ? await control.SetXboxGameProfileGpuMinimumClockAcAsync(xboxKey!, index, cancellationToken).ConfigureAwait(false)
                    : await control.SetXboxGameProfileGpuMinimumClockDcAsync(xboxKey!, index, cancellationToken).ConfigureAwait(false));
            }
            case QuickSettingsRowId.ProfileResolution:
            {
                if (!TryGetSingleIntegerInRange(intent, QuickSettingsRowId.ProfileResolution, 0, 4, out var option))
                    return new QuickSettingsMutationResult(false, "Malformed Profile resolution value.", currentPage);
                var resolution = option switch
                {
                    0 => null,
                    1 => new FrontendGameResolution(1920, 1200),
                    2 => new FrontendGameResolution(1920, 1080),
                    3 => new FrontendGameResolution(1680, 1050),
                    4 => new FrontendGameResolution(1440, 900),
                    _ => null,
                };
                return profileTarget.Kind == QuickSettingsProfileTargetKind.Steam
                    ? FinishProfile(await control.SetGameProfileResolutionAsync(appId!.Value, resolution, displayName, cancellationToken).ConfigureAwait(false))
                    : FinishProfile(await control.SetXboxGameProfileResolutionAsync(xboxKey!, resolution, displayName, cancellationToken).ConfigureAwait(false));
            }
            default:
                return new QuickSettingsMutationResult(false, "This row is not editable.", currentPage);
        }
    }

    // Section 8.7: the typed operation's own returned snapshot is always the authoritative page --
    // never the submitted draft -- for both success and a typed feature failure.
    private static QuickSettingsMutationResult FinishProfile(FrontendGameProfileMutationResult result) =>
        new(result.Succeeded, result.FailureMessage, QuickSettingsPresentation.BuildProfile(result.Snapshot));

    private static QuickSettingsMutationResult FinishProfile(FrontendXboxGameProfileMutationResult result) =>
        new(result.Succeeded, result.FailureMessage, QuickSettingsPresentation.BuildProfile(result.Snapshot));

    private static bool IsProfileBackButtonMappingRow(QuickSettingsRowId rowId) => rowId is
        QuickSettingsRowId.ProfileBackButtonUseGlobal or QuickSettingsRowId.ProfileBackButtonM1 or QuickSettingsRowId.ProfileBackButtonM2;

    private static bool TryGetCurrentProfileBackButtonMapping(QuickSettingsPageSnapshot page, out BackButtonMappingSettings mapping)
    {
        mapping = null!;
        var rows = page.Sections.SelectMany(section => section.Rows).ToDictionary(row => row.RowId);
        if (!rows.TryGetValue(QuickSettingsRowId.ProfileBackButtonM1, out var m1Row)
            || !rows.TryGetValue(QuickSettingsRowId.ProfileBackButtonM2, out var m2Row)
            || m1Row.Value is not { } m1Value || m2Row.Value is not { } m2Value
            || !TryGetInteger(m1Value, out var m1) || !Enum.IsDefined(typeof(Xbox360BackButtonTarget), m1)
            || !TryGetInteger(m2Value, out var m2) || !Enum.IsDefined(typeof(Xbox360BackButtonTarget), m2))
            return false;

        mapping = new BackButtonMappingSettings((Xbox360BackButtonTarget)m1, (Xbox360BackButtonTarget)m2);
        return BackButtonMappingValidation.IsValid(mapping);
    }

    private static bool TryGetProfileBackButtonMappingGroup(QuickSettingsMutationIntent intent, out BackButtonMappingSettings mapping)
    {
        mapping = null!;
        if (intent.EditedRowId is not (QuickSettingsRowId.ProfileBackButtonM1 or QuickSettingsRowId.ProfileBackButtonM2)
            || intent.Values is not { Count: 3 } values)
            return false;

        bool? perGameEnabled = null;
        int? m1 = null;
        int? m2 = null;
        var seen = new HashSet<QuickSettingsRowId>();
        foreach (var entry in values)
        {
            if (entry is null || entry.Value is null) return false;
            if (!seen.Add(entry.RowId)) return false;
            if (entry.RowId == QuickSettingsRowId.ProfileBackButtonUseGlobal)
            {
                if (entry.Value.Kind != QuickSettingsValueKind.Boolean || !entry.Value.IsStructurallyValid) return false;
                perGameEnabled = entry.Value.BooleanValue!.Value;
            }
            else if (entry.RowId == QuickSettingsRowId.ProfileBackButtonM1)
            {
                if (!TryGetInteger(entry.Value, out var value) || !Enum.IsDefined(typeof(Xbox360BackButtonTarget), value)) return false;
                m1 = value;
            }
            else if (entry.RowId == QuickSettingsRowId.ProfileBackButtonM2)
            {
                if (!TryGetInteger(entry.Value, out var value) || !Enum.IsDefined(typeof(Xbox360BackButtonTarget), value)) return false;
                m2 = value;
            }
            else return false;
        }

        if (perGameEnabled is not true || m1 is null || m2 is null) return false;
        mapping = new BackButtonMappingSettings((Xbox360BackButtonTarget)m1.Value, (Xbox360BackButtonTarget)m2.Value);
        return BackButtonMappingValidation.IsValid(mapping);
    }

    private static bool TryGetSingleBoolean(QuickSettingsMutationIntent intent, QuickSettingsRowId rowId, out bool value)
    {
        value = false;
        if (intent.EditedRowId != rowId || intent.Values is not { Count: 1 } values) return false;
        var entry = values[0];
        if (entry is null || entry.Value is null) return false;
        if (entry.RowId != rowId || entry.Value.Kind != QuickSettingsValueKind.Boolean || !entry.Value.IsStructurallyValid) return false;
        value = entry.Value.BooleanValue!.Value;
        return true;
    }

    private static bool TryGetSingleIntegerInRange(QuickSettingsMutationIntent intent, QuickSettingsRowId rowId, int minimum, int maximum, out int value)
    {
        value = 0;
        if (intent.EditedRowId != rowId || intent.Values.Count != 1) return false;
        var entry = intent.Values[0];
        if (entry.RowId != rowId || !TryGetInteger(entry.Value, out value)) return false;
        return value >= minimum && value <= maximum;
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
        if (value is not { Kind: QuickSettingsValueKind.Integer } || !value.IsStructurallyValid) return false;
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
