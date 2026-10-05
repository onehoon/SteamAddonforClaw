using System.Diagnostics;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;

namespace SteamInputAddonforClaw.Views;

public sealed partial class XboxCatalogDiagnosticPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private CancellationTokenSource? _scanCancellation;
    private string? _reportPath;

    public event EventHandler? BackRequested;

    public XboxCatalogDiagnosticPage() => InitializeComponent();

    internal void Initialize(IAddonFrontendControl frontend) => _frontend = frontend;

    internal void Activate() { }

    internal void Deactivate() => _scanCancellation?.Cancel();

    private void BackButton_Click(object sender, RoutedEventArgs args) => BackRequested?.Invoke(this, EventArgs.Empty);

    private async void ScanButton_Click(object sender, RoutedEventArgs args)
    {
        if (_frontend is null || _scanCancellation is not null)
            return;

        var scanCancellation = new CancellationTokenSource();
        _scanCancellation = scanCancellation;
        ScanButton.IsEnabled = false;
        OpenReportButton.IsEnabled = false;
        _reportPath = null;
        StatusText.Text = "Scanning installed packages once…";
        SummaryText.Text = string.Empty;
        GamesPanel.Children.Clear();
        FailuresPanel.Children.Clear();

        try
        {
            var result = await _frontend.RunXboxCatalogDiagnosticAsync(scanCancellation.Token);
            Render(result);
        }
        catch (OperationCanceledException) when (scanCancellation.IsCancellationRequested)
        {
            StatusText.Text = "Scan cancelled.";
        }
        catch (Exception exception)
        {
            AppLog.Warn("XboxCatalogDiagnostic", "Main UI diagnostic request failed.", exception,
                ("Reason", exception.GetType().Name));
            StatusText.Text = "Scan failed. See the Runtime application log for details.";
        }
        finally
        {
            ScanButton.IsEnabled = true;
            if (ReferenceEquals(_scanCancellation, scanCancellation))
                _scanCancellation = null;
            scanCancellation.Dispose();
        }
    }

    private void Render(FrontendXboxCatalogDiagnosticResult result)
    {
        StatusText.Text = $"{result.Outcome}: {result.Status}";
        SummaryText.Text =
            $"Enumerated packages: {result.EnumeratedPackageCount:N0}\n" +
            $"Accessible package locations: {result.AccessiblePackageCount:N0}\n" +
            $"MicrosoftGame.config candidates: {result.ConfigCandidateCount:N0}\n" +
            $"Recognized/parseable game configs: {result.ParsedConfigCount:N0}\n" +
            $"Valid XBOX/GDK games: {result.ValidGameCount:N0}\n" +
            $"Skipped or failed items: {result.SkippedOrFailedCount:N0}";

        foreach (var game in result.Games)
            GamesPanel.Children.Add(CreateGameCard(game));
        if (result.OmittedGameCount > 0)
            GamesPanel.Children.Add(new TextBlock { Text = $"{result.OmittedGameCount:N0} additional game(s) are included in the report." });

        foreach (var failure in result.Failures)
        {
            FailuresPanel.Children.Add(new TextBlock
            {
                Text = $"{failure.Stage} — {failure.PackageIdentity}\n{failure.Reason}",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.85,
            });
        }
        if (result.OmittedFailureCount > 0)
            FailuresPanel.Children.Add(new TextBlock { Text = $"{result.OmittedFailureCount:N0} additional failure(s) are included in the report." });

        _reportPath = result.ReportPath;
        OpenReportButton.IsEnabled = !string.IsNullOrWhiteSpace(_reportPath);
    }

    private static Border CreateGameCard(FrontendXboxCatalogDiagnosticGame game)
    {
        var details = new StringBuilder();
        details.AppendLine($"Candidate key: {game.CandidateKey}");
        details.AppendLine($"Package full name: {game.PackageFullName}");
        details.AppendLine($"Package family name: {game.PackageFamilyName}");
        details.AppendLine($"StoreId: {ValueOrAbsent(game.StoreId)}    TitleId: {ValueOrAbsent(game.TitleId)}");
        details.AppendLine($"Identity: {game.IdentityName}    Publisher: {game.IdentityPublisher}");
        details.AppendLine($"ResourceId: {ValueOrAbsent(game.IdentityResourceId)}");
        details.AppendLine($"Location: {game.PackageLocationKind} — {game.PackageRoot}");
        details.AppendLine($"MicrosoftGame.config: {game.ConfigPath}");
        details.AppendLine("Executables:");
        foreach (var executable in game.Executables)
            details.AppendLine($"  {executable.Name}; Id={ValueOrAbsent(executable.Id)}; TargetDeviceFamily={ValueOrAbsent(executable.TargetDeviceFamily)}; Architecture={ValueOrAbsent(executable.Architecture)}");
        if (game.OmittedExecutableCount > 0)
            details.AppendLine($"  {game.OmittedExecutableCount:N0} additional executable(s) are included in the report.");

        return new Border
        {
            Padding = new Thickness(16),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = game.DisplayName, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = details.ToString(), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
                },
            },
        };
    }

    private void OpenReportButton_Click(object sender, RoutedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(_reportPath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(_reportPath) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppLog.Warn("XboxCatalogDiagnostic", "Diagnostic report could not be opened.", exception,
                ("Reason", exception.GetType().Name));
            StatusText.Text = "Report could not be opened. You can use the shown path from Explorer.";
        }
    }

    private static string ValueOrAbsent(string? value) => string.IsNullOrWhiteSpace(value) ? "<Absent>" : value;
}
