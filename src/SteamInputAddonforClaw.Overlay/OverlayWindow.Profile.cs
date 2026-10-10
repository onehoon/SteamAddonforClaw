using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow
{
    private const string NoActiveGameMessage = "No game is currently running. Start a game to configure its profile.";
    private const string NoActiveGameDisplayMessage = "No game is currently running.\nStart a game to configure its profile.";
    private const string UnavailableProfileMessage = "Profile settings are unavailable.";

    private FrameworkElement? _profileDetailRoot;
    private TextBlock? _profileStatusMessage;
    private bool _activeProfileInitialLoadPending;
    private bool _noGameRecheckRequestedForThisEntry;

    internal event Action? NoRunningGameProfileRecheckRequested;

    private FrameworkElement BuildProfilePage()
    {
        var root = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        _profileDetailRoot = BuildQuickSettingsPage(AddonQuickSettingsTabId.Profile, QuickSettingsPageId.Profile);
        _profileDetailRoot.Visibility = Visibility.Collapsed;
        root.Children.Add(_profileDetailRoot);

        _profileStatusMessage = new TextBlock
        {
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        OverlayQamResources.ApplyTextStyle(_profileStatusMessage, "QamBodyStrongTextStyle");
        _profileStatusMessage.FontSize = 16;
        root.Children.Add(_profileStatusMessage);
        return root;
    }

    internal bool TryHandleBack() => TryHandleSettingBack();

    private void OnProfileTabSelectionChanged(bool selected)
    {
        if (!selected)
        {
            _noGameRecheckRequestedForThisEntry = false;
            return;
        }

        if (!_quickSettingsSurfaces.TryGetValue(QuickSettingsPageId.Profile, out var surface))
            return;

        ApplyProfilePresentation(surface.Binding?.AuthoritativePage);
        TryRequestNoRunningGameRecheck(surface.Binding?.AuthoritativePage);
    }

    private void PrepareActiveProfileFirstShow()
    {
        if (!_quickSettingsSurfaces.TryGetValue(QuickSettingsPageId.Profile, out var surface)) return;
        _noGameRecheckRequestedForThisEntry = false;
        _activeProfileInitialLoadPending = true;
        var page = QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, null, "Loading the active game profile.");
        surface.Binding?.ApplyAuthoritativePage(page);
        RenderQuickSettingsPage(surface);
        ApplyProfilePresentation(page);
    }

    internal void ApplyActiveProfilePage(QuickSettingsPageSnapshot page)
    {
        if (page.PageId != QuickSettingsPageId.Profile) return;
        if (!_quickSettingsSurfaces.TryGetValue(QuickSettingsPageId.Profile, out var surface)) return;

        _activeProfileInitialLoadPending = false;
        surface.Binding?.ApplyAuthoritativePage(page);
        RenderQuickSettingsPage(surface);
        ApplyProfilePresentation(page);
        TryRequestNoRunningGameRecheck(page);
    }

    private void ApplyProfilePresentation(QuickSettingsPageSnapshot? page)
    {
        if (_profileDetailRoot is null || _profileStatusMessage is null) return;

        if (_activeProfileInitialLoadPending)
        {
            _profileDetailRoot.Visibility = Visibility.Collapsed;
            _profileStatusMessage.Visibility = Visibility.Collapsed;
            return;
        }

        var activeProfileReady = HasRenderableActiveProfile(page);
        if (activeProfileReady)
        {
            _profileStatusMessage.Visibility = Visibility.Collapsed;
            _profileDetailRoot.Visibility = Visibility.Visible;
            return;
        }

        _profileDetailRoot.Visibility = Visibility.Collapsed;
        _profileStatusMessage.Text = ResolveProfileStatusMessage(page);
        _profileStatusMessage.Visibility = Visibility.Visible;
    }

    private void TryRequestNoRunningGameRecheck(QuickSettingsPageSnapshot? page)
    {
        if (!ShouldRequestNoRunningGameRecheck(
                page,
                _tabState.SelectedTab == AddonQuickSettingsTabId.Profile,
                _activeProfileInitialLoadPending,
                _noGameRecheckRequestedForThisEntry))
            return;

        _noGameRecheckRequestedForThisEntry = true;
        NoRunningGameProfileRecheckRequested?.Invoke();
    }

    internal static bool ShouldRequestNoRunningGameRecheck(
        QuickSettingsPageSnapshot? page,
        bool profileSelected,
        bool initialLoadPending,
        bool alreadyRequestedForEntry) =>
        profileSelected && !initialLoadPending && !alreadyRequestedForEntry && IsExactNoRunningGamePage(page);

    internal static bool IsExactNoRunningGamePage(QuickSettingsPageSnapshot? page) =>
        page is { PageId: QuickSettingsPageId.Profile, Available: false, ProfileTarget: null, Message: NoActiveGameMessage };

    internal static bool HasRenderableActiveProfile(QuickSettingsPageSnapshot? page) =>
        page is { PageId: QuickSettingsPageId.Profile, Available: true, ProfileTarget: { IsStructurallyValid: true } };

    internal static string ResolveProfileStatusMessage(QuickSettingsPageSnapshot? page) =>
        page is { PageId: QuickSettingsPageId.Profile, ProfileTarget: null, Message: NoActiveGameMessage }
            ? NoActiveGameDisplayMessage
            : page?.Message ?? UnavailableProfileMessage;
}
