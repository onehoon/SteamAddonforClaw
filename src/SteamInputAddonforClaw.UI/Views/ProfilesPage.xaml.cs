using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.FrontendTransport.XboxArtwork;
using SteamInputAddonforClaw.UI;
using SteamInputAddonforClaw.UI.Diagnostics;
using Windows.Storage;

namespace SteamInputAddonforClaw.Views;

public sealed partial class ProfilesPage : UserControl
{
    private readonly ObservableCollection<ProfilesLibraryItem> _visibleItems = [];
    private readonly Dictionary<string, ProfilesLibraryItem> _itemsByIdentity = new(StringComparer.Ordinal);
    private readonly Dictionary<SelectorItem, RealizedCard> _realizedCards = [];
    private readonly DispatcherQueueTimer _coverGridResizeTimer;
    private IAddonFrontendControl? _frontend;
    private FrontendModelFeatureSupport _featureSupport = FrontendModelFeatureSupport.None;
    private IReadOnlyList<FrontendProfileGameCatalogEntry> _steamCatalog = [];
    private IReadOnlyList<FrontendXboxGameCatalogEntry> _xboxCatalog = [];
    private IReadOnlyList<string> _catalogFailures = [];
    private SteamPortraitArtworkContext? _steamArtworkContext;
    private XboxStoreArtworkResolver? _xboxArtworkResolver;
    private ItemsWrapGrid? _coverItemsPanel;
    private XamlRoot? _observedXamlRoot;
    private SteamCoverGridLayout? _coverGridLayout;
    private double _lastRasterizationScale = double.NaN;
    private double _pendingLayoutRasterizationScale = 1.0;
    private ProfilesLibraryFilter _filter = ProfilesLibraryFilter.All;
    private CancellationTokenSource? _refreshCancellation;
    private ProfilePage? _steamEditor;
    private XboxPage? _xboxEditor;
    private bool? _steamActive;
    private bool _active;
    private bool _initialScanStarted;
    private bool _initialScanCompleted;
    private bool _shuttingDown;

    public ProfilesPage()
    {
        InitializeComponent();
        GameGrid.ItemsSource = _visibleItems;
        _coverGridResizeTimer = DispatcherQueue.CreateTimer();
        _coverGridResizeTimer.Interval = TimeSpan.FromMilliseconds(180);
        _coverGridResizeTimer.IsRepeating = false;
        _coverGridResizeTimer.Tick += CoverGridResizeTimer_Tick;
        Loaded += ProfilesPage_Loaded;
        Unloaded += ProfilesPage_Unloaded;
        GameFilterSelector.SelectedItem = GameFilterSelector.Items.OfType<SelectorBarItem>().First();
    }

    internal event EventHandler? SteamActivityRefreshRequested;

    internal void Initialize(IAddonFrontendControl frontend, FrontendModelFeatureSupport featureSupport)
    {
        _frontend = frontend;
        _featureSupport = featureSupport;
    }

    internal void SetSteamActivity(bool? active)
    {
        _steamActive = active;
        _steamEditor?.SetSteamActivity(active);
    }

    internal void Activate()
    {
        if (_active || _shuttingDown) return;
        _active = true;
        if (DetailHost.Content is ProfilePage)
            _steamEditor?.ActivateForUnifiedProfiles();
        else if (DetailHost.Content is XboxPage)
            _xboxEditor?.ActivateForUnifiedProfiles();
        else
            RestartMissingArtworkLoads();

        if (!_initialScanStarted)
        {
            _initialScanStarted = true;
            _ = RefreshCatalogsAsync(explicitRefresh: false);
        }
    }

    internal void Deactivate()
    {
        if (!_active) return;
        _active = false;
        _steamEditor?.Deactivate();
        _xboxEditor?.Deactivate();
        CancelPendingArtworkLoads();
    }

    internal void Shutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        Deactivate();
        _refreshCancellation?.Cancel();
        _xboxArtworkResolver?.Dispose();
        _xboxArtworkResolver = null;
    }

    private void ProfilesPage_Loaded(object sender, RoutedEventArgs e)
    {
        AttachXamlRoot();
        RestartMissingArtworkLoads();
    }

    private void ProfilesPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _coverGridResizeTimer.Stop();
        DetachXamlRoot();
        DetachCoverItemsPanel();
        CancelPendingArtworkLoads();
    }

    private async void RefreshGamesButton_Click(object sender, RoutedEventArgs e) =>
        await RefreshCatalogsAsync(explicitRefresh: true);

    private async Task RefreshCatalogsAsync(bool explicitRefresh)
    {
        if (_frontend is null || _shuttingDown || _refreshCancellation is not null)
            return;

        var refresh = new CancellationTokenSource();
        _refreshCancellation = refresh;
        RefreshGamesButton.IsEnabled = false;
        var failures = new List<string>();
        try
        {
            var steamRefresh = RefreshSteamCatalogAsync(refresh.Token, failures);
            var xboxRefresh = RefreshXboxCatalogAsync(refresh.Token, failures);
            await Task.WhenAll(steamRefresh, xboxRefresh);
            refresh.Token.ThrowIfCancellationRequested();

            _initialScanCompleted = true;
            _catalogFailures = failures;
            UpdateCatalogStatus(_catalogFailures);
            RestartMissingArtworkLoads(forceRetry: explicitRefresh);
        }
        finally
        {
            if (ReferenceEquals(_refreshCancellation, refresh))
            {
                _refreshCancellation = null;
                RefreshGamesButton.IsEnabled = true;
            }
            refresh.Dispose();
        }
    }

    private async Task RefreshSteamCatalogAsync(CancellationToken cancellationToken, List<string> failures)
    {
        try
        {
            var scannedCatalog = await _frontend!.ScanProfileGamesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _steamCatalog = ProfilePage.FilterSteamCatalog(scannedCatalog);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            failures.Add(MainUiStrings.Get("Profiles_SteamCatalogWarning", "Steam games could not be refreshed."));
            UiLog.Warn("Profiles.Catalog", "Steam profile catalog refresh failed; keeping the last valid catalog.", exception);
        }

        RebuildCatalogItems();
        _catalogFailures = failures.ToArray();
        UpdateCatalogStatus(_catalogFailures);

        if (_steamCatalog.Count == 0)
        {
            _steamArtworkContext = null;
            return;
        }

        try
        {
            _steamArtworkContext = await Task.Run(SteamPortraitArtworkResolver.CreateContext, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            _steamArtworkContext = null;
            UiLog.Debug("Profiles.Artwork", "Optional Steam artwork context is unavailable.",
                ("ErrorType", exception.GetType().Name));
        }
        RestartMissingArtworkLoads(forceRetry: true);
    }

    private async Task RefreshXboxCatalogAsync(CancellationToken cancellationToken, List<string> failures)
    {
        try
        {
            var snapshot = await _frontend!.ScanXboxGamesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot.Outcome != FrontendXboxGameCatalogOutcome.Ready)
            {
                failures.Add(MainUiStrings.Get("Profiles_XboxCatalogWarning", "Xbox games could not be refreshed."));
            }
            else
            {
                _xboxCatalog = snapshot.Games;
                EnsureXboxArtworkResolver();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            failures.Add(MainUiStrings.Get("Profiles_XboxCatalogWarning", "Xbox games could not be refreshed."));
            UiLog.Warn("Profiles.Catalog", "Xbox catalog refresh failed; keeping the last valid catalog.", exception);
        }

        RebuildCatalogItems();
        _catalogFailures = failures.ToArray();
        UpdateCatalogStatus(_catalogFailures);
    }

    private void EnsureXboxArtworkResolver()
    {
        _xboxArtworkResolver ??= new XboxStoreArtworkResolver(
            XboxStoreArtworkResolver.ResolveCacheDirectory([FrontendLaunchArguments.LogDirectoryOption, UiLog.DirectoryPath]),
            diagnostic => UiLog.Debug("Xbox.Artwork", "Optional Xbox artwork lookup completed.",
                ("StoreId", diagnostic.StoreId), ("Market", diagnostic.Market), ("Purpose", diagnostic.Purpose),
                ("Outcome", diagnostic.Outcome), ("DurationMs", diagnostic.DurationMs), ("ErrorType", diagnostic.ErrorType ?? "None")));
    }

    private void RebuildCatalogItems()
    {
        var removed = ProfilesCatalogPolicy.SynchronizeItems(_itemsByIdentity, _steamCatalog, _xboxCatalog);
        foreach (var identity in removed)
        {
            foreach (var realized in _realizedCards.Values.Where(card => card.Item.Identity == identity).ToArray())
                realized.CancelLoad();
        }
        RebuildVisibleItems();
    }

    private void UpdateCatalogStatus(IReadOnlyList<string> failures)
    {
        var messages = failures.ToList();
        if (_itemsByIdentity.Values.Any(item => !item.FavoriteStateKnown))
            messages.Add(MainUiStrings.Get("Profiles_FavoriteStateUnavailable", "Favorite status is unavailable for some games."));
        CatalogWarningInfoBar.Message = string.Join(" ", messages.Distinct(StringComparer.CurrentCulture));
        CatalogWarningInfoBar.IsOpen = messages.Count > 0;
        EmptyCatalogText.Visibility = _initialScanCompleted && _visibleItems.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void GameFilterSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is not SelectorBarItem { Tag: string tag }) return;
        var nextFilter = tag switch
        {
            "Steam" => ProfilesLibraryFilter.Steam,
            "Xbox" => ProfilesLibraryFilter.Xbox,
            _ => ProfilesLibraryFilter.All,
        };
        if (_filter == nextFilter) return;
        _filter = nextFilter;
        RebuildVisibleItems();
    }

    private void GameSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RebuildVisibleItems();

    private void RebuildVisibleItems()
    {
        var desired = ProfilesCatalogPolicy.FilterAndSort(_itemsByIdentity.Values, _filter, GameSearchBox.Text);
        var visibleIdentities = desired.Select(item => item.Identity).ToHashSet(StringComparer.Ordinal);
        foreach (var card in _realizedCards.Values.Where(card => !visibleIdentities.Contains(card.Item.Identity)))
            card.CancelLoad();
        ProfilesCatalogPolicy.ReconcileVisibleCards(_visibleItems, desired);
        EmptyCatalogText.Visibility = _initialScanCompleted && _visibleItems.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        RestartMissingArtworkLoads();
    }

    private async void GameCardButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_active || (sender as Button)?.Tag is not ProfilesLibraryItem item
            || !_itemsByIdentity.TryGetValue(item.Identity, out var currentItem)
            || !ReferenceEquals(currentItem, item)) return;
        CancelPendingArtworkLoads();
        LibraryPanel.Visibility = Visibility.Collapsed;
        DetailHost.Visibility = Visibility.Visible;

        if (item.SteamGame is { } steamGame)
        {
            var editor = GetOrCreateSteamEditor();
            DetailHost.Content = editor;
            await editor.OpenGameFromUnifiedProfilesAsync(steamGame);
        }
        else if (item.XboxGame is { } xboxGame)
        {
            var editor = GetOrCreateXboxEditor();
            DetailHost.Content = editor;
            await editor.OpenGameFromUnifiedProfilesAsync(xboxGame);
        }
    }

    private ProfilePage GetOrCreateSteamEditor()
    {
        if (_steamEditor is not null) return _steamEditor;
        var editor = new ProfilePage();
        editor.Initialize(_frontend!, _featureSupport);
        editor.SetSteamActivity(_steamActive);
        editor.SteamActivityRefreshRequested += OnSteamActivityRefreshRequested;
        editor.UnifiedProfilesBackRequested += OnEditorBackRequested;
        return _steamEditor = editor;
    }

    private XboxPage GetOrCreateXboxEditor()
    {
        if (_xboxEditor is not null) return _xboxEditor;
        var editor = new XboxPage();
        editor.Initialize(_frontend!);
        editor.UnifiedProfilesBackRequested += OnEditorBackRequested;
        return _xboxEditor = editor;
    }

    private void OnEditorBackRequested(object? sender, EventArgs args)
    {
        _steamEditor?.Deactivate();
        _xboxEditor?.Deactivate();
        DetailHost.Content = null;
        DetailHost.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Visible;
        RestartMissingArtworkLoads();
    }

    private void OnSteamActivityRefreshRequested(object? sender, EventArgs args) =>
        SteamActivityRefreshRequested?.Invoke(this, EventArgs.Empty);

    private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_active || _frontend is null || (sender as Button)?.Tag is not ProfilesLibraryItem item
            || !item.FavoriteStateKnown)
            return;

        try
        {
            if (item.SteamGame is { } steamGame)
            {
                var result = await _frontend.SetGameProfileFavoriteAsync(steamGame.AppId, !steamGame.Favorite, steamGame.Name);
                if (!result.Succeeded) throw new InvalidOperationException(result.FailureMessage);
                if (!_itemsByIdentity.TryGetValue(item.Identity, out var currentItem) || !ReferenceEquals(currentItem, item)) return;
                var updated = steamGame with { Favorite = !steamGame.Favorite };
                _steamCatalog = _steamCatalog.Select(game => ProfilesLibraryItem.GetIdentity(game) == item.Identity ? updated : game).ToArray();
                item.Update(updated);
            }
            else if (item.XboxGame is { } xboxGame)
            {
                var result = await _frontend.SetXboxGameProfileFavoriteAsync(xboxGame.Key, !xboxGame.Favorite, xboxGame.DisplayName);
                if (!result.Succeeded || result.Snapshot.Key != xboxGame.Key)
                    throw new InvalidOperationException(result.FailureMessage);
                if (!_itemsByIdentity.TryGetValue(item.Identity, out var currentItem) || !ReferenceEquals(currentItem, item)) return;
                var updated = xboxGame with { Favorite = !xboxGame.Favorite };
                _xboxCatalog = _xboxCatalog.Select(game => game.Key == xboxGame.Key ? updated : game).ToArray();
                item.Update(updated);
            }
            RebuildVisibleItems();
            UpdateCatalogStatus(_catalogFailures);
        }
        catch (Exception exception)
        {
            UiLog.Warn("Profiles.Catalog", "Game favorite could not be saved.", exception);
            CatalogWarningInfoBar.Message = MainUiStrings.Get("Profiles_FavoriteSaveFailed", "Favorite could not be saved.");
            CatalogWarningInfoBar.IsOpen = true;
        }
    }

    private void CoverItemsPanel_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ItemsWrapGrid itemsPanel) AttachCoverItemsPanel(itemsPanel);
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

    private void DetachCoverItemsPanel()
    {
        if (_coverItemsPanel is not null) _coverItemsPanel.SizeChanged -= GameGridItemsPanel_SizeChanged;
        _coverItemsPanel = null;
    }

    private void AttachXamlRoot()
    {
        var xamlRoot = GameGrid.XamlRoot;
        if (ReferenceEquals(_observedXamlRoot, xamlRoot)) return;
        DetachXamlRoot();
        _observedXamlRoot = xamlRoot;
        if (_observedXamlRoot is not null) _observedXamlRoot.Changed += XamlRoot_Changed;
    }

    private void DetachXamlRoot()
    {
        if (_observedXamlRoot is not null) _observedXamlRoot.Changed -= XamlRoot_Changed;
        _observedXamlRoot = null;
        _lastRasterizationScale = double.NaN;
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        var scale = NormalizeRasterizationScale(sender.RasterizationScale);
        if (Math.Abs(_lastRasterizationScale - scale) >= 0.0001) ApplyCoverGridLayout(scale);
    }

    private void GameGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SteamCoverGridLayout.HasMeaningfulWidthChange(e.PreviousSize.Width, e.NewSize.Width))
            ScheduleCoverGridLayoutUpdate(GameGrid.XamlRoot?.RasterizationScale ?? 1.0);
    }

    private void GameGridItemsPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SteamCoverGridLayout.HasMeaningfulWidthChange(e.PreviousSize.Width, e.NewSize.Width))
            ScheduleCoverGridLayoutUpdate(GameGrid.XamlRoot?.RasterizationScale ?? 1.0);
    }

    private void ScheduleCoverGridLayoutUpdate(double rasterizationScale)
    {
        if (_coverItemsPanel is null) return;
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
        if (_coverItemsPanel is not { } itemsPanel
            || !TryCalculateCoverGridLayout(itemsPanel.ActualWidth, rasterizationScale, out var layout))
            return;
        _lastRasterizationScale = rasterizationScale;
        if (_coverGridLayout is { } current && LayoutsMatch(current, layout))
        {
            GameGrid.Opacity = 1;
            return;
        }
        _coverGridLayout = layout;
        itemsPanel.ItemWidth = layout.SlotWidthDip;
        itemsPanel.ItemHeight = layout.SlotHeightDip;
        foreach (var card in _realizedCards.Values) SetCardLayout(card.Root, layout);
        GameGrid.Opacity = 1;
    }

    internal static bool TryCalculateCoverGridLayout(double availableWidthDip, double rasterizationScale,
        out SteamCoverGridLayout layout) =>
        SteamCoverGridLayout.TryCalculate(availableWidthDip, rasterizationScale, out layout);

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
            if (_realizedCards.Remove(container, out var recycled)) recycled.CancelLoad();
            return;
        }
        if (args.Item is not ProfilesLibraryItem item) return;
        if (container.ContentTemplateRoot is not Border root)
        {
            if (args.Phase == 0) args.RegisterUpdateCallback(GameGrid_ContainerContentChanging);
            return;
        }
        if (root.FindName("PosterImage") is not Image
            || root.FindName("BoxArtPanel") is not FrameworkElement
            || root.FindName("FallbackCard") is not FrameworkElement)
        {
            if (args.Phase == 0) args.RegisterUpdateCallback(GameGrid_ContainerContentChanging);
            return;
        }
        if (!ReferenceEquals(root.DataContext, item)) root.DataContext = item;
        if (root.FindName("OpenGameButton") is Button openButton && !ReferenceEquals(openButton.DataContext, item)) openButton.DataContext = item;
        if (root.FindName("FavoriteButton") is Button favoriteButton && !ReferenceEquals(favoriteButton.DataContext, item)) favoriteButton.DataContext = item;
        if (_coverGridLayout is { } layout) SetCardLayout(root, layout);

        if (_realizedCards.TryGetValue(container, out var existing))
        {
            if (ReferenceEquals(existing.Item, item))
            {
                if (item.Artwork is not null || existing.LoadCancellation is not null || existing.LoadAttempted) return;
            }
            else
            {
                existing.CancelLoad();
                existing.Reset(item);
            }
        }
        else
        {
            existing = new RealizedCard(container, root, item);
            _realizedCards.Add(container, existing);
        }
        StartArtworkLoad(existing);
    }

    private void StartArtworkLoad(RealizedCard card, bool forceRetry = false)
    {
        if (!_active || LibraryPanel.Visibility != Visibility.Visible || card.Item.Artwork is not null
            || card.LoadCancellation is not null || (card.LoadAttempted && !forceRetry)) return;
        card.LoadAttempted = true;
        card.LoadCancellation = new CancellationTokenSource();
        var generation = ++card.Generation;
        _ = LoadArtworkAsync(card, generation, card.Item, forceRetry, card.LoadCancellation.Token);
    }

    private async Task LoadArtworkAsync(RealizedCard card, long generation, ProfilesLibraryItem item, bool forceRetry,
        CancellationToken cancellationToken)
    {
        try
        {
            BitmapImage? bitmap = null;
            string? purpose = null;
            if (item.SteamGame is { } steamGame && _steamArtworkContext is { } steamContext)
            {
                var paths = await Task.Run(() => SteamPortraitArtworkResolver.ResolveCandidates(steamContext, steamGame), cancellationToken);
                bitmap = await TryLoadFirstImageAsync(paths, CurrentCardDecodePixelWidth(), cancellationToken);
                if (bitmap is not null) purpose = "Poster";
            }
            else if (item.XboxGame is { } xboxGame && _xboxArtworkResolver is { } xboxResolver)
            {
                var artwork = await xboxResolver.ResolveCatalogCardAsync(
                    xboxGame.Key, CultureInfo.CurrentUICulture.Name, cancellationToken, forceRetry);
                if (artwork is not null)
                {
                    bitmap = await TryLoadImageAsync(artwork.Path, CurrentCardDecodePixelWidth(), cancellationToken);
                    if (bitmap is not null) purpose = artwork.Purpose;
                }
            }

            if (bitmap is null || !IsCurrentArtworkResult(card, generation, item.Identity)) return;
            item.SetArtwork(bitmap, purpose);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            UiLog.Debug("Profiles.Artwork", "Optional game cover could not be displayed.",
                ("Platform", item.IsXbox ? "Xbox" : "Steam"), ("ErrorType", exception.GetType().Name));
        }
        finally
        {
            if (IsCurrentArtworkResult(card, generation, item.Identity))
            {
                card.LoadCancellation?.Dispose();
                card.LoadCancellation = null;
                if (cancellationToken.IsCancellationRequested) card.LoadAttempted = false;
            }
        }
    }

    private static async Task<BitmapImage?> TryLoadFirstImageAsync(IReadOnlyList<string> paths, int decodePixelWidth, CancellationToken cancellationToken)
    {
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bitmap = await TryLoadImageAsync(path, decodePixelWidth, cancellationToken);
            if (bitmap is not null) return bitmap;
        }
        return null;
    }

    private static async Task<BitmapImage?> TryLoadImageAsync(string path, int decodePixelWidth, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage { DecodePixelWidth = decodePixelWidth };
            await bitmap.SetSourceAsync(stream);
            cancellationToken.ThrowIfCancellationRequested();
            return bitmap;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return null; }
    }

    private bool IsCurrentArtworkResult(RealizedCard card, long generation, string identity) =>
        _active && _realizedCards.TryGetValue(card.Container, out var current)
        && ReferenceEquals(current, card) && card.Generation == generation
        && card.Item.Identity == identity && ReferenceEquals(card.Root.DataContext, card.Item)
        && _itemsByIdentity.TryGetValue(identity, out var currentItem) && ReferenceEquals(currentItem, card.Item);

    private void RestartMissingArtworkLoads(bool forceRetry = false)
    {
        foreach (var card in _realizedCards.Values.ToArray()) StartArtworkLoad(card, forceRetry);
    }

    private void CancelPendingArtworkLoads()
    {
        foreach (var card in _realizedCards.Values)
        {
            if (card.LoadCancellation is not null)
            {
                card.CancelLoad();
                card.LoadAttempted = false;
            }
        }
    }

    private int CurrentCardDecodePixelWidth()
    {
        var widthDip = _coverGridLayout?.CoverWidthDip ?? 175;
        var scale = NormalizeRasterizationScale(GameGrid.XamlRoot?.RasterizationScale ?? 1.0);
        return (int)Math.Clamp(Math.Ceiling(widthDip * scale), 1, 1200);
    }

    private static void SetCardLayout(FrameworkElement card, SteamCoverGridLayout layout)
    {
        if (Math.Abs(card.Width - layout.CoverWidthDip) >= 0.0001) card.Width = layout.CoverWidthDip;
        if (Math.Abs(card.Height - layout.CoverHeightDip) >= 0.0001) card.Height = layout.CoverHeightDip;
        var margin = new Thickness(0, 0, layout.ColumnGapDip, layout.RowGapDip);
        if (!card.Margin.Equals(margin)) card.Margin = margin;
    }

    private sealed class RealizedCard(
        SelectorItem container,
        Border root,
        ProfilesLibraryItem item)
    {
        internal SelectorItem Container { get; } = container;
        internal Border Root { get; } = root;
        internal ProfilesLibraryItem Item { get; private set; } = item;
        internal long Generation { get; set; }
        internal bool LoadAttempted { get; set; }
        internal CancellationTokenSource? LoadCancellation { get; set; }

        internal void Reset(ProfilesLibraryItem item)
        {
            Item = item;
            Generation++;
            LoadAttempted = false;
        }

        internal void CancelLoad()
        {
            Generation++;
            LoadCancellation?.Cancel();
            LoadCancellation?.Dispose();
            LoadCancellation = null;
        }
    }
}
