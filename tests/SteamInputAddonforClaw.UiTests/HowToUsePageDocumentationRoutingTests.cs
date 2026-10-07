using System.Globalization;
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
    public void WebView_navigation_is_cancelled_except_for_the_generated_blank_document(
        string uri,
        bool shouldCancel)
    {
        Assert.Equal(shouldCancel, HowToUsePage.ShouldCancelWebViewNavigation(uri));
    }

    [Fact]
    public void How_to_use_uses_app_owned_html_and_does_not_navigate_to_github_pages()
    {
        var code = ReadPageCode();

        Assert.Contains("webView.NavigateToString(html)", code, StringComparison.Ordinal);
        Assert.Contains("NavigationStarting += DocumentationWebView_NavigationStarting", code, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = ShouldCancelWebViewNavigation(e.Uri)", code, StringComparison.Ordinal);
        Assert.Contains("WebMessageReceived += DocumentationWebView_WebMessageReceived", code, StringComparison.Ordinal);
        Assert.Contains("catch (Exception exception)", code, StringComparison.Ordinal);
        Assert.Contains("Windows.System.Launcher.LaunchUriAsync(uri)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("webView.Source", code, StringComparison.Ordinal);
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

        var navigationAwait = code.IndexOf("var navigation = await NavigateToStringAsync(webView, html);", StringComparison.Ordinal);
        var failedNavigationCheck = code.IndexOf("if (!navigation.IsSuccess)", navigationAwait, StringComparison.Ordinal);
        var loadedCommit = code.IndexOf("_loaded = true;", failedNavigationCheck, StringComparison.Ordinal);

        Assert.True(navigationAwait >= 0);
        Assert.True(failedNavigationCheck > navigationAwait);
        Assert.True(loadedCommit > failedNavigationCheck);
        var eventSubscription = code.IndexOf("webView.NavigationCompleted += OnCompleted", StringComparison.Ordinal);
        var navigateCall = code.IndexOf("webView.NavigateToString(html)", StringComparison.Ordinal);
        Assert.True(eventSubscription >= 0 && navigateCall > eventSubscription);
        Assert.Contains("completion.TrySetResult(args)", code, StringComparison.Ordinal);
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
        Assert.Contains("RetryButton_Click", code, StringComparison.Ordinal);
    }

    private static string ReadPageCode() => File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "SteamInputAddonforClaw.UI", "Views", "HowToUsePage.xaml.cs"));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
