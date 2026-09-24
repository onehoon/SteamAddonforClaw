using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using SteamInputAddonforClaw.Contracts.Frontend;
using WinRT.Interop;
using Windows.ApplicationModel.DataTransfer;

namespace SteamInputAddonforClaw.Views;

public sealed partial class ShortcutPage : UserControl
{
    private readonly ObservableCollection<FrontendShortcutEditorTile> _tiles = [];
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private IAddonFrontendControl? _frontend;
    private Func<nint>? _windowHandleProvider;
    private FrontendScreenshotFolderSnapshot? _screenshotFolder;
    private bool _active;
    private bool _operationInProgress;
    private bool _dialogOpen;
    private bool _refreshInProgress;
    private bool _editorAvailable;

    public ShortcutPage()
    {
        InitializeComponent();
        ShortcutList.ItemsSource = _tiles;
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
        _screenshotFolder = snapshot.ScreenshotFolder;
        ScreenshotFolderPathText.Text = snapshot.ScreenshotFolder.EffectiveFolder;
        ScreenshotFolderModeText.Text = snapshot.ScreenshotFolder.UsingDefault ? "Using the default folder" : "Custom folder";
        ShortcutEmptyState.Visibility = snapshot.Available && _tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShortcutList.Visibility = snapshot.Available && _tiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AddShortcutButton.IsEnabled = snapshot.Available && !_operationInProgress;
        ShortcutList.IsEnabled = snapshot.Available && _tiles.Count > 0 && !_operationInProgress;
        UseDefaultFolderButton.IsEnabled = !snapshot.ScreenshotFolder.UsingDefault && !_operationInProgress;
        BrowseScreenshotFolderButton.IsEnabled = !_operationInProgress;
        OpenScreenshotFolderButton.IsEnabled = !_operationInProgress;

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
        if (_frontend is null || _operationInProgress || XamlRoot is null) return;
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
        AddActionChoice(actionPicker, "Screenshot", FrontendShortcutEditorActionKind.ScreenshotFullscreen);

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
        var screenshotPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Captures the primary display as JPEG.", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "The save folder is configured on this Shortcut page.", Opacity = 0.7, TextWrapping = TextWrapping.Wrap }
            }
        };

        var panels = new Dictionary<FrontendShortcutEditorActionKind, UIElement>
        {
            [FrontendShortcutEditorActionKind.Executable] = executablePanel,
            [FrontendShortcutEditorActionKind.PowerShell] = scriptPanel,
            [FrontendShortcutEditorActionKind.Url] = urlPanel,
            [FrontendShortcutEditorActionKind.ScreenshotFullscreen] = screenshotPanel
        };

        actionPicker.SelectionChanged += (_, _) => UpdateEditorPanel(actionPicker, panels);
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
            Children = { titleBox, actionPicker, executablePanel, scriptPanel, urlPanel, screenshotPanel }
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
            FrontendShortcutEditorActionKind.ScreenshotFullscreen => new FrontendShortcutActionInput(selectedKind),
            _ => new FrontendShortcutActionInput(FrontendShortcutEditorActionKind.Unsupported)
        };
        var intent = new FrontendShortcutMutationIntent(
            existing is null ? FrontendShortcutMutationKind.Create : FrontendShortcutMutationKind.Update,
            existing?.TileId,
            titleBox.Text,
            action);
        await ApplyMutationAsync(intent);
    }

    private static void AddActionChoice(ComboBox picker, string label, FrontendShortcutEditorActionKind kind) =>
        picker.Items.Add(new ComboBoxItem { Content = label, Tag = kind });

    private static void UpdateEditorPanel(ComboBox picker, IReadOnlyDictionary<FrontendShortcutEditorActionKind, UIElement> panels)
    {
        foreach (var panel in panels.Values) panel.Visibility = Visibility.Collapsed;
        if ((picker.SelectedItem as ComboBoxItem)?.Tag is FrontendShortcutEditorActionKind kind
            && panels.TryGetValue(kind, out var selectedPanel))
            selectedPanel.Visibility = Visibility.Visible;
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

    private async void ShortcutList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.DropResult != DataPackageOperation.Move || args.Items.FirstOrDefault() is not FrontendShortcutEditorTile movedTile)
        {
            if (_screenshotFolder is not null) await RefreshIfActiveAsync();
            return;
        }

        var targetIndex = _tiles.IndexOf(movedTile);
        if (targetIndex < 0) return;
        await ApplyMutationAsync(new FrontendShortcutMutationIntent(
            FrontendShortcutMutationKind.Move,
            TileId: movedTile.TileId,
            TargetIndex: targetIndex));
    }

    private async Task RefreshIfActiveAsync()
    {
        if (_active) await CaptureAndRenderAsync();
    }

    private async void BrowseScreenshotFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_frontend is null || _operationInProgress) return;
        try
        {
            _dialogOpen = true;
            var hwnd = _windowHandleProvider?.Invoke() ?? 0;
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var picker = new FolderPicker(windowId);
            var folder = await picker.PickSingleFolderAsync();
            _dialogOpen = false;
            if (folder is not null) await SetScreenshotFolderAsync(folder.Path);
        }
        catch
        {
            ShowMessage("The folder picker could not be opened.", InfoBarSeverity.Error);
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private async void UseDefaultFolderButton_Click(object sender, RoutedEventArgs e) =>
        await SetScreenshotFolderAsync(null);

    private async Task SetScreenshotFolderAsync(string? folder)
    {
        if (_frontend is null || _operationInProgress) return;
        if (!FrontendShortcutEditorPayloadPolicy.IsScreenshotFolderRequestWithinLimit(folder))
        {
            ShowMessage("Screenshot folder path exceeds the supported size.", InfoBarSeverity.Warning);
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _frontend.SetScreenshotSaveFolderAsync(folder);
            _screenshotFolder = result.Snapshot;
            ScreenshotFolderPathText.Text = result.Snapshot.EffectiveFolder;
            ScreenshotFolderModeText.Text = result.Snapshot.UsingDefault ? "Using the default folder" : "Custom folder";
            UseDefaultFolderButton.IsEnabled = !result.Snapshot.UsingDefault;
            if (!result.Succeeded)
                ShowMessage(result.FailureMessage ?? "Screenshot folder could not be saved.", InfoBarSeverity.Error);
            else
                ShortcutInfoBar.IsOpen = false;
        }
        catch
        {
            ShowMessage("Screenshot folder could not be saved.", InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenScreenshotFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = _screenshotFolder?.EffectiveFolder;
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
    }

    private void SetBusy(bool busy)
    {
        _operationInProgress = busy;
        AddShortcutButton.IsEnabled = !busy && _editorAvailable;
        ShortcutList.IsEnabled = !busy && _editorAvailable && _tiles.Count > 0;
        BrowseScreenshotFolderButton.IsEnabled = !busy;
        OpenScreenshotFolderButton.IsEnabled = !busy;
        UseDefaultFolderButton.IsEnabled = !busy && _screenshotFolder?.UsingDefault == false;
    }

    private void ShowMessage(string message, InfoBarSeverity severity)
    {
        ShortcutInfoBar.Message = message;
        ShortcutInfoBar.Severity = severity;
        ShortcutInfoBar.IsOpen = true;
    }
}
