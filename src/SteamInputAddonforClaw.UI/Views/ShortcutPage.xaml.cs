using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using SteamInputAddonforClaw.Contracts.Frontend;
using WinRT.Interop;
using Windows.Foundation;

namespace SteamInputAddonforClaw.Views;

public sealed partial class ShortcutPage : UserControl
{
    private const int ShortcutColumnCount = 3;
    private const double ShortcutCardHorizontalGap = 12;
    private const double ShortcutReorderThresholdDip = 8;
    private readonly ObservableCollection<FrontendShortcutEditorTile> _tiles = [];
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private IAddonFrontendControl? _frontend;
    private Func<nint>? _windowHandleProvider;
    private bool _active;
    private bool _operationInProgress;
    private bool _dialogOpen;
    private bool _refreshInProgress;
    private bool _editorAvailable;
    private ItemsWrapGrid? _shortcutItemsPanel;
    private Pointer? _reorderPointer;
    private Border? _reorderSurface;
    private Guid? _reorderTileId;
    private int _reorderSourceIndex = -1;
    private Point _reorderStartPosition;
    private bool _reorderThresholdPassed;
    private int? _reorderTargetIndex;

    public ShortcutPage()
    {
        InitializeComponent();
        ShortcutList.ItemsSource = _tiles;
        ShortcutList.Loaded += ShortcutList_Loaded;
    }

    public void Initialize(IAddonFrontendControl frontend, Func<nint> windowHandleProvider)
    {
        _frontend = frontend ?? throw new ArgumentNullException(nameof(frontend));
        _windowHandleProvider = windowHandleProvider ?? throw new ArgumentNullException(nameof(windowHandleProvider));
    }

    public void Activate()
    {
        if (_active) return;
        _active = true;
        RequestRefresh();
    }

    public void Deactivate()
    {
        _active = false;
        ClearShortcutReorder("Canceled", releaseCapture: true);
    }

    public void RequestRefresh()
    {
        if (!_active || _operationInProgress || _dialogOpen || _refreshInProgress || _frontend is null) return;
        _dispatcherQueue.TryEnqueue(() => _ = CaptureAndRenderAsync());
    }

    private async Task CaptureAndRenderAsync()
    {
        if (!_active || _operationInProgress || _dialogOpen || _refreshInProgress || _frontend is null) return;
        _refreshInProgress = true;
        SetBusy(true);
        try
        {
            Render(await _frontend.CaptureShortcutEditorAsync());
        }
        catch
        {
            ShowMessage("Shortcut settings could not be loaded.", InfoBarSeverity.Error);
        }
        finally
        {
            _refreshInProgress = false;
            SetBusy(false);
        }
    }

    private void Render(FrontendShortcutEditorSnapshot snapshot)
    {
        ClearShortcutReorder("Canceled", releaseCapture: true);

        _editorAvailable = snapshot.Available;
        _tiles.Clear();
        foreach (var tile in snapshot.Tiles)
            _tiles.Add(tile);
        ShortcutEmptyState.Visibility = snapshot.Available && _tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShortcutList.Visibility = snapshot.Available && _tiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AddShortcutButton.IsEnabled = snapshot.Available && !_operationInProgress;
        ShortcutList.IsEnabled = snapshot.Available && _tiles.Count > 0 && !_operationInProgress;
        UpdateShortcutItemWidth();

        if (!snapshot.Available)
            ShowMessage(snapshot.FailureMessage ?? "Shortcut editing is unavailable.", InfoBarSeverity.Warning);
        else if (ShortcutInfoBar.Severity == InfoBarSeverity.Warning)
            ShortcutInfoBar.IsOpen = false;
    }

    private async void AddShortcutButton_Click(object sender, RoutedEventArgs e) =>
        await ShowEditorAsync(null);

    private async void EditTileButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Guid tileId) return;
        var tile = _tiles.FirstOrDefault(candidate => candidate.TileId == tileId);
        if (tile is null || !tile.Action.Editable) return;
        await ShowEditorAsync(tile);
    }

    private async Task ShowEditorAsync(FrontendShortcutEditorTile? existing)
    {
        if (_frontend is null || _operationInProgress || _dialogOpen || XamlRoot is null) return;
        _dialogOpen = true;
        FrontendShortcutEditorSnapshot authoritativeSnapshot;
        try
        {
            authoritativeSnapshot = await _frontend.CaptureShortcutEditorAsync();
        }
        catch
        {
            _dialogOpen = false;
            ShowMessage("Shortcut settings could not be loaded.", InfoBarSeverity.Error);
            return;
        }

        Render(authoritativeSnapshot);
        if (!authoritativeSnapshot.Available)
        {
            _dialogOpen = false;
            return;
        }

        var existingTileId = existing?.TileId;
        existing = existingTileId is { } tileId
            ? authoritativeSnapshot.Tiles.FirstOrDefault(tile => tile.TileId == tileId)
            : null;
        if (existingTileId is not null && existing is null)
        {
            _dialogOpen = false;
            ShowMessage("Shortcut tile was not found.", InfoBarSeverity.Warning);
            return;
        }

        var titleBox = new TextBox
        {
            Header = "Title",
            PlaceholderText = "Shortcut name",
            Text = existing?.Title ?? string.Empty,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var actionPicker = new ComboBox { Header = "Action type", MinWidth = 280 };
        AddActionChoice(actionPicker, "Application (.exe)", FrontendShortcutEditorActionKind.Executable);
        AddActionChoice(actionPicker, "PowerShell", FrontendShortcutEditorActionKind.PowerShell);
        AddActionChoice(actionPicker, "Website (URL)", FrontendShortcutEditorActionKind.Url);
        AddActionChoice(actionPicker, "Steam Big Picture", FrontendShortcutEditorActionKind.SteamBigPicture);
        AddActionChoice(actionPicker, "Steam", FrontendShortcutEditorActionKind.SteamClient);
        AddActionChoice(actionPicker, "Xbox", FrontendShortcutEditorActionKind.XboxApp);
        var existingScreenshot = existing?.Action.Kind == FrontendShortcutEditorActionKind.ScreenshotFullscreen;
        var screenshotAlreadyExists = authoritativeSnapshot.Tiles.Any(tile =>
            tile.Action.Kind == FrontendShortcutEditorActionKind.ScreenshotFullscreen
            && tile.TileId != existing?.TileId);
        if (existingScreenshot || !screenshotAlreadyExists)
            AddActionChoice(actionPicker, "Screenshot", FrontendShortcutEditorActionKind.ScreenshotFullscreen);
        var closeOverlayToggle = new ToggleSwitch
        {
            Header = "Close Overlay after launch",
            OnContent = "Close Overlay",
            OffContent = "Keep Overlay open",
            IsOn = existing?.CloseOverlayAfterLaunch ?? false,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var executablePath = new TextBox { Header = "Executable path", PlaceholderText = @"C:\Path\Application.exe" };
        var executableBrowse = new Button { Content = "Browse…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        var executableArguments = new TextBox { Header = "Arguments", PlaceholderText = "Optional arguments" };
        var executableRunAsAdministrator = new CheckBox
        {
            Content = "Run as administrator",
            IsChecked = existing?.Action.Kind == FrontendShortcutEditorActionKind.Executable
                && existing.Action.RunAsAdministrator
        };
        var executablePanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                executablePath, executableBrowse, executableArguments, executableRunAsAdministrator,
                new TextBlock { Text = "Runs with administrator privileges; otherwise requests normal user privileges.", TextWrapping = TextWrapping.Wrap }
            }
        };

        var powerShellRunAsAdministrator = new CheckBox
        {
            Content = "Run as administrator",
            IsChecked = existing?.Action.Kind == FrontendShortcutEditorActionKind.PowerShell
                && existing.Action.RunAsAdministrator
        };
        var script = new TextBox
        {
            Header = "Script",
            PlaceholderText = "Enter a PowerShell script",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 180,
            Text = existing?.Action.PowerShellScript ?? string.Empty
        };
        var scriptPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                script, powerShellRunAsAdministrator,
                new TextBlock { Text = "Runs with administrator privileges; otherwise requests normal user privileges.", TextWrapping = TextWrapping.Wrap }
            }
        };

        var url = new TextBox { Header = "URL", PlaceholderText = "https://…", Text = existing?.Action.Url ?? string.Empty };
        var urlPanel = new StackPanel { Spacing = 8, Children = { url } };
        string? stagedScreenshotFolder = existingScreenshot ? existing?.Action.ScreenshotFolder : null;
        var screenshotFolderPath = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var browseScreenshotFolderButton = new Button { Content = "Browse…", HorizontalAlignment = HorizontalAlignment.Left };
        var useDefaultFolderButton = new Button { Content = "Use default", HorizontalAlignment = HorizontalAlignment.Left };
        var openScreenshotFolderButton = new Button { Content = "Open folder", HorizontalAlignment = HorizontalAlignment.Left };
        void RenderScreenshotFolder()
        {
            screenshotFolderPath.Text = ResolveScreenshotFolder(stagedScreenshotFolder);
            useDefaultFolderButton.IsEnabled = !string.IsNullOrWhiteSpace(stagedScreenshotFolder);
        }

        browseScreenshotFolderButton.Click += async (_, _) =>
        {
            try
            {
                var hwnd = _windowHandleProvider?.Invoke() ?? 0;
                if (hwnd == 0) throw new InvalidOperationException("The Main App window is unavailable.");
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var picker = new FolderPicker(windowId);
                var folder = await picker.PickSingleFolderAsync();
                if (folder is null) return;
                stagedScreenshotFolder = folder.Path;
                RenderScreenshotFolder();
            }
            catch
            {
                ShowMessage("The folder picker could not be opened.", InfoBarSeverity.Error);
            }
        };
        useDefaultFolderButton.Click += (_, _) =>
        {
            stagedScreenshotFolder = null;
            RenderScreenshotFolder();
        };
        openScreenshotFolderButton.Click += (_, _) =>
        {
            var folder = ResolveScreenshotFolder(stagedScreenshotFolder);
            if (string.IsNullOrWhiteSpace(folder))
            {
                ShowMessage("The effective Screenshot folder is unavailable.", InfoBarSeverity.Warning);
                return;
            }

            try
            {
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
            catch
            {
                ShowMessage("The Screenshot folder could not be opened.", InfoBarSeverity.Error);
            }
        };
        RenderScreenshotFolder();
        var screenshotPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Captures the primary display as JPEG.", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "Save folder", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                screenshotFolderPath,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { browseScreenshotFolderButton, useDefaultFolderButton, openScreenshotFolderButton }
                }
            }
        };
        var steamBigPicturePanel = CreateBuiltInActionPanel("Opens Steam Big Picture.");
        var steamClientPanel = CreateBuiltInActionPanel("Opens the Steam client.");
        var xboxAppPanel = CreateBuiltInActionPanel("Opens the Xbox app.");

        var panels = new Dictionary<FrontendShortcutEditorActionKind, UIElement>
        {
            [FrontendShortcutEditorActionKind.Executable] = executablePanel,
            [FrontendShortcutEditorActionKind.PowerShell] = scriptPanel,
            [FrontendShortcutEditorActionKind.Url] = urlPanel,
            [FrontendShortcutEditorActionKind.ScreenshotFullscreen] = screenshotPanel,
            [FrontendShortcutEditorActionKind.SteamBigPicture] = steamBigPicturePanel,
            [FrontendShortcutEditorActionKind.SteamClient] = steamClientPanel,
            [FrontendShortcutEditorActionKind.XboxApp] = xboxAppPanel
        };

        string? suggestedTitle = null;
        actionPicker.SelectionChanged += (_, _) =>
        {
            UpdateEditorPanel(actionPicker, panels);
            if (actionPicker.SelectedItem is not ComboBoxItem { Tag: FrontendShortcutEditorActionKind selectedKind })
                return;

            closeOverlayToggle.Visibility = selectedKind == FrontendShortcutEditorActionKind.ScreenshotFullscreen
                ? Visibility.Collapsed
                : Visibility.Visible;
            if (existing is null)
                closeOverlayToggle.IsOn = DefaultCloseOverlayAfterLaunch(selectedKind);

            var defaultTitle = GetDefaultTitle(selectedKind);
            if (defaultTitle is null) return;
            if (ShouldApplyDefaultTitle(existing is null, titleBox.Text, suggestedTitle))
            {
                titleBox.Text = defaultTitle;
                suggestedTitle = defaultTitle;
            }
        };
        executableBrowse.Click += async (_, _) => await BrowseForExecutableAsync(executablePath);
        if (existing is not null)
        {
            executablePath.Text = existing.Action.ExecutablePath ?? string.Empty;
            executableArguments.Text = existing.Action.ExecutableArguments ?? string.Empty;
            var selected = existing.Action.Kind;
            actionPicker.SelectedItem = actionPicker.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is FrontendShortcutEditorActionKind kind && kind == selected);
        }
        if (actionPicker.SelectedItem is null) actionPicker.SelectedIndex = 0;
        UpdateEditorPanel(actionPicker, panels);

        var content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                titleBox, actionPicker, closeOverlayToggle, executablePanel, scriptPanel, urlPanel,
                steamBigPicturePanel, steamClientPanel, xboxAppPanel, screenshotPanel
            }
        };
        var dialog = new ContentDialog
        {
            Title = existing is null ? "Add Shortcut" : "Edit Shortcut",
            Content = content,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        _dialogOpen = true;
        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
        }
        finally
        {
            _dialogOpen = false;
        }
        if (result != ContentDialogResult.Primary) return;

        var selectedKind = (actionPicker.SelectedItem as ComboBoxItem)?.Tag is FrontendShortcutEditorActionKind kind
            ? kind
            : FrontendShortcutEditorActionKind.Unsupported;
        var action = selectedKind switch
        {
            FrontendShortcutEditorActionKind.Executable => new FrontendShortcutActionInput(
                selectedKind, executablePath.Text, executableArguments.Text,
                RunAsAdministrator: executableRunAsAdministrator.IsChecked == true),
            FrontendShortcutEditorActionKind.PowerShell => new FrontendShortcutActionInput(
                selectedKind, PowerShellScript: script.Text,
                RunAsAdministrator: powerShellRunAsAdministrator.IsChecked == true),
            FrontendShortcutEditorActionKind.Url => new FrontendShortcutActionInput(selectedKind, Url: url.Text),
            FrontendShortcutEditorActionKind.ScreenshotFullscreen => new FrontendShortcutActionInput(selectedKind, ScreenshotFolder: stagedScreenshotFolder),
            FrontendShortcutEditorActionKind.SteamBigPicture => new FrontendShortcutActionInput(selectedKind),
            FrontendShortcutEditorActionKind.SteamClient => new FrontendShortcutActionInput(selectedKind),
            FrontendShortcutEditorActionKind.XboxApp => new FrontendShortcutActionInput(selectedKind),
            _ => new FrontendShortcutActionInput(FrontendShortcutEditorActionKind.Unsupported)
        };
        var intent = new FrontendShortcutMutationIntent(
            existing is null ? FrontendShortcutMutationKind.Create : FrontendShortcutMutationKind.Update,
            existing?.TileId,
            titleBox.Text,
            action,
            selectedKind != FrontendShortcutEditorActionKind.ScreenshotFullscreen && closeOverlayToggle.IsOn);
        await ApplyMutationAsync(intent);
    }

    private static void AddActionChoice(ComboBox picker, string label, FrontendShortcutEditorActionKind kind) =>
        picker.Items.Add(new ComboBoxItem { Content = label, Tag = kind });

    private static StackPanel CreateBuiltInActionPanel(string description) => new()
    {
        Spacing = 8,
        Children = { new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap } }
    };

    internal static string? GetDefaultTitle(FrontendShortcutEditorActionKind kind) => kind switch
    {
        FrontendShortcutEditorActionKind.SteamBigPicture => "Steam Big Picture",
        FrontendShortcutEditorActionKind.SteamClient => "Steam",
        FrontendShortcutEditorActionKind.XboxApp => "Xbox",
        FrontendShortcutEditorActionKind.ScreenshotFullscreen => "Screenshot",
        _ => null
    };

    internal static bool DefaultCloseOverlayAfterLaunch(FrontendShortcutEditorActionKind kind) => kind is
        FrontendShortcutEditorActionKind.SteamBigPicture or
        FrontendShortcutEditorActionKind.SteamClient or
        FrontendShortcutEditorActionKind.XboxApp;

    internal static bool ShouldApplyDefaultTitle(bool isCreating, string currentTitle, string? previousSuggestedTitle) =>
        isCreating
        && (string.IsNullOrWhiteSpace(currentTitle)
            || string.Equals(currentTitle, previousSuggestedTitle, StringComparison.Ordinal));

    internal static double GetShortcutItemWidth(double availableWidth) =>
        Math.Max(1, (availableWidth - (ShortcutColumnCount - 1) * ShortcutCardHorizontalGap) / ShortcutColumnCount);

    private static void UpdateEditorPanel(ComboBox picker, IReadOnlyDictionary<FrontendShortcutEditorActionKind, UIElement> panels)
    {
        foreach (var panel in panels.Values) panel.Visibility = Visibility.Collapsed;
        if ((picker.SelectedItem as ComboBoxItem)?.Tag is FrontendShortcutEditorActionKind kind
            && panels.TryGetValue(kind, out var selectedPanel))
            selectedPanel.Visibility = Visibility.Visible;
    }

    private void ShortcutList_Loaded(object sender, RoutedEventArgs e)
    {
        _shortcutItemsPanel ??= FindVisualChild<ItemsWrapGrid>(ShortcutList);
        UpdateShortcutItemWidth();
    }

    private void ShortcutList_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateShortcutItemWidth();

    private void UpdateShortcutItemWidth()
    {
        var itemsPanel = _shortcutItemsPanel ??= FindVisualChild<ItemsWrapGrid>(ShortcutList);
        if (itemsPanel is null || ShortcutList.ActualWidth <= 0) return;

        var itemWidth = GetShortcutItemWidth(ShortcutList.ActualWidth);
        if (!double.IsFinite(itemsPanel.ItemWidth) || Math.Abs(itemsPanel.ItemWidth - itemWidth) > 0.1)
            itemsPanel.ItemWidth = itemWidth;
    }

    private static int IndexOfTile(IReadOnlyList<FrontendShortcutEditorTile> tiles, Guid tileId)
    {
        for (var index = 0; index < tiles.Count; index++)
        {
            if (tiles[index].TileId == tileId) return index;
        }

        return -1;
    }

    internal static bool HasPassedShortcutReorderThreshold(Point start, Point current)
    {
        var deltaX = current.X - start.X;
        var deltaY = current.Y - start.Y;
        return deltaX * deltaX + deltaY * deltaY >= ShortcutReorderThresholdDip * ShortcutReorderThresholdDip;
    }

    internal static int? ResolveShortcutDropIndex(
        Point releasePosition,
        IReadOnlyList<(int Index, Rect Bounds)> realizedCards,
        Rect validDropBounds)
    {
        if (!IsFinite(releasePosition.X) || !IsFinite(releasePosition.Y)
            || !IsUsableBounds(validDropBounds)
            || !validDropBounds.Contains(releasePosition))
            return null;

        var candidates = realizedCards
            .Where(card => card.Index >= 0 && IsUsableBounds(card.Bounds) && Intersects(card.Bounds, validDropBounds))
            .OrderBy(card => card.Index)
            .ToArray();
        if (candidates.Length == 0)
            return null;

        var firstOccupiedRowTop = candidates.Min(card => card.Bounds.Top);
        var lastOccupiedRowBottom = candidates.Max(card => card.Bounds.Bottom);
        const double rowGapToleranceDip = 12;
        if (releasePosition.Y < firstOccupiedRowTop - rowGapToleranceDip
            || releasePosition.Y > lastOccupiedRowBottom + rowGapToleranceDip)
            return null;

        var containingCard = candidates.FirstOrDefault(card => card.Bounds.Contains(releasePosition));
        if (containingCard.Bounds.Width > 0 && containingCard.Bounds.Height > 0)
            return containingCard.Index;

        var nearestRowCenterY = candidates
            .Select(card => card.Bounds.Y + card.Bounds.Height / 2)
            .OrderBy(centerY => Math.Abs(releasePosition.Y - centerY))
            .First();
        var nearestRow = candidates
            .Where(card => Math.Abs(card.Bounds.Y + card.Bounds.Height / 2 - nearestRowCenterY) < 0.5)
            .ToArray();

        var nearestIndex = -1;
        var nearestDistanceSquared = double.PositiveInfinity;
        foreach (var card in nearestRow)
        {
            var centerX = card.Bounds.X + card.Bounds.Width / 2;
            var centerY = card.Bounds.Y + card.Bounds.Height / 2;
            var deltaX = releasePosition.X - centerX;
            var deltaY = releasePosition.Y - centerY;
            var distanceSquared = deltaX * deltaX + deltaY * deltaY;
            if (distanceSquared >= nearestDistanceSquared)
                continue;

            nearestIndex = card.Index;
            nearestDistanceSquared = distanceSquared;
        }

        return nearestIndex >= 0 ? nearestIndex : null;
    }

    internal static FrontendShortcutMutationIntent? TryCreatePointerMoveIntent(
        Guid tileId,
        int sourceIndex,
        int? targetIndex,
        int tileCount,
        bool thresholdPassed)
    {
        if (tileId == Guid.Empty || !thresholdPassed || tileCount <= 0
            || sourceIndex < 0 || sourceIndex >= tileCount
            || targetIndex is not { } target || target < 0 || target >= tileCount
            || sourceIndex == target)
            return null;

        return new FrontendShortcutMutationIntent(
            FrontendShortcutMutationKind.Move,
            TileId: tileId,
            TargetIndex: target);
    }

    private void ShortcutTile_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border surface || surface.Tag is not Guid tileId
            || !_active || !_editorAvailable || _operationInProgress || _refreshInProgress || _dialogOpen
            || IsPointerSourceInsideButton(e.OriginalSource as DependencyObject, surface))
            return;

        var pointerPoint = e.GetCurrentPoint(ShortcutList);
        var isMouse = e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse;
        if (isMouse ? !pointerPoint.Properties.IsLeftButtonPressed : !e.Pointer.IsInContact)
            return;

        var sourceIndex = IndexOfTile(_tiles, tileId);
        if (sourceIndex < 0)
            return;

        if (!isMouse)
            surface.CancelDirectManipulations();

        if (!surface.CapturePointer(e.Pointer))
        {
            LogShortcutReorderReleased(tileId, sourceIndex, null, "Unavailable");
            return;
        }

        _reorderPointer = e.Pointer;
        _reorderSurface = surface;
        _reorderTileId = tileId;
        _reorderSourceIndex = sourceIndex;
        _reorderStartPosition = pointerPoint.Position;
        _reorderThresholdPassed = false;
        _reorderTargetIndex = sourceIndex;
        AppLog.Debug("Shortcut", "Shortcut pointer reorder armed.",
            ("TileId", tileId), ("PointerDeviceType", e.Pointer.PointerDeviceType.ToString()),
            ("SourceIndex", sourceIndex));
        e.Handled = true;
    }

    private void ShortcutTile_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!IsActiveShortcutReorderPointer(e.Pointer))
            return;

        if (!_active || !_editorAvailable || _operationInProgress || _refreshInProgress || _dialogOpen)
        {
            ClearShortcutReorder("Unavailable", releaseCapture: true);
            return;
        }

        var position = e.GetCurrentPoint(ShortcutList).Position;
        if (!_reorderThresholdPassed && !HasPassedShortcutReorderThreshold(_reorderStartPosition, position))
            return;

        _reorderThresholdPassed = true;
        _reorderTargetIndex = ResolveCurrentShortcutDropIndex(position);
        e.Handled = true;
    }

    private async void ShortcutTile_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!IsActiveShortcutReorderPointer(e.Pointer))
            return;

        var tileId = _reorderTileId!.Value;
        var sourceIndex = _reorderSourceIndex;
        var position = e.GetCurrentPoint(ShortcutList).Position;
        var dropBounds = GetShortcutListBounds();
        var targetIndex = (int?)null;
        var outcome = "Unavailable";
        FrontendShortcutMutationIntent? intent = null;

        if (!dropBounds.Contains(position))
        {
            outcome = "OutsideGrid";
        }
        else if (!_active || !_editorAvailable || _operationInProgress || _refreshInProgress || _dialogOpen)
        {
            outcome = "Unavailable";
        }
        else if (!_reorderThresholdPassed)
        {
            outcome = "BelowThreshold";
        }
        else
        {
            targetIndex = ResolveCurrentShortcutDropIndex(position);
            if (targetIndex is null)
            {
                outcome = "Unavailable";
            }
            else if (sourceIndex == targetIndex.Value)
            {
                outcome = "SameCard";
            }
            else
            {
                intent = TryCreatePointerMoveIntent(tileId, sourceIndex, targetIndex, _tiles.Count,
                    thresholdPassed: true);
                outcome = intent is null ? "Unavailable" : "MoveRequested";
            }
        }

        _reorderTargetIndex = targetIndex;
        e.Handled = true;
        ClearShortcutReorder(outcome, releaseCapture: true);

        if (intent is not null)
            await ApplyMutationAsync(intent);
    }

    private void ShortcutTile_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (IsActiveShortcutReorderPointer(e.Pointer))
            ClearShortcutReorder("Canceled", releaseCapture: false);
    }

    private void ShortcutTile_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (IsActiveShortcutReorderPointer(e.Pointer))
            ClearShortcutReorder("CaptureLost", releaseCapture: false);
    }

    private void ShortcutPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _active = false;
        ClearShortcutReorder("Canceled", releaseCapture: true);
    }

    private bool IsActiveShortcutReorderPointer(Pointer pointer) =>
        _reorderPointer is { } activePointer && activePointer.PointerId == pointer.PointerId;

    private int? ResolveCurrentShortcutDropIndex(Point position)
    {
        var bounds = GetShortcutListBounds();
        if (!_editorAvailable || bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        var realizedCards = new List<(int Index, Rect Bounds)>();
        for (var index = 0; index < _tiles.Count; index++)
        {
            if (ShortcutList.ContainerFromIndex(index) is not ListViewItem item
                || item.ActualWidth <= 0 || item.ActualHeight <= 0)
                continue;

            try
            {
                var cardBounds = item.TransformToVisual(ShortcutList)
                    .TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
                realizedCards.Add((index, cardBounds));
            }
            catch (InvalidOperationException)
            {
                // A virtualized container can be detached while the list is relayouting.
            }
        }

        return ResolveShortcutDropIndex(position, realizedCards, bounds);
    }

    private Rect GetShortcutListBounds() =>
        ShortcutList.ActualWidth > 0 && ShortcutList.ActualHeight > 0
            ? new Rect(0, 0, ShortcutList.ActualWidth, ShortcutList.ActualHeight)
            : new Rect(0, 0, 0, 0);

    private static bool IsPointerSourceInsideButton(DependencyObject? source, DependencyObject surface)
    {
        while (source is not null && !ReferenceEquals(source, surface))
        {
            if (source is Button)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void ClearShortcutReorder(string outcome, bool releaseCapture)
    {
        if (_reorderPointer is not { } pointer || _reorderTileId is not { } tileId)
            return;

        var surface = _reorderSurface;
        var sourceIndex = _reorderSourceIndex;
        var targetIndex = _reorderTargetIndex;
        _reorderPointer = null;
        _reorderSurface = null;
        _reorderTileId = null;
        _reorderSourceIndex = -1;
        _reorderStartPosition = default;
        _reorderThresholdPassed = false;
        _reorderTargetIndex = null;

        if (releaseCapture)
            surface?.ReleasePointerCapture(pointer);

        LogShortcutReorderReleased(tileId, sourceIndex, targetIndex, outcome);
    }

    private static void LogShortcutReorderReleased(Guid tileId, int sourceIndex, int? targetIndex, string outcome) =>
        AppLog.Debug("Shortcut", "Shortcut pointer reorder released.",
            ("TileId", tileId), ("SourceIndex", sourceIndex), ("TargetIndex", targetIndex), ("Outcome", outcome));

    private static bool IsFinite(double value) => double.IsFinite(value);

    private static bool IsUsableBounds(Rect bounds) =>
        IsFinite(bounds.X) && IsFinite(bounds.Y) && IsFinite(bounds.Width) && IsFinite(bounds.Height)
        && bounds.Width > 0 && bounds.Height > 0;

    private static bool Intersects(Rect first, Rect second) =>
        first.Left < second.Right && first.Right > second.Left
        && first.Top < second.Bottom && first.Bottom > second.Top;

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } nested) return nested;
        }

        return null;
    }

    private async Task BrowseForExecutableAsync(TextBox pathBox)
    {
        try
        {
            var hwnd = _windowHandleProvider?.Invoke() ?? 0;
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var picker = new FileOpenPicker(windowId) { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add(".exe");
            var file = await picker.PickSingleFileAsync();
            if (file is not null) pathBox.Text = file.Path;
        }
        catch
        {
            ShowMessage("The application picker could not be opened.", InfoBarSeverity.Error);
        }
    }

    private async void DeleteTileButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Guid tileId || _frontend is null || XamlRoot is null) return;
        var tile = _tiles.FirstOrDefault(candidate => candidate.TileId == tileId);
        if (tile is null) return;
        var dialog = new ContentDialog
        {
            Title = "Delete Shortcut?",
            Content = $"Delete ‘{tile.Title}’? This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        _dialogOpen = true;
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        finally { _dialogOpen = false; }
        if (result == ContentDialogResult.Primary)
            await ApplyMutationAsync(new FrontendShortcutMutationIntent(FrontendShortcutMutationKind.Delete, tileId));
    }

    private async Task ApplyMutationAsync(FrontendShortcutMutationIntent intent)
    {
        if (_frontend is null || _operationInProgress) return;
        if (!FrontendShortcutEditorPayloadPolicy.IsMutationWithinLimit(intent))
        {
            ShowMessage("One or more fields exceed the supported Shortcut editor size.", InfoBarSeverity.Warning);
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _frontend.MutateShortcutAsync(intent);
            if (intent.Kind == FrontendShortcutMutationKind.Move)
            {
                var fields = new (string Key, object? Value)[]
                {
                    ("TileId", intent.TileId), ("TargetIndex", intent.TargetIndex),
                    ("Outcome", result.Succeeded ? "Succeeded" : "Rejected"),
                    ("FailureCategory", result.Succeeded ? "None" : "RuntimeRejected"),
                    ("OrderedTileCount", result.Snapshot.Tiles.Count)
                };
                if (result.Succeeded)
                    AppLog.Debug("Shortcut", "Runtime Shortcut move response received.", fields);
                else
                    AppLog.Warn("Shortcut", "Runtime rejected the Shortcut move request.", null, fields);
            }
            Render(result.Snapshot);
            if (!result.Succeeded)
                ShowMessage(result.FailureMessage ?? "Shortcut changes could not be saved.", InfoBarSeverity.Error);
            else
                ShortcutInfoBar.IsOpen = false;
        }
        catch
        {
            if (intent.Kind == FrontendShortcutMutationKind.Move)
                AppLog.Warn("Shortcut", "Runtime Shortcut move request failed.", null,
                    ("Stage", "RuntimeMutation"), ("TileId", intent.TileId),
                    ("TargetIndex", intent.TargetIndex), ("Outcome", "Exception"));
            ShowMessage("Shortcut changes could not be saved.", InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshIfActiveAsync()
    {
        if (_active) await CaptureAndRenderAsync();
    }

    private static string ResolveScreenshotFolder(string? configuredFolder)
    {
        if (!string.IsNullOrWhiteSpace(configuredFolder)) return configuredFolder;
        var pictures = Environment.GetFolderPath(
            Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolderOption.DoNotVerify);
        return string.IsNullOrWhiteSpace(pictures) ? string.Empty : Path.Combine(pictures, "Screenshots");
    }

    private void SetBusy(bool busy)
    {
        if (busy)
            ClearShortcutReorder("Unavailable", releaseCapture: true);

        _operationInProgress = busy;
        AddShortcutButton.IsEnabled = !busy && _editorAvailable;
        ShortcutList.IsEnabled = !busy && _editorAvailable && _tiles.Count > 0;
    }

    private void ShowMessage(string message, InfoBarSeverity severity)
    {
        ShortcutInfoBar.Message = message;
        ShortcutInfoBar.Severity = severity;
        ShortcutInfoBar.IsOpen = true;
    }
}
