using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Overlay.Diagnostics;

namespace SteamInputAddonforClaw.Overlay;

public sealed partial class OverlayWindow
{
    private const int ShortcutVisualColumnCount = 3;
    private FrontendShortcutDashboardSnapshot _shortcutSnapshot =
        FrontendShortcutDashboardSnapshot.Unavailable();
    private readonly Dictionary<Guid, Border> _shortcutTiles = new();
    private Grid? _shortcutGrid;
    private bool _shortcutExecutionInFlight;
    private bool _isVisible;

    internal event Func<Guid, Task>? ShortcutExecutionRequested;

    private FrameworkElement BuildShortcutPage()
    {
        _shortcutGrid = new Grid
        {
            ColumnSpacing = OverlayQamResources.Get("QamTileSpacing", 8.0),
            RowSpacing = OverlayQamResources.Get("QamTileSpacing", 8.0),
        };
        RenderShortcutSnapshot();
        return _shortcutGrid;
    }

    internal void ApplyShortcutState(FrontendShortcutDashboardSnapshot snapshot)
    {
        if (!IsShortcutSnapshotValid(snapshot))
        {
            OverlayLog.Warn("Shortcut", "Ignored a malformed Shortcut dashboard snapshot.");
            return;
        }
        RefreshShortcutSelection(snapshot);
    }

    internal void ApplyShortcutExecutionResult(OverlayShortcutExecuteResponse response)
    {
        if (!IsShortcutSnapshotValid(response.Snapshot))
        {
            ApplyShortcutExecutionFailure();
            return;
        }
        RefreshShortcutSelection(response.Snapshot);
    }

    internal void ApplyShortcutExecutionFailure()
    {
        // The calling transport path records failures; this surface intentionally renders no status text.
    }

    private void ResetShortcutForShow()
    {
        RefreshShortcutSelection(FrontendShortcutDashboardSnapshot.Unavailable());
    }

    private void RenderShortcutSnapshot()
    {
        if (_shortcutGrid is null) return;

        _shortcutTiles.Clear();
        _shortcutGrid.Children.Clear();
        _shortcutGrid.RowDefinitions.Clear();
        _shortcutGrid.ColumnDefinitions.Clear();

        if (!_shortcutSnapshot.Available || _shortcutSnapshot.Tiles.Count == 0)
            return;
        for (var column = 0; column < ShortcutVisualColumnCount; column++)
            _shortcutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var index = 0; index < _shortcutSnapshot.Tiles.Count; index++)
        {
            if (index % ShortcutVisualColumnCount == 0)
                _shortcutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tile = _shortcutSnapshot.Tiles[index];
            var tilePadding = OverlayQamResources.Get("QamTilePadding", new Thickness(12));
            var tileSize = OverlayQamResources.Get("QamShortcutTileSize", 117.33333333333333);
            var contentWidth = Math.Max(1, tileSize - tilePadding.Left - tilePadding.Right);
            var hasStatus = !string.IsNullOrWhiteSpace(tile.StatusText);
            var title = new TextBlock
            {
                Text = tile.Title,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = contentWidth,
                MaxLines = hasStatus ? 1 : 2,
            };
            OverlayQamResources.ApplyTextStyle(title, "QamTileTitleTextStyle");

            var header = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = contentWidth,
                Spacing = hasStatus ? 4 : 6,
            };
            header.Children.Add(new FontIcon
            {
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
                Glyph = tile.IconGlyph,
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            header.Children.Add(title);

            var content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = hasStatus ? 4 : 0,
            };
            content.Children.Add(header);
            if (hasStatus)
            {
                var status = new TextBlock
                {
                    Text = tile.StatusText,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    MaxWidth = contentWidth,
                    MaxLines = 2,
                };
                OverlayQamResources.ApplyTextStyle(status, "QamCaptionTextStyle");
                content.Children.Add(status);
            }

            var border = new Border
            {
                Child = content,
                Padding = tilePadding,
                Width = tileSize,
                Height = tileSize,
                CornerRadius = OverlayQamResources.Get("QamTileCornerRadius", new CornerRadius(2)),
                BorderThickness = OverlayQamResources.Get("QamSelectionBorderThickness", new Thickness(0)),
                BorderBrush = OverlayQamResources.Brush("QamFocusBorderBrush"),
                Background = OverlayQamResources.Brush("QamTileBrush"),
                Opacity = tile.Enabled ? 1.0 : OverlayQamResources.Get("QamDisabledOpacity", 0.48),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsHitTestVisible = true,
            };
            border.Tapped += (_, _) => OnShortcutTileTapped(tile);
            Grid.SetRow(border, index / ShortcutVisualColumnCount);
            Grid.SetColumn(border, index % ShortcutVisualColumnCount);
            _shortcutGrid.Children.Add(border);
            _shortcutTiles[tile.TileId] = border;
        }

        ApplyShortcutSelectionVisual();
    }

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
        }
    }

    private void RequestSelectedShortcutExecution()
    {
        if (GetSelectedShortcutTile() is { Enabled: true } tile)
            RequestShortcutExecution(tile.TileId);
    }

    private static bool IsShortcutSnapshotValid(FrontendShortcutDashboardSnapshot? snapshot) =>
        snapshot is { Tiles: not null }
        && snapshot.Tiles.All(tile => tile is not null && tile.TileId != Guid.Empty)
        && snapshot.Tiles.Select(tile => tile.TileId).Distinct().Count() == snapshot.Tiles.Count;
}
