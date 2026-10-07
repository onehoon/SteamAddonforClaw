using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace SteamInputAddonforClaw.Views;

public sealed partial class HowToUsePage : UserControl
{
    private static readonly HttpClient DocumentationClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private bool _loading;
    private bool _loaded;
    private bool _webViewConfigured;

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
            ConfigureWebView();
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

    internal static bool ShouldCancelWebViewNavigation(string? uriText)
    {
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
            return true;

        return !string.Equals(uri.AbsoluteUri, "about:blank", StringComparison.OrdinalIgnoreCase);
    }

    private void ConfigureWebView()
    {
        if (_webViewConfigured || DocumentationWebView.CoreWebView2 is not { } coreWebView)
            return;

        coreWebView.NavigationStarting += DocumentationWebView_NavigationStarting;
        coreWebView.WebMessageReceived += DocumentationWebView_WebMessageReceived;
        _webViewConfigured = true;
    }

    private void DocumentationWebView_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        e.Cancel = ShouldCancelWebViewNavigation(e.Uri);
    }

    private void DocumentationWebView_WebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = e.TryGetWebMessageAsString();
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String
                || !string.Equals(typeElement.GetString(), "open-link", StringComparison.Ordinal)
                || !root.TryGetProperty("href", out var hrefElement)
                || hrefElement.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var source = HowToUseMarkdownRenderer.ResolveSource(CultureInfo.CurrentUICulture);
            var uri = HowToUseMarkdownRenderer.ResolveExternalLink(source, hrefElement.GetString() ?? string.Empty);
            if (uri is not null)
                _ = LaunchExternalUriAsync(uri);
        }
        catch (JsonException)
        {
            // Malformed or non-JSON messages are ignored.
        }
        catch (InvalidOperationException)
        {
            // Messages that are not string payloads are ignored.
        }
        catch (Exception exception)
        {
            AppLog.Warn("HowToUse", "External documentation link message was ignored.", exception);
        }
    }

    private static async Task LaunchExternalUriAsync(Uri uri)
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch (Exception exception)
        {
            AppLog.Warn("HowToUse", "External documentation link could not be launched.", exception);
        }
    }
}
