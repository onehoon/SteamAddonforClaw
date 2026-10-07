using Microsoft.UI.Xaml.Controls;
using System.Globalization;
using System.Net.Http;

namespace SteamInputAddonforClaw.Views;

public sealed partial class HowToUsePage : UserControl
{
    private static readonly HttpClient DocumentationClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private bool _loading;
    private bool _loaded;

    public HowToUsePage()
    {
        InitializeComponent();
    }

    internal void Activate()
    {
        _ = LoadDocumentationAsync();
    }

    private void RetryButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        _ = LoadDocumentationAsync();

    private async Task LoadDocumentationAsync()
    {
        if (_loaded || _loading)
            return;

        _loading = true;
        LoadingPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        FailurePanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

        try
        {
            var source = HowToUseMarkdownRenderer.ResolveSource(CultureInfo.CurrentUICulture);
            var markdown = await DocumentationClient.GetStringAsync(source.RawMarkdownUrl);
            var html = HowToUseMarkdownRenderer.BuildHtml(markdown, source);

            await DocumentationWebView.EnsureCoreWebView2Async();
            DocumentationWebView.NavigateToString(html);

            _loaded = true;
            LoadingPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            AppLog.Warn("HowToUse", "Remote documentation load failed.", exception);
            LoadingPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            FailurePanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        finally
        {
            _loading = false;
        }
    }
}
