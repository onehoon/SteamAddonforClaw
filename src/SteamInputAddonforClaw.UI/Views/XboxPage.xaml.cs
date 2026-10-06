using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

public sealed partial class XboxPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private IReadOnlyList<FrontendXboxGameCatalogEntry> _catalog = [];
    private CancellationTokenSource? _scanCancellation;
    private bool _active;

    public XboxPage() => InitializeComponent();

    internal void Initialize(IAddonFrontendControl frontend) => _frontend = frontend;

    internal void Activate()
    {
        if (_active) return;
        _active = true;
        _ = RefreshGamesAsync();
    }

    internal void Deactivate()
    {
        _active = false;
        var scan = _scanCancellation;
        _scanCancellation = null;
        scan?.Cancel();
    }

    private async void RefreshGamesButton_Click(object sender, RoutedEventArgs e)
        => await RefreshGamesAsync();

    private async Task RefreshGamesAsync()
    {
        var frontend = _frontend;
        if (!_active || frontend is null) return;

        var scan = new CancellationTokenSource();
        var previousScan = _scanCancellation;
        _scanCancellation = scan;
        previousScan?.Cancel();

        try
        {
            var snapshot = await frontend.ScanXboxGamesAsync(scan.Token).ConfigureAwait(true);
            if (!IsCurrentScan(_active, _scanCancellation, scan)) return;

            _catalog = snapshot.Games;
            Render(snapshot);
        }
        catch (OperationCanceledException) when (scan.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsCurrentScan(_active, _scanCancellation, scan)) return;
            _catalog = [];
            XboxInfoBar.Severity = InfoBarSeverity.Error;
            XboxInfoBar.Message = "XBOX game catalog could not be loaded.";
            XboxInfoBar.IsOpen = true;
            AppLog.Warn("XboxPage", "The XBOX catalog request failed in the frontend.", exception);
        }
        finally
        {
            if (ReferenceEquals(_scanCancellation, scan)) _scanCancellation = null;
            scan.Dispose();
        }
    }

    private void Render(FrontendXboxGameCatalogSnapshot snapshot)
    {
        switch (snapshot.Outcome)
        {
            case FrontendXboxGameCatalogOutcome.Ready:
                XboxInfoBar.IsOpen = false;
                var visibleGames = ApplyCatalogFilter();
                if (_catalog.Count == 0)
                    ShowInfo("No installed XBOX games were found.");
                else if (visibleGames.Count == 0 && GameSearchBox.Text.Trim().Length > 0)
                    ShowInfo("No matching XBOX games were found.");
                break;
            case FrontendXboxGameCatalogOutcome.Unavailable:
                _catalog = [];
                GameGrid.ItemsSource = Array.Empty<FrontendXboxGameCatalogEntry>();
                XboxInfoBar.Severity = InfoBarSeverity.Warning;
                XboxInfoBar.Message = "XBOX game catalog is unavailable.";
                XboxInfoBar.IsOpen = true;
                break;
            default:
                _catalog = [];
                GameGrid.ItemsSource = Array.Empty<FrontendXboxGameCatalogEntry>();
                XboxInfoBar.Severity = InfoBarSeverity.Error;
                XboxInfoBar.Message = "XBOX game catalog could not be loaded.";
                XboxInfoBar.IsOpen = true;
                break;
        }
    }

    private void GameSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var visibleGames = ApplyCatalogFilter();
        if (_active && _catalog.Count > 0 && visibleGames.Count == 0 && GameSearchBox.Text.Trim().Length > 0)
            ShowInfo("No matching XBOX games were found.");
        else if (_active && _catalog.Count > 0)
            XboxInfoBar.IsOpen = false;
    }

    private IReadOnlyList<FrontendXboxGameCatalogEntry> ApplyCatalogFilter()
    {
        var visibleGames = FilterAndSort(_catalog, GameSearchBox.Text);
        GameGrid.ItemsSource = visibleGames;
        return visibleGames;
    }

    private void ShowInfo(string message)
    {
        XboxInfoBar.Severity = InfoBarSeverity.Informational;
        XboxInfoBar.Message = message;
        XboxInfoBar.IsOpen = true;
    }

    internal static IReadOnlyList<FrontendXboxGameCatalogEntry> FilterAndSort(
        IEnumerable<FrontendXboxGameCatalogEntry> games,
        string? query)
    {
        var search = query?.Trim() ?? string.Empty;
        return games
            .Where(game => search.Length == 0 || game.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(game => game.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(game => game.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static bool IsCurrentScan(bool active, CancellationTokenSource? current, CancellationTokenSource request)
        => active && ReferenceEquals(current, request) && !request.IsCancellationRequested;
}
