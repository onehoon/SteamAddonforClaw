using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Frontend;

/// <summary>Shared Quick Settings Device page projection (Shared Frontend V2, SF-V2-03 section 14):
/// one small stateless mapping from the already-captured <see cref="FrontendDeviceQuickSettingsSnapshot"/>
/// truth to the closed shared product rows. Never reads hardware, persists, mutates, subscribes, or
/// caches -- a pure function of its input.</summary>
internal static class QuickSettingsPresentation
{
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

    internal static readonly IReadOnlyList<QuickSettingsDiscreteOption> CpuBoostDiscreteOptions =
        [.. CpuBoostOptions.Select(o => new QuickSettingsDiscreteOption((int)o.Mode, o.Label))];

    internal static readonly IReadOnlyList<QuickSettingsDiscreteOption> PowerModeDiscreteOptions =
        [.. PowerModeOptions.Select(o => new QuickSettingsDiscreteOption((int)o.Mode, o.Label))];

    /// <summary>Frozen Device section/row order (work order section 15): TDP, then CPU Boost, then
    /// Windows Power Mode. One child being unavailable never affects the others (section 19).</summary>
    internal static QuickSettingsPageSnapshot BuildDevice(FrontendDeviceQuickSettingsSnapshot snapshot)
    {
        IReadOnlyList<QuickSettingsSection> sections =
        [
            BuildTdpSection(snapshot.Tdp),
            BuildCpuBoostSection(snapshot.CpuBoost),
            BuildPowerModeSection(snapshot.PowerMode),
        ];

        return new QuickSettingsPageSnapshot(QuickSettingsPageId.Device, AppId: null, Available: true, Message: null, sections, BuildDeviceTdpLinkedConstraints(snapshot.Tdp));
    }

    /// <summary>Shared Quick Settings Profile page projection (SF-V2-08 section 6): the exact current
    /// visible QAM Profile product (General/TDP/CPU Boost/optional Power Mode), frozen from the
    /// pre-migration qam.js policy. Called only for a valid active target -- an unavailable/stale
    /// Profile context is represented separately via <see cref="QuickSettingsPageSnapshot.Unavailable"/>,
    /// never fabricated here.</summary>
    internal static QuickSettingsPageSnapshot BuildProfile(FrontendGameProfileSnapshot snapshot)
    {
        var sections = new List<QuickSettingsSection> { BuildProfileGeneralSection(snapshot) };
        if (snapshot.Limits is not null) sections.Add(BuildProfileTdpSection(snapshot));
        sections.Add(BuildProfileCpuBoostSection(snapshot));
        if (snapshot.PowerMode is not null) sections.Add(BuildProfilePowerModeSection(snapshot));

        var linkedConstraints = snapshot.Limits is { } limits ? BuildProfileTdpLinkedConstraints(limits) : [];
        return new QuickSettingsPageSnapshot(QuickSettingsPageId.Profile, snapshot.AppId, Available: true, Message: null, sections, linkedConstraints);
    }

    private static QuickSettingsSection BuildProfileGeneralSection(FrontendGameProfileSnapshot snapshot)
    {
        var label = string.IsNullOrWhiteSpace(snapshot.DisplayName) ? $"Game {snapshot.AppId}" : snapshot.DisplayName;
        var row = new QuickSettingsRow(QuickSettingsRowId.ProfileEnabled, "Profile", QuickSettingsControlKind.Toggle,
            Available: true,
            Writable: snapshot.PersistenceWritable,
            Value: QuickSettingsValue.Boolean(snapshot.Enabled),
            SliderSpec: null,
            CommitPolicy: QuickSettingsCommitPolicy.Immediate);
        return new QuickSettingsSection(QuickSettingsSectionId.ProfileGeneral, label, [row]);
    }

    private static QuickSettingsSection BuildProfileTdpSection(FrontendGameProfileSnapshot snapshot)
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

        return new QuickSettingsSection(QuickSettingsSectionId.ProfileTdp, null, rows);
    }

    // Section 6.3: unlike Device, the Profile slider keeps a null/empty suffix for parity with the
    // pre-migration Steam native slider's plain-numeric-watts presentation.
    private static QuickSettingsRow BuildProfileTdpSlider(QuickSettingsRowId rowId, string label, int currentWatts, int minimumWatts, int maximumWatts, bool writable) =>
        new(rowId, label, QuickSettingsControlKind.Slider,
            Available: true,
            Writable: writable,
            Value: QuickSettingsValue.Integer(currentWatts),
            SliderSpec: new QuickSettingsSliderSpec(QuickSettingsSliderKind.Numeric, minimumWatts, maximumWatts, Step: 1, Suffix: null),
            CommitPolicy: QuickSettingsCommitPolicy.TrailingDebounce2000,
            CommitGroupId: QuickSettingsCommitGroupId.ProfileTdpConfiguration);

    private static QuickSettingsSection BuildProfileCpuBoostSection(FrontendGameProfileSnapshot snapshot)
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

        return new QuickSettingsSection(QuickSettingsSectionId.ProfileCpuBoost, null, rows);
    }

    private static QuickSettingsRow BuildProfileCpuBoostSlider(QuickSettingsRowId rowId, string label, CpuBoostMode mode, bool writable) =>
        new(rowId, label, QuickSettingsControlKind.Slider,
            Available: true,
            Writable: writable,
            Value: QuickSettingsValue.Integer((int)mode),
            SliderSpec: new QuickSettingsSliderSpec(QuickSettingsSliderKind.Discrete, Options: CpuBoostDiscreteOptions),
            CommitPolicy: QuickSettingsCommitPolicy.TrailingDebounce2000);

    private static QuickSettingsSection BuildProfilePowerModeSection(FrontendGameProfileSnapshot snapshot)
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

        return new QuickSettingsSection(QuickSettingsSectionId.ProfilePowerMode, null, rows);
    }

    private static QuickSettingsRow BuildProfilePowerModeSlider(QuickSettingsRowId rowId, string label, WindowsPowerMode mode, bool writable) =>
        new(rowId, label, QuickSettingsControlKind.Slider,
            Available: true,
            Writable: writable,
            Value: QuickSettingsValue.Integer((int)mode),
            SliderSpec: new QuickSettingsSliderSpec(QuickSettingsSliderKind.Discrete, Options: PowerModeDiscreteOptions),
            CommitPolicy: QuickSettingsCommitPolicy.TrailingDebounce2000);

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

        return new QuickSettingsSection(QuickSettingsSectionId.DeviceTdp, "TDP", rows);
    }

    private static QuickSettingsRow BuildTdpSlider(QuickSettingsRowId rowId, string label, int currentWatts, int minimumWatts, int maximumWatts, bool writable) =>
        new(rowId, label, QuickSettingsControlKind.Slider,
            Available: true,
            Writable: writable,
            Value: QuickSettingsValue.Integer(currentWatts),
            SliderSpec: new QuickSettingsSliderSpec(QuickSettingsSliderKind.Numeric, minimumWatts, maximumWatts, Step: 1, Suffix: "W"),
            CommitPolicy: QuickSettingsCommitPolicy.TrailingDebounce2000,
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
            CommitPolicy: QuickSettingsCommitPolicy.TrailingDebounce2000);
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
            CommitPolicy: QuickSettingsCommitPolicy.TrailingDebounce2000);
    }

    private static bool IsPowerModeAvailable(FrontendPowerModeSnapshot powerMode) =>
        powerMode.PersistenceWritable || powerMode.Enabled
        || powerMode.Ac.CurrentStatus != FrontendPowerModeReadStatus.Unavailable
        || powerMode.Dc.CurrentStatus != FrontendPowerModeReadStatus.Unavailable;
}
