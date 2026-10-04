using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private FrameworkElement? _profileDetailRoot;
    private ProfilePresentationMode _profileMode = ProfilePresentationMode.Catalog;
    private uint? _selectedCatalogAppId;
    private uint? _activeProfileAppId;
    private bool _profileTabSelected;
    private bool _profileDetailNavigationInProgress;

    internal event Action? ProfileCatalogRequestRequested;
    internal event Action<uint>? ProfilePageRequestRequested;

    private FrameworkElement BuildProfilePage()
    {
        var root = new Grid { RowSpacing = OverlayQamResources.Get("QamTileSpacing", 8.0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _profileCatalogGrid = new Grid
        {
            ColumnSpacing = OverlayQamResources.Get("QamTileSpacing", 8.0),
            RowSpacing = OverlayQamResources.Get("QamTileSpacing", 8.0),
        };
        for (var i = 0; i < 2; i++)
            _profileCatalogGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(_profileCatalogGrid, 0);
        root.Children.Add(_profileCatalogGrid);

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
        if (state.Error is not null)
            OverlayLog.Warn("Profile", state.Error);
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
        if (TryHandleSettingBack())
            return true;

        if (_tabState.SelectedTab != AddonQuickSettingsTabId.Profile || _profileMode != ProfilePresentationMode.SelectedDetail)
            return false;
        if (_profileDetailNavigationInProgress) return true;
        _profileDetailNavigationInProgress = true;
        ProfileSelectedDetailBackRequested?.Invoke();
        return true;
    }

    internal void CompleteProfileSelectedDetailBack()
    {
        _profileDetailNavigationInProgress = false;
        if (_profileMode != ProfilePresentationMode.SelectedDetail) return;
        _selectedCatalogAppId = null;
        _profileMode = ProfilePresentationMode.Catalog;
        if (_profileTabSelected)
        {
            ShowProfileCatalog();
            ProfileCatalogRequestRequested?.Invoke();
        }
    }

    internal void CompleteProfileSelectedDetailTabLeave()
    {
        _profileDetailNavigationInProgress = false;
        if (_profileTabSelected || _profileMode != ProfilePresentationMode.SelectedDetail) return;
        _selectedCatalogAppId = null;
        _profileMode = _activeProfileAppId is not null ? ProfilePresentationMode.ActiveDetail : ProfilePresentationMode.Catalog;
    }

    private void OnProfileTabSelectionChanged(bool selected)
    {
        if (!selected)
        {
            if (_profileMode == ProfilePresentationMode.SelectedDetail)
            {
                _profileTabSelected = false;
                if (!_profileDetailNavigationInProgress)
                {
                    _profileDetailNavigationInProgress = true;
                    ProfileSelectedDetailTabLeaveRequested?.Invoke();
                }
                return;
            }
            _profileTabSelected = false;
            _selectedCatalogAppId = null;
            _profileMode = _activeProfileAppId is not null ? ProfilePresentationMode.ActiveDetail : ProfilePresentationMode.Catalog;
            return;
        }

        _profileTabSelected = true;
        if (_profileDetailNavigationInProgress && _profileMode == ProfilePresentationMode.SelectedDetail)
        {
            ShowProfileDetail();
            return;
        }
        if (_activeProfileAppId is not null)
        {
            _profileMode = ProfilePresentationMode.ActiveDetail;
            ShowProfileDetail();
        }
        else
        {
            _profileMode = ProfilePresentationMode.Catalog;
            ShowProfileCatalog();
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

    private void ShowProfileCatalog()
    {
        if (_profileCatalogGrid is null || _profileDetailRoot is null) return;
        _profileCatalogGrid.Visibility = Visibility.Visible;
        _profileDetailRoot.Visibility = Visibility.Collapsed;
        RebuildProfileCatalogCards();
    }

    private void ShowProfileDetail()
    {
        if (_profileCatalogGrid is null || _profileDetailRoot is null) return;
        _profileCatalogGrid.Visibility = Visibility.Collapsed;
        _profileDetailRoot.Visibility = Visibility.Visible;
    }

    private void RebuildProfileCatalogCards()
    {
        if (_profileCatalogGrid is null) return;
        _profileCatalogGrid.Children.Clear();
        _profileCatalogGrid.RowDefinitions.Clear();
        _profileCatalogCards.Clear();
        var rowCount = (_profileCatalog.Count + 1) / 2;
        for (var row = 0; row < rowCount; row++)
            _profileCatalogGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var index = 0; index < _profileCatalog.Count; index++)
        {
            var entry = _profileCatalog[index];
            var title = new TextBlock
            {
                Text = entry.Name,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalTextAlignment = TextAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            OverlayQamResources.ApplyTextStyle(title, "QamTileTitleTextStyle");
            title.FontSize = 15;

            var card = new Button
            {
                Content = title,
                Tag = index,
                Style = OverlayQamResources.Style("QamTileButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            card.Click += OnProfileCatalogCardClick;
            Grid.SetRow(card, index / 2);
            Grid.SetColumn(card, index % 2);
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
            _profileCatalogCards[index].BorderBrush = _rowSelectedBrush;
            _profileCatalogCards[index].Background = OverlayQamResources.Brush(selected ? "QamTileSelectedBrush" : "QamTileBrush");
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
        var loading = QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, entry.AppId);
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
                ShowProfileCatalog();
                ProfileCatalogRequestRequested?.Invoke();
            }
        }
    }
}
