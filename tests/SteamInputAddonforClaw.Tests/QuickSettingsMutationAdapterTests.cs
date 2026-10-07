using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.BackButtons;
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
    public async Task Battery_enabled_toggle_dispatches_once_and_returns_fresh_authoritative_page()
    {
        var control = new RecordingFrontendControl
        {
            NextCaptureResult = FrontendDeviceQuickSettingsSnapshot.Unavailable with
            {
                BatteryChargeLimit = new(true, true, true, false, 70, false, 70, null),
            },
        };

        var result = await QuickSettingsMutationAdapter.MutateAsync(control,
            ToggleIntent(QuickSettingsRowId.DeviceBatteryChargeLimitEnabled, true), CancellationToken.None);

        Assert.Equal(["BatteryEnabled:True"], control.Calls);
        var batterySection = result.Page.Sections.Single(section => section.SectionId == QuickSettingsSectionId.DeviceBatteryChargeLimit);
        Assert.Equal(70, batterySection.Rows.Single(row => row.RowId == QuickSettingsRowId.DeviceBatteryChargeLimitPercent).Value!.IntegerValue);
    }

    [Fact]
    public async Task Battery_supported_limit_dispatches_once()
    {
        var control = new RecordingFrontendControl();

        await QuickSettingsMutationAdapter.MutateAsync(control,
            IntegerIntent(QuickSettingsRowId.DeviceBatteryChargeLimitPercent, 85), CancellationToken.None);

        Assert.Equal(["BatteryPercent:85"], control.Calls);
    }

    [Theory]
    [InlineData(59)]
    [InlineData(61)]
    [InlineData(101)]
    public async Task Battery_invalid_limit_invokes_zero_mutations(int percent)
    {
        var control = new RecordingFrontendControl();

        var result = await QuickSettingsMutationAdapter.MutateAsync(control,
            IntegerIntent(QuickSettingsRowId.DeviceBatteryChargeLimitPercent, percent), CancellationToken.None);

        Assert.Empty(control.Calls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Battery_limit_with_wrong_value_kind_invokes_zero_mutations()
    {
        var control = new RecordingFrontendControl();
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Device, null,
            QuickSettingsRowId.DeviceBatteryChargeLimitPercent,
            [new(QuickSettingsRowId.DeviceBatteryChargeLimitPercent, QuickSettingsValue.Boolean(true))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.Calls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Battery_limit_with_duplicate_values_invokes_zero_mutations()
    {
        var control = new RecordingFrontendControl();
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Device, null,
            QuickSettingsRowId.DeviceBatteryChargeLimitPercent,
            [new(QuickSettingsRowId.DeviceBatteryChargeLimitPercent, QuickSettingsValue.Integer(80)),
                new(QuickSettingsRowId.DeviceBatteryChargeLimitPercent, QuickSettingsValue.Integer(85))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.Calls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Battery_enabled_with_wrong_value_kind_invokes_zero_mutations()
    {
        var control = new RecordingFrontendControl();
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Device, null,
            QuickSettingsRowId.DeviceBatteryChargeLimitEnabled,
            [new(QuickSettingsRowId.DeviceBatteryChargeLimitEnabled, QuickSettingsValue.Integer(1))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.Calls);
        Assert.False(result.Succeeded);
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
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Device, QuickSettingsProfileTarget.ForSteam(12345u), QuickSettingsRowId.DeviceCpuBoostEnabled,
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
    public async Task Profile_intent_with_stale_steam_target_invokes_zero_typed_mutations()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111) };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(999), QuickSettingsRowId.ProfileCpuBoostEnabled,
            [new(QuickSettingsRowId.ProfileCpuBoostEnabled, QuickSettingsValue.Boolean(true))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.ProfileCalls);
        Assert.False(result.Succeeded);
        Assert.Equal(QuickSettingsPageId.Profile, result.Page.PageId);
        Assert.False(result.Page.Available);
        Assert.Equal(999u, result.Page.ProfileTarget?.SteamAppId);
    }

    [Fact]
    public async Task Profile_intent_with_stale_xbox_target_invokes_zero_typed_mutations()
    {
        var control = new RecordingFrontendControl
        {
            ActiveTarget = QuickSettingsProfileTarget.ForXbox("xbox:current"),
            ActiveXboxProfile = RecordingFrontendControl.XboxProfileSnapshot("xbox:current", "Current Xbox Game"),
        };
        var staleTarget = QuickSettingsProfileTarget.ForXbox("xbox:stale");
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, staleTarget, QuickSettingsRowId.ProfileBackButtonUseGlobal,
            [new(QuickSettingsRowId.ProfileBackButtonUseGlobal, QuickSettingsValue.Boolean(false))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.ProfileCalls);
        Assert.False(result.Succeeded);
        Assert.False(result.Page.Available);
        Assert.Equal(staleTarget, result.Page.ProfileTarget);
    }

    [Fact]
    public async Task Xbox_use_global_off_seeds_the_exact_displayed_mapping()
    {
        var currentMapping = new BackButtonMappingSettings(Xbox360BackButtonTarget.LeftBumper, Xbox360BackButtonTarget.RightBumper);
        var control = new RecordingFrontendControl
        {
            ActiveTarget = QuickSettingsProfileTarget.ForXbox("xbox:test"),
            ActiveXboxProfile = RecordingFrontendControl.XboxProfileSnapshot("xbox:test", "Xbox Game") with
            {
                BackButtonMapping = new(true, currentMapping),
            },
        };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForXbox("xbox:test"),
            QuickSettingsRowId.ProfileBackButtonUseGlobal,
            [new(QuickSettingsRowId.ProfileBackButtonUseGlobal, QuickSettingsValue.Boolean(false))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(["XboxProfileBackButtonMapping:xbox:test:override"], control.ProfileCalls);
        Assert.Equal(currentMapping, control.LastXboxBackButtonMapping);
    }

    [Fact]
    public async Task Xbox_use_global_on_mutates_the_typed_authority_with_null()
    {
        var control = new RecordingFrontendControl
        {
            ActiveTarget = QuickSettingsProfileTarget.ForXbox("xbox:test"),
            ActiveXboxProfile = RecordingFrontendControl.XboxProfileSnapshot("xbox:test", "Xbox Game") with
            {
                BackButtonMapping = new(false, new(Xbox360BackButtonTarget.X, Xbox360BackButtonTarget.Y)),
            },
        };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForXbox("xbox:test"),
            QuickSettingsRowId.ProfileBackButtonUseGlobal,
            [new(QuickSettingsRowId.ProfileBackButtonUseGlobal, QuickSettingsValue.Boolean(true))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(["XboxProfileBackButtonMapping:xbox:test:global"], control.ProfileCalls);
        Assert.Null(control.LastXboxBackButtonMapping);
    }

    [Theory]
    [InlineData(QuickSettingsRowId.ProfileBackButtonM1)]
    [InlineData(QuickSettingsRowId.ProfileBackButtonM2)]
    public async Task Xbox_grouped_back_button_edit_submits_one_whole_mapping(QuickSettingsRowId editedRowId)
    {
        var mapping = new BackButtonMappingSettings(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.XboxGuide);
        var control = new RecordingFrontendControl
        {
            ActiveTarget = QuickSettingsProfileTarget.ForXbox("xbox:test"),
            ActiveXboxProfile = RecordingFrontendControl.XboxProfileSnapshot("xbox:test", "Xbox Game") with
            {
                BackButtonMapping = new(false, new(Xbox360BackButtonTarget.B, Xbox360BackButtonTarget.Y)),
            },
        };
        var intent = XboxBackButtonGroupIntent(editedRowId, useGlobal: false, (int)mapping.M1, (int)mapping.M2);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(["XboxProfileBackButtonMapping:xbox:test:override"], control.ProfileCalls);
        Assert.Equal(mapping, control.LastXboxBackButtonMapping);
    }

    [Theory]
    [MemberData(nameof(MalformedXboxBackButtonGroupIntents))]
    public async Task Malformed_xbox_back_button_group_invokes_zero_typed_mutations(QuickSettingsMutationIntent intent)
    {
        var control = new RecordingFrontendControl { ActiveTarget = QuickSettingsProfileTarget.ForXbox("xbox:test") };
        intent = intent with { ProfileTarget = QuickSettingsProfileTarget.ForXbox("xbox:test") };

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.ProfileCalls);
        Assert.False(result.Succeeded);
    }

    public static IEnumerable<object[]> MalformedXboxBackButtonGroupIntents()
    {
        var global = new QuickSettingsRowValue(QuickSettingsRowId.ProfileBackButtonUseGlobal, QuickSettingsValue.Boolean(false));
        var m1 = new QuickSettingsRowValue(QuickSettingsRowId.ProfileBackButtonM1, QuickSettingsValue.Integer((int)Xbox360BackButtonTarget.A));
        var m2 = new QuickSettingsRowValue(QuickSettingsRowId.ProfileBackButtonM2, QuickSettingsValue.Integer((int)Xbox360BackButtonTarget.B));
        var target = QuickSettingsProfileTarget.ForXbox("xbox:test");
        QuickSettingsMutationIntent Intent(QuickSettingsRowId edited, params QuickSettingsRowValue[] values) =>
            new(QuickSettingsPageId.Profile, target, edited, values);

        yield return [Intent(QuickSettingsRowId.ProfileBackButtonM1, global, m1 with { Value = QuickSettingsValue.Integer(999) }, m2)];
        yield return [Intent(QuickSettingsRowId.ProfileBackButtonM2, global, m1, m2 with { Value = QuickSettingsValue.Integer(999) })];
        yield return [Intent(QuickSettingsRowId.ProfileBackButtonM1, global, m1, m1, m2)];
        yield return [Intent(QuickSettingsRowId.ProfileBackButtonM1, m1, m2)];
        yield return [Intent(QuickSettingsRowId.ProfileBackButtonM1, global, m1)];
        yield return [Intent(QuickSettingsRowId.ProfileBackButtonM2, global, m2)];
        yield return [Intent(QuickSettingsRowId.ProfileBackButtonM1, global with { Value = QuickSettingsValue.Boolean(true) }, m1, m2)];
        yield return [Intent(QuickSettingsRowId.ProfileBackButtonM1, global, m1, m2,
            new(QuickSettingsRowId.ProfileResolution, QuickSettingsValue.Integer(1)))];
        yield return [Intent(QuickSettingsRowId.ProfileBackButtonUseGlobal, global, m1, m2)];
        yield return [Intent(QuickSettingsRowId.ProfileBackButtonM1,
            global with { Value = new QuickSettingsValue(QuickSettingsValueKind.Boolean, BooleanValue: false, IntegerValue: 0) }, m1, m2)];
        yield return [Intent(QuickSettingsRowId.ProfileBackButtonM1,
            global with { Value = new QuickSettingsValue(QuickSettingsValueKind.Boolean) }, m1, m2)];
        yield return [new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, target, QuickSettingsRowId.ProfileBackButtonM1, null!)];
        yield return [Intent(QuickSettingsRowId.ProfileBackButtonM1, global, m1, null!, m2)];
    }

    [Fact]
    public async Task Xbox_use_global_off_rejects_invalid_current_displayed_mapping_without_mutation()
    {
        var control = new RecordingFrontendControl
        {
            ActiveTarget = QuickSettingsProfileTarget.ForXbox("xbox:test"),
            ActiveXboxProfile = RecordingFrontendControl.XboxProfileSnapshot("xbox:test", "Xbox Game") with
            {
                BackButtonMapping = new(true, new((Xbox360BackButtonTarget)99, Xbox360BackButtonTarget.B)),
            },
        };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForXbox("xbox:test"),
            QuickSettingsRowId.ProfileBackButtonUseGlobal,
            [new(QuickSettingsRowId.ProfileBackButtonUseGlobal, QuickSettingsValue.Boolean(false))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.ProfileCalls);
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(QuickSettingsRowId.ProfileBackButtonUseGlobal)]
    [InlineData(QuickSettingsRowId.ProfileBackButtonM1)]
    [InlineData(QuickSettingsRowId.ProfileBackButtonM2)]
    public async Task Steam_profile_rejects_every_back_button_mapping_row(QuickSettingsRowId rowId)
    {
        var values = rowId == QuickSettingsRowId.ProfileBackButtonUseGlobal
            ? new[] { new QuickSettingsRowValue(rowId, QuickSettingsValue.Boolean(true)) }
            : XboxBackButtonGroupIntent(QuickSettingsRowId.ProfileBackButtonM1, false, (int)Xbox360BackButtonTarget.A, (int)Xbox360BackButtonTarget.B).Values.ToArray();
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(111), rowId, values);
        var control = new RecordingFrontendControl
        {
            ActiveProfile = ProfileSnapshot(appId: 111),
            ActiveTarget = QuickSettingsProfileTarget.ForSteam(111),
        };

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.ProfileCalls);
        Assert.False(result.Succeeded);
        Assert.Equal(QuickSettingsProfileTarget.ForSteam(111), result.Page.ProfileTarget);
    }

    private static QuickSettingsMutationIntent XboxBackButtonGroupIntent(QuickSettingsRowId editedRowId, bool useGlobal, int m1, int m2) => new(
        QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForXbox("xbox:test"), editedRowId,
        [
            new(QuickSettingsRowId.ProfileBackButtonUseGlobal, QuickSettingsValue.Boolean(useGlobal)),
            new(QuickSettingsRowId.ProfileBackButtonM1, QuickSettingsValue.Integer(m1)),
            new(QuickSettingsRowId.ProfileBackButtonM2, QuickSettingsValue.Integer(m2)),
        ]);

    [Fact]
    public async Task Profile_intent_with_zero_app_id_invokes_zero_typed_mutations()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111) };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(0), QuickSettingsRowId.ProfileCpuBoostEnabled,
            [new(QuickSettingsRowId.ProfileCpuBoostEnabled, QuickSettingsValue.Boolean(true))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.ProfileCalls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Profile_enabled_toggle_dispatches_exactly_once_with_display_name_forwarded()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111, displayName: "My Game") };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(111), QuickSettingsRowId.ProfileEnabled,
            [new(QuickSettingsRowId.ProfileEnabled, QuickSettingsValue.Boolean(true))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Equal(["ProfileEnabled:111:True:My Game"], control.ProfileCalls);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Profile_enabled_toggle_with_malformed_value_invokes_zero_typed_mutations()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111) };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(111), QuickSettingsRowId.ProfileEnabled,
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
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(111), rowId, [new(rowId, QuickSettingsValue.Boolean(true))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Single(control.ProfileCalls);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Profile_feature_toggle_while_profile_disabled_invokes_zero_typed_mutations()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111, enabled: false) };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(111), QuickSettingsRowId.ProfileTdpEnabled,
            [new(QuickSettingsRowId.ProfileTdpEnabled, QuickSettingsValue.Boolean(true))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.ProfileCalls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Profile_cpu_boost_ac_defined_value_dispatches_exactly_once()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111, enabled: true, cpuBoostEnabled: true) };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(111), QuickSettingsRowId.ProfileCpuBoostAc,
            [new(QuickSettingsRowId.ProfileCpuBoostAc, QuickSettingsValue.Integer((int)CpuBoostMode.Aggressive))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Equal([$"ProfileCpuBoostAc:111:{CpuBoostMode.Aggressive}"], control.ProfileCalls);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Profile_cpu_boost_ac_undefined_value_invokes_zero_typed_mutations()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111, enabled: true, cpuBoostEnabled: true) };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(111), QuickSettingsRowId.ProfileCpuBoostAc,
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
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(111), QuickSettingsRowId.ProfileCpuBoostAc,
            [new(QuickSettingsRowId.ProfileCpuBoostAc, QuickSettingsValue.Integer((int)CpuBoostMode.Aggressive))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.ProfileCalls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Profile_power_mode_dc_defined_value_dispatches_exactly_once()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111, enabled: true, powerModeEnabled: true) };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(111), QuickSettingsRowId.ProfilePowerModeDc,
            [new(QuickSettingsRowId.ProfilePowerModeDc, QuickSettingsValue.Integer((int)WindowsPowerMode.BestPowerEfficiency))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Equal([$"ProfilePowerModeDc:111:{WindowsPowerMode.BestPowerEfficiency}"], control.ProfileCalls);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Profile_fps_limit_ac_dispatches_the_typed_value()
    {
        var control = new RecordingFrontendControl
        {
            ActiveProfile = ProfileSnapshot(appId: 111, enabled: true) with
            {
                FpsLimit = new FrontendGameFpsLimitConfiguration(true, 60, 60, Available: true),
            },
        };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(111), QuickSettingsRowId.ProfileFpsLimitAc,
            [new(QuickSettingsRowId.ProfileFpsLimitAc, QuickSettingsValue.Integer(120))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Equal(["ProfileFpsLimitAc:111:120"], control.ProfileCalls);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Profile_fps_limit_out_of_range_invokes_zero_typed_mutations()
    {
        var control = new RecordingFrontendControl
        {
            ActiveProfile = ProfileSnapshot(appId: 111, enabled: true) with
            {
                FpsLimit = new FrontendGameFpsLimitConfiguration(true, 60, 60, Available: true),
            },
        };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(111), QuickSettingsRowId.ProfileFpsLimitAc,
            [new(QuickSettingsRowId.ProfileFpsLimitAc, QuickSettingsValue.Integer(121))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.ProfileCalls);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Profile_resolution_maps_the_closed_option_to_the_typed_mutation()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(appId: 111, enabled: true) };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(111), QuickSettingsRowId.ProfileResolution,
            [new(QuickSettingsRowId.ProfileResolution, QuickSettingsValue.Integer(2))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Equal(["ProfileResolution:111:1920x1080"], control.ProfileCalls);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Profile_target_is_rejected_when_no_game_is_active()
    {
        var control = new RecordingFrontendControl { ActiveProfile = ProfileSnapshot(0, enabled: false), ActiveTarget = null };
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(222), QuickSettingsRowId.ProfileEnabled,
            [new(QuickSettingsRowId.ProfileEnabled, QuickSettingsValue.Boolean(true))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Empty(control.ProfileCalls);
        Assert.False(result.Succeeded);
        Assert.Equal(222u, result.Page.ProfileTarget?.SteamAppId);
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
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(111), QuickSettingsRowId.ProfileTdpAcPl1, values);

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
    public async Task Profile_gpu_minimum_clock_dispatches_current_driver_table_index()
    {
        var control = new RecordingFrontendControl
        {
            ActiveProfile = ProfileSnapshot(111, enabled: true) with
            {
                GpuMinimumClock = new(true, true, true, [1525, 1625, 1725.125, 1825.375], 1625, 1725.125, 1725.125)
            }
        };
        var row = QuickSettingsRowId.ProfileGpuMinimumClockAc;
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(111), row,
            [new(row, QuickSettingsValue.Integer(2))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Equal(["ProfileGpuMinimumClockAc:111:2"], control.ProfileCalls);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Xbox_profile_gpu_minimum_clock_dispatches_current_driver_table_index()
    {
        var control = new RecordingFrontendControl
        {
            ActiveTarget = QuickSettingsProfileTarget.ForXbox("xbox:test"),
            ActiveXboxProfile = RecordingFrontendControl.XboxProfileSnapshot("xbox:test", "Xbox Game") with
            {
                GpuMinimumClock = new(true, true, true, [1525, 1625, 1725.125, 1825.375], 1625, 1725.125, 1725.125)
            }
        };
        var row = QuickSettingsRowId.ProfileGpuMinimumClockDc;
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForXbox("xbox:test"), row,
            [new(row, QuickSettingsValue.Integer(3))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.Equal(["XboxProfileGpuMinimumClockDc:xbox:test:3"], control.ProfileCalls);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Profile_gpu_minimum_clock_rejects_index_outside_current_driver_table()
    {
        var control = new RecordingFrontendControl
        {
            ActiveProfile = ProfileSnapshot(111, enabled: true) with
            {
                GpuMinimumClock = new(true, true, true, [1525, 1625, 1725.125], 1625, 1725.125, 1725.125)
            }
        };
        var row = QuickSettingsRowId.ProfileGpuMinimumClockAc;
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(111), row,
            [new(row, QuickSettingsValue.Integer(3))]);

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
        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(111), QuickSettingsRowId.ProfileCpuBoostAc,
            [new(QuickSettingsRowId.ProfileCpuBoostAc, QuickSettingsValue.Integer((int)CpuBoostMode.Aggressive))]);

        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Windows apply failed.", result.FailureMessage);
        Assert.Equal(QuickSettingsPageId.Profile, result.Page.PageId);
        Assert.True(result.Page.Available);
        Assert.Equal(111u, result.Page.ProfileTarget?.SteamAppId);
    }

    [Fact]
    public async Task Every_profile_row_family_dispatches_to_the_existing_typed_api_for_both_platforms()
    {
        var cases = new (QuickSettingsRowId RowId, QuickSettingsRowValue[] Values, string SteamCall, string XboxCall)[]
        {
            (QuickSettingsRowId.ProfileEnabled, [new(QuickSettingsRowId.ProfileEnabled, QuickSettingsValue.Boolean(true))], "ProfileEnabled:111:True:Steam Game", "XboxProfileEnabled:xbox:test:True:Xbox Game"),
            (QuickSettingsRowId.ProfileTdpEnabled, [new(QuickSettingsRowId.ProfileTdpEnabled, QuickSettingsValue.Boolean(true))], "ProfileTdpEnabled:111:True", "XboxProfileTdpEnabled:xbox:test:True"),
            (QuickSettingsRowId.ProfileTdpAcPl1, [new(QuickSettingsRowId.ProfileTdpEnabled, QuickSettingsValue.Boolean(true)), new(QuickSettingsRowId.ProfileTdpAcPl1, QuickSettingsValue.Integer(15)), new(QuickSettingsRowId.ProfileTdpAcPl2, QuickSettingsValue.Integer(20)), new(QuickSettingsRowId.ProfileTdpDcPl1, QuickSettingsValue.Integer(12)), new(QuickSettingsRowId.ProfileTdpDcPl2, QuickSettingsValue.Integer(18))], "ProfileTdp:111", "XboxProfileTdp:xbox:test"),
            (QuickSettingsRowId.ProfileCpuBoostEnabled, [new(QuickSettingsRowId.ProfileCpuBoostEnabled, QuickSettingsValue.Boolean(true))], "ProfileCpuBoostEnabled:111:True", "XboxProfileCpuBoostEnabled:xbox:test:True"),
            (QuickSettingsRowId.ProfileCpuBoostAc, [new(QuickSettingsRowId.ProfileCpuBoostAc, QuickSettingsValue.Integer((int)CpuBoostMode.Aggressive))], "ProfileCpuBoostAc:111:Aggressive", "XboxProfileCpuBoostAc:xbox:test:Aggressive"),
            (QuickSettingsRowId.ProfileCpuBoostDc, [new(QuickSettingsRowId.ProfileCpuBoostDc, QuickSettingsValue.Integer((int)CpuBoostMode.Disabled))], "ProfileCpuBoostDc:111:Disabled", "XboxProfileCpuBoostDc:xbox:test:Disabled"),
            (QuickSettingsRowId.ProfilePowerModeEnabled, [new(QuickSettingsRowId.ProfilePowerModeEnabled, QuickSettingsValue.Boolean(true))], "ProfilePowerModeEnabled:111:True", "XboxProfilePowerModeEnabled:xbox:test:True"),
            (QuickSettingsRowId.ProfilePowerModeAc, [new(QuickSettingsRowId.ProfilePowerModeAc, QuickSettingsValue.Integer((int)WindowsPowerMode.Balanced))], "ProfilePowerModeAc:111:Balanced", "XboxProfilePowerModeAc:xbox:test:Balanced"),
            (QuickSettingsRowId.ProfilePowerModeDc, [new(QuickSettingsRowId.ProfilePowerModeDc, QuickSettingsValue.Integer((int)WindowsPowerMode.BestPowerEfficiency))], "ProfilePowerModeDc:111:BestPowerEfficiency", "XboxProfilePowerModeDc:xbox:test:BestPowerEfficiency"),
            (QuickSettingsRowId.ProfileFpsLimitEnabled, [new(QuickSettingsRowId.ProfileFpsLimitEnabled, QuickSettingsValue.Boolean(true))], "ProfileFpsLimitEnabled:111:True", "XboxProfileFpsLimitEnabled:xbox:test:True"),
            (QuickSettingsRowId.ProfileFpsLimitAc, [new(QuickSettingsRowId.ProfileFpsLimitAc, QuickSettingsValue.Integer(120))], "ProfileFpsLimitAc:111:120", "XboxProfileFpsLimitAc:xbox:test:120"),
            (QuickSettingsRowId.ProfileFpsLimitDc, [new(QuickSettingsRowId.ProfileFpsLimitDc, QuickSettingsValue.Integer(60))], "ProfileFpsLimitDc:111:60", "XboxProfileFpsLimitDc:xbox:test:60"),
            (QuickSettingsRowId.ProfileResolution, [new(QuickSettingsRowId.ProfileResolution, QuickSettingsValue.Integer(2))], "ProfileResolution:111:1920x1080", "XboxProfileResolution:xbox:test:1920x1080"),
        };

        foreach (var target in new[] { QuickSettingsProfileTarget.ForSteam(111), QuickSettingsProfileTarget.ForXbox("xbox:test") })
        foreach (var testCase in cases)
        {
            var control = new RecordingFrontendControl
            {
                ActiveTarget = target,
                ActiveProfile = ProfileSnapshot(111, "Steam Game", enabled: true, cpuBoostEnabled: true, tdpEnabled: true, powerModeEnabled: true) with
                {
                    Resolution = new FrontendGameResolution(1920, 1080),
                    FpsLimit = new FrontendGameFpsLimitConfiguration(true, 60, 60, true),
                },
                ActiveXboxProfile = RecordingFrontendControl.XboxProfileSnapshot("xbox:test", "Xbox Game"),
            };
            var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, target, testCase.RowId, testCase.Values);

            var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

            Assert.True(result.Succeeded, $"{target.Kind} {testCase.RowId}: {result.FailureMessage}");
            Assert.Equal([target.Kind == QuickSettingsProfileTargetKind.Steam ? testCase.SteamCall : testCase.XboxCall], control.ProfileCalls);
        }
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
        QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(111), editedRowId,
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
                FrontendPowerModeSnapshot.Unavailable,
                FrontendBatteryChargeLimitSnapshot.Unavailable),
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
        public FrontendBatteryChargeLimitMutationResult BatteryMutationResult { get; set; } = new(
            FrontendBatteryChargeLimitMutationOutcome.Succeeded, null,
            new FrontendBatteryChargeLimitSnapshot(true, true, true, true, 70, true, 70, null));

        // ---- SF-V2-08: Profile dispatch test seam ----
        public List<string> ProfileCalls { get; } = [];
        public FrontendGameProfileSnapshot ActiveProfile { get; set; } = ProfileSnapshot(0, enabled: false);
        public FrontendXboxGameProfileSnapshot ActiveXboxProfile { get; set; } = XboxProfileSnapshot("xbox:test", "Xbox Game");
        public QuickSettingsProfileTarget? ActiveTarget { get; set; } = QuickSettingsProfileTarget.ForSteam(111);
        public FrontendGameProfileMutationOutcome NextProfileMutationOutcome { get; set; } = FrontendGameProfileMutationOutcome.Succeeded;
        public string? NextProfileMutationFailure { get; set; }
        public FrontendGameTdpConfiguration? LastProfileTdpConfiguration { get; private set; }
        public BackButtonMappingSettings? LastXboxBackButtonMapping { get; private set; }
        public FrontendGameProfileSnapshot OfflineProfile { get; set; } = ProfileSnapshot(222, "Offline Game", enabled: true);

        public Task<FrontendGameProfileSnapshot> CaptureActiveGameProfileAsync(CancellationToken t = default) => Task.FromResult(ActiveProfile);
        public Task<FrontendGameProfileSnapshot> CaptureGameProfileAsync(uint appId, CancellationToken t = default) => Task.FromResult(OfflineProfile with { AppId = appId });
        public Task<FrontendXboxGameProfileSnapshot> CaptureXboxGameProfileAsync(string key, CancellationToken t = default) => Task.FromResult(ActiveXboxProfile with { Key = key });

        public Task<QuickSettingsPageSnapshot> CaptureQuickSettingsPageAsync(QuickSettingsPageId pageId, QuickSettingsProfileTarget? profileTarget = null, CancellationToken cancellationToken = default)
        {
            if (pageId != QuickSettingsPageId.Profile || profileTarget is null || profileTarget != ActiveTarget)
                return Task.FromResult(QuickSettingsPageSnapshot.Unavailable(pageId, profileTarget));
            return Task.FromResult(profileTarget.Kind switch
            {
                QuickSettingsProfileTargetKind.Steam => QuickSettingsPresentation.BuildProfile(ActiveProfile),
                QuickSettingsProfileTargetKind.Xbox => QuickSettingsPresentation.BuildProfile(ActiveXboxProfile),
                _ => QuickSettingsPageSnapshot.Unavailable(pageId, profileTarget),
            });
        }

        private FrontendGameProfileMutationResult ProfileResult() => new(NextProfileMutationOutcome, NextProfileMutationFailure, ActiveProfile.AppId > 0 ? ActiveProfile : OfflineProfile);
        private FrontendXboxGameProfileMutationResult XboxProfileResult() => new(NextProfileMutationOutcome, NextProfileMutationFailure, ActiveXboxProfile);

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

        public Task<FrontendGameProfileMutationResult> SetGameProfileFpsLimitEnabledAsync(uint appId, bool enabled, CancellationToken t = default)
        { ProfileCalls.Add($"ProfileFpsLimitEnabled:{appId}:{enabled}"); return Task.FromResult(ProfileResult()); }

        public Task<FrontendGameProfileMutationResult> SetGameProfileFpsLimitAcAsync(uint appId, int fps, CancellationToken t = default)
        { ProfileCalls.Add($"ProfileFpsLimitAc:{appId}:{fps}"); return Task.FromResult(ProfileResult()); }

        public Task<FrontendGameProfileMutationResult> SetGameProfileFpsLimitDcAsync(uint appId, int fps, CancellationToken t = default)
        { ProfileCalls.Add($"ProfileFpsLimitDc:{appId}:{fps}"); return Task.FromResult(ProfileResult()); }
        public Task<FrontendGameProfileMutationResult> SetGameProfileGpuMinimumClockAcAsync(uint appId, int selectableClockIndex, CancellationToken t = default)
        { ProfileCalls.Add($"ProfileGpuMinimumClockAc:{appId}:{selectableClockIndex}"); return Task.FromResult(ProfileResult()); }
        public Task<FrontendGameProfileMutationResult> SetGameProfileGpuMinimumClockDcAsync(uint appId, int selectableClockIndex, CancellationToken t = default)
        { ProfileCalls.Add($"ProfileGpuMinimumClockDc:{appId}:{selectableClockIndex}"); return Task.FromResult(ProfileResult()); }
        public Task<FrontendGameProfileMutationResult> SetGameProfileGpuMinimumClockEnabledAsync(uint appId, bool enabled, CancellationToken t = default)
        { ProfileCalls.Add($"ProfileGpuMinimumClockEnabled:{appId}:{enabled}"); return Task.FromResult(ProfileResult()); }

        public Task<FrontendGameProfileMutationResult> SetGameProfileResolutionAsync(uint appId, FrontendGameResolution? resolution, string? displayName, CancellationToken t = default)
        { ProfileCalls.Add($"ProfileResolution:{appId}:{(resolution is null ? "null" : $"{resolution.Width}x{resolution.Height}")}"); return Task.FromResult(ProfileResult()); }

        public Task<FrontendXboxGameProfileMutationResult> SetXboxGameProfileEnabledAsync(string key, bool enabled, string? displayName, CancellationToken t = default)
        { ProfileCalls.Add($"XboxProfileEnabled:{key}:{enabled}:{displayName}"); return Task.FromResult(XboxProfileResult()); }
        public Task<FrontendXboxGameProfileMutationResult> SetXboxGameProfileTdpEnabledAsync(string key, bool enabled, CancellationToken t = default)
        { ProfileCalls.Add($"XboxProfileTdpEnabled:{key}:{enabled}"); return Task.FromResult(XboxProfileResult()); }
        public Task<FrontendXboxGameProfileMutationResult> SetXboxGameProfileTdpAsync(string key, FrontendGameTdpConfiguration configuration, CancellationToken t = default)
        { ProfileCalls.Add($"XboxProfileTdp:{key}"); LastProfileTdpConfiguration = configuration; return Task.FromResult(XboxProfileResult()); }
        public Task<FrontendXboxGameProfileMutationResult> SetXboxGameProfileCpuBoostEnabledAsync(string key, bool enabled, CancellationToken t = default)
        { ProfileCalls.Add($"XboxProfileCpuBoostEnabled:{key}:{enabled}"); return Task.FromResult(XboxProfileResult()); }
        public Task<FrontendXboxGameProfileMutationResult> SetXboxGameProfileCpuBoostAcAsync(string key, CpuBoostMode mode, CancellationToken t = default)
        { ProfileCalls.Add($"XboxProfileCpuBoostAc:{key}:{mode}"); return Task.FromResult(XboxProfileResult()); }
        public Task<FrontendXboxGameProfileMutationResult> SetXboxGameProfileCpuBoostDcAsync(string key, CpuBoostMode mode, CancellationToken t = default)
        { ProfileCalls.Add($"XboxProfileCpuBoostDc:{key}:{mode}"); return Task.FromResult(XboxProfileResult()); }
        public Task<FrontendXboxGameProfileMutationResult> SetXboxGameProfilePowerModeEnabledAsync(string key, bool enabled, CancellationToken t = default)
        { ProfileCalls.Add($"XboxProfilePowerModeEnabled:{key}:{enabled}"); return Task.FromResult(XboxProfileResult()); }
        public Task<FrontendXboxGameProfileMutationResult> SetXboxGameProfilePowerModeAcAsync(string key, WindowsPowerMode mode, CancellationToken t = default)
        { ProfileCalls.Add($"XboxProfilePowerModeAc:{key}:{mode}"); return Task.FromResult(XboxProfileResult()); }
        public Task<FrontendXboxGameProfileMutationResult> SetXboxGameProfilePowerModeDcAsync(string key, WindowsPowerMode mode, CancellationToken t = default)
        { ProfileCalls.Add($"XboxProfilePowerModeDc:{key}:{mode}"); return Task.FromResult(XboxProfileResult()); }
        public Task<FrontendXboxGameProfileMutationResult> SetXboxGameProfileFpsLimitEnabledAsync(string key, bool enabled, CancellationToken t = default)
        { ProfileCalls.Add($"XboxProfileFpsLimitEnabled:{key}:{enabled}"); return Task.FromResult(XboxProfileResult()); }
        public Task<FrontendXboxGameProfileMutationResult> SetXboxGameProfileFpsLimitAcAsync(string key, int fps, CancellationToken t = default)
        { ProfileCalls.Add($"XboxProfileFpsLimitAc:{key}:{fps}"); return Task.FromResult(XboxProfileResult()); }
        public Task<FrontendXboxGameProfileMutationResult> SetXboxGameProfileFpsLimitDcAsync(string key, int fps, CancellationToken t = default)
        { ProfileCalls.Add($"XboxProfileFpsLimitDc:{key}:{fps}"); return Task.FromResult(XboxProfileResult()); }
        public Task<FrontendXboxGameProfileMutationResult> SetXboxGameProfileGpuMinimumClockAcAsync(string key, int selectableClockIndex, CancellationToken t = default)
        { ProfileCalls.Add($"XboxProfileGpuMinimumClockAc:{key}:{selectableClockIndex}"); return Task.FromResult(XboxProfileResult()); }
        public Task<FrontendXboxGameProfileMutationResult> SetXboxGameProfileGpuMinimumClockDcAsync(string key, int selectableClockIndex, CancellationToken t = default)
        { ProfileCalls.Add($"XboxProfileGpuMinimumClockDc:{key}:{selectableClockIndex}"); return Task.FromResult(XboxProfileResult()); }
        public Task<FrontendXboxGameProfileMutationResult> SetXboxGameProfileGpuMinimumClockEnabledAsync(string key, bool enabled, CancellationToken t = default)
        { ProfileCalls.Add($"XboxProfileGpuMinimumClockEnabled:{key}:{enabled}"); return Task.FromResult(XboxProfileResult()); }
        public Task<FrontendXboxGameProfileMutationResult> SetXboxGameProfileResolutionAsync(string key, FrontendGameResolution? resolution, string? displayName, CancellationToken t = default)
        { ProfileCalls.Add($"XboxProfileResolution:{key}:{(resolution is null ? "null" : $"{resolution.Width}x{resolution.Height}")}"); return Task.FromResult(XboxProfileResult()); }
        public Task<FrontendXboxGameProfileMutationResult> SetXboxGameProfileBackButtonMappingAsync(string key, BackButtonMappingSettings? mapping, CancellationToken t = default)
        { ProfileCalls.Add($"XboxProfileBackButtonMapping:{key}:{(mapping is null ? "global" : "override")}"); LastXboxBackButtonMapping = mapping; return Task.FromResult(XboxProfileResult()); }

        private static FrontendGameProfileSnapshot ProfileSnapshot(uint appId, string displayName = "Game", bool enabled = true, bool cpuBoostEnabled = false, bool tdpEnabled = false, bool powerModeEnabled = false) => new(
            appId, displayName, Exists: true, Enabled: enabled,
            new FrontendGameCpuBoostConfiguration(cpuBoostEnabled, CpuBoostMode.Aggressive, CpuBoostMode.Disabled),
            new FrontendGameTdpConfiguration(tdpEnabled, new FrontendTdpPowerPair(15, 20), new FrontendTdpPowerPair(12, 18)),
            PersistenceWritable: true,
            Limits: new FrontendTdpLimits(8, 30, 8, 37),
            Resolution: null,
            PowerMode: new FrontendGamePowerModeConfiguration(powerModeEnabled, WindowsPowerMode.Balanced, WindowsPowerMode.Balanced));

        public static FrontendXboxGameProfileSnapshot XboxProfileSnapshot(string key, string displayName, bool enabled = true) => new(
            key, displayName, Exists: true, Enabled: enabled,
            new FrontendGameCpuBoostConfiguration(true, CpuBoostMode.Aggressive, CpuBoostMode.Disabled),
            new FrontendGameTdpConfiguration(true, new(15, 20), new(12, 18)),
            PersistenceWritable: true, Limits: new FrontendTdpLimits(8, 30, 8, 37),
            Resolution: new FrontendGameResolution(1920, 1080),
            PowerMode: new FrontendGamePowerModeConfiguration(true, WindowsPowerMode.Balanced, WindowsPowerMode.Balanced),
            FpsLimit: new FrontendGameFpsLimitConfiguration(true, 60, 60, true))
        {
            BackButtonMapping = new(true, new(Xbox360BackButtonTarget.LeftBumper, Xbox360BackButtonTarget.RightBumper)),
        };

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

        public Task<FrontendBatteryChargeLimitMutationResult> SetDeviceBatteryChargeLimitEnabledAsync(bool enabled, CancellationToken t = default)
        { Calls.Add($"BatteryEnabled:{enabled}"); return Task.FromResult(BatteryMutationResult); }

        public Task<FrontendBatteryChargeLimitMutationResult> SetDeviceBatteryChargeLimitPercentAsync(int percent, CancellationToken t = default)
        { Calls.Add($"BatteryPercent:{percent}"); return Task.FromResult(BatteryMutationResult); }

        public Task<FrontendBootstrapSnapshot> GetBootstrapAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendStatusSnapshot> CaptureStatusAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendSettingsSnapshot> SetLogLevelAsync(FrontendLogLevel level, CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendSettingsSnapshot> SetFrontButtonMappingAsync(SteamInputAddonforClaw.Contracts.FrontButtons.FrontButtonMappingSettings mapping, CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendSettingsSnapshot> SetBackButtonMappingAsync(SteamInputAddonforClaw.Contracts.BackButtons.BackButtonMappingSettings mapping, CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendSettingsSnapshot> SuppressDeveloperMenuWarningAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendPrerequisiteSetupResult> RunPrerequisiteSetupAsync(CancellationToken t = default) => throw new NotSupportedException();
        public Task<FrontendEnvironmentReportResult> GenerateEnvironmentReportAsync(CancellationToken t = default) => throw new NotSupportedException();
    }
}
