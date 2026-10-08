using System.Globalization;
using System.Text;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class HowToUsePageDocumentationRoutingTests
{
    [Fact]
    public void Korean_ui_routes_to_the_repository_korean_guide()
    {
        var source = HowToUseMarkdownRenderer.ResolveSource(CultureInfo.GetCultureInfo("ko-KR"));

        Assert.Equal(
            "https://raw.githubusercontent.com/onehoon/SteamAddonforClaw/main/docs/howtouse/README_KO.md",
            source.RawMarkdownUrl);
        Assert.Equal("docs/howtouse/", source.RepositoryDirectory);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ja-JP")]
    public void Non_korean_ui_routes_to_the_readme(string cultureName)
    {
        var source = HowToUseMarkdownRenderer.ResolveSource(CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(
            "https://raw.githubusercontent.com/onehoon/SteamAddonforClaw/main/README.md",
            source.RawMarkdownUrl);
        Assert.Equal(string.Empty, source.RepositoryDirectory);
    }

    [Theory]
    [InlineData("about:blank", false)]
    [InlineData("https://raw.githubusercontent.com/onehoon/SteamAddonforClaw/main/README.md", true)]
    [InlineData("https://example.com", true)]
    [InlineData("file:///C:/Windows/win.ini", true)]
    [InlineData("javascript:alert(1)", true)]
    [InlineData("data:text/html;charset=utf-8,%3C!doctype%20html%3E", true)]
    public void WebView_navigation_is_cancelled_except_for_the_generated_blank_document(
        string uri,
        bool shouldCancel)
    {
        Assert.Equal(shouldCancel, HowToUsePage.ShouldCancelWebViewNavigation(uri));
    }

    [Fact]
    public void Generated_html_uri_accepts_exact_percent_encoded_and_base64_documents()
    {
        var percentEncoded = CreateDataUri("text/html;charset=utf-8", GeneratedHtml);
        var base64 = CreateBase64DataUri("text/html;charset=utf-8;base64", GeneratedHtml);
        var base64WithoutCharset = CreateBase64DataUri("text/html;base64", GeneratedHtml);

        Assert.True(HowToUsePage.IsExpectedGeneratedHtmlNavigationUri(percentEncoded, GeneratedHtml));
        Assert.True(HowToUsePage.IsExpectedGeneratedHtmlNavigationUri(base64, GeneratedHtml));
        Assert.True(HowToUsePage.IsExpectedGeneratedHtmlNavigationUri(base64WithoutCharset, GeneratedHtml));
    }

    [Fact]
    public void Generated_html_uri_rejects_unrelated_content_and_unapproved_metadata()
    {
        var unrelatedDocument = CreateBase64DataUri("text/html;charset=utf-8;base64", "<!doctype html><html>other</html>");

        Assert.False(HowToUsePage.IsExpectedGeneratedHtmlNavigationUri(unrelatedDocument, GeneratedHtml));
        Assert.False(HowToUsePage.IsExpectedGeneratedHtmlNavigationUri(
            CreateDataUri("text/plain", GeneratedHtml), GeneratedHtml));
        Assert.False(HowToUsePage.IsExpectedGeneratedHtmlNavigationUri(
            CreateDataUri("text/html;charset=iso-8859-1", GeneratedHtml), GeneratedHtml));
        Assert.False(HowToUsePage.IsExpectedGeneratedHtmlNavigationUri(
            CreateDataUri("text/html;foo=bar", GeneratedHtml), GeneratedHtml));
        Assert.False(HowToUsePage.IsExpectedGeneratedHtmlNavigationUri(
            "data:text/html;base64,not-base64!", GeneratedHtml));
        Assert.False(HowToUsePage.IsExpectedGeneratedHtmlNavigationUri(
            "https://example.com/" + Uri.EscapeDataString(GeneratedHtml), GeneratedHtml));
    }

    [Fact]
    public void Only_a_pending_non_user_generated_document_is_allowed_as_data_navigation()
    {
        var generatedUri = CreateBase64DataUri("text/html;charset=utf-8;base64", GeneratedHtml);
        var unrelatedUri = CreateBase64DataUri("text/html;charset=utf-8;base64", "<!doctype html><html>unrelated</html>");
        var correlation = new HowToUseNavigationCorrelation(GeneratedHtml);

        Assert.False(correlation.TryCaptureNavigationStart(4, generatedUri, isUserInitiated: true));
        Assert.False(correlation.TryCaptureNavigationStart(5, "data:text/plain,hello", isUserInitiated: false));
        Assert.False(correlation.TryCaptureNavigationStart(6, unrelatedUri, isUserInitiated: false));
        Assert.True(correlation.TryCaptureNavigationStart(8, generatedUri, isUserInitiated: false));
        Assert.False(correlation.TryCaptureNavigationStart(9, generatedUri, isUserInitiated: false));
        Assert.Equal(8ul, correlation.NavigationId);
        Assert.False(HowToUsePage.ShouldAllowWebViewNavigation(generatedUri, matchedPendingDocument: false));
        Assert.True(HowToUsePage.ShouldAllowWebViewNavigation(generatedUri, matchedPendingDocument: true));
        Assert.True(HowToUsePage.ShouldAllowWebViewNavigation("about:blank", matchedPendingDocument: false));
        Assert.False(HowToUsePage.ShouldAllowWebViewNavigation("https://example.com", matchedPendingDocument: false));
    }

    [Fact]
    public void How_to_use_uses_app_owned_html_and_does_not_navigate_to_github_pages()
    {
        var code = ReadPageCode();

        Assert.Contains("coreWebView.NavigateToString(html)", code, StringComparison.Ordinal);
        Assert.Contains("NavigationStarting += DocumentationWebView_NavigationStarting", code, StringComparison.Ordinal);
        Assert.Contains("NavigationCompleted += DocumentationWebView_NavigationCompleted", code, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = !ShouldAllowWebViewNavigation(e.Uri, matchedPendingDocument)", code, StringComparison.Ordinal);
        Assert.Contains("e.IsUserInitiated", code, StringComparison.Ordinal);
        Assert.Contains("IsExpectedGeneratedHtmlNavigationUri(uriText, _expectedHtml)", code, StringComparison.Ordinal);
        Assert.Contains("new PendingHtmlNavigation(html)", code, StringComparison.Ordinal);
        Assert.Contains("string.Equals(generatedHtml, expectedHtml, StringComparison.Ordinal)", code, StringComparison.Ordinal);
        Assert.Contains("WebMessageReceived += DocumentationWebView_WebMessageReceived", code, StringComparison.Ordinal);
        Assert.Contains("catch (Exception exception)", code, StringComparison.Ordinal);
        Assert.Contains("Windows.System.Launcher.LaunchUriAsync(uri)", code, StringComparison.Ordinal);
        Assert.Contains("(\"Decision\", e.Cancel ? \"Cancel\" : \"Allow\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("webView.Source", code, StringComparison.Ordinal);
    }

    [Fact]
    public void How_to_use_navigation_completion_requires_the_pending_generated_document_id()
    {
        var correlation = new HowToUseNavigationCorrelation(GeneratedHtml);

        Assert.False(correlation.MatchesCompleted(4));
        var generatedHtml = CreateDataUri("text/html;charset=utf-8", GeneratedHtml);
        Assert.False(correlation.TryCaptureNavigationStart(5, "https://example.com", isUserInitiated: false));
        Assert.True(correlation.TryCaptureNavigationStart(8, generatedHtml, isUserInitiated: false));
        Assert.Equal(8ul, correlation.NavigationId);
        Assert.False(correlation.MatchesCompleted(7));
        Assert.True(correlation.MatchesCompleted(8));
        Assert.False(correlation.TryCaptureNavigationStart(9, generatedHtml, isUserInitiated: false));
    }

    [Fact]
    public void How_to_use_webview_is_created_lazily_and_reused_after_first_activation()
    {
        var xaml = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "SteamInputAddonforClaw.UI", "Views", "HowToUsePage.xaml"));
        var code = ReadPageCode();

        Assert.DoesNotContain("<WebView2", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DocumentationWebViewHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("internal void Activate()", code, StringComparison.Ordinal);
        Assert.Contains("StartDocumentationLoad();", code, StringComparison.Ordinal);
        Assert.Contains("private WebView2? _documentationWebView;", code, StringComparison.Ordinal);
        var existingViewCheck = code.IndexOf("if (_documentationWebView is not null)", StringComparison.Ordinal);
        var createView = code.IndexOf("new WebView2()", StringComparison.Ordinal);
        Assert.True(existingViewCheck >= 0 && createView > existingViewCheck);
        Assert.Contains("DocumentationWebViewHost.Children.Add(webView)", code, StringComparison.Ordinal);
        Assert.Contains("webView.Loaded += DocumentationWebView_Loaded", code, StringComparison.Ordinal);
    }

    [Fact]
    public void How_to_use_load_success_waits_for_successful_navigation_completion()
    {
        var code = ReadPageCode();

        var navigationAwait = code.IndexOf("var navigation = await NavigateToStringAsync(webView, html, language);", StringComparison.Ordinal);
        var failedNavigationCheck = code.IndexOf("if (!navigation.IsSuccess)", navigationAwait, StringComparison.Ordinal);
        var loadedCommit = code.IndexOf("_loaded = true;", failedNavigationCheck, StringComparison.Ordinal);

        Assert.True(navigationAwait >= 0);
        Assert.True(failedNavigationCheck > navigationAwait);
        Assert.True(loadedCommit > failedNavigationCheck);
        var eventSubscription = code.IndexOf("coreWebView.NavigationCompleted += DocumentationWebView_NavigationCompleted", StringComparison.Ordinal);
        var navigateCall = code.IndexOf("coreWebView.NavigateToString(html)", StringComparison.Ordinal);
        var configureCall = code.IndexOf("ConfigureWebView(webView);", StringComparison.Ordinal);
        Assert.True(eventSubscription >= 0 && navigateCall >= 0 && configureCall >= 0 && configureCall < navigationAwait);
        Assert.Contains("if (matchedPendingDocument)", code, StringComparison.Ordinal);
        Assert.Contains("Completion.TrySetResult(e)", code, StringComparison.Ordinal);
        Assert.Contains("WaitAsync(DocumentationClient.Timeout)", code, StringComparison.Ordinal);
        Assert.Contains("Generated document navigation timed out.", code, StringComparison.Ordinal);
        Assert.Contains("if (ReferenceEquals(_pendingHtmlNavigation, pending))", code, StringComparison.Ordinal);
        Assert.Contains("FailurePanel.Visibility = Visibility.Visible", code, StringComparison.Ordinal);
    }

    [Fact]
    public void How_to_use_pipeline_logs_each_meaningful_load_stage()
    {
        var code = ReadPageCode();
        foreach (var stageLog in new[]
        {
            "How to Use activation entered.",
            "Document source selected.",
            "Remote Markdown download completed.",
            "WebView2 control created lazily.",
            "CoreWebView2 initialization completed.",
            "HTML navigation started.",
            "HTML navigation completed."
        })
        {
            Assert.Contains($"\"{stageLog}\"", code, StringComparison.Ordinal);
        }

        Assert.Contains("(\"Stage\", stage)", code, StringComparison.Ordinal);
        Assert.Contains("(\"MarkdownLength\", markdown.Length)", code, StringComparison.Ordinal);
        Assert.Contains("(\"HtmlLength\", html.Length)", code, StringComparison.Ordinal);
        Assert.Contains("(\"WebErrorStatus\", navigation.WebErrorStatus)", code, StringComparison.Ordinal);
        Assert.Contains("(\"NavigationId\", navigation.NavigationId)", code, StringComparison.Ordinal);
        Assert.Contains("(\"MatchedPendingDocument\", matchedPendingDocument)", code, StringComparison.Ordinal);
        Assert.Contains("(\"RetryAvailable\", true)", code, StringComparison.Ordinal);
        Assert.Contains("RetryButton_Click", code, StringComparison.Ordinal);
    }

    private static string ReadPageCode() => File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "SteamInputAddonforClaw.UI", "Views", "HowToUsePage.xaml.cs"));

    private const string GeneratedHtml = "<!doctype html><html><body><p>한글 + English</p></body></html>";

    private static string CreateDataUri(string metadata, string html)
        => $"data:{metadata},{Uri.EscapeDataString(html)}";

    private static string CreateBase64DataUri(string metadata, string html)
        => $"data:{metadata},{Convert.ToBase64String(Encoding.UTF8.GetBytes(html))}";

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
