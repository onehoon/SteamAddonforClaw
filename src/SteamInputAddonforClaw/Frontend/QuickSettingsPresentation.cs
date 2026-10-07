using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Profiles.Performance;

namespace SteamInputAddonforClaw.Frontend;

/// <summary>Shared Quick Settings Device page projection (Shared Frontend V2, SF-V2-03 section 14):
/// one small stateless mapping from the already-captured <see cref="FrontendDeviceQuickSettingsSnapshot"/>
/// truth to the closed shared product rows. Never reads hardware, persists, mutates, subscribes, or
/// caches -- a pure function of its input.</summary>
internal static class QuickSettingsPresentation
{
    private sealed record ProfileQuickSettingsProjection(
        string DisplayName,
        bool Enabled,
        FrontendGameCpuBoostConfiguration CpuBoost,
        FrontendGameTdpConfiguration Tdp,
        bool PersistenceWritable,
        FrontendTdpLimits? Limits,
        FrontendGameResolution? Resolution,
        FrontendGamePowerModeConfiguration? PowerMode,
        FrontendGameFpsLimitConfiguration? FpsLimit,
        FrontendGameGpuMinimumClockConfiguration? GpuMinimumClock,
        QuickSettingsProfileTarget ProfileTarget,
        FrontendGameBackButtonMappingConfiguration? BackButtonMapping);

    internal static QuickSettingsPageSnapshot ApplyPowerSourceVisibility(
        QuickSettingsPageSnapshot page,
        bool currentPowerSourceOnly,
        AcDcPowerSource? source)
    {
        ArgumentNullException.ThrowIfNull(page);

        return page with
        {
            Sections = page.Sections
                .Select(section => section with
                {
                    Rows = section.Rows
                        .Select(row => row with
                        {
                            Visible = !currentPowerSourceOnly || source is null
                                || IsVisibleForPowerSource(row.RowId, source.Value),
                        })
                        .ToArray(),
                })
                .ToArray(),
        };
    }

    private static bool IsVisibleForPowerSource(QuickSettingsRowId rowId, AcDcPowerSource source) => rowId switch
    {
        QuickSettingsRowId.DeviceTdpAcPl1 or QuickSettingsRowId.DeviceTdpAcPl2
            or QuickSettingsRowId.DeviceCpuBoostAc or QuickSettingsRowId.DevicePowerModeAc
            or QuickSettingsRowId.ProfileTdpAcPl1 or QuickSettingsRowId.ProfileTdpAcPl2
            or QuickSettingsRowId.ProfileCpuBoostAc or QuickSettingsRowId.ProfilePowerModeAc
            or QuickSettingsRowId.ProfileFpsLimitAc
            or QuickSettingsRowId.ProfileGpuMinimumClockAc
            => source == AcDcPowerSource.AC,
        QuickSettingsRowId.DeviceTdpDcPl1 or QuickSettingsRowId.DeviceTdpDcPl2
            or QuickSettingsRowId.DeviceCpuBoostDc or QuickSettingsRowId.DevicePowerModeDc
            or QuickSettingsRowId.ProfileTdpDcPl1 or QuickSettingsRowId.ProfileTdpDcPl2
            or QuickSettingsRowId.ProfileCpuBoostDc or QuickSettingsRowId.ProfilePowerModeDc
            or QuickSettingsRowId.ProfileFpsLimitDc
            or QuickSettingsRowId.ProfileGpuMinimumClockDc
            => source == AcDcPowerSource.DC,
        _ => true,
    };
    /// <summary>CPU Boost discrete option order/labels (work order section 16.1). Fixed, not derived
    /// from the enum member names in each renderer.</summary>
    private static readonly (CpuBoostMode Mode, string Label)[] CpuBoostOptions =
    [
        (CpuBoostMode.Disabled, "Disabled"),
        (CpuBoostMode.Enabled, "Enabled"),
        (CpuBoostMode.Aggressive, "Aggressive"),
        (CpuBoostMode.EfficientEnabled, "Efficient Enabled"),
        (CpuBoostMode.EfficientAggressive, "Efficient Aggressive"),
        (CpuBoostMode.AggressiveAtGuaranteed, "Aggressive At Guaranteed"),
        (CpuBoostMode.EfficientAggressiveAtGuaranteed, "Efficient Aggressive At Guaranteed"),
    ];

    /// <summary>Windows Power Mode discrete option order/labels (work order section 17.1).</summary>
    private static readonly (WindowsPowerMode Mode, string Label)[] PowerModeOptions =
    [
        (WindowsPowerMode.BestPowerEfficiency, "Best power efficiency"),
        (WindowsPowerMode.Balanced, "Balanced"),
        (WindowsPowerMode.BestPerformance, "Best performance"),
    ];

    internal static readonly IReadOnlyList<QuickSettingsDiscreteOption> ProfileResolutionOptions =
    [
        new(0, "Do not change"),
        new(1, "1920 × 1200"),
        new(2, "1920 × 1080"),
        new(3, "1680 × 1050"),
        new(4, "1440 × 900"),
    ];

    internal static readonly IReadOnlyList<QuickSettingsDiscreteOption> CpuBoostDiscreteOptions =
        [.. CpuBoostOptions.Select(o => new QuickSettingsDiscreteOption((int)o.Mode, o.Label))];

    internal static readonly IReadOnlyList<QuickSettingsDiscreteOption> PowerModeDiscreteOptions =
        [.. PowerModeOptions.Select(o => new QuickSettingsDiscreteOption((int)o.Mode, o.Label))];

    internal static readonly IReadOnlyList<QuickSettingsDiscreteOption> BackButtonMappingOptions =
        Enum.GetValues<Xbox360BackButtonTarget>()
            .Select(target => new QuickSettingsDiscreteOption((int)target, BackButtonMappingLabels.GetDisplayName(target)))
            .ToArray();

    /// <summary>Frozen Device section/row order: TDP, CPU Boost, Windows Power Mode, then Battery
    /// Charge Limit. One child being unavailable never affects the others.</summary>
    internal static QuickSettingsPageSnapshot BuildDevice(FrontendDeviceQuickSettingsSnapshot snapshot)
    {
        IReadOnlyList<QuickSettingsSection> sections =
        [
            BuildTdpSection(snapshot.Tdp),
            BuildCpuBoostSection(snapshot.CpuBoost),
            BuildPowerModeSection(snapshot.PowerMode),
            BuildBatteryChargeLimitSection(snapshot.BatteryChargeLimit),
        ];

        return new QuickSettingsPageSnapshot(QuickSettingsPageId.Device, ProfileTarget: null, Available: true, Message: null, sections, BuildDeviceTdpLinkedConstraints(snapshot.Tdp));
    }

    private static QuickSettingsSection BuildBatteryChargeLimitSection(FrontendBatteryChargeLimitSnapshot snapshot)
    {
        var enabled = snapshot.DesiredEnabled ?? snapshot.CurrentEnabled ?? false;
        var percent = IsSupportedBatteryChargeLimit(snapshot.DesiredLimitPercent)
            ? snapshot.DesiredLimitPercent!.Value
            : IsSupportedBatteryChargeLimit(snapshot.CurrentLimitPercent)
                ? snapshot.CurrentLimitPercent!.Value
                : 60;
        var rows = new QuickSettingsRow[]
        {
            new(QuickSettingsRowId.DeviceBatteryChargeLimitEnabled, "Enabled", QuickSettingsControlKind.Toggle,
                Available: snapshot.Available,
                Writable: snapshot.Available && snapshot.PersistenceWritable && snapshot.Initialized,
                Value: snapshot.Available ? QuickSettingsValue.Boolean(enabled) : null,
                SliderSpec: null,
                CommitPolicy: QuickSettingsCommitPolicy.Immediate),
            new(QuickSettingsRowId.DeviceBatteryChargeLimitPercent, "Limit", QuickSettingsControlKind.Slider,
                Available: snapshot.Available,
                Writable: snapshot.Available && snapshot.PersistenceWritable,
                Value: snapshot.Available ? QuickSettingsValue.Integer(percent) : null,
                SliderSpec: new QuickSettingsSliderSpec(QuickSettingsSliderKind.Numeric, 60, 100, Step: 5, Suffix: "%"),
                CommitPolicy: QuickSettingsCommitPolicy.TrailingDebounce300),
        };
        return new QuickSettingsSection(QuickSettingsSectionId.DeviceBatteryChargeLimit, "Battery Charge Limit", rows, snapshot.LastFailure);
    }

    private static bool IsSupportedBatteryChargeLimit(int? percent) =>
        percent is >= 60 and <= 100 && (percent.Value - 60) % 5 == 0;

    /// <summary>Shared Quick Settings Profile page projection (SF-V2-08 section 6): the exact current
    /// visible Overlay Profile product. Called only for a valid target -- an unavailable/stale Profile
    /// context is represented separately via <see cref="QuickSettingsPageSnapshot.Unavailable"/>, never
    /// fabricated here.</summary>
    internal static QuickSettingsPageSnapshot BuildProfile(FrontendGameProfileSnapshot snapshot) => BuildProfile(new ProfileQuickSettingsProjection(
        string.IsNullOrWhiteSpace(snapshot.DisplayName) ? $"Game {snapshot.AppId}" : snapshot.DisplayName,
        snapshot.Enabled, snapshot.CpuBoost, snapshot.Tdp, snapshot.PersistenceWritable, snapshot.Limits,
        snapshot.Resolution, snapshot.PowerMode, snapshot.FpsLimit, snapshot.GpuMinimumClock,
        QuickSettingsProfileTarget.ForSteam(snapshot.AppId), BackButtonMapping: null));

    internal static QuickSettingsPageSnapshot BuildProfile(FrontendXboxGameProfileSnapshot snapshot) => BuildProfile(new ProfileQuickSettingsProjection(
        string.IsNullOrWhiteSpace(snapshot.DisplayName) ? "XBOX game" : snapshot.DisplayName,
        snapshot.Enabled, snapshot.CpuBoost, snapshot.Tdp, snapshot.PersistenceWritable, snapshot.Limits,
        snapshot.Resolution, snapshot.PowerMode, snapshot.FpsLimit, snapshot.GpuMinimumClock,
        QuickSettingsProfileTarget.ForXbox(snapshot.Key), snapshot.BackButtonMapping));

    private static QuickSettingsPageSnapshot BuildProfile(ProfileQuickSettingsProjection snapshot)
    {
        var sections = new List<QuickSettingsSection> { BuildProfileGeneralSection(snapshot) };
        if (snapshot.BackButtonMapping is not null) sections.Add(BuildProfileControllerSection(snapshot));
        if (snapshot.Limits is not null) sections.Add(BuildProfileTdpSection(snapshot));
        sections.Add(BuildProfileCpuBoostSection(snapshot));
        if (snapshot.PowerMode is not null) sections.Add(BuildProfilePowerModeSection(snapshot));
        if (snapshot.GpuMinimumClock is not null) sections.Add(BuildProfileGpuMinimumClockSection(snapshot));
        if (snapshot.FpsLimit is not null) sections.Add(BuildProfileFpsLimitSection(snapshot));
        sections.Add(BuildProfileResolutionSection(snapshot));

        var linkedConstraints = snapshot.Limits is { } limits ? BuildProfileTdpLinkedConstraints(limits) : [];
        return new QuickSettingsPageSnapshot(QuickSettingsPageId.Profile, snapshot.ProfileTarget, Available: true, Message: null, sections, linkedConstraints);
    }

    private static QuickSettingsSection BuildProfileGeneralSection(ProfileQuickSettingsProjection snapshot)
    {
        var row = new QuickSettingsRow(QuickSettingsRowId.ProfileEnabled, "Profile", QuickSettingsControlKind.Toggle,
            Available: true,
            Writable: snapshot.PersistenceWritable,
            Value: QuickSettingsValue.Boolean(snapshot.Enabled),
            SliderSpec: null,
            CommitPolicy: QuickSettingsCommitPolicy.Immediate);
        return new QuickSettingsSection(QuickSettingsSectionId.ProfileGeneral, snapshot.DisplayName, [row]);
    }

    private static QuickSettingsSection BuildProfileControllerSection(ProfileQuickSettingsProjection snapshot)
    {
        var configuration = snapshot.BackButtonMapping!;
        var perGameEnabled = !configuration.UseGlobalMapping;
        var mappingWritable = snapshot.PersistenceWritable && perGameEnabled;
        var rows = new QuickSettingsRow[]
        {
            // Preserve the existing transport row identity; its displayed value means per-game mapping enabled.
            new(QuickSettingsRowId.ProfileBackButtonUseGlobal, "M1 / M2 Button Mapping", QuickSettingsControlKind.Toggle,
                Available: true,
                Writable: snapshot.PersistenceWritable,
                Value: QuickSettingsValue.Boolean(perGameEnabled),
                SliderSpec: null,
                CommitPolicy: QuickSettingsCommitPolicy.Immediate),
            BuildProfileBackButtonMappingSlider(QuickSettingsRowId.ProfileBackButtonM1, "M1", configuration.Mapping.M1, mappingWritable),
            BuildProfileBackButtonMappingSlider(QuickSettingsRowId.ProfileBackButtonM2, "M2", configuration.Mapping.M2, mappingWritable),
        };
        return new QuickSettingsSection(QuickSettingsSectionId.ProfileController, "M1 / M2 Button Mapping", rows);
    }

    private static QuickSettingsRow BuildProfileBackButtonMappingSlider(QuickSettingsRowId rowId, string label, Xbox360BackButtonTarget target, bool writable) =>
        new(rowId, label, QuickSettingsControlKind.Slider,
            Available: true,
            Writable: writable,
            Value: QuickSettingsValue.Integer((int)target),
            SliderSpec: new QuickSettingsSliderSpec(QuickSettingsSliderKind.Discrete, Options: BackButtonMappingOptions),
            CommitPolicy: QuickSettingsCommitPolicy.TrailingDebounce300,
            CommitGroupId: QuickSettingsCommitGroupId.ProfileBackButtonMapping);

    private static QuickSettingsSection BuildProfileTdpSection(ProfileQuickSettingsProjection snapshot)
    {
        var limits = snapshot.Limits!;
        var writable = snapshot.PersistenceWritable && snapshot.Enabled;
        var rows = new List<QuickSettingsRow>
        {
            new(QuickSettingsRowId.ProfileTdpEnabled, "TDP Control", QuickSettingsControlKind.Toggle,
                Available: true,
                Writable: writable,
                Value: QuickSettingsValue.Boolean(snapshot.Tdp.Enabled),
                SliderSpec: null,
                CommitPolicy: QuickSettingsCommitPolicy.Immediate),
        };

        if (snapshot.Tdp.Enabled)
        {
            rows.Add(BuildProfileTdpSlider(QuickSettingsRowId.ProfileTdpAcPl1, "Plugged in · PL1", snapshot.Tdp.Ac.Pl1Watts, limits.Pl1MinimumWatts, limits.Pl1MaximumWatts, writable));
            rows.Add(BuildProfileTdpSlider(QuickSettingsRowId.ProfileTdpAcPl2, "Plugged in · PL2", snapshot.Tdp.Ac.Pl2Watts, limits.Pl2MinimumWatts, limits.Pl2MaximumWatts, writable));
            rows.Add(BuildProfileTdpSlider(QuickSettingsRowId.ProfileTdpDcPl1, "On battery · PL1", snapshot.Tdp.Dc.Pl1Watts, limits.Pl1MinimumWatts, limits.Pl1MaximumWatts, writable));
            rows.Add(BuildProfileTdpSlider(QuickSettingsRowId.ProfileTdpDcPl2, "On battery · PL2", snapshot.Tdp.Dc.Pl2Watts, limits.Pl2MinimumWatts, limits.Pl2MaximumWatts, writable));
        }

        return new QuickSettingsSection(QuickSettingsSectionId.ProfileTdp, "TDP Control", rows);
    }

    // Section 6.3: unlike Device, the Profile slider keeps a null/empty suffix for parity with the
    // pre-migration Steam native slider's plain-numeric-watts presentation.
    private static QuickSettingsRow BuildProfileTdpSlider(QuickSettingsRowId rowId, string label, int currentWatts, int minimumWatts, int maximumWatts, bool writable) =>
        new(rowId, label, QuickSettingsControlKind.Slider,
            Available: true,
            Writable: writable,
            Value: QuickSettingsValue.Integer(currentWatts),
            SliderSpec: new QuickSettingsSliderSpec(QuickSettingsSliderKind.Numeric, minimumWatts, maximumWatts, Step: 1, Suffix: null),
            CommitPolicy: QuickSettingsCommitPolicy.TrailingDebounce300,
            CommitGroupId: QuickSettingsCommitGroupId.ProfileTdpConfiguration);

    private static QuickSettingsSection BuildProfileCpuBoostSection(ProfileQuickSettingsProjection snapshot)
    {
        var writable = snapshot.PersistenceWritable && snapshot.Enabled;
        var rows = new List<QuickSettingsRow>
        {
            new(QuickSettingsRowId.ProfileCpuBoostEnabled, "CPU Boost", QuickSettingsControlKind.Toggle,
                Available: true,
                Writable: writable,
                Value: QuickSettingsValue.Boolean(snapshot.CpuBoost.Enabled),
                SliderSpec: null,
                CommitPolicy: QuickSettingsCommitPolicy.Immediate),
        };

        if (snapshot.CpuBoost.Enabled)
        {
            rows.Add(BuildProfileCpuBoostSlider(QuickSettingsRowId.ProfileCpuBoostAc, "Plugged in", snapshot.CpuBoost.Ac, writable));
            rows.Add(BuildProfileCpuBoostSlider(QuickSettingsRowId.ProfileCpuBoostDc, "On battery", snapshot.CpuBoost.Dc, writable));
        }

        return new QuickSettingsSection(QuickSettingsSectionId.ProfileCpuBoost, "CPU Boost", rows);
    }

    private static QuickSettingsRow BuildProfileCpuBoostSlider(QuickSettingsRowId rowId, string label, CpuBoostMode mode, bool writable) =>
        new(rowId, label, QuickSettingsControlKind.Slider,
            Available: true,
            Writable: writable,
            Value: QuickSettingsValue.Integer((int)mode),
            SliderSpec: new QuickSettingsSliderSpec(QuickSettingsSliderKind.Discrete, Options: CpuBoostDiscreteOptions),
            CommitPolicy: QuickSettingsCommitPolicy.TrailingDebounce300);

    private static QuickSettingsSection BuildProfilePowerModeSection(ProfileQuickSettingsProjection snapshot)
    {
        var powerMode = snapshot.PowerMode!;
        var writable = snapshot.PersistenceWritable && snapshot.Enabled;
        var rows = new List<QuickSettingsRow>
        {
            new(QuickSettingsRowId.ProfilePowerModeEnabled, "Windows Power Mode", QuickSettingsControlKind.Toggle,
                Available: true,
                Writable: writable,
                Value: QuickSettingsValue.Boolean(powerMode.Enabled),
                SliderSpec: null,
                CommitPolicy: QuickSettingsCommitPolicy.Immediate),
        };

        if (powerMode.Enabled)
        {
            rows.Add(BuildProfilePowerModeSlider(QuickSettingsRowId.ProfilePowerModeAc, "Plugged in", powerMode.Ac, writable));
            rows.Add(BuildProfilePowerModeSlider(QuickSettingsRowId.ProfilePowerModeDc, "On battery", powerMode.Dc, writable));
        }

        return new QuickSettingsSection(QuickSettingsSectionId.ProfilePowerMode, "Windows Power Mode", rows);
    }

    private static QuickSettingsRow BuildProfilePowerModeSlider(QuickSettingsRowId rowId, string label, WindowsPowerMode mode, bool writable) =>
        new(rowId, label, QuickSettingsControlKind.Slider,
            Available: true,
            Writable: writable,
            Value: QuickSettingsValue.Integer((int)mode),
            SliderSpec: new QuickSettingsSliderSpec(QuickSettingsSliderKind.Discrete, Options: PowerModeDiscreteOptions),
            CommitPolicy: QuickSettingsCommitPolicy.TrailingDebounce300);

    private static QuickSettingsSection BuildProfileFpsLimitSection(ProfileQuickSettingsProjection snapshot)
    {
        var fps = snapshot.FpsLimit!;
        if (!fps.Available)
        {
            return new QuickSettingsSection(
                QuickSettingsSectionId.ProfileFpsLimit,
                "Intel FPS Limit",
                [new QuickSettingsRow(QuickSettingsRowId.ProfileFpsLimitEnabled, "Enabled", QuickSettingsControlKind.Toggle,
                    Available: false, Writable: false, Value: null, SliderSpec: null, CommitPolicy: QuickSettingsCommitPolicy.Immediate)],
                fps.UnavailableReason ?? "Intel FPS Limit is unavailable.");
        }

        var writable = snapshot.PersistenceWritable && snapshot.Enabled;
        var rows = new List<QuickSettingsRow>
        {
            new(QuickSettingsRowId.ProfileFpsLimitEnabled, "Enabled", QuickSettingsControlKind.Toggle,
                Available: true, Writable: writable, Value: QuickSettingsValue.Boolean(fps.Enabled), SliderSpec: null,
                CommitPolicy: QuickSettingsCommitPolicy.Immediate),
        };

        if (fps.Enabled)
        {
            rows.Add(BuildProfileFpsLimitSlider(QuickSettingsRowId.ProfileFpsLimitAc, "Plugged in", fps.AcFps, writable));
            rows.Add(BuildProfileFpsLimitSlider(QuickSettingsRowId.ProfileFpsLimitDc, "On battery", fps.DcFps, writable));
        }

        return new QuickSettingsSection(QuickSettingsSectionId.ProfileFpsLimit, "Intel FPS Limit", rows);
    }

    private static QuickSettingsSection BuildProfileGpuMinimumClockSection(ProfileQuickSettingsProjection snapshot)
    {
        var gpu = snapshot.GpuMinimumClock!;
        var writable = snapshot.PersistenceWritable && snapshot.Enabled && gpu.Available;
        var rows = new List<QuickSettingsRow>
        {
            new(QuickSettingsRowId.ProfileGpuMinimumClockEnabled, "Minimum GPU Clock", QuickSettingsControlKind.Toggle,
                Available: gpu.Available, Writable: writable,
                Value: gpu.Available ? QuickSettingsValue.Boolean(gpu.Enabled) : null,
                SliderSpec: null, CommitPolicy: QuickSettingsCommitPolicy.Immediate),
        };

        if (gpu.Available && gpu.Enabled)
        {
            var options = BuildGpuClockOptions(gpu.SelectableClocksMhz);
            var fallbackIndex = FindGpuClockIndex(gpu.SelectableClocksMhz, gpu.RecommendedDefaultMhz) ?? 0;
            var acIndex = FindGpuClockIndex(gpu.SelectableClocksMhz, gpu.AcMhz) ?? fallbackIndex;
            var dcIndex = FindGpuClockIndex(gpu.SelectableClocksMhz, gpu.DcMhz) ?? fallbackIndex;
            rows.Add(BuildProfileGpuMinimumClockSlider(QuickSettingsRowId.ProfileGpuMinimumClockAc, "Plugged in", acIndex, options, writable));
            rows.Add(BuildProfileGpuMinimumClockSlider(QuickSettingsRowId.ProfileGpuMinimumClockDc, "On battery", dcIndex, options, writable));
        }

        var warning = gpu.UnavailableReason;
        return new QuickSettingsSection(QuickSettingsSectionId.ProfileGpuMinimumClock, "Minimum GPU Clock", rows, warning);
    }

    private static QuickSettingsRow BuildProfileGpuMinimumClockSlider(
        QuickSettingsRowId rowId,
        string label,
        int index,
        IReadOnlyList<QuickSettingsDiscreteOption> options,
        bool writable) =>
        new(rowId, label, QuickSettingsControlKind.Slider,
            Available: options.Count > 0, Writable: writable && options.Count > 0,
            Value: options.Count > 0 ? QuickSettingsValue.Integer(index) : null,
            SliderSpec: new QuickSettingsSliderSpec(QuickSettingsSliderKind.Discrete, Options: options),
            CommitPolicy: QuickSettingsCommitPolicy.TrailingDebounce300);

    private static IReadOnlyList<QuickSettingsDiscreteOption> BuildGpuClockOptions(IReadOnlyList<double> clocks) =>
        clocks.Select((mhz, index) => new QuickSettingsDiscreteOption(index, FormatGpuClockMhz(mhz))).ToArray();

    internal static string FormatGpuClockMhz(double mhz) => $"{mhz.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} MHz";

    private static int? FindGpuClockIndex(IReadOnlyList<double> clocks, double? target)
    {
        if (target is not { } value) return null;
        for (var index = 0; index < clocks.Count; index++)
            if (Math.Abs(clocks[index] - value) <= 0.1) return index;
        return null;
    }

    private static QuickSettingsRow BuildProfileFpsLimitSlider(QuickSettingsRowId rowId, string label, int fps, bool writable) =>
        new(rowId, label, QuickSettingsControlKind.Slider,
            Available: true, Writable: writable, Value: QuickSettingsValue.Integer(fps),
            SliderSpec: new QuickSettingsSliderSpec(QuickSettingsSliderKind.Numeric, Minimum: 40, Maximum: 120, Step: 1, Suffix: " FPS"),
            CommitPolicy: QuickSettingsCommitPolicy.TrailingDebounce300);

    private static QuickSettingsSection BuildProfileResolutionSection(ProfileQuickSettingsProjection snapshot)
    {
        var current = snapshot.Resolution switch
        {
            null => 0,
            { Width: 1920, Height: 1200 } => 1,
            { Width: 1920, Height: 1080 } => 2,
            { Width: 1680, Height: 1050 } => 3,
            { Width: 1440, Height: 900 } => 4,
            _ => -1,
        };
        if (current < 0)
        {
            return new QuickSettingsSection(QuickSettingsSectionId.ProfileResolution, "Resolution",
                [new QuickSettingsRow(QuickSettingsRowId.ProfileResolution, "Resolution", QuickSettingsControlKind.Slider,
                    Available: false, Writable: false, Value: null,
                    SliderSpec: new QuickSettingsSliderSpec(QuickSettingsSliderKind.Discrete, Options: ProfileResolutionOptions),
                    CommitPolicy: QuickSettingsCommitPolicy.Immediate)],
                "The saved display resolution is not supported by this UI.");
        }

        return new QuickSettingsSection(QuickSettingsSectionId.ProfileResolution, "Resolution",
            [new QuickSettingsRow(QuickSettingsRowId.ProfileResolution, "Resolution", QuickSettingsControlKind.Slider,
                Available: true, Writable: snapshot.PersistenceWritable, Value: QuickSettingsValue.Integer(current),
                SliderSpec: new QuickSettingsSliderSpec(QuickSettingsSliderKind.Discrete, Options: ProfileResolutionOptions),
                CommitPolicy: QuickSettingsCommitPolicy.Immediate)]);
    }

    private static QuickSettingsSection BuildTdpSection(FrontendTdpSnapshot tdp)
    {
        var rows = new List<QuickSettingsRow>
        {
            new(QuickSettingsRowId.DeviceTdpEnabled, "TDP Control", QuickSettingsControlKind.Toggle,
                Available: tdp.Available,
                Writable: tdp.Available && tdp.PersistenceWritable,
                Value: tdp.Available ? QuickSettingsValue.Boolean(tdp.Configuration?.Enabled ?? false) : null,
                SliderSpec: null,
                CommitPolicy: QuickSettingsCommitPolicy.Immediate),
        };

        // Section 18.3: never fabricate slider values/ranges when TDP is enabled but the
        // configuration/limits snapshot is missing or invalid -- simply omit the numeric rows.
        if (tdp.Available && tdp.Configuration is { Enabled: true } configuration && tdp.Limits is { } limits)
        {
            rows.Add(BuildTdpSlider(QuickSettingsRowId.DeviceTdpAcPl1, "Plugged in · PL1", configuration.Ac.Pl1Watts, limits.Pl1MinimumWatts, limits.Pl1MaximumWatts, tdp.PersistenceWritable));
            rows.Add(BuildTdpSlider(QuickSettingsRowId.DeviceTdpAcPl2, "Plugged in · PL2", configuration.Ac.Pl2Watts, limits.Pl2MinimumWatts, limits.Pl2MaximumWatts, tdp.PersistenceWritable));
            rows.Add(BuildTdpSlider(QuickSettingsRowId.DeviceTdpDcPl1, "On battery · PL1", configuration.Dc.Pl1Watts, limits.Pl1MinimumWatts, limits.Pl1MaximumWatts, tdp.PersistenceWritable));
            rows.Add(BuildTdpSlider(QuickSettingsRowId.DeviceTdpDcPl2, "On battery · PL2", configuration.Dc.Pl2Watts, limits.Pl2MinimumWatts, limits.Pl2MaximumWatts, tdp.PersistenceWritable));
        }

        return new QuickSettingsSection(QuickSettingsSectionId.DeviceTdp, "TDP Control", rows);
    }

    private static QuickSettingsRow BuildTdpSlider(QuickSettingsRowId rowId, string label, int currentWatts, int minimumWatts, int maximumWatts, bool writable) =>
        new(rowId, label, QuickSettingsControlKind.Slider,
            Available: true,
            Writable: writable,
            Value: QuickSettingsValue.Integer(currentWatts),
            SliderSpec: new QuickSettingsSliderSpec(QuickSettingsSliderKind.Numeric, minimumWatts, maximumWatts, Step: 1, Suffix: "W"),
            CommitPolicy: QuickSettingsCommitPolicy.TrailingDebounce300,
            CommitGroupId: QuickSettingsCommitGroupId.DeviceTdpConfiguration);

    /// <summary>Known proven PL1/PL2 gap policy (work order section 13.1/SF-V2-08 section 6.4). Any
    /// other limit shape emits no linked constraint -- the existing typed TDP Runtime remains the
    /// final validity authority. Shared between Device and Profile so the tuple switch is not
    /// duplicated (SF-V2-08 section 6.4).</summary>
    private static int GetKnownTdpGap(FrontendTdpLimits limits) => (limits.Pl1MinimumWatts, limits.Pl1MaximumWatts, limits.Pl2MinimumWatts, limits.Pl2MaximumWatts) switch
    {
        (8, 30, 8, 37) => 1,
        (8, 35, 8, 45) => 2,
        _ => 0,
    };

    private static IReadOnlyList<QuickSettingsLinkedSliderConstraint> BuildDeviceTdpLinkedConstraints(FrontendTdpSnapshot tdp)
    {
        if (tdp.Limits is not { } limits) return [];
        var gap = GetKnownTdpGap(limits);
        if (gap <= 0) return [];

        return
        [
            new QuickSettingsLinkedSliderConstraint(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsRowId.DeviceTdpAcPl2, gap),
            new QuickSettingsLinkedSliderConstraint(QuickSettingsRowId.DeviceTdpDcPl1, QuickSettingsRowId.DeviceTdpDcPl2, gap),
        ];
    }

    private static IReadOnlyList<QuickSettingsLinkedSliderConstraint> BuildProfileTdpLinkedConstraints(FrontendTdpLimits limits)
    {
        var gap = GetKnownTdpGap(limits);
        if (gap <= 0) return [];

        return
        [
            new QuickSettingsLinkedSliderConstraint(QuickSettingsRowId.ProfileTdpAcPl1, QuickSettingsRowId.ProfileTdpAcPl2, gap),
            new QuickSettingsLinkedSliderConstraint(QuickSettingsRowId.ProfileTdpDcPl1, QuickSettingsRowId.ProfileTdpDcPl2, gap),
        ];
    }

    private static QuickSettingsSection BuildCpuBoostSection(FrontendCpuBoostSnapshot cpuBoost)
    {
        var available = IsCpuBoostAvailable(cpuBoost);
        var rows = new List<QuickSettingsRow>
        {
            new(QuickSettingsRowId.DeviceCpuBoostEnabled, "CPU Boost", QuickSettingsControlKind.Toggle,
                Available: available,
                Writable: available && cpuBoost.PersistenceWritable,
                Value: available ? QuickSettingsValue.Boolean(cpuBoost.Enabled) : null,
                SliderSpec: null,
                CommitPolicy: QuickSettingsCommitPolicy.Immediate),
        };

        if (available && cpuBoost.Enabled)
        {
            rows.Add(BuildCpuBoostSlider(QuickSettingsRowId.DeviceCpuBoostAc, "Plugged in", cpuBoost.Ac, cpuBoost.PersistenceWritable));
            rows.Add(BuildCpuBoostSlider(QuickSettingsRowId.DeviceCpuBoostDc, "On battery", cpuBoost.Dc, cpuBoost.PersistenceWritable));
        }

        return new QuickSettingsSection(QuickSettingsSectionId.DeviceCpuBoost, "CPU Boost", rows);
    }

    private static QuickSettingsRow BuildCpuBoostSlider(QuickSettingsRowId rowId, string label, FrontendCpuBoostSideSnapshot side, bool persistenceWritable)
    {
        // Section 16.2: Desired wins; else a Known Current is the fallback; never fabricate a value.
        var mode = side.Desired ?? (side.CurrentStatus == FrontendCpuBoostReadStatus.Known ? side.Current : null);
        var hasValue = mode is not null;
        return new(rowId, label, QuickSettingsControlKind.Slider,
            Available: hasValue,
            Writable: hasValue && persistenceWritable,
            Value: hasValue ? QuickSettingsValue.Integer((int)mode!.Value) : null,
            SliderSpec: new QuickSettingsSliderSpec(QuickSettingsSliderKind.Discrete, Options: CpuBoostDiscreteOptions),
            CommitPolicy: QuickSettingsCommitPolicy.TrailingDebounce300);
    }

    /// <summary>Section 16.3's "no meaningful state to represent" case: the Unavailable sentinel is
    /// both sides <see cref="FrontendCpuBoostReadStatus.Unavailable"/>, not writable, and not enabled.
    /// A real (even fully-disabled) snapshot always differs from that sentinel in at least one field.</summary>
    private static bool IsCpuBoostAvailable(FrontendCpuBoostSnapshot cpuBoost) =>
        cpuBoost.PersistenceWritable || cpuBoost.Enabled
        || cpuBoost.Ac.CurrentStatus != FrontendCpuBoostReadStatus.Unavailable
        || cpuBoost.Dc.CurrentStatus != FrontendCpuBoostReadStatus.Unavailable;

    private static QuickSettingsSection BuildPowerModeSection(FrontendPowerModeSnapshot powerMode)
    {
        var available = IsPowerModeAvailable(powerMode);
        var writable = available && powerMode.PersistenceWritable && powerMode.Ac.Desired is not null && powerMode.Dc.Desired is not null;
        var rows = new List<QuickSettingsRow>
        {
            new(QuickSettingsRowId.DevicePowerModeEnabled, "Windows Power Mode", QuickSettingsControlKind.Toggle,
                Available: available,
                Writable: writable,
                Value: available ? QuickSettingsValue.Boolean(powerMode.Enabled) : null,
                SliderSpec: null,
                CommitPolicy: QuickSettingsCommitPolicy.Immediate),
        };

        if (available && powerMode.Enabled)
        {
            rows.Add(BuildPowerModeSlider(QuickSettingsRowId.DevicePowerModeAc, "Plugged in", powerMode.Ac, writable));
            rows.Add(BuildPowerModeSlider(QuickSettingsRowId.DevicePowerModeDc, "On battery", powerMode.Dc, writable));
        }

        return new QuickSettingsSection(QuickSettingsSectionId.DevicePowerMode, "Windows Power Mode", rows);
    }

    private static QuickSettingsRow BuildPowerModeSlider(QuickSettingsRowId rowId, string label, FrontendPowerModeSideSnapshot side, bool writable)
    {
        var mode = side.Desired ?? (side.CurrentStatus == FrontendPowerModeReadStatus.Known ? side.Current : null);
        var hasValue = mode is not null;
        return new(rowId, label, QuickSettingsControlKind.Slider,
            Available: hasValue,
            Writable: hasValue && writable,
            Value: hasValue ? QuickSettingsValue.Integer((int)mode!.Value) : null,
            SliderSpec: new QuickSettingsSliderSpec(QuickSettingsSliderKind.Discrete, Options: PowerModeDiscreteOptions),
            CommitPolicy: QuickSettingsCommitPolicy.TrailingDebounce300);
    }

    private static bool IsPowerModeAvailable(FrontendPowerModeSnapshot powerMode) =>
        powerMode.PersistenceWritable || powerMode.Enabled
        || powerMode.Ac.CurrentStatus != FrontendPowerModeReadStatus.Unavailable
        || powerMode.Dc.CurrentStatus != FrontendPowerModeReadStatus.Unavailable;
}
