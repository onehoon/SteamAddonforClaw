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
    private PendingHtmlNavigation? _pendingHtmlNavigation;
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
            var language = culture.TwoLetterISOLanguageName;
            AppLog.Debug("HowToUse", "HTML navigation started.", ("Language", language), ("HtmlLength", html.Length));
            var navigation = await NavigateToStringAsync(webView, html, language);
            AppLog.Info(
                "HowToUse",
                "HTML navigation completed.",
                ("Language", language),
                ("NavigationId", navigation.NavigationId),
                ("NavigationSuccess", navigation.IsSuccess),
                ("WebErrorStatus", navigation.WebErrorStatus));

            if (!navigation.IsSuccess)
            {
                AppLog.Warn(
                    "HowToUse",
                    "Documentation load failed.",
                    null,
                    ("Stage", stage),
                    ("NavigationId", navigation.NavigationId),
                    ("NavigationSuccess", navigation.IsSuccess),
                    ("WebErrorStatus", navigation.WebErrorStatus),
                    ("RetryAvailable", true));
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
                ("SourceUrl", sourceUrl ?? "Unavailable"),
                ("RetryAvailable", true));
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

    private async Task<CoreWebView2NavigationCompletedEventArgs> NavigateToStringAsync(
        WebView2 webView,
        string html,
        string language)
    {
        var coreWebView = webView.CoreWebView2
            ?? throw new InvalidOperationException("CoreWebView2 is not initialized.");
        var pending = new PendingHtmlNavigation();
        _pendingHtmlNavigation = pending;
        AppLog.Debug("HowToUse", "Generated document navigation requested.",
            ("Language", language), ("HtmlLength", html.Length));
        try
        {
            coreWebView.NavigateToString(html);
            return await pending.Completion.Task.WaitAsync(DocumentationClient.Timeout);
        }
        catch (TimeoutException)
        {
            AppLog.Warn("HowToUse", "Generated document navigation timed out.", null,
                ("Stage", "HtmlNavigation"), ("Language", language),
                ("NavigationId", pending.Correlation.NavigationId), ("RetryAvailable", true));
            throw;
        }
        finally
        {
            if (ReferenceEquals(_pendingHtmlNavigation, pending))
                _pendingHtmlNavigation = null;
        }
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
        coreWebView.NavigationCompleted += DocumentationWebView_NavigationCompleted;
        coreWebView.WebMessageReceived += DocumentationWebView_WebMessageReceived;
        _webViewConfigured = true;
    }

    private void DocumentationWebView_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        e.Cancel = ShouldCancelWebViewNavigation(e.Uri);
        var matchedPendingDocument = !e.Cancel
            && _pendingHtmlNavigation?.Correlation.TryCaptureNavigationStart(e.NavigationId, e.Uri) == true;
        AppLog.Debug("HowToUse", "WebView navigation starting.",
            ("NavigationId", e.NavigationId), ("UriKind", ClassifyNavigationUri(e.Uri)),
            ("IsUserInitiated", e.IsUserInitiated), ("Decision", e.Cancel ? "Cancel" : "Allow"),
            ("MatchedPendingDocument", matchedPendingDocument));
    }

    private void DocumentationWebView_NavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        var matchedPendingDocument = _pendingHtmlNavigation?.Correlation.MatchesCompleted(e.NavigationId) == true;
        AppLog.Debug("HowToUse", "WebView navigation completed.",
            ("NavigationId", e.NavigationId), ("IsSuccess", e.IsSuccess),
            ("WebErrorStatus", e.WebErrorStatus), ("MatchedPendingDocument", matchedPendingDocument));
        if (matchedPendingDocument)
            _pendingHtmlNavigation?.Completion.TrySetResult(e);
    }

    private static string ClassifyNavigationUri(string? uriText)
    {
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri)) return "Invalid";
        if (string.Equals(uri.AbsoluteUri, "about:blank", StringComparison.OrdinalIgnoreCase)) return "GeneratedDocument";
        if (uri.Scheme is "http" or "https") return "ExternalHttp";
        if (uri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase)) return "ExternalFile";
        return uri.Scheme;
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

    private sealed class PendingHtmlNavigation
    {
        internal HowToUseNavigationCorrelation Correlation { get; } = new();
        internal TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

internal sealed class HowToUseNavigationCorrelation
{
    private ulong? _navigationId;

    internal ulong? NavigationId => _navigationId;

    internal bool TryCaptureNavigationStart(ulong navigationId, string? uriText)
    {
        if (_navigationId is not null || HowToUsePage.ShouldCancelWebViewNavigation(uriText))
            return false;

        _navigationId = navigationId;
        return true;
    }

    internal bool MatchesCompleted(ulong navigationId) => _navigationId == navigationId;
}
