using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Frontend;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Shared Frontend V2, SF-V2-03: <see cref="QuickSettingsMutationAdapter"/> must validate a
/// closed <see cref="QuickSettingsMutationIntent"/> before invoking any typed Device mutation, invoke
/// exactly one existing typed <see cref="IAddonFrontendControl"/> method for a valid intent, invoke
/// zero for a malformed one, and always return a freshly re-projected page rather than the submitted
/// draft (work order sections 25-28).</summary>
public sealed class QuickSettingsMutationAdapterTests
{
    [Fact]
    public async Task Cpu_boost_enabled_toggle_dispatches_exactly_one_call()
    {
        var control = new RecordingFrontendControl();
        var intent = ToggleIntent(QuickSettingsRowId.DeviceCpuBoostEnabled, true);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Equal(["CpuBoostEnabled:True"], control.Calls);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Cpu_boost_ac_defined_value_dispatches_exactly_one_call()
    {
        var control = new RecordingFrontendControl();
        var intent = IntegerIntent(QuickSettingsRowId.DeviceCpuBoostAc, (int)CpuBoostMode.Aggressive);

        await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Equal([$"CpuBoostAc:{CpuBoostMode.Aggressive}"], control.Calls);
    }

    [Fact]
    public async Task Cpu_boost_dc_undefined_value_invokes_zero_mutations()
    {
        var control = new RecordingFrontendControl();
        var intent = IntegerIntent(QuickSettingsRowId.DeviceCpuBoostDc, 99);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.Calls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Power_mode_ac_defined_value_dispatches_exactly_one_call()
    {
        var control = new RecordingFrontendControl();
        var intent = IntegerIntent(QuickSettingsRowId.DevicePowerModeAc, (int)WindowsPowerMode.BestPerformance);

        await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Equal([$"PowerModeAc:{WindowsPowerMode.BestPerformance}"], control.Calls);
    }

    [Fact]
    public async Task Power_mode_dc_undefined_value_invokes_zero_mutations()
    {
        var control = new RecordingFrontendControl();
        var intent = IntegerIntent(QuickSettingsRowId.DevicePowerModeDc, 42);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.Calls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Power_mode_enabled_toggle_dispatches_exactly_one_call()
    {
        var control = new RecordingFrontendControl();
        var intent = ToggleIntent(QuickSettingsRowId.DevicePowerModeEnabled, false);

        await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Equal(["PowerModeEnabled:False"], control.Calls);
    }

    [Fact]
    public async Task Tdp_enabled_toggle_dispatches_exactly_one_call()
    {
        var control = new RecordingFrontendControl();
        var intent = ToggleIntent(QuickSettingsRowId.DeviceTdpEnabled, true);

        await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Equal(["TdpEnabled:True"], control.Calls);
    }

    [Fact]
    public async Task Toggle_with_wrong_value_kind_invokes_zero_mutations()
    {
        var control = new RecordingFrontendControl();
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Device, null, QuickSettingsRowId.DeviceCpuBoostEnabled,
            [new(QuickSettingsRowId.DeviceCpuBoostEnabled, QuickSettingsValue.Integer(1))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.Calls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Toggle_with_duplicate_value_invokes_zero_mutations()
    {
        var control = new RecordingFrontendControl();
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Device, null, QuickSettingsRowId.DeviceCpuBoostEnabled,
            [new(QuickSettingsRowId.DeviceCpuBoostEnabled, QuickSettingsValue.Boolean(true)), new(QuickSettingsRowId.DeviceCpuBoostEnabled, QuickSettingsValue.Boolean(false))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.Calls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Toggle_with_extra_unrelated_value_invokes_zero_mutations()
    {
        var control = new RecordingFrontendControl();
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Device, null, QuickSettingsRowId.DeviceCpuBoostEnabled,
            [new(QuickSettingsRowId.DeviceCpuBoostEnabled, QuickSettingsValue.Boolean(true)), new(QuickSettingsRowId.DeviceTdpEnabled, QuickSettingsValue.Boolean(true))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.Calls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Toggle_with_mismatched_row_id_invokes_zero_mutations()
    {
        var control = new RecordingFrontendControl();
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Device, null, QuickSettingsRowId.DeviceCpuBoostEnabled,
            [new(QuickSettingsRowId.DeviceTdpEnabled, QuickSettingsValue.Boolean(true))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.Calls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Complete_tdp_group_constructs_exactly_one_configuration_and_calls_set_tdp_once()
    {
        var control = new RecordingFrontendControl();
        var intent = TdpGroupIntent(QuickSettingsRowId.DeviceTdpAcPl1, enabled: true, acPl1: 15, acPl2: 20, dcPl1: 12, dcPl2: 18);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Equal(["TdpConfiguration"], control.Calls);
        Assert.NotNull(control.LastTdpConfiguration);
        Assert.Equal(new FrontendTdpConfiguration(true, new(15, 20), new(12, 18)), control.LastTdpConfiguration);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Tdp_group_missing_a_member_invokes_zero_mutations()
    {
        var control = new RecordingFrontendControl();
        var values = new List<QuickSettingsRowValue>
        {
            new(QuickSettingsRowId.DeviceTdpEnabled, QuickSettingsValue.Boolean(true)),
            new(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(15)),
            new(QuickSettingsRowId.DeviceTdpAcPl2, QuickSettingsValue.Integer(20)),
            new(QuickSettingsRowId.DeviceTdpDcPl1, QuickSettingsValue.Integer(12)),
            // DeviceTdpDcPl2 missing
        };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Device, null, QuickSettingsRowId.DeviceTdpAcPl1, values);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.Calls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Tdp_group_with_duplicate_row_invokes_zero_mutations()
    {
        var control = new RecordingFrontendControl();
        var values = new List<QuickSettingsRowValue>
        {
            new(QuickSettingsRowId.DeviceTdpEnabled, QuickSettingsValue.Boolean(true)),
            new(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(15)),
            new(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(16)),
            new(QuickSettingsRowId.DeviceTdpDcPl1, QuickSettingsValue.Integer(12)),
            new(QuickSettingsRowId.DeviceTdpDcPl2, QuickSettingsValue.Integer(18)),
        };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Device, null, QuickSettingsRowId.DeviceTdpAcPl1, values);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.Calls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Tdp_group_with_extra_unrelated_row_invokes_zero_mutations()
    {
        var control = new RecordingFrontendControl();
        var values = new List<QuickSettingsRowValue>
        {
            new(QuickSettingsRowId.DeviceTdpEnabled, QuickSettingsValue.Boolean(true)),
            new(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(15)),
            new(QuickSettingsRowId.DeviceTdpAcPl2, QuickSettingsValue.Integer(20)),
            new(QuickSettingsRowId.DeviceTdpDcPl1, QuickSettingsValue.Integer(12)),
            new(QuickSettingsRowId.DeviceCpuBoostEnabled, QuickSettingsValue.Boolean(true)),
        };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Device, null, QuickSettingsRowId.DeviceTdpAcPl1, values);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.Calls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Tdp_group_with_disabled_toggle_invokes_zero_mutations()
    {
        var control = new RecordingFrontendControl();
        var intent = TdpGroupIntent(QuickSettingsRowId.DeviceTdpAcPl1, enabled: false, acPl1: 15, acPl2: 20, dcPl1: 12, dcPl2: 18);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.Calls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Tdp_group_with_wrong_typed_value_invokes_zero_mutations()
    {
        var control = new RecordingFrontendControl();
        var values = new List<QuickSettingsRowValue>
        {
            new(QuickSettingsRowId.DeviceTdpEnabled, QuickSettingsValue.Boolean(true)),
            new(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Boolean(true)), // wrong kind
            new(QuickSettingsRowId.DeviceTdpAcPl2, QuickSettingsValue.Integer(20)),
            new(QuickSettingsRowId.DeviceTdpDcPl1, QuickSettingsValue.Integer(12)),
            new(QuickSettingsRowId.DeviceTdpDcPl2, QuickSettingsValue.Integer(18)),
        };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Device, null, QuickSettingsRowId.DeviceTdpAcPl1, values);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.Calls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Device_intent_with_app_id_invokes_zero_mutations()
    {
        var control = new RecordingFrontendControl();
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Device, 12345u, QuickSettingsRowId.DeviceCpuBoostEnabled,
            [new(QuickSettingsRowId.DeviceCpuBoostEnabled, QuickSettingsValue.Boolean(true))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.Calls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Profile_intent_with_no_app_id_invokes_zero_device_mutations_and_returns_unavailable_profile_page()
    {
        var control = new RecordingFrontendControl();
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, null, QuickSettingsRowId.ProfileCpuBoostEnabled,
            [new(QuickSettingsRowId.ProfileCpuBoostEnabled, QuickSettingsValue.Boolean(true))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.Calls);
        Assert.Equal(0, control.CaptureCount);
        Assert.False(result.Succeeded);
        Assert.Equal(QuickSettingsPageId.Profile, result.Page.PageId);
        Assert.False(result.Page.Available);
    }

    [Fact]
    public async Task Profile_intent_with_stale_app_id_invokes_zero_typed_mutations()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111) };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, 999, QuickSettingsRowId.ProfileCpuBoostEnabled,
            [new(QuickSettingsRowId.ProfileCpuBoostEnabled, QuickSettingsValue.Boolean(true))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.ProfileCalls);
        Assert.False(result.Succeeded);
        Assert.Equal(QuickSettingsPageId.Profile, result.Page.PageId);
        Assert.False(result.Page.Available);
        Assert.Equal(999u, result.Page.AppId);
    }

    [Fact]
    public async Task Profile_intent_with_zero_app_id_invokes_zero_typed_mutations()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111) };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, 0, QuickSettingsRowId.ProfileCpuBoostEnabled,
            [new(QuickSettingsRowId.ProfileCpuBoostEnabled, QuickSettingsValue.Boolean(true))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.ProfileCalls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Profile_enabled_toggle_dispatches_exactly_once_with_display_name_forwarded()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111, displayName: "My Game") };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, 111, QuickSettingsRowId.ProfileEnabled,
            [new(QuickSettingsRowId.ProfileEnabled, QuickSettingsValue.Boolean(true))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Equal(["ProfileEnabled:111:True:My Game"], control.ProfileCalls);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Profile_enabled_toggle_with_malformed_value_invokes_zero_typed_mutations()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111) };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, 111, QuickSettingsRowId.ProfileEnabled,
            [new(QuickSettingsRowId.ProfileEnabled, QuickSettingsValue.Integer(1))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.ProfileCalls);
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(QuickSettingsRowId.ProfileTdpEnabled)]
    [InlineData(QuickSettingsRowId.ProfileCpuBoostEnabled)]
    [InlineData(QuickSettingsRowId.ProfilePowerModeEnabled)]
    public async Task Profile_feature_toggle_maps_to_exactly_one_typed_method(QuickSettingsRowId rowId)
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111, enabled: true) };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, 111, rowId, [new(rowId, QuickSettingsValue.Boolean(true))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Single(control.ProfileCalls);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Profile_feature_toggle_while_profile_disabled_invokes_zero_typed_mutations()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111, enabled: false) };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, 111, QuickSettingsRowId.ProfileTdpEnabled,
            [new(QuickSettingsRowId.ProfileTdpEnabled, QuickSettingsValue.Boolean(true))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.ProfileCalls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Profile_cpu_boost_ac_defined_value_dispatches_exactly_once()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111, enabled: true, cpuBoostEnabled: true) };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, 111, QuickSettingsRowId.ProfileCpuBoostAc,
            [new(QuickSettingsRowId.ProfileCpuBoostAc, QuickSettingsValue.Integer((int)CpuBoostMode.Aggressive))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Equal([$"ProfileCpuBoostAc:111:{CpuBoostMode.Aggressive}"], control.ProfileCalls);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Profile_cpu_boost_ac_undefined_value_invokes_zero_typed_mutations()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111, enabled: true, cpuBoostEnabled: true) };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, 111, QuickSettingsRowId.ProfileCpuBoostAc,
            [new(QuickSettingsRowId.ProfileCpuBoostAc, QuickSettingsValue.Integer(99))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.ProfileCalls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Profile_cpu_boost_slider_while_cpu_boost_feature_disabled_invokes_zero_typed_mutations()
    {
        // Section 8.2: a feature-specific slider cannot mutate when its current projected row is
        // absent/non-writable, even though the Profile master toggle itself is enabled.
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111, enabled: true, cpuBoostEnabled: false) };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, 111, QuickSettingsRowId.ProfileCpuBoostAc,
            [new(QuickSettingsRowId.ProfileCpuBoostAc, QuickSettingsValue.Integer((int)CpuBoostMode.Aggressive))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.ProfileCalls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Profile_power_mode_dc_defined_value_dispatches_exactly_once()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111, enabled: true, powerModeEnabled: true) };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, 111, QuickSettingsRowId.ProfilePowerModeDc,
            [new(QuickSettingsRowId.ProfilePowerModeDc, QuickSettingsValue.Integer((int)WindowsPowerMode.BestPowerEfficiency))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Equal([$"ProfilePowerModeDc:111:{WindowsPowerMode.BestPowerEfficiency}"], control.ProfileCalls);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Profile_complete_tdp_group_constructs_exactly_one_configuration_and_calls_set_tdp_once()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111, enabled: true, tdpEnabled: true) };
        var intent = ProfileTdpGroupIntent(QuickSettingsRowId.ProfileTdpAcPl1, enabled: true, acPl1: 15, acPl2: 20, dcPl1: 12, dcPl2: 18);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Equal(["ProfileTdp:111"], control.ProfileCalls);
        Assert.NotNull(control.LastProfileTdpConfiguration);
        Assert.Equal(new FrontendGameTdpConfiguration(true, new(15, 20), new(12, 18)), control.LastProfileTdpConfiguration);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Profile_tdp_group_missing_a_member_invokes_zero_typed_mutations()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111, enabled: true, tdpEnabled: true) };
        var values = new List<QuickSettingsRowValue>
        {
            new(QuickSettingsRowId.ProfileTdpEnabled, QuickSettingsValue.Boolean(true)),
            new(QuickSettingsRowId.ProfileTdpAcPl1, QuickSettingsValue.Integer(15)),
            new(QuickSettingsRowId.ProfileTdpAcPl2, QuickSettingsValue.Integer(20)),
            new(QuickSettingsRowId.ProfileTdpDcPl1, QuickSettingsValue.Integer(12)),
        };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, 111, QuickSettingsRowId.ProfileTdpAcPl1, values);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.ProfileCalls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Profile_tdp_group_while_tdp_feature_disabled_invokes_zero_typed_mutations()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111, enabled: true, tdpEnabled: false) };
        var intent = ProfileTdpGroupIntent(QuickSettingsRowId.ProfileTdpAcPl1, enabled: true, acPl1: 15, acPl2: 20, dcPl1: 12, dcPl2: 18);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.ProfileCalls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Profile_typed_failure_still_returns_authoritative_profile_page()
    {
        var control = new RecordingFrontendControl
        {
            ActiveProfile = ProfileSnapshot(appId: 111, enabled: true, cpuBoostEnabled: true),
            NextProfileMutationOutcome = FrontendGameProfileMutationOutcome.ApplyFailed,
            NextProfileMutationFailure = "Windows apply failed.",
        };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, 111, QuickSettingsRowId.ProfileCpuBoostAc,
            [new(QuickSettingsRowId.ProfileCpuBoostAc, QuickSettingsValue.Integer((int)CpuBoostMode.Aggressive))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Windows apply failed.", result.FailureMessage);
        Assert.Equal(QuickSettingsPageId.Profile, result.Page.PageId);
        Assert.True(result.Page.Available);
        Assert.Equal(111u, result.Page.AppId);
    }

    private static FrontendGameProfileSnapshot ProfileSnapshot(uint appId, string displayName = "Game", bool enabled = true, bool cpuBoostEnabled = false, bool tdpEnabled = false, bool powerModeEnabled = false) => new(
        appId, displayName, Exists: true, Enabled: enabled,
        new FrontendGameCpuBoostConfiguration(cpuBoostEnabled, CpuBoostMode.Aggressive, CpuBoostMode.Disabled),
        new FrontendGameTdpConfiguration(tdpEnabled, new FrontendTdpPowerPair(15, 20), new FrontendTdpPowerPair(12, 18)),
        PersistenceWritable: true,
        Limits: new FrontendTdpLimits(8, 30, 8, 37),
        Resolution: null,
        PowerMode: new FrontendGamePowerModeConfiguration(powerModeEnabled, WindowsPowerMode.Balanced, WindowsPowerMode.Balanced));

    private static QuickSettingsMutationIntent ProfileTdpGroupIntent(QuickSettingsRowId editedRowId, bool enabled, int acPl1, int acPl2, int dcPl1, int dcPl2) => new(
        QuickSettingsPageId.Profile, 111, editedRowId,
        [
            new(QuickSettingsRowId.ProfileTdpEnabled, QuickSettingsValue.Boolean(enabled)),
            new(QuickSettingsRowId.ProfileTdpAcPl1, QuickSettingsValue.Integer(acPl1)),
            new(QuickSettingsRowId.ProfileTdpAcPl2, QuickSettingsValue.Integer(acPl2)),
            new(QuickSettingsRowId.ProfileTdpDcPl1, QuickSettingsValue.Integer(dcPl1)),
            new(QuickSettingsRowId.ProfileTdpDcPl2, QuickSettingsValue.Integer(dcPl2)),
        ]);

    [Fact]
    public async Task Successful_mutation_returns_freshly_captured_page_not_the_submitted_draft()
    {
        var control = new RecordingFrontendControl
        {
            // The apply itself failed after the new value was already durably persisted (work order
            // section 21): the fresh authoritative capture disagrees with the submitted draft.
            NextCaptureResult = new FrontendDeviceQuickSettingsSnapshot(
                new FrontendCpuBoostSnapshot(
                    new(FrontendCpuBoostReadStatus.Known, CpuBoostMode.Enabled, CpuBoostMode.Aggressive),
                    new(FrontendCpuBoostReadStatus.Known, CpuBoostMode.Disabled, CpuBoostMode.Disabled),
                    Enabled: true, PersistenceWritable: true, LastFailure: "Windows apply failed."),
                FrontendTdpSnapshot.Unavailable,
                FrontendPowerModeSnapshot.Unavailable),
        };
        var intent = IntegerIntent(QuickSettingsRowId.DeviceCpuBoostAc, (int)CpuBoostMode.EfficientEnabled);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Equal(1, control.CaptureCount);
        var acRow = result.Page.Sections.SelectMany(s => s.Rows).Single(r => r.RowId == QuickSettingsRowId.DeviceCpuBoostAc);
        Assert.Equal((int)CpuBoostMode.Aggressive, acRow.Value!.IntegerValue);
    }

    private static QuickSettingsMutationIntent ToggleIntent(QuickSettingsRowId rowId, bool value) =>
        new(QuickSettingsPageId.Device, null, rowId, [new(rowId, QuickSettingsValue.Boolean(value))]);

    private static QuickSettingsMutationIntent IntegerIntent(QuickSettingsRowId rowId, int value) =>
        new(QuickSettingsPageId.Device, null, rowId, [new(rowId, QuickSettingsValue.Integer(value))]);

    private static QuickSettingsMutationIntent TdpGroupIntent(QuickSettingsRowId editedRowId, bool enabled, int acPl1, int acPl2, int dcPl1, int dcPl2) => new(
        QuickSettingsPageId.Device, null, editedRowId,
        [
            new(QuickSettingsRowId.DeviceTdpEnabled, QuickSettingsValue.Boolean(enabled)),
            new(QuickSettingsRowId.DeviceTdpAcPl1, QuickSettingsValue.Integer(acPl1)),
            new(QuickSettingsRowId.DeviceTdpAcPl2, QuickSettingsValue.Integer(acPl2)),
            new(QuickSettingsRowId.DeviceTdpDcPl1, QuickSettingsValue.Integer(dcPl1)),
            new(QuickSettingsRowId.DeviceTdpDcPl2, QuickSettingsValue.Integer(dcPl2)),
        ]);

    private sealed class RecordingFrontendControl : IAddonFrontendControl
    {
        public event EventHandler? StateInvalidated { add { } remove { } }

        public List<string> Calls { get; } = [];
        public int CaptureCount { get; private set; }
        public FrontendDeviceQuickSettingsSnapshot NextCaptureResult { get; set; } = FrontendDeviceQuickSettingsSnapshot.Unavailable;
        public FrontendTdpConfiguration? LastTdpConfiguration { get; private set; }

        // ---- SF-V2-08: Profile dispatch test seam ----
        public List<string> ProfileCalls { get; } = [];
        public FrontendGameProfileSnapshot ActiveProfile { get; set; } = ProfileSnapshot(0, enabled: false);
        public FrontendGameProfileMutationOutcome NextProfileMutationOutcome { get; set; } = FrontendGameProfileMutationOutcome.Succeeded;
        public string? NextProfileMutationFailure { get; set; }
        public FrontendGameTdpConfiguration? LastProfileTdpConfiguration { get; private set; }

        public Task<FrontendGameProfileSnapshot> CaptureActiveGameProfileAsync(CancellationToken t = default) => Task.FromResult(ActiveProfile);

        private FrontendGameProfileMutationResult ProfileResult() => new(NextProfileMutationOutcome, NextProfileMutationFailure, ActiveProfile);

        public Task<FrontendGameProfileMutationResult> SetGameProfileEnabledAsync(uint appId, bool enabled, string? displayName, CancellationToken t = default)
        { ProfileCalls.Add($"ProfileEnabled:{appId}:{enabled}:{displayName}"); return Task.FromResult(ProfileResult()); }

        public Task<FrontendGameProfileMutationResult> SetGameProfileTdpEnabledAsync(uint appId, bool enabled, CancellationToken t = default)
        { ProfileCalls.Add($"ProfileTdpEnabled:{appId}:{enabled}"); return Task.FromResult(ProfileResult()); }

        public Task<FrontendGameProfileMutationResult> SetGameProfileTdpAsync(uint appId, FrontendGameTdpConfiguration configuration, CancellationToken t = default)
        { ProfileCalls.Add($"ProfileTdp:{appId}"); LastProfileTdpConfiguration = configuration; return Task.FromResult(ProfileResult()); }

        public Task<FrontendGameProfileMutationResult> SetGameProfileCpuBoostEnabledAsync(uint appId, bool enabled, CancellationToken t = default)
        { ProfileCalls.Add($"ProfileCpuBoostEnabled:{appId}:{enabled}"); return Task.FromResult(ProfileResult()); }

        public Task<FrontendGameProfileMutationResult> SetGameProfileCpuBoostAcAsync(uint appId, CpuBoostMode mode, CancellationToken t = default)
        { ProfileCalls.Add($"ProfileCpuBoostAc:{appId}:{mode}"); return Task.FromResult(ProfileResult()); }

        public Task<FrontendGameProfileMutationResult> SetGameProfileCpuBoostDcAsync(uint appId, CpuBoostMode mode, CancellationToken t = default)
        { ProfileCalls.Add($"ProfileCpuBoostDc:{appId}:{mode}"); return Task.FromResult(ProfileResult()); }

        public Task<FrontendGameProfileMutationResult> SetGameProfilePowerModeEnabledAsync(uint appId, bool enabled, CancellationToken t = default)
        { ProfileCalls.Add($"ProfilePowerModeEnabled:{appId}:{enabled}"); return Task.FromResult(ProfileResult()); }

        public Task<FrontendGameProfileMutationResult> SetGameProfilePowerModeAcAsync(uint appId, WindowsPowerMode mode, CancellationToken t = default)
        { ProfileCalls.Add($"ProfilePowerModeAc:{appId}:{mode}"); return Task.FromResult(ProfileResult()); }

        public Task<FrontendGameProfileMutationResult> SetGameProfilePowerModeDcAsync(uint appId, WindowsPowerMode mode, CancellationToken t = default)
        { ProfileCalls.Add($"ProfilePowerModeDc:{appId}:{mode}"); return Task.FromResult(ProfileResult()); }

        private static FrontendGameProfileSnapshot ProfileSnapshot(uint appId, string displayName = "Game", bool enabled = true, bool cpuBoostEnabled = false, bool tdpEnabled = false, bool powerModeEnabled = false) => new(
            appId, displayName, Exists: true, Enabled: enabled,
            new FrontendGameCpuBoostConfiguration(cpuBoostEnabled, CpuBoostMode.Aggressive, CpuBoostMode.Disabled),
            new FrontendGameTdpConfiguration(tdpEnabled, new FrontendTdpPowerPair(15, 20), new FrontendTdpPowerPair(12, 18)),
            PersistenceWritable: true,
            Limits: new FrontendTdpLimits(8, 30, 8, 37),
            Resolution: null,
            PowerMode: new FrontendGamePowerModeConfiguration(powerModeEnabled, WindowsPowerMode.Balanced, WindowsPowerMode.Balanced));

        public Task<FrontendDeviceQuickSettingsSnapshot> CaptureDeviceQuickSettingsAsync(CancellationToken t = default)
        {
            CaptureCount++;
            return Task.FromResult(NextCaptureResult);
        }

        public Task<FrontendCpuBoostMutationResult> SetDeviceCpuBoostEnabledAsync(bool enabled, CancellationToken t = default)
        { Calls.Add($"CpuBoostEnabled:{enabled}"); return Task.FromResult(new FrontendCpuBoostMutationResult(FrontendCpuBoostMutationOutcome.Succeeded, null, FrontendCpuBoostSnapshot.Unavailable)); }

        public Task<FrontendCpuBoostMutationResult> SetDeviceCpuBoostAcAsync(CpuBoostMode mode, CancellationToken t = default)
        { Calls.Add($"CpuBoostAc:{mode}"); return Task.FromResult(new FrontendCpuBoostMutationResult(FrontendCpuBoostMutationOutcome.Succeeded, null, FrontendCpuBoostSnapshot.Unavailable)); }

        public Task<FrontendCpuBoostMutationResult> SetDeviceCpuBoostDcAsync(CpuBoostMode mode, CancellationToken t = default)
        { Calls.Add($"CpuBoostDc:{mode}"); return Task.FromResult(new FrontendCpuBoostMutationResult(FrontendCpuBoostMutationOutcome.Succeeded, null, FrontendCpuBoostSnapshot.Unavailable)); }

        public Task<FrontendTdpMutationResult> SetDeviceTdpEnabledAsync(bool enabled, CancellationToken t = default)
        { Calls.Add($"TdpEnabled:{enabled}"); return Task.FromResult(new FrontendTdpMutationResult(FrontendTdpMutationOutcome.Succeeded, null, FrontendTdpSnapshot.Unavailable)); }

        public Task<FrontendTdpMutationResult> SetDeviceTdpAsync(FrontendTdpConfiguration configuration, CancellationToken t = default)
        { Calls.Add("TdpConfiguration"); LastTdpConfiguration = configuration; return Task.FromResult(new FrontendTdpMutationResult(FrontendTdpMutationOutcome.Succeeded, null, FrontendTdpSnapshot.Unavailable)); }

        public Task<FrontendPowerModeMutationResult> SetDevicePowerModeEnabledAsync(bool enabled, CancellationToken t = default)
        { Calls.Add($"PowerModeEnabled:{enabled}"); return Task.FromResult(new FrontendPowerModeMutationResult(FrontendPowerModeMutationOutcome.Succeeded, null, FrontendPowerModeSnapshot.Unavailable)); }

        public Task<FrontendPowerModeMutationResult> SetDevicePowerModeAcAsync(WindowsPowerMode mode, CancellationToken t = default)
        { Calls.Add($"PowerModeAc:{mode}"); return Task.FromResult(new FrontendPowerModeMutationResult(FrontendPowerModeMutationOutcome.Succeeded, null, FrontendPowerModeSnapshot.Unavailable)); }

        public Task<FrontendPowerModeMutationResult> SetDevicePowerModeDcAsync(WindowsPowerMode mode, CancellationToken t = default)
        { Calls.Add($"PowerModeDc:{mode}"); return Task.FromResult(new FrontendPowerModeMutationResult(FrontendPowerModeMutationOutcome.Succeeded, null, FrontendPowerModeSnapshot.Unavailable)); }

        public Task<FrontendBootstrapSnapshot> GetBootstrapAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendStatusSnapshot> CaptureStatusAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendSettingsSnapshot> SetLogLevelAsync(FrontendLogLevel level, CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendSettingsSnapshot> SetFrontButtonMappingAsync(SteamInputAddonforClaw.Contracts.FrontButtons.FrontButtonMappingSettings mapping, CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendSettingsSnapshot> SuppressDeveloperMenuWarningAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendDeveloperSnapshot> SetDeveloperTestModeAsync(bool enabled, CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendPrerequisiteSetupResult> RunPrerequisiteSetupAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendEnvironmentReportResult> GenerateEnvironmentReportAsync(CancellationToken t = default) => throw new NotSupportedException();
    }
}
