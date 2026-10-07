using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow
{
    private const string NoActiveGameMessage = "No game is currently running. Start a game to configure its profile.";
    private const string UnavailableProfileMessage = "Profile settings are unavailable.";

    private FrameworkElement? _profileDetailRoot;
    private TextBlock? _profileStatusMessage;

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
            Text = NoActiveGameMessage,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 20,
        };
        OverlayQamResources.ApplyTextStyle(_profileStatusMessage, "QamBodyStrongTextStyle");
        _profileStatusMessage.FontSize = 20;
        root.Children.Add(_profileStatusMessage);
        return root;
    }

    internal bool TryHandleBack() => TryHandleSettingBack();

    private void OnProfileTabSelectionChanged(bool selected)
    {
        if (!selected || !_quickSettingsSurfaces.TryGetValue(QuickSettingsPageId.Profile, out var surface))
            return;

        ApplyProfilePresentation(surface.Binding?.AuthoritativePage);
    }

    private void PrepareActiveProfileFirstShow()
    {
        if (!_quickSettingsSurfaces.TryGetValue(QuickSettingsPageId.Profile, out var surface)) return;
        var page = QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, null, NoActiveGameMessage);
        surface.Binding?.ApplyAuthoritativePage(page);
        RenderQuickSettingsPage(surface);
        ApplyProfilePresentation(page);
    }

    internal void ApplyActiveProfilePage(QuickSettingsPageSnapshot page)
    {
        if (page.PageId != QuickSettingsPageId.Profile) return;
        if (!_quickSettingsSurfaces.TryGetValue(QuickSettingsPageId.Profile, out var surface)) return;

        surface.Binding?.ApplyAuthoritativePage(page);
        RenderQuickSettingsPage(surface);
        ApplyProfilePresentation(page);
    }

    private void ApplyProfilePresentation(QuickSettingsPageSnapshot? page)
    {
        if (_profileDetailRoot is null || _profileStatusMessage is null) return;

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

    internal static bool HasRenderableActiveProfile(QuickSettingsPageSnapshot? page) =>
        page is { PageId: QuickSettingsPageId.Profile, Available: true, ProfileTarget: { IsStructurallyValid: true } };

    internal static string ResolveProfileStatusMessage(QuickSettingsPageSnapshot? page) =>
        page?.Message ?? UnavailableProfileMessage;
}
