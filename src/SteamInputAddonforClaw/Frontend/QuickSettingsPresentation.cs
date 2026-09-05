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

        return new QuickSettingsPageSnapshot(QuickSettingsPageId.Device, AppId: null, Available: true, Message: null, sections, BuildTdpLinkedConstraints(snapshot.Tdp));
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

    /// <summary>Known proven PL1/PL2 gap policy (work order section 13.1). Any other limit shape emits
    /// no linked constraint -- the existing typed TDP Runtime remains the final validity authority.</summary>
    private static IReadOnlyList<QuickSettingsLinkedSliderConstraint> BuildTdpLinkedConstraints(FrontendTdpSnapshot tdp)
    {
        if (tdp.Limits is not { } limits) return [];

        var gap = (limits.Pl1MinimumWatts, limits.Pl1MaximumWatts, limits.Pl2MinimumWatts, limits.Pl2MaximumWatts) switch
        {
            (8, 30, 8, 37) => 1,
            (8, 35, 8, 45) => 2,
            _ => 0,
        };
        if (gap <= 0) return [];

        return
        [
            new QuickSettingsLinkedSliderConstraint(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsRowId.DeviceTdpAcPl2, gap),
            new QuickSettingsLinkedSliderConstraint(QuickSettingsRowId.DeviceTdpDcPl1, QuickSettingsRowId.DeviceTdpDcPl2, gap),
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
