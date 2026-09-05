using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Shared Frontend V2, SF-V2-03: <see cref="QuickSettingsPresentation.BuildDevice"/> is one
/// pure, stateless mapping from <see cref="FrontendDeviceQuickSettingsSnapshot"/> to the closed shared
/// Quick Settings product model. It must reproduce the exact section/row order, labels, discrete
/// option vocabulary, commit policy, TDP commit group, and linked-slider gap policy from work order
/// sections 15-19, and must isolate one child's failure from the others.</summary>
public sealed class QuickSettingsPresentationTests
{
    private static readonly FrontendTdpLimits GapOneLimits = new(8, 30, 8, 37);
    private static readonly FrontendTdpLimits GapTwoLimits = new(8, 35, 8, 45);
    private static readonly FrontendTdpLimits NoGapLimits = new(10, 25, 10, 40);

    [Fact]
    public void Exact_device_section_order()
    {
        var page = QuickSettingsPresentation.BuildDevice(EnabledSnapshot());

        Assert.Collection(page.Sections,
            s => Assert.Equal(QuickSettingsSectionId.DeviceTdp, s.SectionId),
            s => Assert.Equal(QuickSettingsSectionId.DeviceCpuBoost, s.SectionId),
            s => Assert.Equal(QuickSettingsSectionId.DevicePowerMode, s.SectionId));
    }

    [Fact]
    public void Exact_device_row_order_when_all_enabled()
    {
        var page = QuickSettingsPresentation.BuildDevice(EnabledSnapshot());

        var tdp = page.Sections.Single(s => s.SectionId == QuickSettingsSectionId.DeviceTdp);
        Assert.Equal([QuickSettingsRowId.DeviceTdpEnabled, QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsRowId.DeviceTdpAcPl2, QuickSettingsRowId.DeviceTdpDcPl1, QuickSettingsRowId.DeviceTdpDcPl2],
            tdp.Rows.Select(r => r.RowId).ToArray());

        var cpuBoost = page.Sections.Single(s => s.SectionId == QuickSettingsSectionId.DeviceCpuBoost);
        Assert.Equal([QuickSettingsRowId.DeviceCpuBoostEnabled, QuickSettingsRowId.DeviceCpuBoostAc, QuickSettingsRowId.DeviceCpuBoostDc],
            cpuBoost.Rows.Select(r => r.RowId).ToArray());

        var powerMode = page.Sections.Single(s => s.SectionId == QuickSettingsSectionId.DevicePowerMode);
        Assert.Equal([QuickSettingsRowId.DevicePowerModeEnabled, QuickSettingsRowId.DevicePowerModeAc, QuickSettingsRowId.DevicePowerModeDc],
            powerMode.Rows.Select(r => r.RowId).ToArray());
    }

    [Fact]
    public void Parent_disabled_omits_child_rows()
    {
        var snapshot = new FrontendDeviceQuickSettingsSnapshot(
            new FrontendCpuBoostSnapshot(new(FrontendCpuBoostReadStatus.Known, CpuBoostMode.Aggressive, CpuBoostMode.Aggressive), new(FrontendCpuBoostReadStatus.Known, CpuBoostMode.Disabled, CpuBoostMode.Disabled), Enabled: false, PersistenceWritable: true, LastFailure: null),
            new FrontendTdpSnapshot(true, true, new FrontendTdpConfiguration(false, new(20, 25), new(20, 25)), GapOneLimits),
            new FrontendPowerModeSnapshot(new(FrontendPowerModeReadStatus.Known, WindowsPowerMode.Balanced, WindowsPowerMode.Balanced), new(FrontendPowerModeReadStatus.Known, WindowsPowerMode.Balanced, WindowsPowerMode.Balanced), Enabled: false, PersistenceWritable: true, LastFailure: null));

        var page = QuickSettingsPresentation.BuildDevice(snapshot);

        Assert.Equal([QuickSettingsRowId.DeviceTdpEnabled], page.Sections.Single(s => s.SectionId == QuickSettingsSectionId.DeviceTdp).Rows.Select(r => r.RowId).ToArray());
        Assert.Equal([QuickSettingsRowId.DeviceCpuBoostEnabled], page.Sections.Single(s => s.SectionId == QuickSettingsSectionId.DeviceCpuBoost).Rows.Select(r => r.RowId).ToArray());
        Assert.Equal([QuickSettingsRowId.DevicePowerModeEnabled], page.Sections.Single(s => s.SectionId == QuickSettingsSectionId.DevicePowerMode).Rows.Select(r => r.RowId).ToArray());
    }

    [Fact]
    public void Cpu_boost_discrete_options_are_exact_and_ordered()
    {
        var page = QuickSettingsPresentation.BuildDevice(EnabledSnapshot());
        var acRow = FindRow(page, QuickSettingsRowId.DeviceCpuBoostAc);

        var expected = new (int, string)[]
        {
            (0, "Disabled"), (1, "Enabled"), (2, "Aggressive"), (3, "Efficient Enabled"),
            (4, "Efficient Aggressive"), (5, "Aggressive At Guaranteed"), (6, "Efficient Aggressive At Guaranteed"),
        };
        Assert.Equal(expected, acRow.SliderSpec!.Options!.Select(o => (o.Value, o.Label)).ToArray());
    }

    [Fact]
    public void Power_mode_discrete_options_are_exact_and_ordered()
    {
        var page = QuickSettingsPresentation.BuildDevice(EnabledSnapshot());
        var acRow = FindRow(page, QuickSettingsRowId.DevicePowerModeAc);

        var expected = new (int, string)[] { (0, "Best power efficiency"), (1, "Balanced"), (2, "Best performance") };
        Assert.Equal(expected, acRow.SliderSpec!.Options!.Select(o => (o.Value, o.Label)).ToArray());
    }

    [Fact]
    public void Toggles_are_immediate_and_sliders_are_two_second_trailing_debounce()
    {
        var page = QuickSettingsPresentation.BuildDevice(EnabledSnapshot());

        foreach (var toggleId in new[] { QuickSettingsRowId.DeviceTdpEnabled, QuickSettingsRowId.DeviceCpuBoostEnabled, QuickSettingsRowId.DevicePowerModeEnabled })
            Assert.Equal(QuickSettingsCommitPolicy.Immediate, FindRow(page, toggleId).CommitPolicy);

        foreach (var sliderId in new[] { QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsRowId.DeviceTdpAcPl2, QuickSettingsRowId.DeviceTdpDcPl1, QuickSettingsRowId.DeviceTdpDcPl2, QuickSettingsRowId.DeviceCpuBoostAc, QuickSettingsRowId.DeviceCpuBoostDc, QuickSettingsRowId.DevicePowerModeAc, QuickSettingsRowId.DevicePowerModeDc })
            Assert.Equal(QuickSettingsCommitPolicy.TrailingDebounce2000, FindRow(page, sliderId).CommitPolicy);
    }

    [Fact]
    public void Tdp_commit_group_covers_exactly_the_four_numeric_rows()
    {
        var page = QuickSettingsPresentation.BuildDevice(EnabledSnapshot());

        foreach (var rowId in new[] { QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsRowId.DeviceTdpAcPl2, QuickSettingsRowId.DeviceTdpDcPl1, QuickSettingsRowId.DeviceTdpDcPl2 })
            Assert.Equal(QuickSettingsCommitGroupId.DeviceTdpConfiguration, FindRow(page, rowId).CommitGroupId);

        Assert.Null(FindRow(page, QuickSettingsRowId.DeviceTdpEnabled).CommitGroupId);
        foreach (var rowId in new[] { QuickSettingsRowId.DeviceCpuBoostAc, QuickSettingsRowId.DeviceCpuBoostDc, QuickSettingsRowId.DevicePowerModeAc, QuickSettingsRowId.DevicePowerModeDc })
            Assert.Null(FindRow(page, rowId).CommitGroupId);
    }

    [Fact]
    public void Tdp_numeric_ranges_come_from_runtime_limits()
    {
        var page = QuickSettingsPresentation.BuildDevice(EnabledSnapshot());

        var pl1 = FindRow(page, QuickSettingsRowId.DeviceTdpAcPl1).SliderSpec!;
        var pl2 = FindRow(page, QuickSettingsRowId.DeviceTdpAcPl2).SliderSpec!;
        Assert.Equal((GapOneLimits.Pl1MinimumWatts, GapOneLimits.Pl1MaximumWatts, 1), (pl1.Minimum, pl1.Maximum, pl1.Step));
        Assert.Equal((GapOneLimits.Pl2MinimumWatts, GapOneLimits.Pl2MaximumWatts, 1), (pl2.Minimum, pl2.Maximum, pl2.Step));
    }

    [Theory]
    [MemberData(nameof(GapCases))]
    public void Known_tdp_gap_policies_are_emitted(FrontendTdpLimits limits, int expectedGap)
    {
        var snapshot = EnabledSnapshot(limits);
        var page = QuickSettingsPresentation.BuildDevice(snapshot);

        if (expectedGap <= 0)
        {
            Assert.Empty(page.LinkedSliderConstraints);
            return;
        }

        Assert.Equal(2, page.LinkedSliderConstraints.Count);
        Assert.Contains(page.LinkedSliderConstraints, c => c.LowerRowId == QuickSettingsRowId.DeviceTdpAcPl1 && c.UpperRowId == QuickSettingsRowId.DeviceTdpAcPl2 && c.MinimumGap == expectedGap);
        Assert.Contains(page.LinkedSliderConstraints, c => c.LowerRowId == QuickSettingsRowId.DeviceTdpDcPl1 && c.UpperRowId == QuickSettingsRowId.DeviceTdpDcPl2 && c.MinimumGap == expectedGap);
    }

    public static IEnumerable<object[]> GapCases()
    {
        yield return [GapOneLimits, 1];
        yield return [GapTwoLimits, 2];
        yield return [NoGapLimits, 0];
    }

    [Fact]
    public void Tdp_unavailable_does_not_affect_cpu_boost_or_power_mode()
    {
        var snapshot = new FrontendDeviceQuickSettingsSnapshot(
            EnabledSnapshot().CpuBoost,
            FrontendTdpSnapshot.Unavailable,
            EnabledSnapshot().PowerMode);

        var page = QuickSettingsPresentation.BuildDevice(snapshot);

        Assert.False(FindRow(page, QuickSettingsRowId.DeviceTdpEnabled).Available);
        Assert.True(FindRow(page, QuickSettingsRowId.DeviceCpuBoostEnabled).Available);
        Assert.True(FindRow(page, QuickSettingsRowId.DevicePowerModeEnabled).Available);
    }

    [Fact]
    public void Cpu_boost_unavailable_does_not_affect_tdp_or_power_mode()
    {
        var snapshot = new FrontendDeviceQuickSettingsSnapshot(
            FrontendCpuBoostSnapshot.Unavailable,
            EnabledSnapshot().Tdp,
            EnabledSnapshot().PowerMode);

        var page = QuickSettingsPresentation.BuildDevice(snapshot);

        Assert.False(FindRow(page, QuickSettingsRowId.DeviceCpuBoostEnabled).Available);
        Assert.True(FindRow(page, QuickSettingsRowId.DeviceTdpEnabled).Available);
        Assert.True(FindRow(page, QuickSettingsRowId.DevicePowerModeEnabled).Available);
    }

    [Fact]
    public void Power_mode_unavailable_does_not_affect_tdp_or_cpu_boost()
    {
        var snapshot = new FrontendDeviceQuickSettingsSnapshot(
            EnabledSnapshot().CpuBoost,
            EnabledSnapshot().Tdp,
            FrontendPowerModeSnapshot.Unavailable);

        var page = QuickSettingsPresentation.BuildDevice(snapshot);

        Assert.False(FindRow(page, QuickSettingsRowId.DevicePowerModeEnabled).Available);
        Assert.True(FindRow(page, QuickSettingsRowId.DeviceTdpEnabled).Available);
        Assert.True(FindRow(page, QuickSettingsRowId.DeviceCpuBoostEnabled).Available);
    }

    [Fact]
    public void Desired_wins_over_current_for_cpu_boost_and_power_mode()
    {
        var snapshot = new FrontendDeviceQuickSettingsSnapshot(
            new FrontendCpuBoostSnapshot(
                new(FrontendCpuBoostReadStatus.Known, CpuBoostMode.Disabled, CpuBoostMode.Aggressive),
                new(FrontendCpuBoostReadStatus.Known, CpuBoostMode.Disabled, CpuBoostMode.Enabled),
                Enabled: true, PersistenceWritable: true, LastFailure: null),
            EnabledSnapshot().Tdp,
            new FrontendPowerModeSnapshot(
                new(FrontendPowerModeReadStatus.Known, WindowsPowerMode.Balanced, WindowsPowerMode.BestPerformance),
                new(FrontendPowerModeReadStatus.Known, WindowsPowerMode.Balanced, WindowsPowerMode.BestPowerEfficiency),
                Enabled: true, PersistenceWritable: true, LastFailure: null));

        var page = QuickSettingsPresentation.BuildDevice(snapshot);

        Assert.Equal((int)CpuBoostMode.Aggressive, FindRow(page, QuickSettingsRowId.DeviceCpuBoostAc).Value!.IntegerValue);
        Assert.Equal((int)CpuBoostMode.Enabled, FindRow(page, QuickSettingsRowId.DeviceCpuBoostDc).Value!.IntegerValue);
        Assert.Equal((int)WindowsPowerMode.BestPerformance, FindRow(page, QuickSettingsRowId.DevicePowerModeAc).Value!.IntegerValue);
        Assert.Equal((int)WindowsPowerMode.BestPowerEfficiency, FindRow(page, QuickSettingsRowId.DevicePowerModeDc).Value!.IntegerValue);
    }

    [Fact]
    public void Known_current_is_the_fallback_when_desired_is_absent()
    {
        var snapshot = new FrontendDeviceQuickSettingsSnapshot(
            new FrontendCpuBoostSnapshot(
                new(FrontendCpuBoostReadStatus.Known, CpuBoostMode.Aggressive, null),
                new(FrontendCpuBoostReadStatus.Known, CpuBoostMode.Disabled, null),
                Enabled: true, PersistenceWritable: true, LastFailure: null),
            EnabledSnapshot().Tdp,
            EnabledSnapshot().PowerMode);

        var page = QuickSettingsPresentation.BuildDevice(snapshot);

        Assert.Equal((int)CpuBoostMode.Aggressive, FindRow(page, QuickSettingsRowId.DeviceCpuBoostAc).Value!.IntegerValue);
        Assert.Equal((int)CpuBoostMode.Disabled, FindRow(page, QuickSettingsRowId.DeviceCpuBoostDc).Value!.IntegerValue);
    }

    [Fact]
    public void Unknown_status_never_fabricates_a_slider_value()
    {
        var snapshot = new FrontendDeviceQuickSettingsSnapshot(
            new FrontendCpuBoostSnapshot(
                new(FrontendCpuBoostReadStatus.Unknown, null, null),
                new(FrontendCpuBoostReadStatus.Known, CpuBoostMode.Disabled, null),
                Enabled: true, PersistenceWritable: true, LastFailure: null),
            EnabledSnapshot().Tdp,
            EnabledSnapshot().PowerMode);

        var page = QuickSettingsPresentation.BuildDevice(snapshot);

        var acRow = FindRow(page, QuickSettingsRowId.DeviceCpuBoostAc);
        Assert.False(acRow.Available);
        Assert.False(acRow.Writable);
        Assert.Null(acRow.Value);
    }

    [Fact]
    public void Tdp_enabled_but_missing_configuration_omits_slider_rows_without_fabricating_values()
    {
        var snapshot = new FrontendDeviceQuickSettingsSnapshot(
            EnabledSnapshot().CpuBoost,
            new FrontendTdpSnapshot(true, true, Configuration: null, Limits: GapOneLimits),
            EnabledSnapshot().PowerMode);

        var page = QuickSettingsPresentation.BuildDevice(snapshot);

        Assert.Equal([QuickSettingsRowId.DeviceTdpEnabled], page.Sections.Single(s => s.SectionId == QuickSettingsSectionId.DeviceTdp).Rows.Select(r => r.RowId).ToArray());
    }

    [Fact]
    public void Power_mode_not_writable_unless_both_sides_are_initialized()
    {
        var snapshot = new FrontendDeviceQuickSettingsSnapshot(
            EnabledSnapshot().CpuBoost,
            EnabledSnapshot().Tdp,
            new FrontendPowerModeSnapshot(
                new(FrontendPowerModeReadStatus.Known, WindowsPowerMode.Balanced, null),
                new(FrontendPowerModeReadStatus.Known, WindowsPowerMode.Balanced, WindowsPowerMode.Balanced),
                Enabled: true, PersistenceWritable: true, LastFailure: null));

        var page = QuickSettingsPresentation.BuildDevice(snapshot);

        Assert.False(FindRow(page, QuickSettingsRowId.DevicePowerModeEnabled).Writable);
    }

    private static QuickSettingsRow FindRow(QuickSettingsPageSnapshot page, QuickSettingsRowId rowId) =>
        page.Sections.SelectMany(s => s.Rows).Single(r => r.RowId == rowId);

    private static FrontendDeviceQuickSettingsSnapshot EnabledSnapshot(FrontendTdpLimits? limits = null) => new(
        new FrontendCpuBoostSnapshot(
            new(FrontendCpuBoostReadStatus.Known, CpuBoostMode.Aggressive, CpuBoostMode.Aggressive),
            new(FrontendCpuBoostReadStatus.Known, CpuBoostMode.Disabled, CpuBoostMode.Disabled),
            Enabled: true, PersistenceWritable: true, LastFailure: null),
        new FrontendTdpSnapshot(true, true, new FrontendTdpConfiguration(true, new(20, 25), new(20, 25)), limits ?? GapOneLimits),
        new FrontendPowerModeSnapshot(
            new(FrontendPowerModeReadStatus.Known, WindowsPowerMode.Balanced, WindowsPowerMode.Balanced),
            new(FrontendPowerModeReadStatus.Known, WindowsPowerMode.Balanced, WindowsPowerMode.Balanced),
            Enabled: true, PersistenceWritable: true, LastFailure: null));
}
