using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
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

    private WebView2? _documentationWebView;
    private bool _loading;
    private bool _loaded;
    private bool _webViewConfigured;

    public HowToUsePage()
    {
        InitializeComponent();
    }

    internal void Activate()
    {
        AppLog.Info(
            "HowToUse",
            "How to Use activation entered.",
            ("Language", CultureInfo.CurrentUICulture.Name),
            ("WebViewCreated", _documentationWebView is not null));
        StartDocumentationLoad();
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        AppLog.Info("HowToUse", "Documentation retry requested.");
        StartDocumentationLoad();
    }

    private void StartDocumentationLoad()
    {
        if (_loaded || _loading)
            return;

        _loading = true;
        LoadingPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        FailurePanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

        try
        {
            var webView = EnsureDocumentationWebView();
            if (webView.IsLoaded)
            {
                _ = LoadDocumentationAsync(webView);
                return;
            }

            webView.Loaded -= DocumentationWebView_Loaded;
            webView.Loaded += DocumentationWebView_Loaded;
        }
        catch (Exception exception)
        {
            AppLog.Warn("HowToUse", "Documentation load failed.", exception, ("Stage", "WebViewCreation"));
            ShowFailure();
            _loading = false;
        }
    }

    private WebView2 EnsureDocumentationWebView()
    {
        if (_documentationWebView is not null)
            return _documentationWebView;

        var webView = new WebView2();
        DocumentationWebViewHost.Children.Add(webView);
        _documentationWebView = webView;
        AppLog.Debug("HowToUse", "WebView2 control created lazily.");
        return webView;
    }

    private void DocumentationWebView_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not WebView2 webView)
            return;

        webView.Loaded -= DocumentationWebView_Loaded;
        if (_loading && !_loaded)
            _ = LoadDocumentationAsync(webView);
    }

    private async Task LoadDocumentationAsync(WebView2 webView)
    {
        var stage = "DocumentSourceSelection";
        string? sourceUrl = null;
        try
        {
            var culture = CultureInfo.CurrentUICulture;
            var source = HowToUseMarkdownRenderer.ResolveSource(culture);
            sourceUrl = source.RawMarkdownUrl;
            AppLog.Info(
                "HowToUse",
                "Document source selected.",
                ("Language", culture.Name),
                ("SourceUrl", sourceUrl));

            stage = "MarkdownDownload";
            var markdown = await DocumentationClient.GetStringAsync(source.RawMarkdownUrl);
            AppLog.Info(
                "HowToUse",
                "Remote Markdown download completed.",
                ("SourceUrl", source.RawMarkdownUrl),
                ("MarkdownLength", markdown.Length));

            stage = "HtmlRendering";
            var html = HowToUseMarkdownRenderer.BuildHtml(markdown, source);

            stage = "CoreWebViewInitialization";
            await webView.EnsureCoreWebView2Async();
            ConfigureWebView(webView);
            AppLog.Info("HowToUse", "CoreWebView2 initialization completed.");

            stage = "HtmlNavigation";
            AppLog.Debug("HowToUse", "HTML navigation started.", ("HtmlLength", html.Length));
            var navigation = await NavigateToStringAsync(webView, html);
            AppLog.Info(
                "HowToUse",
                "HTML navigation completed.",
                ("NavigationSuccess", navigation.IsSuccess),
                ("WebErrorStatus", navigation.WebErrorStatus));

            if (!navigation.IsSuccess)
            {
                AppLog.Warn(
                    "HowToUse",
                    "Documentation load failed.",
                    null,
                    ("Stage", stage),
                    ("NavigationSuccess", navigation.IsSuccess),
                    ("WebErrorStatus", navigation.WebErrorStatus));
                ShowFailure();
                return;
            }

            _loaded = true;
            LoadingPanel.Visibility = Visibility.Collapsed;
            FailurePanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            AppLog.Warn(
                "HowToUse",
                "Documentation load failed.",
                exception,
                ("Stage", stage),
                ("SourceUrl", sourceUrl ?? "Unavailable"));
            ShowFailure();
        }
        finally
        {
            _loading = false;
        }
    }

    private void ShowFailure()
    {
        _loaded = false;
        LoadingPanel.Visibility = Visibility.Collapsed;
        FailurePanel.Visibility = Visibility.Visible;
    }

    private static Task<CoreWebView2NavigationCompletedEventArgs> NavigateToStringAsync(
        WebView2 webView,
        string html)
    {
        var completion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            sender.NavigationCompleted -= OnCompleted;
            completion.TrySetResult(args);
        }

        webView.NavigationCompleted += OnCompleted;
        try
        {
            webView.NavigateToString(html);
        }
        catch
        {
            webView.NavigationCompleted -= OnCompleted;
            throw;
        }

        return completion.Task;
    }

    internal static bool ShouldCancelWebViewNavigation(string? uriText)
    {
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
            return true;

        return !string.Equals(uri.AbsoluteUri, "about:blank", StringComparison.OrdinalIgnoreCase);
    }

    private void ConfigureWebView(WebView2 webView)
    {
        if (_webViewConfigured)
            return;

        var coreWebView = webView.CoreWebView2
            ?? throw new InvalidOperationException("CoreWebView2 initialization completed without a core instance.");
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
