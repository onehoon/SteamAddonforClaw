using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.UI;

namespace SteamInputAddonforClaw.Views;

public sealed partial class MsiUpdatesPage : UserControl
{
    private readonly string[] _categoryTabBaseHeaders;
    private IAddonFrontendControl? _frontend;
    private CancellationTokenSource? _requestCancellation;
    private bool _isActive;
    private bool _msiUpdatesBusy;
    private FrontendMsiLiveUpdateCatalogSnapshot _msiUpdatesSnapshot = CreateNotCheckedSnapshot();

    public MsiUpdatesPage()
    {
        InitializeComponent();
        _categoryTabBaseHeaders =
        [
            ReadHeader(MsiUpdatesBiosTab, "BIOS"),
            ReadHeader(MsiUpdatesFirmwareTab, "Firmware"),
            ReadHeader(MsiUpdatesDriversTab, "Drivers"),
            ReadHeader(MsiUpdatesUtilitiesTab, "Utilities")
        ];
        RenderMsiLiveUpdateCatalog();
    }

    internal void Initialize(IAddonFrontendControl frontend) => _frontend = frontend;

    internal void Activate()
    {
        _isActive = true;
        _msiUpdatesBusy = false;
        _msiUpdatesSnapshot = CreateNotCheckedSnapshot();
        MsiUpdatesTabView.SelectedIndex = 0;
        MsiUpdatesScrollViewer.ChangeView(null, 0, null);
        RenderMsiLiveUpdateCatalog();
    }

    internal void Deactivate()
    {
        _isActive = false;
        var requestCancellation = _requestCancellation;
        _requestCancellation = null;
        _msiUpdatesBusy = false;
        try
        {
            requestCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The request completed while the page was being deactivated.
        }
    }

    private async void MsiUpdatesCheckButton_Click(object sender, RoutedEventArgs e)
    {
        if (_frontend is null || !_isActive || _msiUpdatesBusy) return;

        var requestCancellation = new CancellationTokenSource();
        _requestCancellation = requestCancellation;
        _msiUpdatesBusy = true;
        _msiUpdatesSnapshot = CreateNotCheckedSnapshot();
        RenderMsiLiveUpdateCatalog();

        try
        {
            var result = await _frontend.RequestMsiLiveUpdateCatalogAsync(requestCancellation.Token);
            if (IsCurrentRequest(requestCancellation))
                _msiUpdatesSnapshot = result;
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
            // Navigation cancellation is not a catalog failure.
        }
        catch (Exception exception)
        {
            if (IsCurrentRequest(requestCancellation))
            {
                AppLog.Debug("MsiUpdates", "MSI catalog query request failed.", ("Reason", exception.GetType().Name));
                _msiUpdatesSnapshot = new(FrontendMsiLiveUpdateCatalogState.Failed, null, null, null, [],
                    DateTimeOffset.UtcNow, "RuntimeRequestFailed");
            }
        }
        finally
        {
            if (IsCurrentRequest(requestCancellation))
            {
                _requestCancellation = null;
                _msiUpdatesBusy = false;
                RenderMsiLiveUpdateCatalog();
            }

            requestCancellation.Dispose();
        }
    }

    private bool IsCurrentRequest(CancellationTokenSource requestCancellation) =>
        _isActive && ReferenceEquals(_requestCancellation, requestCancellation);

    private void RenderMsiLiveUpdateCatalog()
    {
        MsiUpdatesCheckButton.IsEnabled = _isActive && !_msiUpdatesBusy;
        MsiUpdatesProgressRing.IsActive = _msiUpdatesBusy;
        MsiUpdatesProgressRing.Visibility = _msiUpdatesBusy ? Visibility.Visible : Visibility.Collapsed;
        MsiUpdatesStatusText.Text = _msiUpdatesBusy
            ? MainUiStrings.Get("MsiUpdates_Checking", "Checking MSI catalog…")
            : _msiUpdatesSnapshot.State switch
            {
                FrontendMsiLiveUpdateCatalogState.NotChecked => MainUiStrings.Get("MsiUpdates_NotChecked", "Select Check for updates to load MSI catalog records."),
                FrontendMsiLiveUpdateCatalogState.Completed => MainUiStrings.Get("MsiUpdates_Completed", "Catalog records loaded."),
                FrontendMsiLiveUpdateCatalogState.PartialFailure => MainUiStrings.Get("MsiUpdates_PartialFailure", "Some catalog categories could not be loaded. Check the affected tabs."),
                FrontendMsiLiveUpdateCatalogState.Unavailable => MainUiStrings.Get("MsiUpdates_Unavailable", "The MSI catalog is unavailable for this device or MSI installation."),
                _ => MainUiStrings.Get("MsiUpdates_Failed", "The catalog query failed. You can try again.")
            };
        RenderSourceStatus();

        RenderCategoryTabHeaders();
        RenderMsiLiveUpdateCategory(FrontendMsiLiveUpdateCatalogCategory.Bios, MsiUpdatesBiosEntriesPanel);
        RenderMsiLiveUpdateCategory(FrontendMsiLiveUpdateCatalogCategory.Firmware, MsiUpdatesFirmwareEntriesPanel);
        RenderMsiLiveUpdateCategory(FrontendMsiLiveUpdateCatalogCategory.Driver, MsiUpdatesDriverEntriesPanel);
        RenderMsiLiveUpdateCategory(FrontendMsiLiveUpdateCatalogCategory.Utility, MsiUpdatesUtilityEntriesPanel);
    }

    private void RenderSourceStatus()
    {
        var messages = new List<string>(capacity: 3);
        AddSourceMessage(_msiUpdatesSnapshot.MsiSource, "MsiUpdates_MsiSourcePartial", "MSI source is partially unavailable.");
        AddSourceMessage(_msiUpdatesSnapshot.IntelDsaSource, "MsiUpdates_IntelSourcePartial", "Intel DSA source is unavailable.");
        if (_msiUpdatesSnapshot.LocalInventoryWarnings is { Count: > 0 })
            messages.Add(MainUiStrings.Get("MsiUpdates_InventoryPartial", "Some installed versions could not be read."));
        MsiUpdatesSourceStatusText.Text = string.Join(" · ", messages);
        MsiUpdatesSourceStatusText.Visibility = messages.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        void AddSourceMessage(FrontendMsiLiveUpdateSourceSnapshot? source, string resourceKey, string fallback)
        {
            if (source is null || source.State == FrontendMsiLiveUpdateSourceState.Completed) return;
            messages.Add(MainUiStrings.Get(resourceKey, fallback));
        }
    }

    private void RenderCategoryTabHeaders()
    {
        MsiUpdatesBiosTab.Header = FormatCategoryHeader(_categoryTabBaseHeaders[0], FindCategory(FrontendMsiLiveUpdateCatalogCategory.Bios));
        MsiUpdatesFirmwareTab.Header = FormatCategoryHeader(_categoryTabBaseHeaders[1], FindCategory(FrontendMsiLiveUpdateCatalogCategory.Firmware));
        MsiUpdatesDriversTab.Header = FormatCategoryHeader(_categoryTabBaseHeaders[2], FindCategory(FrontendMsiLiveUpdateCatalogCategory.Driver));
        MsiUpdatesUtilitiesTab.Header = FormatCategoryHeader(_categoryTabBaseHeaders[3], FindCategory(FrontendMsiLiveUpdateCatalogCategory.Utility));
    }

    private FrontendMsiLiveUpdateCatalogCategorySnapshot? FindCategory(FrontendMsiLiveUpdateCatalogCategory category) =>
        _msiUpdatesSnapshot.Categories.FirstOrDefault(item => item.Category == category);

    internal static string FormatCategoryHeader(
        string localizedBaseHeader,
        FrontendMsiLiveUpdateCatalogCategorySnapshot? category)
    {
        if (category is null || category.State is not (
                FrontendMsiLiveUpdateCatalogCategoryState.Success or
                FrontendMsiLiveUpdateCatalogCategoryState.Empty))
            return localizedBaseHeader;

        return $"{localizedBaseHeader} ({category.Entries.Count.ToString(CultureInfo.CurrentCulture)})";
    }

    private void RenderMsiLiveUpdateCategory(
        FrontendMsiLiveUpdateCatalogCategory category,
        StackPanel panel)
    {
        panel.Children.Clear();
        var result = FindCategory(category);
        if (result?.State == FrontendMsiLiveUpdateCatalogCategoryState.Failed)
        {
            panel.Children.Add(new TextBlock
            {
                Text = MainUiStrings.Get("MsiUpdates_CategoryFailed", "Catalog request failed for this category."),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }
        if (result?.State == FrontendMsiLiveUpdateCatalogCategoryState.Empty ||
            result?.State == FrontendMsiLiveUpdateCatalogCategoryState.Success && result.Entries.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = MainUiStrings.Get("MsiUpdates_NoEntries", "No catalog entries."),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }
        if (result is null)
        {
            var messageKey = _msiUpdatesSnapshot.State switch
            {
                FrontendMsiLiveUpdateCatalogState.NotChecked => "MsiUpdates_NotCheckedCategory",
                FrontendMsiLiveUpdateCatalogState.Unavailable => "MsiUpdates_UnavailableCategory",
                _ => "MsiUpdates_NoData"
            };
            var fallback = _msiUpdatesSnapshot.State switch
            {
                FrontendMsiLiveUpdateCatalogState.NotChecked => "No catalog loaded yet.",
                FrontendMsiLiveUpdateCatalogState.Unavailable => "Catalog data is unavailable.",
                _ => "No catalog data was returned for this category."
            };
            panel.Children.Add(new TextBlock { Text = MainUiStrings.Get(messageKey, fallback), TextWrapping = TextWrapping.Wrap });
            return;
        }

        foreach (var entry in result.Entries)
        {
            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(new TextBlock
            {
                Text = entry.Name,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });

            var metadata = new List<string>(capacity: 4);
            if (!string.IsNullOrWhiteSpace(entry.InstalledVersion))
            {
                var installedKey = entry.InstalledVersionConfidence == FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic
                    ? "MsiUpdates_InstalledEstimated"
                    : "MsiUpdates_InstalledVersion";
                var installedFallback = entry.InstalledVersionConfidence == FrontendMsiLiveUpdateInstalledVersionConfidence.Heuristic
                    ? "Installed (estimated) {0}"
                    : "Installed {0}";
                metadata.Add(MainUiStrings.Format(installedKey, installedFallback, entry.InstalledVersion.Trim()));
            }
            else metadata.Add(MainUiStrings.Get("MsiUpdates_InstalledUnavailable", "Installed version unavailable"));

            if (!string.IsNullOrWhiteSpace(entry.Version))
            {
                var latestLabel = GetLatestVersionLabel(entry);
                metadata.Add(MainUiStrings.Format(latestLabel.ResourceKey, latestLabel.EnglishFallback, entry.Version.Trim()));
            }
            else metadata.Add(MainUiStrings.Get("MsiUpdates_LatestUnavailable", "Latest version unavailable"));

            metadata.Add(MainUiStrings.Get(
                entry.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.IntelDsa
                    ? "MsiUpdates_SourceIntelDsa"
                    : "MsiUpdates_SourceMsi",
                entry.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.IntelDsa ? "Source: Intel DSA" : "Source: MSI"));
            if (!string.IsNullOrWhiteSpace(entry.ReleaseDate))
            {
                metadata.Add(MainUiStrings.Format("MsiUpdates_ReleaseDate", "Released {0}", FormatCatalogReleaseDate(entry.ReleaseDate)));
            }
            if (metadata.Count > 0)
            {
                content.Children.Add(new TextBlock
                {
                    Text = string.Join(" · ", metadata),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.75
                });
            }

            var cardContent = new Grid { ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
            cardContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cardContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cardContent.Children.Add(content);
            panel.Children.Add(new Border
            {
                Style = (Style)Resources["MsiCatalogEntryCardStyle"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = cardContent
            });
        }
    }

    internal static string FormatCatalogReleaseDate(string releaseDate)
    {
        var value = releaseDate.Trim();
        if (value.Length >= 10 &&
            (value.Length == 10 || value[10] is 'T' or 't' or ' ') &&
            DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return value;
    }

    internal static (string ResourceKey, string EnglishFallback) GetLatestVersionLabel(
        FrontendMsiLiveUpdateCatalogEntry entry)
    {
        var isIntelPackage = entry.LatestVersionSource == FrontendMsiLiveUpdateVersionSource.IntelDsa
            && entry.LatestVersionKind == FrontendMsiLiveUpdateVersionKind.Package;
        return isIntelPackage
            ? ("MsiUpdates_LatestIntelPackage", "Latest Intel package {0}")
            : ("MsiUpdates_LatestVersion", "Latest {0}");
    }

    private static string ReadHeader(TabViewItem tab, string fallback) =>
        tab.Header?.ToString() is { Length: > 0 } header ? header : fallback;

    private static FrontendMsiLiveUpdateCatalogSnapshot CreateNotCheckedSnapshot() =>
        new(FrontendMsiLiveUpdateCatalogState.NotChecked, null, null, null, [], null);
}
