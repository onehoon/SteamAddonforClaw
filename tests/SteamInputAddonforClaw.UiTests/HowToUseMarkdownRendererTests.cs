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
    }
}
