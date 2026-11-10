using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Windows.Storage;
using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.UI;

namespace SteamInputAddonforClaw.Views;

public sealed partial class ProfilePage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private FrontendModelFeatureSupport _featureSupport = FrontendModelFeatureSupport.None;
    private bool _active, _suppressEvents, _suppressTdpEvents, _suppressFpsEvents, _suppressFeatureEvents, _suppressGpuEvents;
    private bool _catalogLoaded, _catalogScanInProgress;
    private bool? _steamActive;
    private int _steamVirtualControllerMutationInProgress;
    private readonly Dictionary<SelectorItem, RealizedGameCard> _realizedGameCards = [];
    private ItemsWrapGrid? _coverItemsPanel;
    private SteamPortraitArtworkContext? _portraitArtworkContext;
    private XamlRoot? _observedXamlRoot;
    private double _lastRasterizationScale = double.NaN;
    private double _pendingLayoutRasterizationScale = 1.0;
    private SteamCoverGridLayout? _coverGridLayout;
    private readonly DispatcherQueueTimer _coverGridResizeTimer;
    private IReadOnlyList<FrontendProfileGameCatalogEntry> _catalog = [];
    private ObservableCollection<GameCardItem> _visibleGameCards = new();
    private FrontendProfileGameCatalogEntry? _selectedGame;
    private FrontendGameProfileSnapshot? _snapshot;
    private int? _acPl1, _acPl2, _dcPl1, _dcPl2;
    private CancellationTokenSource? _tdpDebounce;
    private long _tdpGeneration;
    private bool _tdpDraftDirty;
    private CancellationTokenSource? _acFpsDebounce, _dcFpsDebounce; private long _acFpsGeneration, _dcFpsGeneration; private int? _acFpsDraft, _dcFpsDraft;
    private static readonly HashSet<uint> HiddenSteamAppIds =
    [
        480u,     // Spacewar
        228980u,  // Steamworks Common Redistributables
        993090u   // Lossless Scaling
    ];
    private CancellationTokenSource? _acGpuDebounce, _dcGpuDebounce; private long _acGpuGeneration, _dcGpuGeneration, _gpuDraftGeneration; private int? _acGpuDraftIndex, _dcGpuDraftIndex; private bool _gpuDraftDirty;
    private static readonly CpuBoostModeItem[] Modes = [new(CpuBoostMode.Disabled, MainUiStrings.Get("Steam_CpuBoost_Mode_Disabled", "Disabled")), new(CpuBoostMode.Enabled, MainUiStrings.Get("Steam_CpuBoost_Mode_Enabled", "Enabled")), new(CpuBoostMode.Aggressive, MainUiStrings.Get("Steam_CpuBoost_Mode_Aggressive", "Aggressive")), new(CpuBoostMode.EfficientEnabled, MainUiStrings.Get("Steam_CpuBoost_Mode_EfficientEnabled", "Efficient Enabled")), new(CpuBoostMode.EfficientAggressive, MainUiStrings.Get("Steam_CpuBoost_Mode_EfficientAggressive", "Efficient Aggressive")), new(CpuBoostMode.AggressiveAtGuaranteed, MainUiStrings.Get("Steam_CpuBoost_Mode_AggressiveAtGuaranteed", "Aggressive At Guaranteed")), new(CpuBoostMode.EfficientAggressiveAtGuaranteed, MainUiStrings.Get("Steam_CpuBoost_Mode_EfficientAggressiveAtGuaranteed", "Efficient Aggressive At Guaranteed"))];
    private static readonly ResolutionItem[] ResolutionItems = [new(null, null, MainUiStrings.Get("Steam_Resolution_DoNotChange", "Do not change")), new(1920, 1200, "1920 × 1200"), new(1920, 1080, "1920 × 1080"), new(1680, 1050, "1680 × 1050"), new(1440, 900, "1440 × 900")];

    public ProfilePage()
    {
        InitializeComponent(); CpuBoostAcComboBox.ItemsSource = Modes; CpuBoostDcComboBox.ItemsSource = Modes; PowerModeAcComboBox.ItemsSource = PowerModes; PowerModeDcComboBox.ItemsSource = PowerModes; ResolutionComboBox.ItemsSource = ResolutionItems; SetEditorsEnabled(false);
        _coverGridResizeTimer = DispatcherQueue.CreateTimer();
        _coverGridResizeTimer.Interval = TimeSpan.FromMilliseconds(180);
        _coverGridResizeTimer.IsRepeating = false;
        _coverGridResizeTimer.Tick += CoverGridResizeTimer_Tick;
        Loaded += ProfilePage_Loaded;
        Unloaded += ProfilePage_Unloaded;
}
    internal event EventHandler? SteamActivityRefreshRequested;
    internal void Initialize(IAddonFrontendControl frontend, FrontendModelFeatureSupport? featureSupport = null)
    {
        _frontend = frontend;
        _featureSupport = featureSupport ?? FrontendModelFeatureSupport.None;
        TdpExpander.Visibility = _featureSupport.Tdp ? Visibility.Visible : Visibility.Collapsed;
        IntelFpsExpander.Visibility = _featureSupport.IntelFpsLimit ? Visibility.Visible : Visibility.Collapsed;
        GpuMinimumClockExpander.Visibility = _featureSupport.IntelGpuMinimumClock ? Visibility.Visible : Visibility.Collapsed;
    }
    internal void Activate()
    {
        _active = true;
        if (_frontend is not null) _frontend.StateInvalidated += OnStateInvalidated;
        RestartMissingGameCardArtworkLoads();

        if (_catalogLoaded)
        {
            if (_selectedGame is { } selected)
                _ = CaptureSelectedAsync(selected.AppId);
            return;
        }

        if (ShouldStartCatalogScan(_catalogLoaded, _catalogScanInProgress))
            _ = RefreshGamesAsync();
    }

    internal static bool ShouldStartCatalogScan(bool catalogLoaded, bool scanInProgress) =>
        !catalogLoaded && !scanInProgress;

    internal void Deactivate() { _active = false; _frontend?.StateInvalidated -= OnStateInvalidated; CancelGpuMinimumClockDebounce(); CancelPendingGameCardArtworkLoads(); }
    private void OnStateInvalidated(object? sender, EventArgs e) { if (_active && _selectedGame is not null) DispatcherQueue.TryEnqueue(() => _ = CaptureSelectedAsync(_selectedGame.AppId)); }

    private async void RefreshGamesButton_Click(object sender, RoutedEventArgs e)
        => await RefreshGamesAsync(explicitRefresh: true);

    private async Task RefreshGamesAsync(bool explicitRefresh = false)
    {
        if (_frontend is null || _catalogScanInProgress) return;
        _catalogScanInProgress = true;
        RefreshGamesButton.IsEnabled = false;
        var trigger = explicitRefresh ? "ManualRefresh" : "FirstTabEntry";
        var scanTimer = Stopwatch.StartNew();
        AppLog.Info("Profiles.Catalog", "Steam profile catalog scan requested.", ("Trigger", trigger));
        try
        {
            var selected = _selectedGame?.AppId;
            var scannedCatalog = await _frontend.ScanProfileGamesAsync();
            if (scannedCatalog.Count > 0)
            {
                try { _portraitArtworkContext = await Task.Run(SteamPortraitArtworkResolver.CreateContext); }
                catch (Exception) { _portraitArtworkContext = null; }
            }
            else
            {
                _portraitArtworkContext = null;
            }
            _catalog = FilterSteamCatalog(scannedCatalog);
            _catalogLoaded = true;
            FavoriteStateInfoBar.IsOpen = _catalog.Any(game => !game.FavoriteStateKnown);
            ApplyCatalogFilter();
            _selectedGame = selected is { } id ? _catalog.FirstOrDefault(x => x.AppId == id) : null;
            if (_selectedGame is null)
            {
                ClearSelection();
                if (_catalog.Count == 0)
                {
                    var message = GetEmptyCatalogMessageKey(scannedCatalog.Count) switch
                    {
                        "Steam_Catalog_NoGames" => MainUiStrings.Get("Steam_Catalog_NoGames", "No installed Steam games were found."),
                        _ => MainUiStrings.Get("Steam_Catalog_NoVisibleGames", "No games to display.")
                    };
                    ShowInfo(message);
                }
                else
                {
                    ClearError();
                }
            }
            else
            {
                await CaptureSelectedAsync(_selectedGame.AppId);
            }
            AppLog.Info("Profiles.Catalog", "Steam profile catalog scan completed.",
                ("GameCount", _catalog.Count),
                ("DurationMs", scanTimer.Elapsed.TotalMilliseconds));
        }
        catch (Exception exception)
        {
            AppLog.Warn("Profiles.Catalog", "Steam profile catalog scan failed.", exception,
                ("DurationMs", scanTimer.Elapsed.TotalMilliseconds),
                ("CatalogWasLoaded", _catalogLoaded));
            ShowError(MainUiStrings.Get("Steam_Catalog_RefreshFailed", "Game catalog could not be refreshed."), exception);
        }
        finally
        {
            _catalogScanInProgress = false;
            RefreshGamesButton.IsEnabled = true;
        }
    }

    private void GameSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyCatalogFilter();
    private void ApplyCatalogFilter()
    {
        var query = GameSearchBox.Text.Trim();
        _visibleGameCards = new ObservableCollection<GameCardItem>(
            FilterAndSort(_catalog, query).Select(game => new GameCardItem(game)));
        GameGrid.ItemsSource = _visibleGameCards;
    }

    private void ProfilePage_Loaded(object sender, RoutedEventArgs e)
    {
        AttachXamlRoot();
        RestartMissingGameCardArtworkLoads();
    }

    private void CoverItemsPanel_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ItemsWrapGrid itemsPanel)
            AttachCoverItemsPanel(itemsPanel);
    }

    private void AttachCoverItemsPanel(ItemsWrapGrid itemsPanel)
    {
        if (!ReferenceEquals(_coverItemsPanel, itemsPanel))
        {
            DetachCoverItemsPanel();
            _coverItemsPanel = itemsPanel;
            _coverItemsPanel.SizeChanged += GameGridItemsPanel_SizeChanged;
            _coverGridLayout = null;
        }

        ApplyCoverGridLayout(GameGrid.XamlRoot?.RasterizationScale ?? 1.0);
    }

    private void ProfilePage_Unloaded(object sender, RoutedEventArgs e)
    {
        _coverGridResizeTimer.Stop();
        DetachXamlRoot();
        DetachCoverItemsPanel();
        CancelPendingGameCardArtworkLoads();
    }

    private void DetachCoverItemsPanel()
    {
        if (_coverItemsPanel is not null)
            _coverItemsPanel.SizeChanged -= GameGridItemsPanel_SizeChanged;
        _coverItemsPanel = null;
    }

    private void AttachXamlRoot()
    {
        var xamlRoot = GameGrid.XamlRoot;
        if (ReferenceEquals(_observedXamlRoot, xamlRoot))
            return;

        DetachXamlRoot();
        _observedXamlRoot = xamlRoot;
        if (_observedXamlRoot is not null)
            _observedXamlRoot.Changed += XamlRoot_Changed;
    }

    private void DetachXamlRoot()
    {
        if (_observedXamlRoot is not null)
            _observedXamlRoot.Changed -= XamlRoot_Changed;
        _observedXamlRoot = null;
        _lastRasterizationScale = double.NaN;
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        var scale = NormalizeRasterizationScale(sender.RasterizationScale);
        if (Math.Abs(_lastRasterizationScale - scale) >= 0.0001)
            ApplyCoverGridLayout(scale);
    }

    private void GameGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!SteamCoverGridLayout.HasMeaningfulWidthChange(e.PreviousSize.Width, e.NewSize.Width))
            return;

        ScheduleCoverGridLayoutUpdate(GameGrid.XamlRoot?.RasterizationScale ?? 1.0);
    }

    private void GameGridItemsPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!SteamCoverGridLayout.HasMeaningfulWidthChange(e.PreviousSize.Width, e.NewSize.Width))
            return;

        ScheduleCoverGridLayoutUpdate(GameGrid.XamlRoot?.RasterizationScale ?? 1.0);
    }

    private void ScheduleCoverGridLayoutUpdate(double rasterizationScale)
    {
        if (_coverItemsPanel is null)
            return;

        _pendingLayoutRasterizationScale = NormalizeRasterizationScale(rasterizationScale);
        _coverGridResizeTimer.Stop();
        _coverGridResizeTimer.Start();
    }

    private void CoverGridResizeTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        ApplyCoverGridLayout(_pendingLayoutRasterizationScale);
    }

    private void ApplyCoverGridLayout(double rasterizationScale)
    {
        rasterizationScale = NormalizeRasterizationScale(rasterizationScale);
        if (_coverItemsPanel is not { } itemsPanel)
            return;

        if (!SteamCoverGridLayout.TryCalculate(itemsPanel.ActualWidth, rasterizationScale, out var nextLayout))
            return;

        var snapshotChanged = _coverGridLayout is not { } current || !LayoutsMatch(current, nextLayout);
        _lastRasterizationScale = rasterizationScale;
        if (!snapshotChanged)
        {
            GameGrid.Opacity = 1;
            return;
        }

        // The panel slot and card dimensions come from the same measured snapshot.
        _coverGridLayout = nextLayout;
        itemsPanel.ItemWidth = nextLayout.SlotWidthDip;
        itemsPanel.ItemHeight = nextLayout.SlotHeightDip;
        foreach (var card in _realizedGameCards.Values)
            SetCardLayout(card.Root, nextLayout);
        GameGrid.Opacity = 1;
    }

    private static bool LayoutsMatch(SteamCoverGridLayout left, SteamCoverGridLayout right) =>
        left.Columns == right.Columns
        && Math.Abs(left.CoverWidthDip - right.CoverWidthDip) < 0.01
        && Math.Abs(left.CoverHeightDip - right.CoverHeightDip) < 0.01
        && Math.Abs(left.ColumnGapDip - right.ColumnGapDip) < 0.01
        && Math.Abs(left.RowGapDip - right.RowGapDip) < 0.01;

    private static double NormalizeRasterizationScale(double rasterizationScale) =>
        double.IsFinite(rasterizationScale) && rasterizationScale > 0 ? rasterizationScale : 1.0;

    private void GameGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        var container = args.ItemContainer;
        if (args.InRecycleQueue)
        {
            if (_realizedGameCards.Remove(container, out var recycledCard))
            {
                recycledCard.CancelLoad();
                recycledCard.ClearArtwork();
                recycledCard.FallbackCard.Visibility = Visibility.Visible;
            }
            return;
        }

        if (args.Item is not GameCardItem item)
            return;

        if (container.ContentTemplateRoot is not Border root
            || root.DataContext is not GameCardItem dataContextItem
            || !ReferenceEquals(item, dataContextItem)
            || root.FindName("CoverImageFrame") is not Border coverImageFrame
            || root.FindName("FallbackCard") is not FrameworkElement fallbackCard)
        {
            if (args.Phase == 0)
            {
                args.RegisterUpdateCallback(GameGrid_ContainerContentChanging);
                args.Handled = true;
            }
            return;
        }

        // Give nested action buttons the live item explicitly, including when their inherited context is stale.
        if (root.FindName("FavoriteButton") is Button favoriteButton
            && !ReferenceEquals(favoriteButton.DataContext, item))
            favoriteButton.DataContext = item;
        if (root.FindName("OpenGameButton") is Button openButton
            && !ReferenceEquals(openButton.DataContext, item))
            openButton.DataContext = item;

        if (_coverGridLayout is { } layout)
            SetCardLayout(root, layout);

        if (_realizedGameCards.TryGetValue(container, out var existing))
        {
            if (existing.Matches(item))
            {
                existing.Item = item;
                if (existing.HasArtwork || existing.LoadCancellation is not null || existing.LoadAttempted)
                    return;
            }
            else
            {
                existing.CancelLoad();
                existing.Reset(item);
            }
        }
        else
        {
            existing = new RealizedGameCard(container, root, coverImageFrame, fallbackCard, item, coverImageFrame.Background);
            existing.ClearArtwork();
            existing.FallbackCard.Visibility = Visibility.Visible;
            _realizedGameCards.Add(container, existing);
        }

        StartGameCardArtworkLoad(existing);
    }

    private void StartGameCardArtworkLoad(RealizedGameCard card)
    {
        if (card.LoadAttempted || card.LoadCancellation is not null || _portraitArtworkContext is null)
            return;

        card.LoadAttempted = true;
        card.LoadCancellation = new CancellationTokenSource();
        var generation = ++card.Generation;
        _ = LoadGameCardArtworkAsync(card, generation, card.Item.Game, _portraitArtworkContext, card.LoadCancellation.Token);
    }

    private async Task LoadGameCardArtworkAsync(
        RealizedGameCard card,
        long generation,
        FrontendProfileGameCatalogEntry game,
        SteamPortraitArtworkContext artworkContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var paths = await Task.Run(
                () => SteamPortraitArtworkResolver.ResolveCandidates(artworkContext, game),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var bitmap = await TryLoadProfileArtworkAsync(paths, cancellationToken);
            if (bitmap is null || !IsCurrentGameCard(card, generation))
                return;

            card.CoverImageFrame.Background = new ImageBrush
            {
                ImageSource = bitmap,
                Stretch = Stretch.UniformToFill
            };
            card.FallbackCard.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Artwork is optional; keep the named fallback card available.
        }
        finally
        {
            if (IsCurrentGameCard(card, generation))
            {
                card.LoadCancellation?.Dispose();
                card.LoadCancellation = null;
                if (cancellationToken.IsCancellationRequested)
                    card.LoadAttempted = false;
            }
        }
    }

    private static Task<BitmapImage?> TryLoadProfileArtworkAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken) =>
        TryLoadFirstAvailableAsync(paths, async path =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage { DecodePixelWidth = 352 };
            await bitmap.SetSourceAsync(stream);
            cancellationToken.ThrowIfCancellationRequested();
            return bitmap;
        });

    internal static async Task<T?> TryLoadFirstAvailableAsync<T>(
        IEnumerable<string> paths,
        Func<string, Task<T?>> tryLoad)
        where T : class
    {
        foreach (var path in paths)
        {
            try
            {
                var result = await tryLoad(path);
                if (result is not null)
                    return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // An unsupported or corrupt optional image falls through to the next candidate.
            }
        }

        return null;
    }

    private bool IsCurrentGameCard(RealizedGameCard card, long generation) =>
        _realizedGameCards.TryGetValue(card.Container, out var current)
        && ReferenceEquals(current, card)
        && card.Generation == generation
        && card.Root.DataContext is GameCardItem item
        && card.Matches(item);

    private void RestartMissingGameCardArtworkLoads()
    {
        foreach (var card in _realizedGameCards.Values.ToArray())
            StartGameCardArtworkLoad(card);
    }

    private void CancelPendingGameCardArtworkLoads()
    {
        foreach (var card in _realizedGameCards.Values)
        {
            if (card.LoadCancellation is null)
                continue;
            card.CancelLoad();
            card.LoadAttempted = false;
        }
    }

    private static void SetCardLayout(FrameworkElement card, SteamCoverGridLayout layout)
    {
        if (Math.Abs(card.Width - layout.CoverWidthDip) >= 0.0001)
            card.Width = layout.CoverWidthDip;
        if (Math.Abs(card.Height - layout.CoverHeightDip) >= 0.0001)
            card.Height = layout.CoverHeightDip;

        var margin = new Thickness(0, 0, layout.ColumnGapDip, layout.RowGapDip);
        if (!card.Margin.Equals(margin))
            card.Margin = margin;
    }

    internal static IReadOnlyList<FrontendProfileGameCatalogEntry> FilterAndSort(
        IEnumerable<FrontendProfileGameCatalogEntry> games,
        string? query)
    {
        var search = query?.Trim() ?? string.Empty;
        return games
            .Where(game => search.Length == 0
                || game.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || game.AppId.ToString().Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(game => game.FavoriteStateKnown && game.Favorite)
            .ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(game => game.AppId)
            .ToArray();
    }

    internal static bool ShouldShowInSteamCatalog(FrontendProfileGameCatalogEntry game) =>
        game.Source != FrontendProfileGameSource.Steam || !HiddenSteamAppIds.Contains(game.AppId);

    internal static IReadOnlyList<FrontendProfileGameCatalogEntry> FilterSteamCatalog(
        IEnumerable<FrontendProfileGameCatalogEntry> games) =>
        games.Where(ShouldShowInSteamCatalog).ToArray();

    internal static string GetEmptyCatalogMessageKey(int scannedGameCount) =>
        scannedGameCount == 0 ? "Steam_Catalog_NoGames" : "Steam_Catalog_NoVisibleGames";

    internal static IReadOnlyList<FrontendProfileGameCatalogEntry> ApplyFavoriteToVisibleCards(
        IReadOnlyList<FrontendProfileGameCatalogEntry> catalog,
        ObservableCollection<GameCardItem> visibleGameCards,
        string? query,
        FrontendProfileGameCatalogEntry oldGame,
        FrontendProfileGameCatalogEntry updatedGame)
    {
        var updatedCatalog = catalog.Select(game =>
            game.AppId == oldGame.AppId && game.Source == oldGame.Source ? updatedGame : game).ToArray();
        var card = visibleGameCards.FirstOrDefault(item =>
            item.AppId == updatedGame.AppId && item.Source == updatedGame.Source);
        if (card is null)
            return updatedCatalog;

        card.UpdateFavorite(updatedGame);

        var desired = FilterAndSort(updatedCatalog, query);
        var target = -1;
        for (var index = 0; index < desired.Count; index++)
        {
            if (desired[index].AppId == updatedGame.AppId && desired[index].Source == updatedGame.Source)
            {
                target = index;
                break;
            }
        }
        var current = visibleGameCards.IndexOf(card);
        if (current >= 0 && target >= 0 && current != target)
            visibleGameCards.Move(current, target);

        return updatedCatalog;
    }

    internal static FrontendProfileGameCatalogEntry? ResolveGameCardItem(object? item) =>
        item is GameCardItem card ? card.Game : null;

    private FrontendProfileGameCatalogEntry? ResolveClickedGame(Button button)
    {
        GridViewItem? container = null;
        for (DependencyObject? node = button; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (container is null && node is GridViewItem itemContainer)
                container = itemContainer;

            if (ReferenceEquals(node, GameGrid))
                return container is null
                    ? null
                    : ResolveGameCardItem(GameGrid.ItemFromContainer(container));
        }

        return null;
    }

    private async void GameCardButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var game = ResolveClickedGame(button);
        if (game is null)
        {
            AppLog.Warn("Profiles.Catalog", "Steam cover click could not resolve its GridView item.");
            return;
        }
        CancelTdpDebounce(); CancelGpuMinimumClockDebounce(); SelectedGameNameText.Text = game.Name; CatalogPanel.Visibility = Visibility.Collapsed; DetailPanel.Visibility = Visibility.Visible; RefreshGamesButton.Visibility = Visibility.Collapsed;
        await SelectGameAsync(game);
    }
    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        CancelTdpDebounce(); CancelFpsDebounce(); CancelGpuMinimumClockDebounce();
        DetailPanel.Visibility = Visibility.Collapsed;
        CatalogPanel.Visibility = Visibility.Visible;
        RefreshGamesButton.Visibility = Visibility.Visible;
    }
    private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_frontend is null || sender is not Button button) return;
        if (ResolveClickedGame(button) is not { FavoriteStateKnown: true } game) return;
        try
        {
            var result = await _frontend.SetGameProfileFavoriteAsync(game.AppId, !game.Favorite, game.Name);
            if (!result.Succeeded) { ShowError(result.FailureMessage ?? MainUiStrings.Get("Steam_Favorite_SaveFailed", "Favorite could not be saved."), null); return; }
            var currentGame = _catalog.FirstOrDefault(x => x.AppId == game.AppId && x.Source == game.Source);
            if (currentGame is null || !currentGame.FavoriteStateKnown || currentGame.Favorite != game.Favorite) return;
            var updatedGame = currentGame with { Favorite = !currentGame.Favorite };
            _catalog = ApplyFavoriteToVisibleCards(_catalog, _visibleGameCards, GameSearchBox.Text.Trim(), currentGame, updatedGame);
        }
        catch (Exception exception) { ShowError(MainUiStrings.Get("Steam_Favorite_SaveFailed", "Favorite could not be saved."), exception); }
    }

    private async Task SelectGameAsync(FrontendProfileGameCatalogEntry game) { CancelTdpDebounce(); CancelGpuMinimumClockDebounce(); _gpuDraftDirty = false; _tdpDraftDirty = false; _selectedGame = game; BeginProfileLoad(game); await CaptureSelectedAsync(game.AppId, preserveDirtyTdpDraft: false); }
    private void BeginProfileLoad(FrontendProfileGameCatalogEntry game) { _snapshot = null; _suppressEvents = _suppressTdpEvents = _suppressFpsEvents = true; CancelFpsDebounce(); try { ProfileEnabledToggle.IsOn = false; ProfileEnabledToggle.IsEnabled = false; SteamVirtualControllerComboBox.SelectedItem = null; SteamVirtualControllerComboBox.IsEnabled = false; FpsEnabledToggle.IsOn = false; FpsEnabledToggle.IsEnabled = false; CpuBoostAcComboBox.SelectedItem = null; CpuBoostDcComboBox.SelectedItem = null; PowerModeAcComboBox.SelectedItem = null; PowerModeDcComboBox.SelectedItem = null; ResolutionComboBox.SelectedItem = null; ResolutionComboBox.IsEnabled = false; _acPl1 = _acPl2 = _dcPl1 = _dcPl2 = null; SetTdpText(); } finally { _suppressEvents = _suppressTdpEvents = _suppressFpsEvents = false; } SetEditorsEnabled(false); }
    private async Task CaptureSelectedAsync(uint appId, bool preserveDirtyTdpDraft = true) { if (_frontend is null) return; try { var snapshot = await _frontend.CaptureGameProfileAsync(appId); if (!IsCurrentProfileResponse(_selectedGame?.AppId, appId)) return; Render(snapshot, preserveDirtyTdpDraft && _tdpDraftDirty, _gpuDraftDirty, preservePendingFpsDraft: true); } catch (Exception exception) { if (IsCurrentProfileResponse(_selectedGame?.AppId, appId)) { DisableEditorsAfterCaptureFailure(); ShowError(MainUiStrings.Get("Steam_Profile_LoadFailed", "Profile settings could not be loaded."), exception); } } }
    private void ClearSelection() { CancelTdpDebounce(); CancelFpsDebounce(); CancelGpuMinimumClockDebounce(); _gpuDraftDirty = false; _tdpDraftDirty = false; _selectedGame = null; _snapshot = null; _suppressEvents = true; try { SteamVirtualControllerComboBox.SelectedItem = null; SteamVirtualControllerComboBox.IsEnabled = false; } finally { _suppressEvents = false; } ProfileEnabledToggle.IsOn = false; ProfileEnabledToggle.IsEnabled = false; ResolutionComboBox.SelectedItem = null; ResolutionComboBox.IsEnabled = false; SetEditorsEnabled(false); }

    private void Render(FrontendGameProfileSnapshot snapshot, bool preserveDirtyTdpDraft = false, bool preserveDirtyGpuDraft = false, bool preservePendingFpsDraft = false)
    {
        var preserveAcFpsDraft = preservePendingFpsDraft && _acFpsDebounce is { IsCancellationRequested: false } && _acFpsDraft.HasValue;
        var preserveDcFpsDraft = preservePendingFpsDraft && _dcFpsDebounce is { IsCancellationRequested: false } && _dcFpsDraft.HasValue;
        var acFpsValue = preserveAcFpsDraft ? _acFpsDraft!.Value : snapshot.FpsLimit?.AcFps ?? 60;
        var dcFpsValue = preserveDcFpsDraft ? _dcFpsDraft!.Value : snapshot.FpsLimit?.DcFps ?? 60;
        _snapshot = snapshot; _suppressEvents = _suppressTdpEvents = _suppressFpsEvents = _suppressFeatureEvents = true;
        try
        {
            ProfileEnabledToggle.IsOn = snapshot.Enabled; ProfileEnabledToggle.IsEnabled = snapshot.PersistenceWritable; CpuBoostEnabledToggle.IsOn = snapshot.CpuBoost.Enabled; TdpEnabledToggle.IsOn = snapshot.Tdp.Enabled; PowerModeEnabledToggle.IsOn = snapshot.PowerMode?.Enabled == true; FpsEnabledToggle.IsOn = snapshot.FpsLimit?.Enabled == true; CpuBoostEnabledToggle.IsEnabled = snapshot.Exists && snapshot.Enabled && snapshot.PersistenceWritable; TdpEnabledToggle.IsEnabled = snapshot.Exists && snapshot.Enabled && snapshot.PersistenceWritable && _featureSupport.Tdp && snapshot.Limits is not null; PowerModeEnabledToggle.IsEnabled = snapshot.Exists && snapshot.Enabled && snapshot.PersistenceWritable && snapshot.PowerMode is not null; FpsEnabledToggle.IsEnabled = snapshot.Exists && snapshot.Enabled && snapshot.PersistenceWritable && snapshot.FpsLimit?.Available == true; _acFpsDraft = acFpsValue; _dcFpsDraft = dcFpsValue; AcFpsSlider.Value = _acFpsDraft.Value; DcFpsSlider.Value = _dcFpsDraft.Value; AcFpsText.Text = $"{_acFpsDraft} FPS"; DcFpsText.Text = $"{_dcFpsDraft} FPS"; IntelFpsExpander.Description = snapshot.FpsLimit?.Available == true ? MainUiStrings.Get("Steam_IntelFpsExpander/Description", "Uses Intel's official API. Some games may not support FPS limiting.") : snapshot.FpsLimit?.UnavailableReason ?? MainUiStrings.Get("Steam_IntelFps_Unavailable", "Intel FPS Limit is unavailable.");
            CpuBoostAcComboBox.SelectedItem = Modes.FirstOrDefault(x => x.Mode == snapshot.CpuBoost.Ac); CpuBoostDcComboBox.SelectedItem = Modes.FirstOrDefault(x => x.Mode == snapshot.CpuBoost.Dc); PowerModeAcComboBox.SelectedItem = snapshot.PowerMode is { } power ? PowerModes.FirstOrDefault(x => x.Mode == power.Ac) : null; PowerModeDcComboBox.SelectedItem = snapshot.PowerMode is { } powerDc ? PowerModes.FirstOrDefault(x => x.Mode == powerDc.Dc) : null;
            RenderSteamVirtualController(snapshot.SteamVirtualController);
            ResolutionComboBox.SelectedItem = ResolutionItems.FirstOrDefault(x => x.Width == snapshot.Resolution?.Width && x.Height == snapshot.Resolution?.Height);
            RenderGpuMinimumClock(snapshot, preserveDirtyGpuDraft);
            if (!preserveDirtyTdpDraft || !_tdpDraftDirty)
            {
                _acPl1 = snapshot.Tdp.Ac.Pl1Watts; _acPl2 = snapshot.Tdp.Ac.Pl2Watts; _dcPl1 = snapshot.Tdp.Dc.Pl1Watts; _dcPl2 = snapshot.Tdp.Dc.Pl2Watts;
                ConfigureSlider(AcPl1Slider, snapshot.Limits?.Pl1MinimumWatts, snapshot.Limits?.Pl2MaximumWatts, _acPl1.Value); ConfigureSlider(AcPl2Slider, snapshot.Limits?.Pl2MinimumWatts, snapshot.Limits?.Pl2MaximumWatts, _acPl2.Value); ConfigureSlider(DcPl1Slider, snapshot.Limits?.Pl1MinimumWatts, snapshot.Limits?.Pl2MaximumWatts, _dcPl1.Value); ConfigureSlider(DcPl2Slider, snapshot.Limits?.Pl2MinimumWatts, snapshot.Limits?.Pl2MaximumWatts, _dcPl2.Value);
                SetTdpText();
            }
        }
        finally { _suppressEvents = _suppressTdpEvents = _suppressFpsEvents = _suppressFeatureEvents = false; }
        CpuBoostAcCard.Visibility = CpuBoostDcCard.Visibility = snapshot.CpuBoost.Enabled ? Visibility.Visible : Visibility.Collapsed; TdpAcCard.Visibility = TdpDcCard.Visibility = snapshot.Tdp.Enabled ? Visibility.Visible : Visibility.Collapsed; PowerModeAcCard.Visibility = PowerModeDcCard.Visibility = snapshot.PowerMode?.Enabled == true ? Visibility.Visible : Visibility.Collapsed; GpuMinimumClockAcCard.Visibility = GpuMinimumClockDcCard.Visibility = snapshot.GpuMinimumClock?.Enabled == true ? Visibility.Visible : Visibility.Collapsed; FpsAcCard.Visibility = FpsDcCard.Visibility = snapshot.FpsLimit?.Enabled == true ? Visibility.Visible : Visibility.Collapsed;
        SetEditorsEnabled(snapshot.Exists && snapshot.Enabled && snapshot.PersistenceWritable); ResolutionComboBox.IsEnabled = snapshot.PersistenceWritable; RenderProfileStatus(snapshot);
    }
    private void RenderProfileStatus(FrontendGameProfileSnapshot snapshot) { if (!snapshot.PersistenceWritable) { ShowError(MainUiStrings.Get("Steam_Profile_SettingsUnwritable", "Profile settings could not be loaded, so changes are disabled to avoid overwriting the existing profile."), null); return; } if (_featureSupport.Tdp && snapshot.Exists && snapshot.Enabled && snapshot.Limits is null) { ProfileInfoBar.Severity = InfoBarSeverity.Warning; ProfileInfoBar.Message = MainUiStrings.Get("Steam_Tdp_Unavailable", "TDP Control is unavailable on this device."); ProfileInfoBar.IsOpen = true; return; } ClearError(); }
    private void SetEditorsEnabled(bool enabled) { var cpu = enabled && _snapshot?.CpuBoost.Enabled == true; CpuBoostAcComboBox.IsEnabled = CpuBoostDcComboBox.IsEnabled = cpu; var powerModeEditable = enabled && _snapshot?.PowerMode is { Enabled: true }; PowerModeAcComboBox.IsEnabled = PowerModeDcComboBox.IsEnabled = powerModeEditable; var tdp = enabled && _snapshot?.Tdp.Enabled == true && _snapshot.Limits is not null; foreach (var slider in new[] { AcPl1Slider, AcPl2Slider, DcPl1Slider, DcPl2Slider }) slider.IsEnabled = tdp; var fps = enabled && _snapshot?.FpsLimit?.Available == true && FpsEnabledToggle.IsOn; AcFpsSlider.IsEnabled = DcFpsSlider.IsEnabled = fps; var gpu = enabled && _snapshot?.GpuMinimumClock is { Available: true, Enabled: true }; GpuMinimumClockAcSlider.IsEnabled = GpuMinimumClockDcSlider.IsEnabled = gpu; GpuMinimumClockEnabledToggle.IsEnabled = enabled && _snapshot?.GpuMinimumClock?.Available == true && !_gpuDraftDirty; ProfileEnabledToggle.IsEnabled = _snapshot?.PersistenceWritable == true && !_gpuDraftDirty; SteamVirtualControllerComboBox.IsEnabled = CanEditGameSteamVirtualController(_steamActive, _snapshot, Volatile.Read(ref _steamVirtualControllerMutationInProgress) != 0); }

    internal void SetSteamActivity(bool? active)
    {
        _steamActive = active;
        SetEditorsEnabled(_snapshot is { Exists: true, Enabled: true, PersistenceWritable: true });
    }

    internal static bool CanEditGameSteamVirtualController(
        bool? steamActive,
        FrontendGameProfileSnapshot? snapshot,
        bool mutationInProgress) =>
        steamActive == false
        && snapshot is { Exists: true, Enabled: true, PersistenceWritable: true }
        && !mutationInProgress;

    private void RenderSteamVirtualController(SteamVirtualControllerPreference? preference)
    {
        var tag = preference?.ToString() ?? "Default";
        SteamVirtualControllerComboBox.SelectedItem = SteamVirtualControllerComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal));
    }

    private async void SteamVirtualControllerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_suppressEvents || _frontend is null || _selectedGame is null || _snapshot is null
            || SteamVirtualControllerComboBox.SelectedItem is not ComboBoxItem { Tag: string tag })
            return;

        SteamVirtualControllerPreference? requested = tag switch
        {
            "Default" => null,
            "SteamDeck" => SteamVirtualControllerPreference.SteamDeck,
            "DualSenseEdge" => SteamVirtualControllerPreference.DualSenseEdge,
            _ => (SteamVirtualControllerPreference?)null
        };
        if (tag is not ("Default" or "SteamDeck" or "DualSenseEdge")) return;

        var appId = _selectedGame.AppId;
        if (!CanEditGameSteamVirtualController(_steamActive, _snapshot, Volatile.Read(ref _steamVirtualControllerMutationInProgress) != 0))
        {
            RenderSteamVirtualController(_snapshot.SteamVirtualController);
            SteamActivityRefreshRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (requested == _snapshot.SteamVirtualController) return;
        if (Interlocked.Exchange(ref _steamVirtualControllerMutationInProgress, 1) != 0)
        {
            RenderSteamVirtualController(_snapshot.SteamVirtualController);
            SetEditorsEnabled(_snapshot.Exists && _snapshot.Enabled && _snapshot.PersistenceWritable);
            return;
        }

        var saved = _snapshot.SteamVirtualController;
        RenderSteamVirtualController(saved);
        SetEditorsEnabled(_snapshot.Exists && _snapshot.Enabled && _snapshot.PersistenceWritable);
        try
        {
            var result = await _frontend.SetGameProfileSteamVirtualControllerAsync(appId, requested).ConfigureAwait(true);
            if (!IsCurrentProfileResponse(_selectedGame?.AppId, appId)
                || !IsCurrentProfileResponse(_selectedGame?.AppId, result.Snapshot.AppId))
                return;

            RenderControllerMutationSnapshot(result.Snapshot);
            if (!result.Succeeded)
            {
                ShowError(result.FailureMessage ?? MainUiStrings.Get("Steam_VirtualController_UpdateFailed", "Virtual Controller could not be updated."), null);
                SetSteamActivity(null);
                SteamActivityRefreshRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception exception)
        {
            await RestoreSelectedAfterMutationFailureAsync(
                appId,
                MainUiStrings.Get("Steam_VirtualController_UpdateFailed", "Virtual Controller could not be updated."),
                exception,
                preservePendingProfileDrafts: true);
            SetSteamActivity(null);
            SteamActivityRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            Volatile.Write(ref _steamVirtualControllerMutationInProgress, 0);
            SetEditorsEnabled(_snapshot is { Exists: true, Enabled: true, PersistenceWritable: true });
        }
    }

    private void RenderControllerMutationSnapshot(FrontendGameProfileSnapshot snapshot)
    {
        var appId = snapshot.AppId;
        var acFpsDraft = _acFpsDraft;
        var dcFpsDraft = _dcFpsDraft;
        var acFpsPending = _acFpsDebounce is { IsCancellationRequested: false } && acFpsDraft.HasValue;
        var dcFpsPending = _dcFpsDebounce is { IsCancellationRequested: false } && dcFpsDraft.HasValue;
        Render(snapshot, preserveDirtyTdpDraft: _tdpDraftDirty, preserveDirtyGpuDraft: _gpuDraftDirty);
        _suppressFpsEvents = true;
        try
        {
            if (acFpsPending && acFpsDraft is { } acFps && _selectedGame?.AppId == appId)
            {
                _acFpsDraft = acFps;
                AcFpsSlider.Value = acFps;
                AcFpsText.Text = $"{acFps} FPS";
            }
            if (dcFpsPending && dcFpsDraft is { } dcFps && _selectedGame?.AppId == appId)
            {
                _dcFpsDraft = dcFps;
                DcFpsSlider.Value = dcFps;
                DcFpsText.Text = $"{dcFps} FPS";
            }
        }
        finally { _suppressFpsEvents = false; }
    }
    private void RenderGpuMinimumClock(FrontendGameProfileSnapshot snapshot, bool preserveDraft)
    {
        var gpu = snapshot.GpuMinimumClock;
        var preserveUnavailableDraft = preserveDraft && _gpuDraftDirty && gpu?.Available != true;
        _suppressGpuEvents = true;
        try
        {
            GpuMinimumClockEnabledToggle.IsOn = gpu?.Enabled == true;
            var clocks = gpu?.SelectableClocksMhz ?? Array.Empty<double>();
            if (!preserveUnavailableDraft && (!preserveDraft || !_gpuDraftDirty || _acGpuDraftIndex is null || _dcGpuDraftIndex is null))
            {
                _acGpuDraftIndex = GpuMinimumClockUiPolicy.TryGetIndex(clocks, gpu?.AcMhz, out var acSaved)
                    ? acSaved
                    : GpuMinimumClockUiPolicy.TryGetIndex(clocks, gpu?.RecommendedDefaultMhz, out var acDefault) ? acDefault : null;
                _dcGpuDraftIndex = GpuMinimumClockUiPolicy.TryGetIndex(clocks, gpu?.DcMhz, out var dcSaved)
                    ? dcSaved
                    : GpuMinimumClockUiPolicy.TryGetIndex(clocks, gpu?.RecommendedDefaultMhz, out var dcDefault) ? dcDefault : null;
            }

            if (!preserveUnavailableDraft)
            {
                ConfigureGpuClockSlider(GpuMinimumClockAcSlider, clocks, _acGpuDraftIndex);
                ConfigureGpuClockSlider(GpuMinimumClockDcSlider, clocks, _dcGpuDraftIndex);
                GpuMinimumClockAcValueText.Text = FormatGpuClockAtIndex(clocks, _acGpuDraftIndex);
                GpuMinimumClockDcValueText.Text = FormatGpuClockAtIndex(clocks, _dcGpuDraftIndex);
            }
            GpuMinimumClockInfoBar.Message = gpu?.UnavailableReason ?? string.Empty;
            GpuMinimumClockInfoBar.IsOpen = !string.IsNullOrWhiteSpace(gpu?.UnavailableReason);
            GpuMinimumClockExpander.Description = gpu?.UnavailableReason is { Length: > 0 } warning
                ? warning
                : MainUiStrings.Get("Steam_GpuMinimumClockExpander/Description", "Overrides the Device minimum GPU clock for this game. Actual clock may still fall lower when required by power or thermal limits.");
        }
        finally { _suppressGpuEvents = false; }
    }

    private void DisableEditorsAfterCaptureFailure()
    {
        ProfileEnabledToggle.IsEnabled = false;
        CpuBoostEnabledToggle.IsEnabled = false;
        TdpEnabledToggle.IsEnabled = false;
        PowerModeEnabledToggle.IsEnabled = false;
        FpsEnabledToggle.IsEnabled = false;
        ResolutionComboBox.IsEnabled = false;
        SteamVirtualControllerComboBox.IsEnabled = false;
        CpuBoostAcComboBox.IsEnabled = false;
        CpuBoostDcComboBox.IsEnabled = false;
        PowerModeAcComboBox.IsEnabled = false;
        PowerModeDcComboBox.IsEnabled = false;
        foreach (var slider in new[] { AcPl1Slider, AcPl2Slider, DcPl1Slider, DcPl2Slider, AcFpsSlider, DcFpsSlider, GpuMinimumClockAcSlider, GpuMinimumClockDcSlider })
            slider.IsEnabled = false;
        GpuMinimumClockEnabledToggle.IsEnabled = false;
    }

    private static void ConfigureGpuClockSlider(Slider slider, IReadOnlyList<double> clocks, int? index)
    {
        slider.Minimum = 0;
        slider.Maximum = Math.Max(0, clocks.Count - 1);
        slider.StepFrequency = 1;
        slider.Value = index is { } value && value >= 0 && value < clocks.Count ? value : 0;
    }

    private static string FormatGpuClockAtIndex(IReadOnlyList<double> clocks, int? index) =>
        index is { } value && value >= 0 && value < clocks.Count
            ? GpuMinimumClockUiPolicy.FormatMhz(clocks[value])
            : "— MHz";

    private async void GpuMinimumClockEnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressGpuEvents || _suppressFeatureEvents || _frontend is null || _selectedGame is null || _snapshot?.Enabled != true) return;
        var appId = _selectedGame.AppId;
        try
        {
            var result = await _frontend.SetGameProfileGpuMinimumClockEnabledAsync(appId, GpuMinimumClockEnabledToggle.IsOn);
            if (!IsCurrentProfileResponse(_selectedGame?.AppId, result.Snapshot.AppId)) return;
            Render(result.Snapshot);
            if (!result.Succeeded) ShowError(result.FailureMessage ?? MainUiStrings.Get("Steam_GpuMinimumClock_UpdateFailed", "Minimum GPU Clock could not be updated."), null);
        }
        catch (Exception exception) { await RestoreSelectedAfterMutationFailureAsync(appId, MainUiStrings.Get("Steam_GpuMinimumClock_UpdateFailed", "Minimum GPU Clock could not be updated."), exception); }
    }

    private void GpuMinimumClockSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressGpuEvents || _frontend is null || _selectedGame is null || _snapshot?.GpuMinimumClock is not { Available: true, Enabled: true }) return;
        var index = (int)Math.Round(e.NewValue);
        if (ReferenceEquals(sender, GpuMinimumClockAcSlider))
        {
            _acGpuDraftIndex = index;
            GpuMinimumClockAcValueText.Text = FormatGpuClockAtIndex(_snapshot.GpuMinimumClock.SelectableClocksMhz, index);
            _acGpuGeneration++;
            _acGpuDebounce?.Cancel();
            _acGpuDebounce = new CancellationTokenSource();
            _gpuDraftDirty = true;
            SetEditorsEnabled(_snapshot.Enabled && _snapshot.PersistenceWritable);
            _ = SubmitGpuMinimumClockAfterDelayAsync(ac: true, index, _acGpuGeneration, ++_gpuDraftGeneration, _acGpuDebounce.Token);
        }
        else
        {
            _dcGpuDraftIndex = index;
            GpuMinimumClockDcValueText.Text = FormatGpuClockAtIndex(_snapshot.GpuMinimumClock.SelectableClocksMhz, index);
            _dcGpuGeneration++;
            _dcGpuDebounce?.Cancel();
            _dcGpuDebounce = new CancellationTokenSource();
            _gpuDraftDirty = true;
            SetEditorsEnabled(_snapshot.Enabled && _snapshot.PersistenceWritable);
            _ = SubmitGpuMinimumClockAfterDelayAsync(ac: false, index, _dcGpuGeneration, ++_gpuDraftGeneration, _dcGpuDebounce.Token);
        }
    }

    private async Task SubmitGpuMinimumClockAfterDelayAsync(bool ac, int index, long generation, long draftGeneration, CancellationToken token)
    {
        if (_selectedGame is null) return;
        var appId = _selectedGame.AppId;
        try
        {
            await Task.Delay(300, token);
            if ((ac ? generation != _acGpuGeneration : generation != _dcGpuGeneration)
                || _frontend is null || _selectedGame?.AppId != appId || _snapshot is not { Enabled: true, GpuMinimumClock.Enabled: true }) return;

            var result = ac
                ? await _frontend.SetGameProfileGpuMinimumClockAcAsync(appId, index)
                : await _frontend.SetGameProfileGpuMinimumClockDcAsync(appId, index);
            if (!IsCurrentProfileResponse(_selectedGame?.AppId, result.Snapshot.AppId)) return;
            var preserveDraft = _gpuDraftDirty && draftGeneration != _gpuDraftGeneration;
            if (!preserveDraft) _gpuDraftDirty = false;
            Render(result.Snapshot, preserveDirtyGpuDraft: preserveDraft);
            if (!result.Succeeded) ShowError(result.FailureMessage ?? MainUiStrings.Get("Steam_GpuMinimumClock_UpdateFailed", "Minimum GPU Clock could not be updated."), null);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (_selectedGame?.AppId == appId)
            {
                var preserveDraft = GpuMinimumClockUiPolicy.ResolveFailedCommit(
                    ref _gpuDraftDirty, draftGeneration, _gpuDraftGeneration);
                await RestoreSelectedAfterMutationFailureAsync(appId, MainUiStrings.Get("Steam_GpuMinimumClock_UpdateFailed", "Minimum GPU Clock could not be updated."), exception,
                    preserveDirtyGpuDraft: preserveDraft);
            }
        }
    }

    private void CancelGpuMinimumClockDebounce()
    {
        _acGpuGeneration++; _dcGpuGeneration++;
        _acGpuDebounce?.Cancel(); _dcGpuDebounce?.Cancel();
        _acGpuDebounce = _dcGpuDebounce = null;
        _gpuDraftDirty = false;
    }
    private async void FpsEnabledToggle_Toggled(object sender, RoutedEventArgs e) { if (_suppressFpsEvents || _frontend is null || _selectedGame is null) return; try { var result = await _frontend.SetGameProfileFpsLimitEnabledAsync(_selectedGame.AppId, FpsEnabledToggle.IsOn); if (IsCurrentProfileResponse(_selectedGame?.AppId, result.Snapshot.AppId)) Render(result.Snapshot); if (!result.Succeeded) ShowError(result.FailureMessage ?? MainUiStrings.Get("Steam_IntelFps_UpdateFailed", "Intel FPS Limit could not be updated."), null); } catch (Exception ex) { await RestoreSelectedAfterMutationFailureAsync(_selectedGame.AppId, MainUiStrings.Get("Steam_IntelFps_UpdateFailed", "Intel FPS Limit could not be updated."), ex); } }
    private async void CpuBoostEnabledToggle_Toggled(object sender, RoutedEventArgs e) => await MutateFeatureEnabledAsync(MainUiStrings.Get("Steam_CpuBoost_UpdateFailed", "CPU Boost could not be updated."), CpuBoostEnabledToggle.IsOn, (id, value) => _frontend!.SetGameProfileCpuBoostEnabledAsync(id, value));
    private async void TdpEnabledToggle_Toggled(object sender, RoutedEventArgs e) => await MutateFeatureEnabledAsync(MainUiStrings.Get("Steam_TdpControl_ToggleUpdateFailed", "TDP Control could not be updated."), TdpEnabledToggle.IsOn, (id, value) => _frontend!.SetGameProfileTdpEnabledAsync(id, value));
    private async void PowerModeEnabledToggle_Toggled(object sender, RoutedEventArgs e) => await MutateFeatureEnabledAsync(MainUiStrings.Get("Steam_PowerMode_ToggleUpdateFailed", "Windows Power Mode could not be updated."), PowerModeEnabledToggle.IsOn, (id, value) => _frontend!.SetGameProfilePowerModeEnabledAsync(id, value));
    private async Task MutateFeatureEnabledAsync(string failureMessage, bool enabled, Func<uint, bool, Task<FrontendGameProfileMutationResult>> mutation)
    {
        if (_suppressFeatureEvents || _frontend is null || _selectedGame is null || _snapshot?.Enabled != true) return;
        var appId = _selectedGame.AppId;
        try { var result = await mutation(appId, enabled); if (IsCurrentProfileResponse(_selectedGame?.AppId, appId)) Render(result.Snapshot); if (!result.Succeeded) ShowError(result.FailureMessage ?? failureMessage, null); }
        catch (Exception exception) { await RestoreSelectedAfterMutationFailureAsync(appId, failureMessage, exception); }
    }
    private void FpsSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e) { if (_suppressFpsEvents || _frontend is null || _selectedGame is null) return; var value = Math.Clamp((int)Math.Round(e.NewValue), 40, 120); var ac = ReferenceEquals(sender, AcFpsSlider); if (ac) { _acFpsDraft = value; AcFpsText.Text = $"{value} FPS"; _acFpsGeneration++; _acFpsDebounce?.Cancel(); _acFpsDebounce = new(); _ = SubmitFpsAfterDelayAsync(true, value, _acFpsGeneration, _acFpsDebounce.Token); } else { _dcFpsDraft = value; DcFpsText.Text = $"{value} FPS"; _dcFpsGeneration++; _dcFpsDebounce?.Cancel(); _dcFpsDebounce = new(); _ = SubmitFpsAfterDelayAsync(false, value, _dcFpsGeneration, _dcFpsDebounce.Token); } }
    private async Task SubmitFpsAfterDelayAsync(bool ac, int value, long generation, CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            if ((ac ? generation != _acFpsGeneration : generation != _dcFpsGeneration)
                || _frontend is null || _selectedGame is null)
                return;

            var appId = _selectedGame.AppId;
            var result = ac
                ? await _frontend.SetGameProfileFpsLimitAcAsync(appId, value)
                : await _frontend.SetGameProfileFpsLimitDcAsync(appId, value);
            if (!IsCurrentProfileResponse(_selectedGame?.AppId, appId)
                || (ac ? generation != _acFpsGeneration : generation != _dcFpsGeneration))
                return;

            if (ac) _acFpsDebounce = null;
            else _dcFpsDebounce = null;
            Render(result.Snapshot, preservePendingFpsDraft: true);
            if (!result.Succeeded)
                ShowError(result.FailureMessage ?? MainUiStrings.Get("Steam_IntelFps_UpdateFailed", "Intel FPS Limit could not be updated."), null);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (_selectedGame is { } selected
                && (ac ? generation == _acFpsGeneration : generation == _dcFpsGeneration))
            {
                if (ac) _acFpsDebounce = null;
                else _dcFpsDebounce = null;

                await RestoreSelectedAfterMutationFailureAsync(selected.AppId,
                    MainUiStrings.Get("Steam_IntelFps_UpdateFailed", "Intel FPS Limit could not be updated."), exception,
                    preservePendingProfileDrafts: true);
            }
        }
    }
    private void CancelFpsDebounce() { _acFpsGeneration++; _dcFpsGeneration++; _acFpsDebounce?.Cancel(); _dcFpsDebounce?.Cancel(); _acFpsDebounce = _dcFpsDebounce = null; }
    private static void ConfigureSlider(Slider slider, int? minimum, int? maximum, int value) { if (minimum is not { } min || maximum is not { } max) { slider.IsEnabled = false; return; } slider.Minimum = min; slider.Maximum = max; slider.StepFrequency = 1; slider.Value = Math.Clamp(value, min, max); }

    private async void ProfileEnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || _frontend is null || _selectedGame is null) return;
        var appId = _selectedGame.AppId;
        CancelTdpDebounce();
        try { var result = await _frontend.SetGameProfileEnabledAsync(appId, ProfileEnabledToggle.IsOn, _selectedGame.Name); if (!IsCurrentProfileResponse(_selectedGame?.AppId, appId)) return; Render(result.Snapshot); if (!result.Succeeded) ShowError(result.FailureMessage ?? MainUiStrings.Get("Steam_Profile_UpdateFailed", "Profile could not be updated."), null); } catch (Exception exception) { await RestoreSelectedAfterMutationFailureAsync(appId, MainUiStrings.Get("Steam_Profile_RuntimeInterrupted", "Profile could not be updated because the Runtime connection was interrupted."), exception); }
    }
    private async void CpuBoostAcComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => await MutateCpuAsync(true);
    private async void CpuBoostDcComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => await MutateCpuAsync(false);
    private async void PowerModeAcComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => await MutatePowerModeAsync(true);
    private async void PowerModeDcComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => await MutatePowerModeAsync(false);
    private async Task MutatePowerModeAsync(bool ac)
    {
        if (_suppressEvents || _frontend is null || _selectedGame is null) return;
        var item = (ac ? PowerModeAcComboBox : PowerModeDcComboBox).SelectedItem as PowerModeItem; if (item is null) return;
        try { var appId = _selectedGame.AppId; var result = ac ? await _frontend.SetGameProfilePowerModeAcAsync(appId, item.Mode) : await _frontend.SetGameProfilePowerModeDcAsync(appId, item.Mode); if (IsCurrentProfileResponse(_selectedGame?.AppId, appId)) Render(result.Snapshot); if (!result.Succeeded) ShowError(result.FailureMessage ?? MainUiStrings.Get("Steam_PowerMode_UpdateFailed", "Power Mode could not be updated."), null); } catch (Exception exception) { await RestoreSelectedAfterMutationFailureAsync(_selectedGame.AppId, MainUiStrings.Get("Steam_PowerMode_UpdateFailed", "Power Mode could not be updated."), exception); }
    }
    private async void ResolutionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || _frontend is null || _selectedGame is null || ResolutionComboBox.SelectedItem is not ResolutionItem item) return;
        var appId = _selectedGame.AppId;
        try { var result = await _frontend.SetGameProfileResolutionAsync(appId, item.Width is { } w && item.Height is { } h ? new(w, h) : null, _selectedGame.Name); if (!IsCurrentProfileResponse(_selectedGame?.AppId, appId)) return; Render(result.Snapshot); if (!result.Succeeded) ShowError(result.FailureMessage ?? MainUiStrings.Get("Steam_Resolution_UpdateFailed", "Display resolution could not be updated."), null); }
        catch (Exception exception) { await RestoreSelectedAfterMutationFailureAsync(appId, MainUiStrings.Get("Steam_Resolution_UpdateFailed", "Display resolution could not be updated."), exception); }
    }
    private async Task MutateCpuAsync(bool acSide)
    {
        if (_suppressEvents || _frontend is null || _selectedGame is null) return;
        var mode = (acSide ? CpuBoostAcComboBox : CpuBoostDcComboBox).SelectedItem as CpuBoostModeItem;
        if (mode is null) return;
        var appId = _selectedGame.AppId;
        try { var result = acSide ? await _frontend.SetGameProfileCpuBoostAcAsync(appId, mode.Mode) : await _frontend.SetGameProfileCpuBoostDcAsync(appId, mode.Mode); if (!IsCurrentProfileResponse(_selectedGame?.AppId, appId)) return; Render(result.Snapshot); if (!result.Succeeded) ShowError(result.FailureMessage ?? MainUiStrings.Get("Steam_CpuBoost_UpdateFailed", "CPU Boost could not be updated."), null); } catch (Exception exception) { await RestoreSelectedAfterMutationFailureAsync(appId, MainUiStrings.Get("Steam_CpuBoost_UpdateFailed", "CPU Boost could not be updated."), exception); }
    }
    private void TdpSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressTdpEvents || _selectedGame is null) return; var value = (int)Math.Round(e.NewValue); var slider = (Slider)sender; var ac = ReferenceEquals(slider, AcPl1Slider) || ReferenceEquals(slider, AcPl2Slider); var pl1Edited = ReferenceEquals(slider, AcPl1Slider) || ReferenceEquals(slider, DcPl1Slider); var pl1 = ac ? _acPl1 : _dcPl1; var pl2 = ac ? _acPl2 : _dcPl2; if (pl1Edited) { if (_snapshot?.Limits is { } currentLimits) value = Math.Min(value, currentLimits.Pl1MaximumWatts); pl1 = value; } else pl2 = value;
        if (_snapshot?.Limits is { } limits) { var adjusted = DevicePage.TdpDraftPolicy.AdjustAfterEdit(pl1Edited, pl1, pl2, limits); pl1 = adjusted.Pl1Watts; pl2 = adjusted.Pl2Watts; _suppressTdpEvents = true; try { (ac ? AcPl1Slider : DcPl1Slider).Value = pl1 ?? 0; (ac ? AcPl2Slider : DcPl2Slider).Value = pl2 ?? 0; } finally { _suppressTdpEvents = false; } }
        if (ac) { _acPl1 = pl1; _acPl2 = pl2; } else { _dcPl1 = pl1; _dcPl2 = pl2; } _tdpDraftDirty = true; SetTdpText(); _tdpGeneration++; var generation = _tdpGeneration; _tdpDebounce?.Cancel(); _tdpDebounce = new CancellationTokenSource(); _ = SubmitTdpAfterDelayAsync(generation, _tdpDebounce.Token);
    }
    private async Task SubmitTdpAfterDelayAsync(long generation, CancellationToken token)
    {
        uint? appId = null;
        try { await Task.Delay(300, token); if (!DevicePage.TdpDraftPolicy.CanSubmitDebouncedEdit(generation, Volatile.Read(ref _tdpGeneration), ProfileEnabledToggle.IsOn) || _frontend is null || _selectedGame is null || _acPl1 is not { } ac1 || _acPl2 is not { } ac2 || _dcPl1 is not { } dc1 || _dcPl2 is not { } dc2) return; appId = _selectedGame.AppId; var result = await _frontend.SetGameProfileTdpAsync(appId.Value, new(true, new(ac1, ac2), new(dc1, dc2))); if (!IsCurrentProfileResponse(_selectedGame?.AppId, appId.Value)) return; var preserveDraft = ShouldPreserveDirtyTdpDraft(_tdpDraftDirty, generation, _tdpGeneration); if (!preserveDraft) _tdpDraftDirty = false; Render(result.Snapshot, preserveDraft); if (!result.Succeeded) ShowError(result.FailureMessage ?? MainUiStrings.Get("Steam_Tdp_UpdateFailed", "TDP could not be updated."), null); } catch (OperationCanceledException) { } catch (Exception exception) { if (appId is { } targetAppId) await RestoreSelectedAfterMutationFailureAsync(targetAppId, MainUiStrings.Get("Steam_Tdp_UpdateFailed", "TDP could not be updated."), exception); }
    }
    private void SetTdpText() { AcPl1Value.Text = _acPl1 is { } x ? $"{x} W" : "— W"; AcPl2Value.Text = _acPl2 is { } y ? $"{y} W" : "— W"; DcPl1Value.Text = _dcPl1 is { } z ? $"{z} W" : "— W"; DcPl2Value.Text = _dcPl2 is { } w ? $"{w} W" : "— W"; }
    private void CancelTdpDebounce() { _tdpGeneration++; _tdpDebounce?.Cancel(); _tdpDebounce = null; }
    private void ShowError(string message, Exception? exception) { ProfileInfoBar.Severity = InfoBarSeverity.Error; ProfileInfoBar.Message = message; ProfileInfoBar.IsOpen = true; if (exception is not null) AppLog.Warn("Profile", message, exception); }
    private void ShowInfo(string message) { ProfileInfoBar.Severity = InfoBarSeverity.Informational; ProfileInfoBar.Message = message; ProfileInfoBar.IsOpen = true; }
    private void ClearError() => ProfileInfoBar.IsOpen = false;
    internal static bool IsCurrentProfileResponse(uint? selectedAppId, uint responseAppId) => selectedAppId == responseAppId;
    internal static bool ShouldPreserveDirtyTdpDraft(bool dirty, long submittedGeneration, long currentGeneration) => dirty && submittedGeneration != currentGeneration;
    private async Task RestoreSelectedAfterMutationFailureAsync(
        uint appId,
        string message,
        Exception exception,
        bool preserveDirtyGpuDraft = false,
        bool preservePendingProfileDrafts = false)
    {
        ShowError(message, exception);
        if (_frontend is null || _selectedGame?.AppId != appId) return;
        try { var snapshot = await _frontend.CaptureGameProfileAsync(appId); if (_selectedGame?.AppId == appId) { if (preservePendingProfileDrafts) RenderControllerMutationSnapshot(snapshot); else Render(snapshot, preserveDirtyGpuDraft: preserveDirtyGpuDraft); ShowError(message, null); } } catch { }
    }
    private sealed record CpuBoostModeItem(CpuBoostMode Mode, string Label);
    private static readonly PowerModeItem[] PowerModes = [new(WindowsPowerMode.BestPowerEfficiency, MainUiStrings.Get("Steam_PowerMode_Mode_BestPowerEfficiency", "Best power efficiency")), new(WindowsPowerMode.Balanced, MainUiStrings.Get("Steam_PowerMode_Mode_Balanced", "Balanced")), new(WindowsPowerMode.BestPerformance, MainUiStrings.Get("Steam_PowerMode_Mode_BestPerformance", "Best performance"))];
    private sealed record PowerModeItem(WindowsPowerMode Mode, string Label);
    private sealed record ResolutionItem(int? Width, int? Height, string Label);
    internal sealed class GameCardItem : INotifyPropertyChanged
    {
        internal GameCardItem(FrontendProfileGameCatalogEntry game) => Game = game;

        public event PropertyChangedEventHandler? PropertyChanged;
        public FrontendProfileGameCatalogEntry Game { get; private set; }
        public uint AppId => Game.AppId;
        public string Name => Game.Name;
        public FrontendProfileGameSource Source => Game.Source;
        public bool Favorite => Game.Favorite;
        public bool FavoriteStateKnown => Game.FavoriteStateKnown;
        public string FavoriteGlyph => GetFavoriteGlyph(Game.Favorite, Game.FavoriteStateKnown);
        public string FavoriteAutomationName
        {
            get
            {
                var (key, fallback) = !Game.FavoriteStateKnown
                    ? ("Steam_Favorite_UnknownAutomationName", "Favorite state unavailable for {0}")
                    : Game.Favorite
                        ? ("Steam_Favorite_RemoveAutomationName", "Remove {0} from favorites")
                        : ("Steam_Favorite_AddAutomationName", "Add {0} to favorites");
                return string.Format(CultureInfo.CurrentUICulture, MainUiStrings.Get(key, fallback), Name);
            }
        }

        internal void UpdateFavorite(FrontendProfileGameCatalogEntry updatedGame)
        {
            Game = updatedGame;
            PropertyChanged?.Invoke(this, new(nameof(Game)));
            PropertyChanged?.Invoke(this, new(nameof(Favorite)));
            PropertyChanged?.Invoke(this, new(nameof(FavoriteGlyph)));
            PropertyChanged?.Invoke(this, new(nameof(FavoriteAutomationName)));
        }
    }

    private sealed class RealizedGameCard(
        SelectorItem container,
        Border root,
        Border coverImageFrame,
        FrameworkElement fallbackCard,
        GameCardItem item,
        Brush? fallbackBrush)
    {
        public SelectorItem Container { get; } = container;
        public Border Root { get; } = root;
        public Border CoverImageFrame { get; } = coverImageFrame;
        public FrameworkElement FallbackCard { get; } = fallbackCard;
        public Brush? FallbackBrush { get; } = fallbackBrush;
        public GameCardItem Item { get; set; } = item;
        public long Generation { get; set; }
        public bool LoadAttempted { get; set; }
        public CancellationTokenSource? LoadCancellation { get; set; }
        public bool HasArtwork => CoverImageFrame.Background is ImageBrush;

        public bool Matches(GameCardItem other) => Item.AppId == other.AppId && Item.Source == other.Source;

        public void Reset(GameCardItem item)
        {
            Item = item;
            Generation++;
            LoadAttempted = false;
            ClearArtwork();
            FallbackCard.Visibility = Visibility.Visible;
        }

        public void ClearArtwork() => CoverImageFrame.Background = FallbackBrush;

        public void CancelLoad()
        {
            Generation++;
            LoadCancellation?.Cancel();
            LoadCancellation?.Dispose();
            LoadCancellation = null;
        }
    }

    internal static string GetFavoriteGlyph(bool favorite, bool stateKnown) =>
        !stateKnown ? "—" : favorite ? "★" : "☆";
}
