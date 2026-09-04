using SteamInputAddonforClaw.CenterM;
using SteamInputAddonforClaw.Contracts.FrontButtons;
using SteamInputAddonforClaw.Wing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>App UI PR-C section 22.6: both physical-button dispatchers resolve the domain from the
/// actual SteamDeck presentation fact and execute the resolved binding through the one shared action
/// executor -- including QuickSettingsOverlay from either button in either domain.</summary>
public sealed class FrontButtonDispatchTests
{
    private sealed class Seams
    {
        public int Overlay, BigPicture, SteamPulse, QuickAccess;
        public FrontButtonHotkeyBinding? Hotkey;
        public FrontButtonLaunchApplicationBinding? Launch;

        public FrontButtonActionExecutor Executor() => new(
            requestOverlayToggle: () => Overlay++,
            launchBigPicture: () => BigPicture++,
            tryRequestSteamPulse: () => { SteamPulse++; return true; },
            tryRequestQuickAccessPulse: () => { QuickAccess++; return true; },
            sendHotkey: h => Hotkey = h,
            launchApplication: l => Launch = l);
    }

    [Fact]
    public void Center_m_default_normal_press_launches_big_picture()
    {
        var seams = new Seams();
        var dispatcher = new Oem1ActionDispatcher(() => FrontButtonMappingSettings.Default, () => false, seams.Executor());

        Assert.True(dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single)));
        Assert.Equal(1, seams.BigPicture);
        Assert.Equal(0, seams.QuickAccess);
    }

    [Fact]
    public void Center_m_default_steam_press_pulses_quick_access()
    {
        var seams = new Seams();
        var dispatcher = new Oem1ActionDispatcher(() => FrontButtonMappingSettings.Default, () => true, seams.Executor());

        dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));
        Assert.Equal(1, seams.QuickAccess);
        Assert.Equal(0, seams.BigPicture);
    }

    [Fact]
    public void Gamebar_default_normal_press_toggles_the_overlay_and_steam_press_pulses_steam()
    {
        var seams = new Seams();
        var steamActive = false;
        var dispatcher = new WingActionDispatcher(() => FrontButtonMappingSettings.Default, () => steamActive, seams.Executor());

        dispatcher.Dispatch(WingGesture.Single);
        Assert.Equal(1, seams.Overlay);

        steamActive = true;
        dispatcher.Dispatch(WingGesture.Single);
        Assert.Equal(1, seams.SteamPulse);
    }

    [Fact]
    public void Quick_settings_overlay_works_from_either_button_in_either_domain()
    {
        var overlayMapping = new FrontButtonMappingSettings
        {
            Normal = new() { Gamebar = FrontButtonBinding.Of(FrontButtonAction.QuickSettingsOverlay), CenterM = FrontButtonBinding.Of(FrontButtonAction.SteamBigPicture) },
            Steam = new() { Gamebar = FrontButtonBinding.Of(FrontButtonAction.SteamButton), CenterM = FrontButtonBinding.Of(FrontButtonAction.QuickSettingsOverlay) },
        };
        var seams = new Seams();

        new WingActionDispatcher(() => overlayMapping, () => false, seams.Executor()).Dispatch(WingGesture.Single);
        new Oem1ActionDispatcher(() => overlayMapping, () => true, seams.Executor()).Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single));

        Assert.Equal(2, seams.Overlay);
    }

    [Fact]
    public void A_domain_invalid_persisted_binding_is_refused_not_executed()
    {
        // Bypass the settings validation entirely: Center M / Normal = SteamButton is not offered in
        // the Normal domain, so the runtime must refuse it rather than pulse Steam.
        var mapping = FrontButtonMappingSettings.Default.With(
            FrontButtonKind.CenterM, FrontButtonDomain.Normal, FrontButtonBinding.Of(FrontButtonAction.SteamButton));
        var seams = new Seams();
        var dispatcher = new Oem1ActionDispatcher(() => mapping, () => false, seams.Executor());

        Assert.True(dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single)));
        Assert.Equal(0, seams.SteamPulse);
    }

    [Fact]
    public void A_gamebar_win_g_hotkey_binding_is_refused()
    {
        var mapping = FrontButtonMappingSettings.Default.With(
            FrontButtonKind.Gamebar, FrontButtonDomain.Normal, FrontButtonBinding.Of(FrontButtonAction.KeyboardHotkey) with
            {
                Hotkey = new FrontButtonHotkeyBinding(FrontButtonHotkeyModifiers.Windows, FrontButtonHotkeyKey.G)
            });
        var seams = new Seams();
        new WingActionDispatcher(() => mapping, () => false, seams.Executor()).Dispatch(WingGesture.Single);

        Assert.Null(seams.Hotkey);
    }

    [Fact]
    public void A_center_m_action_execution_failure_returns_false_for_fail_open()
    {
        var executor = new FrontButtonActionExecutor(
            requestOverlayToggle: () => { },
            launchBigPicture: () => throw new InvalidOperationException("boom"),
            tryRequestSteamPulse: () => false,
            tryRequestQuickAccessPulse: () => false);
        var dispatcher = new Oem1ActionDispatcher(() => FrontButtonMappingSettings.Default, () => false, executor);

        Assert.False(dispatcher.Dispatch(new Oem1GesturePolicyRequest(Oem1Gesture.Single)));
    }

    [Fact]
    public void A_mapping_capture_failure_is_contained()
    {
        var dispatcher = new WingActionDispatcher(() => throw new InvalidOperationException("mapping"), () => false, new Seams().Executor());
        Assert.Null(Record.Exception(() => dispatcher.Dispatch(WingGesture.Single)));
    }
}
