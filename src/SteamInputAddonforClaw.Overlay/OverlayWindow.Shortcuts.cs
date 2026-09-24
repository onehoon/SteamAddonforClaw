using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Overlay.Diagnostics;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow
{
    private FrontendShortcutDashboardSnapshot _shortcutSnapshot =
        FrontendShortcutDashboardSnapshot.Unavailable("Loading shortcuts...");
    private readonly Dictionary<Guid, Border> _shortcutTiles = new();
    private Grid? _shortcutGrid;
    private TextBlock? _shortcutStatus;
    private bool _shortcutExecutionInFlight;
    private bool _isVisible;
    private string? _shortcutFeedbackMessage;

    internal event Func<Guid, Task>? ShortcutExecutionRequested;

    private FrameworkElement BuildShortcutPage()
    {
        var page = new StackPanel { Spacing = 8 };
        _shortcutStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Visibility = Visibility.Collapsed,
        };
        if (Application.Current.Resources.TryGetValue("BodyTextBlockStyle", out var style) && style is Style bodyStyle)
            _shortcutStatus.Style = bodyStyle;
        page.Children.Add(_shortcutStatus);

        _shortcutGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        page.Children.Add(_shortcutGrid);
        RenderShortcutSnapshot();
        return page;
    }

    internal void ApplyShortcutState(FrontendShortcutDashboardSnapshot snapshot)
    {
        if (!IsShortcutSnapshotValid(snapshot))
        {
            OverlayLog.Warn("Shortcut", "Ignored a malformed Shortcut dashboard snapshot.");
            return;
        }
        _shortcutFeedbackMessage = null;
        RefreshShortcutSelection(snapshot);
    }

    internal void ApplyShortcutExecutionResult(OverlayShortcutExecuteResponse response)
    {
        if (!IsShortcutSnapshotValid(response.Snapshot))
        {
            ApplyShortcutExecutionFailure();
            return;
        }
        _shortcutFeedbackMessage = null;
        RefreshShortcutSelection(response.Snapshot);
        if (!response.Succeeded && _isVisible)
            _shortcutFeedbackMessage = response.FailureMessage ?? "Shortcut could not be executed.";
        UpdateShortcutMessage();
    }

    internal void ApplyShortcutExecutionFailure()
    {
        if (_isVisible)
            _shortcutFeedbackMessage = "Shortcut could not be executed.";
        UpdateShortcutMessage();
    }

    private void ResetShortcutForShow()
    {
        _shortcutFeedbackMessage = null;
        RefreshShortcutSelection(FrontendShortcutDashboardSnapshot.Unavailable("Loading shortcuts..."));
    }

    private void RenderShortcutSnapshot()
    {
        if (_shortcutGrid is null || _shortcutStatus is null) return;

        _shortcutTiles.Clear();
        _shortcutGrid.Children.Clear();
        _shortcutGrid.RowDefinitions.Clear();
        _shortcutGrid.ColumnDefinitions.Clear();

        if (!_shortcutSnapshot.Available || _shortcutSnapshot.Tiles.Count == 0)
        {
            UpdateShortcutMessage();
            return;
        }

        UpdateShortcutMessage();
        var columnCount = Math.Min(2, _shortcutSnapshot.Tiles.Count);
        for (var column = 0; column < columnCount; column++)
            _shortcutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var index = 0; index < _shortcutSnapshot.Tiles.Count; index++)
        {
            if (index % columnCount == 0)
                _shortcutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tile = _shortcutSnapshot.Tiles[index];
            var content = new StackPanel { Spacing = 3 };
            var title = new TextBlock
            {
                Text = tile.Title,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 17,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            };
            content.Children.Add(title);
            if (tile.StatusText is { } statusText)
            {
                content.Children.Add(new TextBlock
                {
                    Text = statusText,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7,
                });
            }

            var border = new Border
            {
                Child = content,
                Padding = new Thickness(14, 13, 14, 13),
                MinHeight = 76,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                BorderBrush = RowUnselectedBrush,
                Background = ResolveShortcutTileBackground(),
                Opacity = tile.Enabled ? 1.0 : 0.48,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsHitTestVisible = true,
            };
            border.Tapped += (_, _) => OnShortcutTileTapped(tile);
            Grid.SetRow(border, index / columnCount);
            Grid.SetColumn(border, index % columnCount);
            _shortcutGrid.Children.Add(border);
            _shortcutTiles[tile.TileId] = border;
        }

        ApplyShortcutSelectionVisual();
    }

    private static Brush ResolveShortcutTileBackground() =>
        Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var fill)
            && fill is Brush brush
                ? brush
                : new SolidColorBrush(Microsoft.UI.Colors.LightGray);

    private void OnShortcutTileTapped(FrontendShortcutTile tile)
    {
        SelectShortcutTile(tile.TileId, "Pointer");
        if (tile.Enabled)
            RequestShortcutExecution(tile.TileId);
    }

    private void RequestShortcutExecution(Guid tileId)
    {
        var request = ShortcutExecutionRequested;
        if (!_isVisible || _shortcutExecutionInFlight || request is null)
            return;

        var tile = _shortcutSnapshot.Tiles.FirstOrDefault(item => item.TileId == tileId);
        if (tile is not { Enabled: true }) return;

        _shortcutExecutionInFlight = true;
        _shortcutFeedbackMessage = null;
        UpdateShortcutMessage();
        _ = ExecuteShortcutIntentAsync(request, tileId);
    }

    private async Task ExecuteShortcutIntentAsync(Func<Guid, Task> request, Guid tileId)
    {
        try { await request(tileId); }
        catch (Exception exception)
        {
            OverlayLog.Warn("Shortcut", "Shortcut execution request failed.", null,
                ("ExceptionType", exception.GetType().Name));
            ApplyShortcutExecutionFailure();
        }
        finally
        {
            _shortcutExecutionInFlight = false;
            UpdateShortcutMessage();
        }
    }

    private void RequestSelectedShortcutExecution()
    {
        if (GetSelectedShortcutTile() is { Enabled: true } tile)
            RequestShortcutExecution(tile.TileId);
    }

    private void UpdateShortcutMessage()
    {
        if (_shortcutStatus is null) return;
        var message = _shortcutFeedbackMessage
            ?? (_shortcutExecutionInFlight ? "Running shortcut..."
                : !_shortcutSnapshot.Available ? _shortcutSnapshot.FailureMessage ?? "Shortcut settings are unavailable."
                : _shortcutSnapshot.Tiles.Count == 0 ? "No shortcuts configured. Add shortcuts in the Main App."
                : null);
        _shortcutStatus.Text = message ?? string.Empty;
        _shortcutStatus.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool IsShortcutSnapshotValid(FrontendShortcutDashboardSnapshot? snapshot) =>
        snapshot is { Tiles: not null }
        && snapshot.Tiles.All(tile => tile is not null && tile.TileId != Guid.Empty)
        && snapshot.Tiles.Select(tile => tile.TileId).Distinct().Count() == snapshot.Tiles.Count;
}
