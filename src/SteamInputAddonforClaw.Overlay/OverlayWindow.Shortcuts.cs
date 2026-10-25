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

    private sealed record ShortcutTileControls(
        Border Border,
        FontIcon Icon,
        TextBlock Title,
        TextBlock Status,
        StackPanel Header,
        StackPanel Content)
    {
        public bool IsSelected { get; set; }
    }

    private FrontendShortcutDashboardSnapshot _shortcutSnapshot =
        FrontendShortcutDashboardSnapshot.Unavailable();
    private readonly Dictionary<Guid, ShortcutTileControls> _shortcutTiles = new();
    private IReadOnlyList<FrontendShortcutTile> _renderedShortcutTiles = [];
    private Guid? _renderedShortcutSelectedTileId;
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
        for (var column = 0; column < ShortcutVisualColumnCount; column++)
            _shortcutGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
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

        IReadOnlyList<FrontendShortcutTile> tiles = _shortcutSnapshot.Available
            ? _shortcutSnapshot.Tiles
            : [];
        var reconciliation = OverlayShortcutTileReconciliation.Between(_renderedShortcutTiles, tiles);
        if (reconciliation.IsUnchanged)
        {
            ApplyShortcutSelectionVisual();
            return;
        }

        foreach (var tileId in reconciliation.RemovedTileIds)
        {
            if (!_shortcutTiles.Remove(tileId, out var controls)) continue;
            _shortcutGrid.Children.Remove(controls.Border);
        }

        var added = reconciliation.AddedTileIds.ToHashSet();
        var updated = reconciliation.UpdatedTileIds.ToHashSet();
        var repositioned = reconciliation.RepositionedTileIds.ToHashSet();
        for (var index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            if (added.Contains(tile.TileId))
            {
                var controls = CreateShortcutTile(tile, index,
                    GetSelectedShortcutTile()?.TileId == tile.TileId);
                _shortcutTiles.Add(tile.TileId, controls);
                _shortcutGrid.Children.Add(controls.Border);
                continue;
            }

            if (!_shortcutTiles.TryGetValue(tile.TileId, out var existing))
                continue;

            if (updated.Contains(tile.TileId))
                UpdateShortcutTile(existing, tile);
            if (repositioned.Contains(tile.TileId))
                UpdateShortcutTilePosition(existing.Border, index);
        }

        var requiredRowCount = (tiles.Count + ShortcutVisualColumnCount - 1) / ShortcutVisualColumnCount;
        while (_shortcutGrid.RowDefinitions.Count < requiredRowCount)
            _shortcutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        while (_shortcutGrid.RowDefinitions.Count > requiredRowCount)
            _shortcutGrid.RowDefinitions.RemoveAt(_shortcutGrid.RowDefinitions.Count - 1);

        _renderedShortcutTiles = tiles.ToArray();
        ApplyShortcutSelectionVisual();
    }

    private ShortcutTileControls CreateShortcutTile(FrontendShortcutTile tile, int index, bool selected)
    {
        var tilePadding = OverlayQamResources.Get("QamTilePadding", new Thickness(12));
        var tileSize = OverlayQamResources.Get("QamShortcutTileSize", 117.33333333333333);
        var hasStatus = !string.IsNullOrWhiteSpace(tile.StatusText);
        var title = new TextBlock
        {
            Text = tile.Title,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxLines = hasStatus ? 1 : 2,
        };
        OverlayQamResources.ApplyTextStyle(title, "QamTileTitleTextStyle");

        var icon = new FontIcon
        {
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
            Glyph = tile.IconGlyph,
            FontSize = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var header = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = hasStatus ? 4 : 6,
        };
        header.Children.Add(icon);
        header.Children.Add(title);

        var status = new TextBlock
        {
            Text = tile.StatusText ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxLines = 2,
            Visibility = hasStatus ? Visibility.Visible : Visibility.Collapsed,
        };
        OverlayQamResources.ApplyTextStyle(status, "QamCaptionTextStyle");

        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = hasStatus ? 4 : 0,
        };
        content.Children.Add(header);
        content.Children.Add(status);

        var border = new Border
        {
            Child = content,
            Padding = tilePadding,
            Height = tileSize,
            CornerRadius = OverlayQamResources.Get("QamTileCornerRadius", new CornerRadius(2)),
            BorderThickness = OverlayQamResources.Get("QamSelectionBorderThickness", new Thickness(0)),
            BorderBrush = _rowSelectedBrush,
            Background = OverlayQamResources.Brush(selected ? "QamTileSelectedBrush" : "QamTileBrush"),
            Opacity = tile.Enabled ? 1.0 : OverlayQamResources.Get("QamDisabledOpacity", 0.48),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsHitTestVisible = true,
        };
        border.Tapped += (_, _) => OnShortcutTileTapped(tile.TileId);
        UpdateShortcutTilePosition(border, index);
        return new(border, icon, title, status, header, content) { IsSelected = selected };
    }

    private static void UpdateShortcutTilePosition(Border border, int index)
    {
        var row = index / ShortcutVisualColumnCount;
        var column = index % ShortcutVisualColumnCount;
        if (Grid.GetRow(border) != row) Grid.SetRow(border, row);
        if (Grid.GetColumn(border) != column) Grid.SetColumn(border, column);
    }

    private static void UpdateShortcutTile(ShortcutTileControls controls, FrontendShortcutTile tile)
    {
        if (controls.Title.Text != tile.Title)
            controls.Title.Text = tile.Title;
        if (controls.Icon.Glyph != tile.IconGlyph)
            controls.Icon.Glyph = tile.IconGlyph;

        var statusText = tile.StatusText ?? string.Empty;
        if (controls.Status.Text != statusText)
            controls.Status.Text = statusText;

        var hasStatus = !string.IsNullOrWhiteSpace(tile.StatusText);
        var visibility = hasStatus ? Visibility.Visible : Visibility.Collapsed;
        if (controls.Status.Visibility != visibility)
            controls.Status.Visibility = visibility;
        var titleMaxLines = hasStatus ? 1 : 2;
        if (controls.Title.MaxLines != titleMaxLines)
            controls.Title.MaxLines = titleMaxLines;
        var headerSpacing = hasStatus ? 4 : 6;
        if (controls.Header.Spacing != headerSpacing)
            controls.Header.Spacing = headerSpacing;
        var contentSpacing = hasStatus ? 4 : 0;
        if (controls.Content.Spacing != contentSpacing)
            controls.Content.Spacing = contentSpacing;

        var opacity = tile.Enabled ? 1.0 : OverlayQamResources.Get("QamDisabledOpacity", 0.48);
        if (controls.Border.Opacity != opacity)
            controls.Border.Opacity = opacity;
    }

    private void OnShortcutTileTapped(Guid tileId)
    {
        SelectShortcutTile(tileId, "Pointer");
        if (_shortcutSnapshot.Tiles.FirstOrDefault(item => item.TileId == tileId) is { Enabled: true })
            RequestShortcutExecution(tileId);
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
