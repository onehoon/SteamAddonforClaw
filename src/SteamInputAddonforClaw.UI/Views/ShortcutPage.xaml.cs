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
    internal readonly record struct ShortcutItemBounds(int Index, double Left, double Top, double Width, double Height);

    private const int ShortcutColumnCount = 3;
    private const double ShortcutCardHorizontalGap = 12;
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
    private Guid? _draggedShortcutTileId;
    private uint? _dragPointerId;
    private int _dragStartIndex;
    private int _dragTargetIndex;

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

    public void Deactivate() => _active = false;

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
        var executablePanel = new StackPanel { Spacing = 8, Children = { executablePath, executableBrowse, executableArguments } };

        var script = new TextBox
        {
            Header = "Script",
            PlaceholderText = "Enter a PowerShell script",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 180,
            Text = existing?.Action.PowerShellScript ?? string.Empty
        };
        var scriptPanel = new StackPanel { Spacing = 8, Children = { script } };

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
            FrontendShortcutEditorActionKind.Executable => new FrontendShortcutActionInput(selectedKind, executablePath.Text, executableArguments.Text),
            FrontendShortcutEditorActionKind.PowerShell => new FrontendShortcutActionInput(selectedKind, PowerShellScript: script.Text),
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

    private void ShortcutDragHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_operationInProgress || _refreshInProgress || _draggedShortcutTileId is not null
            || sender is not FrameworkElement handle
            || handle.Tag is not Guid tileId)
            return;

        var startIndex = -1;
        for (var index = 0; index < _tiles.Count; index++)
        {
            if (_tiles[index].TileId != tileId) continue;
            startIndex = index;
            break;
        }

        if (startIndex < 0 || !handle.CapturePointer(e.Pointer)) return;

        _draggedShortcutTileId = tileId;
        _dragPointerId = e.Pointer.PointerId;
        _dragStartIndex = startIndex;
        _dragTargetIndex = startIndex;
        e.Handled = true;
    }

    private void ShortcutDragHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!OwnsShortcutDragPointer(e)) return;

        var position = e.GetCurrentPoint(ShortcutList).Position;
        if (ResolveShortcutDropTargetIndex(CaptureRealizedShortcutItemBounds(), position.X, position.Y, _tiles.Count, _dragStartIndex)
            is { } targetIndex)
            _dragTargetIndex = targetIndex;

        e.Handled = true;
    }

    private async void ShortcutDragHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!OwnsShortcutDragPointer(e)) return;

        var position = e.GetCurrentPoint(ShortcutList).Position;
        var targetIndex = ResolveShortcutDropTargetIndex(
            CaptureRealizedShortcutItemBounds(), position.X, position.Y, _tiles.Count, _dragStartIndex) ?? _dragTargetIndex;
        var tileId = _draggedShortcutTileId;
        var pointerId = _dragPointerId;
        ClearShortcutDrag();
        if (pointerId == e.Pointer.PointerId && sender is UIElement handle)
            handle.ReleasePointerCapture(e.Pointer);
        e.Handled = true;

        if (tileId is { } movedTileId && _tiles.Any(tile => tile.TileId == movedTileId))
            await ApplyMutationAsync(new FrontendShortcutMutationIntent(
                FrontendShortcutMutationKind.Move,
                TileId: movedTileId,
                TargetIndex: targetIndex));
    }

    private void ShortcutDragHandle_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (!OwnsShortcutDragPointer(e)) return;

        ClearShortcutDrag();
        if (sender is UIElement handle) handle.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void ShortcutDragHandle_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (OwnsShortcutDragPointer(e)) ClearShortcutDrag();
    }

    private bool OwnsShortcutDragPointer(PointerRoutedEventArgs e) =>
        _draggedShortcutTileId is not null && _dragPointerId == e.Pointer.PointerId;

    private void ClearShortcutDrag()
    {
        _draggedShortcutTileId = null;
        _dragPointerId = null;
        _dragStartIndex = 0;
        _dragTargetIndex = 0;
    }

    private IReadOnlyList<ShortcutItemBounds> CaptureRealizedShortcutItemBounds()
    {
        var bounds = new List<ShortcutItemBounds>();
        for (var index = 0; index < _tiles.Count; index++)
        {
            if (ShortcutList.ContainerFromIndex(index) is not ListViewItem container
                || container.ActualWidth <= 0 || container.ActualHeight <= 0)
                continue;

            var origin = container.TransformToVisual(ShortcutList).TransformPoint(new Point(0, 0));
            bounds.Add(new ShortcutItemBounds(index, origin.X, origin.Y, container.ActualWidth, container.ActualHeight));
        }

        return bounds;
    }

    internal static int? ResolveShortcutDropTargetIndex(
        IReadOnlyList<ShortcutItemBounds> realizedItems,
        double pointerX,
        double pointerY,
        int itemCount,
        int draggedItemIndex)
    {
        if (itemCount <= 0 || draggedItemIndex < 0 || draggedItemIndex >= itemCount
            || !double.IsFinite(pointerX) || !double.IsFinite(pointerY))
            return null;

        var items = realizedItems
            .Where(item => item.Index >= 0 && item.Index < itemCount
                && double.IsFinite(item.Left) && double.IsFinite(item.Top)
                && double.IsFinite(item.Width) && double.IsFinite(item.Height)
                && item.Width > 0 && item.Height > 0)
            .OrderBy(item => item.Top)
            .ThenBy(item => item.Left)
            .ToArray();
        if (items.Length == 0) return null;

        var rows = new List<List<ShortcutItemBounds>>();
        foreach (var item in items)
        {
            if (rows.Count == 0 || item.Top >= rows[^1].Max(rowItem => rowItem.Top + rowItem.Height))
                rows.Add([item]);
            else
                rows[^1].Add(item);
        }

        var insertionIndex = items[^1].Index + 1;
        List<ShortcutItemBounds>? previousRow = null;
        foreach (var row in rows)
        {
            var rowTop = row.Min(item => item.Top);
            var rowBottom = row.Max(item => item.Top + item.Height);
            if (pointerY < rowTop)
            {
                insertionIndex = previousRow is null
                    ? row.Min(item => item.Index)
                    : previousRow.Max(item => item.Index) + 1;
                break;
            }

            if (pointerY <= rowBottom)
            {
                var itemBeforePointer = row
                    .OrderBy(item => item.Left)
                    .FirstOrDefault(item => pointerX < item.Left + item.Width / 2);
                insertionIndex = itemBeforePointer.Width > 0
                    ? itemBeforePointer.Index
                    : row.Max(item => item.Index) + 1;
                break;
            }

            previousRow = row;
        }

        var targetIndex = insertionIndex > draggedItemIndex ? insertionIndex - 1 : insertionIndex;
        return Math.Clamp(targetIndex, 0, itemCount - 1);
    }

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
            Render(result.Snapshot);
            if (!result.Succeeded)
                ShowMessage(result.FailureMessage ?? "Shortcut changes could not be saved.", InfoBarSeverity.Error);
            else
                ShortcutInfoBar.IsOpen = false;
        }
        catch
        {
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
