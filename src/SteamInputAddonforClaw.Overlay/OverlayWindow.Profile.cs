using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Overlay.Diagnostics;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow
{
    private enum ProfilePresentationMode { Catalog, SelectedDetail, ActiveDetail }

    private readonly OverlayProfileCatalogSelection _profileCatalogSelection = new();
    private readonly List<FrontendProfileGameCatalogEntry> _profileCatalog = [];
    private readonly List<Button> _profileCatalogCards = [];
    private Grid? _profileCatalogGrid;
    private StackPanel? _profileCatalogPanel;
    private TextBlock? _profileCatalogStatus;
    private FrameworkElement? _profileDetailRoot;
    private ProfilePresentationMode _profileMode = ProfilePresentationMode.Catalog;
    private uint? _selectedCatalogAppId;
    private uint? _activeProfileAppId;
    private bool _profileTabSelected;

    internal event Action? ProfileCatalogRequestRequested;
    internal event Action<uint>? ProfilePageRequestRequested;

    private FrameworkElement BuildProfilePage()
    {
        var root = new Grid { RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _profileCatalogStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.72 };
        if (Application.Current.Resources.TryGetValue("CaptionTextBlockStyle", out var statusStyle) && statusStyle is Style style)
            _profileCatalogStatus.Style = style;
        _profileCatalogGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        for (var i = 0; i < 3; i++)
            _profileCatalogGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _profileCatalogPanel = new StackPanel { Spacing = 10 };
        _profileCatalogPanel.Children.Add(_profileCatalogStatus);
        _profileCatalogPanel.Children.Add(_profileCatalogGrid);
        Grid.SetRow(_profileCatalogPanel, 0);
        root.Children.Add(_profileCatalogPanel);

        _profileDetailRoot = BuildQuickSettingsPage(AddonQuickSettingsTabId.Profile, QuickSettingsPageId.Profile);
        _profileDetailRoot.Visibility = Visibility.Collapsed;
        Grid.SetRow(_profileDetailRoot, 1);
        root.Children.Add(_profileDetailRoot);
        return root;
    }

    internal void ApplyProfileCatalogState(OverlayProfileCatalogState state)
    {
        if (!_profileTabSelected || _profileMode != ProfilePresentationMode.Catalog) return;
        _profileCatalog.Clear();
        _profileCatalog.AddRange(state.Entries
            .OrderByDescending(entry => entry.Favorite)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.AppId)
            .ToArray());
        _profileCatalogSelection.Reset(_profileCatalog.Count);
        RebuildProfileCatalogCards();
        _profileCatalogStatus!.Text = state.Error is not null
            ? state.Error
            : _profileCatalog.Count == 0 ? "No games found." : string.Empty;
        ApplyProfileCatalogSelectionVisual();
    }

    internal void ApplyProfilePageResult(OverlayProfilePageResponse response)
    {
        if (!_profileTabSelected || _profileMode != ProfilePresentationMode.SelectedDetail || _selectedCatalogAppId != response.AppId)
            return;
        if (response.Page.PageId != QuickSettingsPageId.Profile || response.Page.AppId != response.AppId)
            return;
        ApplyProfileDetailPage(response.Page);
        if (response.Error is not null)
            OverlayLog.Warn("Profile", response.Error);
    }

    internal bool TryHandleBack()
    {
        if (_tabState.SelectedTab != AddonQuickSettingsTabId.Profile || _profileMode != ProfilePresentationMode.SelectedDetail)
            return false;
        _quickSettingsSurfaces[QuickSettingsPageId.Profile].Binding?.CancelUnsubmittedDrafts();
        _selectedCatalogAppId = null;
        _profileMode = ProfilePresentationMode.Catalog;
        ShowProfileCatalog("Loading games…");
        ProfileCatalogRequestRequested?.Invoke();
        return true;
    }

    private void OnProfileTabSelectionChanged(bool selected)
    {
        if (!selected)
        {
            if (_profileMode == ProfilePresentationMode.SelectedDetail)
                _quickSettingsSurfaces[QuickSettingsPageId.Profile].Binding?.CancelUnsubmittedDrafts();
            _profileTabSelected = false;
            _selectedCatalogAppId = null;
            _profileMode = _activeProfileAppId is not null ? ProfilePresentationMode.ActiveDetail : ProfilePresentationMode.Catalog;
            return;
        }

        _profileTabSelected = true;
        if (_activeProfileAppId is not null)
        {
            _profileMode = ProfilePresentationMode.ActiveDetail;
            ShowProfileDetail();
        }
        else
        {
            _profileMode = ProfilePresentationMode.Catalog;
            ShowProfileCatalog("Loading games…");
            ProfileCatalogRequestRequested?.Invoke();
        }
    }

    private void ApplyProfileDetailPage(QuickSettingsPageSnapshot page)
    {
        if (!_quickSettingsSurfaces.TryGetValue(QuickSettingsPageId.Profile, out var surface)) return;
        surface.Binding?.ApplyAuthoritativePage(page);
        RenderQuickSettingsPage(surface);
        ShowProfileDetail();
    }

    private void ShowProfileCatalog(string status)
    {
        if (_profileCatalogPanel is null || _profileDetailRoot is null) return;
        _profileCatalogPanel.Visibility = Visibility.Visible;
        _profileDetailRoot.Visibility = Visibility.Collapsed;
        if (_profileCatalogStatus is not null) _profileCatalogStatus.Text = status;
        RebuildProfileCatalogCards();
    }

    private void ShowProfileDetail()
    {
        if (_profileCatalogPanel is null || _profileDetailRoot is null) return;
        _profileCatalogPanel.Visibility = Visibility.Collapsed;
        _profileDetailRoot.Visibility = Visibility.Visible;
    }

    private void RebuildProfileCatalogCards()
    {
        if (_profileCatalogGrid is null) return;
        _profileCatalogGrid.Children.Clear();
        _profileCatalogGrid.RowDefinitions.Clear();
        _profileCatalogCards.Clear();
        var rowCount = (_profileCatalog.Count + 2) / 3;
        for (var row = 0; row < rowCount; row++)
            _profileCatalogGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var index = 0; index < _profileCatalog.Count; index++)
        {
            var entry = _profileCatalog[index];
            var card = new Button
            {
                Content = entry.Name,
                Tag = index,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 12, 12, 12),
                MinHeight = 58,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
            };
            if (Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var fill) && fill is Brush brush)
                card.Background = brush;
            card.Click += OnProfileCatalogCardClick;
            Grid.SetRow(card, index / 3);
            Grid.SetColumn(card, index % 3);
            _profileCatalogGrid.Children.Add(card);
            _profileCatalogCards.Add(card);
        }
    }

    private void OnProfileCatalogCardClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: int index })
        {
            _profileCatalogSelection.Select(index);
            ApplyProfileCatalogSelectionVisual();
            OpenSelectedProfile();
        }
    }

    private void ApplyProfileCatalogSelectionVisual()
    {
        for (var index = 0; index < _profileCatalogCards.Count; index++)
        {
            var selected = index == _profileCatalogSelection.SelectedIndex;
            _profileCatalogCards[index].BorderBrush = selected ? _rowSelectedBrush : RowUnselectedBrush;
            _profileCatalogCards[index].Background = selected ? _rowSelectedFillBrush : RowUnselectedFillBrush;
        }
    }

    private void OpenSelectedProfile()
    {
        var index = _profileCatalogSelection.SelectedIndex;
        if (index < 0 || index >= _profileCatalog.Count) return;
        var entry = _profileCatalog[index];
        _selectedCatalogAppId = entry.AppId;
        _profileMode = ProfilePresentationMode.SelectedDetail;
        ShowProfileDetail();
        var loading = QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, entry.AppId, "Loading Profile settings…");
        ApplyProfileDetailPage(loading);
        ProfilePageRequestRequested?.Invoke(entry.AppId);
    }

    private bool OnProfileCatalogPage() => _tabState.SelectedTab == AddonQuickSettingsTabId.Profile && _profileMode == ProfilePresentationMode.Catalog;

    private bool OnProfileSelectedDetailPage() => _tabState.SelectedTab == AddonQuickSettingsTabId.Profile && _profileMode == ProfilePresentationMode.SelectedDetail;

    private void RefreshProfileCatalogSelectionAfterMove()
    {
        ApplyProfileCatalogSelectionVisual();
        var index = _profileCatalogSelection.SelectedIndex;
        if (index < 0 || index >= _profileCatalogCards.Count) return;

        try
        {
            _profileCatalogCards[index].StartBringIntoView(
                new BringIntoViewOptions { AnimationDesired = false });
        }
        catch (Exception exception)
        {
            OverlayLog.Warn("Profile", "Could not bring the selected Profile card into view.", exception);
        }
    }

    internal void NavigateProfileCatalogUp() { if (_profileCatalogSelection.MoveUp()) RefreshProfileCatalogSelectionAfterMove(); }
    internal void NavigateProfileCatalogDown() { if (_profileCatalogSelection.MoveDown()) RefreshProfileCatalogSelectionAfterMove(); }
    internal void NavigateProfileCatalogLeft() { if (_profileCatalogSelection.MoveLeft()) RefreshProfileCatalogSelectionAfterMove(); }
    internal void NavigateProfileCatalogRight() { if (_profileCatalogSelection.MoveRight()) RefreshProfileCatalogSelectionAfterMove(); }

    internal void ActivateProfileCatalogSelection() => OpenSelectedProfile();

    internal void ApplyActiveProfilePage(QuickSettingsPageSnapshot page)
    {
        if (page.PageId != QuickSettingsPageId.Profile) return;
        if (page.Available && page.AppId is > 0)
        {
            if (_profileMode == ProfilePresentationMode.SelectedDetail)
                _quickSettingsSurfaces[QuickSettingsPageId.Profile].Binding?.CancelUnsubmittedDrafts();
            _activeProfileAppId = page.AppId;
            _selectedCatalogAppId = null;
            _profileMode = ProfilePresentationMode.ActiveDetail;
            ApplyProfileDetailPage(page);
        }
        else
        {
            _activeProfileAppId = null;

            if (_profileMode == ProfilePresentationMode.SelectedDetail && _selectedCatalogAppId is { } selectedAppId)
            {
                ProfilePageRequestRequested?.Invoke(selectedAppId);
                return;
            }

            _selectedCatalogAppId = null;
            _profileMode = ProfilePresentationMode.Catalog;
            if (_profileTabSelected)
            {
                ShowProfileCatalog("Loading games…");
                ProfileCatalogRequestRequested?.Invoke();
            }
        }
    }
}
