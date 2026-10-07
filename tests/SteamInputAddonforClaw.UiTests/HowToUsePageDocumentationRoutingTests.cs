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
        var code = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "SteamInputAddonforClaw.UI", "Views", "HowToUsePage.xaml.cs"));

        Assert.Contains("DocumentationWebView.NavigateToString(html)", code, StringComparison.Ordinal);
        Assert.Contains("NavigationStarting += DocumentationWebView_NavigationStarting", code, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = ShouldCancelWebViewNavigation(e.Uri)", code, StringComparison.Ordinal);
        Assert.Contains("WebMessageReceived += DocumentationWebView_WebMessageReceived", code, StringComparison.Ordinal);
        Assert.Contains("catch (Exception exception)", code, StringComparison.Ordinal);
        Assert.Contains("Windows.System.Launcher.LaunchUriAsync(uri)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DocumentationWebView.Source", code, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
