using System.Globalization;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class HowToUseMarkdownRendererTests
{
    [Fact]
    public void Markdown_body_renders_common_guide_features_and_github_style_anchors()
    {
        const string markdown = """
            # Title

            **Bold**

            - One
            - Two

            | A | B |
            |---|---|
            | 1 | 2 |

            [Section](#section)

            ## Section

            ```text
            hello
            ```
            """;

        var html = HowToUseMarkdownRenderer.RenderMarkdownBody(markdown);

        Assert.Contains("<h1", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Bold</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<ul>", html, StringComparison.Ordinal);
        Assert.Contains("<table>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#section\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"section\"", html, StringComparison.Ordinal);
        Assert.Contains("<pre>", html, StringComparison.Ordinal);
        Assert.Contains("<code", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_authored_raw_html_is_not_emitted_as_executable_markup()
    {
        var html = HowToUseMarkdownRenderer.RenderMarkdownBody("<script>alert('x')</script>");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void App_html_shell_embeds_rendered_body_and_uses_local_light_dark_css()
    {
        var source = HowToUseMarkdownRenderer.ResolveSource(CultureInfo.GetCultureInfo("ko-KR"));
        var html = HowToUseMarkdownRenderer.BuildHtml("# 지원 기기", source);

        Assert.Contains("<base href=\"https://raw.githubusercontent.com/onehoon/SteamAddonforClaw/main/docs/howtouse/\">", html, StringComparison.Ordinal);
        Assert.Contains("<h1 id=\"지원-기기\">지원 기기</h1>", html, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-color-scheme: dark)", html, StringComparison.Ordinal);
        Assert.Contains("max-width: 1100px", html, StringComparison.Ordinal);
        Assert.DoesNotContain("github.com/onehoon/SteamAddonforClaw/blob", html, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault()", html, StringComparison.Ordinal);
        Assert.Contains("scrollIntoView", html, StringComparison.Ordinal);
        Assert.Contains("type: \"open-link\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Fragment_links_remain_internal_and_http_links_are_preserved()
    {
        var source = HowToUseMarkdownRenderer.ResolveSource(CultureInfo.GetCultureInfo("en-US"));

        Assert.Null(HowToUseMarkdownRenderer.ResolveExternalLink(source, "#shortcut"));
        Assert.Equal(
            new Uri("https://example.com/docs"),
            HowToUseMarkdownRenderer.ResolveExternalLink(source, "https://example.com/docs"));
        Assert.Equal(
            new Uri("http://example.com/docs"),
            HowToUseMarkdownRenderer.ResolveExternalLink(source, "http://example.com/docs"));
    }

    [Fact]
    public void Relative_repository_links_resolve_to_github_blob_pages()
    {
        var korean = HowToUseMarkdownRenderer.ResolveSource(CultureInfo.GetCultureInfo("ko-KR"));
        var english = HowToUseMarkdownRenderer.ResolveSource(CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal(
            new Uri("https://github.com/onehoon/SteamAddonforClaw/blob/main/LICENSE"),
            HowToUseMarkdownRenderer.ResolveExternalLink(korean, "../../LICENSE"));
        Assert.Equal(
            new Uri("https://github.com/onehoon/SteamAddonforClaw/blob/main/docs/howtouse/README_KO.md"),
            HowToUseMarkdownRenderer.ResolveExternalLink(english, "docs/howtouse/README_KO.md"));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("steam://open/bigpicture")]
    [InlineData("mailto:help@example.com")]
    [InlineData("//evil.example/path")]
    [InlineData("../../../outside")]
    [InlineData("%2e%2e/%2e%2e/%2e%2e/outside")]
    public void Unsupported_link_schemes_and_protocol_relative_links_are_rejected(string href)
    {
        var source = HowToUseMarkdownRenderer.ResolveSource(CultureInfo.GetCultureInfo("en-US"));

        Assert.Null(HowToUseMarkdownRenderer.ResolveExternalLink(source, href));
    }

    [Fact]
    public void Empty_and_oversized_links_are_rejected()
    {
        var source = HowToUseMarkdownRenderer.ResolveSource(CultureInfo.GetCultureInfo("en-US"));

        Assert.Null(HowToUseMarkdownRenderer.ResolveExternalLink(source, string.Empty));
        Assert.Null(HowToUseMarkdownRenderer.ResolveExternalLink(source, new string('a', 2049)));
    }
}
