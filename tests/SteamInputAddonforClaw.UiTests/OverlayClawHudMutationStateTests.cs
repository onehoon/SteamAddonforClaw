using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayClawHudMutationStateTests
{
    private sealed record ValueCase(
        double Minimum,
        double Maximum,
        double Step,
        double InitialValue,
        double RequestedValue,
        Func<double, FrontendClawHudMutationIntent> CreateIntent,
        Func<FrontendClawHudSettingsSnapshot, double> ReadValue);

    private static FrontendClawHudSettingsSnapshot Settings() => new(
        FrontendClawHudDisplayMode.Always,
        0,
        FrontendClawHudFont.Unispace,
        FrontendClawHudAlignment.Left,
        FrontendClawHudBackgroundMode.FullWidth,
        50,
        false,
        null);

    private static FrontendClawHudSnapshot Snapshot(
        FrontendClawHudSettingsSnapshot settings,
        bool desiredEnabled = false,
        FrontendClawHudRuntimeState runtimeState = FrontendClawHudRuntimeState.Ready) =>
        new(desiredEnabled, runtimeState, "Ready", "1.0", "1.0", settings);

    private static IReadOnlyList<ValueCase> ValueCases =>
    [
        new(0, 1, 1, 0, 1,
            value => new(FrontendClawHudMutationKind.DisplayMode, DisplayMode: (FrontendClawHudDisplayMode)(int)value),
            settings => (int)settings.DisplayMode),
        new(-2, 2, 1, 0, 1,
            value => new(FrontendClawHudMutationKind.HudSizeOffset, HudSizeOffset: (int)value),
            settings => settings.HudSizeOffset),
        new(0, 1, 1, 0, 1,
            value => new(FrontendClawHudMutationKind.Font, Font: (FrontendClawHudFont)(int)value),
            settings => (int)settings.Font),
        new(0, 2, 1, 0, 1,
            value => new(FrontendClawHudMutationKind.Alignment, Alignment: (FrontendClawHudAlignment)(int)value),
            settings => (int)settings.Alignment),
        new(0, 1, 1, 0, 1,
            value => new(FrontendClawHudMutationKind.BackgroundMode, BackgroundMode: (FrontendClawHudBackgroundMode)(int)value),
            settings => (int)settings.BackgroundMode),
        new(50, 100, 5, 50, 55,
            value => new(FrontendClawHudMutationKind.CommitOpacity, OpacityPercent: (int)value),
            settings => settings.BackgroundOpacityPercent),
    ];

    [Fact]
    public void Value_rows_keep_pointer_and_dpad_preview_until_the_correlated_result()
    {
        foreach (var valueCase in ValueCases)
        foreach (var usePointer in new[] { false, true })
        {
            var state = new OverlayClawHudMutationState();
            var initial = Settings();
            state.ApplyState(Snapshot(initial));

            var model = new OverlayValueModel(value =>
                Assert.True(state.TryBeginSetting(valueCase.CreateIntent(value))));
            model.ApplyState(true, valueCase.Minimum, valueCase.Maximum, valueCase.Step, valueCase.InitialValue);

            if (usePointer)
                model.RequestSet(valueCase.RequestedValue);
            else
                model.RequestAdjust(+1);

            Assert.True(state.IsInFlight);
            Assert.Equal(valueCase.RequestedValue, model.PreviewValue);

            var independentlyUpdated = initial with { IntelVrrRangeFixEnabled = true };
            state.ApplyState(Snapshot(independentlyUpdated));
            var pendingSettings = state.DisplaySettings!;
            Assert.Equal(valueCase.RequestedValue, valueCase.ReadValue(pendingSettings));
            Assert.True(pendingSettings.IntelVrrRangeFixEnabled);
            Assert.True(state.IsInFlight);
            Assert.False(state.TryBeginSetting(valueCase.CreateIntent(valueCase.RequestedValue)));

            // The normal renderer applies the pending value while disabling the single-flight row.
            model.ApplyState(false, valueCase.Minimum, valueCase.Maximum, valueCase.Step,
                valueCase.ReadValue(pendingSettings));
            model.RequestSet(valueCase.RequestedValue + valueCase.Step);
            model.RequestAdjust(+1);
            Assert.Equal(valueCase.RequestedValue, model.PreviewValue);

            var completedSettings = pendingSettings;
            state.Complete(new(true, null, Snapshot(completedSettings)));
            Assert.False(state.IsInFlight);
            Assert.Null(state.PendingIntent);
            Assert.Equal(completedSettings, state.DisplaySettings);
            model.ApplyState(true, valueCase.Minimum, valueCase.Maximum, valueCase.Step,
                valueCase.ReadValue(state.DisplaySettings!));
            Assert.Equal(valueCase.RequestedValue, model.PreviewValue);
        }
    }

    [Fact]
    public void Toggle_rows_keep_pointer_and_accept_preview_until_the_correlated_result()
    {
        foreach (var usePointer in new[] { false, true })
        foreach (var isEnableHud in new[] { false, true })
        {
            var state = new OverlayClawHudMutationState();
            var initial = Settings();
            state.ApplyState(Snapshot(initial));
            const bool target = true;
            var model = new OverlayToggleModel(desired =>
            {
                var accepted = isEnableHud
                    ? state.TryBeginEnabled(desired)
                    : state.TryBeginSetting(new(FrontendClawHudMutationKind.IntelVrrRangeFixEnabled,
                        IntelVrrRangeFixEnabled: desired));
                Assert.True(accepted);
            });
            model.ApplyState(true, false);

            if (usePointer)
                model.RequestSet(target);
            else
                model.RequestToggle();

            model.ApplyState(false, isEnableHud ? state.DisplayEnabled : state.DisplaySettings!.IntelVrrRangeFixEnabled);
            Assert.True(model.IsOn);

            // An independent publication updates the other authoritative field without settling the edit.
            var independentlyUpdated = initial with { BackgroundOpacityPercent = 65 };
            state.ApplyState(Snapshot(independentlyUpdated));
            var pendingValue = isEnableHud ? state.DisplayEnabled : state.DisplaySettings!.IntelVrrRangeFixEnabled;
            model.ApplyState(false, pendingValue);
            Assert.True(model.IsOn);
            Assert.True(state.IsInFlight);
            Assert.False(isEnableHud
                ? state.TryBeginEnabled(false)
                : state.TryBeginSetting(new(FrontendClawHudMutationKind.IntelVrrRangeFixEnabled, IntelVrrRangeFixEnabled: false)));
            model.RequestSet(false);
            Assert.True(model.IsOn);

            var completedSettings = independentlyUpdated with
            {
                IntelVrrRangeFixEnabled = !isEnableHud,
            };
            state.Complete(new(true, null, Snapshot(completedSettings, desiredEnabled: isEnableHud)));
            model.ApplyState(true, isEnableHud ? state.DisplayEnabled : state.DisplaySettings!.IntelVrrRangeFixEnabled);
            Assert.True(model.IsOn);
            Assert.False(state.IsInFlight);
            Assert.Equal(65, state.DisplaySettings!.BackgroundOpacityPercent);
        }
    }

    [Fact]
    public void Rejected_result_and_transport_failure_clear_preview_to_latest_authority()
    {
        var initial = Settings();
        var state = new OverlayClawHudMutationState();
        state.ApplyState(Snapshot(initial));
        var intent = new FrontendClawHudMutationIntent(FrontendClawHudMutationKind.HudSizeOffset, HudSizeOffset: 1);
        Assert.True(state.TryBeginSetting(intent));

        var latest = initial with { HudSizeOffset = -1, Alignment = FrontendClawHudAlignment.Right };
        state.ApplyState(Snapshot(latest));
        Assert.Equal(1, state.DisplaySettings!.HudSizeOffset);
        state.Complete(FrontendClawHudMutationResult.Failed(Snapshot(latest), "Rejected"));
        Assert.False(state.IsInFlight);
        Assert.Equal(latest, state.DisplaySettings);

        Assert.True(state.TryBeginSetting(intent));
        var duringTransportFailure = latest with { Font = FrontendClawHudFont.SegoeUiVariable };
        state.ApplyState(Snapshot(duringTransportFailure));
        Assert.Equal(1, state.DisplaySettings!.HudSizeOffset);
        state.Fail();
        Assert.False(state.IsInFlight);
        Assert.Null(state.PendingIntent);
        Assert.Equal(duringTransportFailure, state.DisplaySettings);
    }

    [Fact]
    public void Current_power_source_toggle_keeps_its_pending_value_across_independent_settings_state()
    {
        foreach (var usePointer in new[] { false, true })
        {
            var authoritativeValue = false;
            bool? pendingValue = null;
            var available = true;
            var inFlight = false;
            var requests = 0;
            OverlayToggleModel model = null!;
            model = new OverlayToggleModel(desired =>
            {
                if (!available || inFlight)
                    return;

                inFlight = true;
                pendingValue = desired;
                requests++;
                model.ApplyState(available && !inFlight, pendingValue.Value);
            });
            model.ApplyState(available, authoritativeValue);

            if (usePointer)
                model.RequestSet(true);
            else
                model.RequestToggle();

            Assert.True(model.IsOn);
            Assert.False(model.IsAvailable);
            Assert.Equal(1, requests);

            // An unrelated FrontendSettingsState refresh updates authority but does not complete
            // the pending request; the renderer continues to use the accepted local preview.
            authoritativeValue = false;
            model.ApplyState(available && !inFlight, pendingValue ?? authoritativeValue);
            model.RequestSet(false);
            model.RequestToggle();
            Assert.True(model.IsOn);
            Assert.Equal(1, requests);

            authoritativeValue = true;
            pendingValue = null;
            inFlight = false;
            model.ApplyState(available && !inFlight, pendingValue ?? authoritativeValue);
            Assert.True(model.IsOn);
            Assert.True(model.IsAvailable);
        }
    }
}
