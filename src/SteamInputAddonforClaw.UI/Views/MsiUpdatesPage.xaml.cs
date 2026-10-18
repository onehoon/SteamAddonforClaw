using System.Net;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.UI;

namespace SteamInputAddonforClaw.Views;

public sealed partial class MsiUpdatesPage : UserControl
{
    private IAddonFrontendControl? _frontend;
    private bool _msiUpdatesBusy;
    private FrontendMsiLiveUpdateCatalogSnapshot _msiUpdatesSnapshot = new(
        FrontendMsiLiveUpdateCatalogState.NotChecked, null, null, null, [], null);

    public MsiUpdatesPage()
    {
        InitializeComponent();
        RenderMsiLiveUpdateCatalog();
    }

    internal void Initialize(IAddonFrontendControl frontend) => _frontend = frontend;

    private async void MsiUpdatesCheckButton_Click(object sender, RoutedEventArgs e)
    {
        if (_frontend is null || _msiUpdatesBusy) return;
        _msiUpdatesBusy = true;
        RenderMsiLiveUpdateCatalog();
        try
        {
            _msiUpdatesSnapshot = await _frontend.RequestMsiLiveUpdateCatalogAsync();
        }
        catch (Exception exception)
        {
            AppLog.Debug("MsiUpdates", "MSI catalog query request failed.", ("Reason", exception.GetType().Name));
            _msiUpdatesSnapshot = new(FrontendMsiLiveUpdateCatalogState.Failed, null, null, null, [],
                DateTimeOffset.UtcNow, "RuntimeRequestFailed");
        }
        finally
        {
            _msiUpdatesBusy = false;
            RenderMsiLiveUpdateCatalog();
        }
    }

    private void RenderMsiLiveUpdateCatalog()
    {
        MsiUpdatesCheckButton.IsEnabled = !_msiUpdatesBusy;
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

        if (_msiUpdatesSnapshot.CheckedAtUtc is { } checkedAt)
        {
            MsiUpdatesLastCheckedText.Text = MainUiStrings.Format("MsiUpdates_LastChecked",
                "Last checked: {0}", checkedAt.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture));
            MsiUpdatesLastCheckedText.Visibility = Visibility.Visible;
        }
        else
        {
            MsiUpdatesLastCheckedText.Text = string.Empty;
            MsiUpdatesLastCheckedText.Visibility = Visibility.Collapsed;
        }

        RenderMsiLiveUpdateCategory(FrontendMsiLiveUpdateCatalogCategory.Bios, MsiUpdatesBiosEntriesPanel);
        RenderMsiLiveUpdateCategory(FrontendMsiLiveUpdateCatalogCategory.Firmware, MsiUpdatesFirmwareEntriesPanel);
        RenderMsiLiveUpdateCategory(FrontendMsiLiveUpdateCatalogCategory.Driver, MsiUpdatesDriverEntriesPanel);
        RenderMsiLiveUpdateCategory(FrontendMsiLiveUpdateCatalogCategory.Utility, MsiUpdatesUtilityEntriesPanel);
    }

    private void RenderMsiLiveUpdateCategory(
        FrontendMsiLiveUpdateCatalogCategory category,
        StackPanel panel)
    {
        panel.Children.Clear();
        var result = _msiUpdatesSnapshot.Categories.FirstOrDefault(item => item.Category == category);
        if (result?.State == FrontendMsiLiveUpdateCatalogCategoryState.Failed)
        {
            panel.Children.Add(new TextBlock
            {
                Text = MainUiStrings.Get("MsiUpdates_CategoryFailed", "Catalog request failed for this category."),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }
        if (result?.State == FrontendMsiLiveUpdateCatalogCategoryState.Empty)
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
            content.Children.Add(new TextBlock
            {
                Text = MainUiStrings.Format("MsiUpdates_LatestVersion",
                    "Latest published version: {0}",
                    string.IsNullOrWhiteSpace(entry.Version)
                        ? MainUiStrings.Get("MsiUpdates_VersionNotProvided", "Not provided")
                        : entry.Version),
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrWhiteSpace(entry.ReleaseDate))
                content.Children.Add(new TextBlock
                {
                    Text = MainUiStrings.Format("MsiUpdates_ReleaseDate", "Release date: {0}", entry.ReleaseDate),
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.75
                });
            if (!string.IsNullOrWhiteSpace(entry.Description))
                content.Children.Add(new TextBlock
                {
                    Text = FormatMsiCatalogDescription(entry.Description),
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.85
                });
            panel.Children.Add(new Border
            {
                Style = (Style)Resources["MsiCatalogEntryCardStyle"],
                Child = content
            });
        }
    }

    internal static string FormatMsiCatalogDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return string.Empty;

        var decoded = WebUtility.HtmlDecode(description)
            .Replace('\u00a0', ' ')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var withLineBreaks = Regex.Replace(decoded, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
        var plainText = Regex.Replace(withLineBreaks, @"</?[A-Za-z][^>]*>", string.Empty);
        var lines = plainText.Split('\n')
            .Select(line => Regex.Replace(line, @"[ \t]+", " ").Trim());

        return string.Join(Environment.NewLine, lines).Trim();
    }
}
