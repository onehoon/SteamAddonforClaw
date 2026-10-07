using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using System.Globalization;
using System.Text.Encodings.Web;

namespace SteamInputAddonforClaw.Views;

internal sealed record HowToUseDocumentSource(string RawMarkdownUrl, string RepositoryDirectory)
{
    internal string RawRepositoryBaseUrl =>
        $"https://raw.githubusercontent.com/onehoon/SteamAddonforClaw/main/{RepositoryDirectory}";
}

internal static class HowToUseMarkdownRenderer
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    internal static HowToUseDocumentSource ResolveSource(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return string.Equals(culture.TwoLetterISOLanguageName, "ko", StringComparison.OrdinalIgnoreCase)
            ? new HowToUseDocumentSource(
                "https://raw.githubusercontent.com/onehoon/SteamAddonforClaw/main/docs/howtouse/README_KO.md",
                "docs/howtouse/")
            : new HowToUseDocumentSource(
                "https://raw.githubusercontent.com/onehoon/SteamAddonforClaw/main/README.md",
                string.Empty);
    }

    internal static string RenderMarkdownBody(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return Markdown.ToHtml(markdown, MarkdownPipeline);
    }

    internal static string BuildHtml(string markdown, HowToUseDocumentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var language = source.RepositoryDirectory.Length == 0 ? "en" : "ko";
        var baseUrl = HtmlEncoder.Default.Encode(source.RawRepositoryBaseUrl);
        var bodyHtml = RenderMarkdownBody(markdown);

        return $$"""
            <!doctype html>
            <html lang="{{language}}">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <base href="{{baseUrl}}">
                <style>
                    html { color-scheme: light dark; }
                    body {
                        box-sizing: border-box;
                        max-width: 1100px;
                        margin: 0 auto;
                        padding: 32px 40px 64px;
                        font-family: "Segoe UI Variable", "Segoe UI", sans-serif;
                        font-size: 16px;
                        line-height: 1.6;
                        color: #202124;
                        background: #ffffff;
                    }
                    a { color: #0067c0; }
                    img { max-width: 100%; height: auto; }
                    pre {
                        overflow-x: auto;
                        padding: 16px;
                        border-radius: 6px;
                        background: #f0f0f0;
                    }
                    code { font-family: Consolas, "Cascadia Code", monospace; }
                    pre code { padding: 0; background: transparent; }
                    blockquote { margin-left: 0; padding-left: 16px; border-left: 3px solid #9a9a9a; }
                    table { display: block; max-width: 100%; overflow-x: auto; border-collapse: collapse; }
                    th, td { padding: 8px 12px; border: 1px solid #a0a0a0; text-align: left; }
                    hr { border: 0; border-top: 1px solid #a0a0a0; }
                    @media (prefers-color-scheme: dark) {
                        body { color: #f3f5f7; background: #0e141b; }
                        a { color: #7dbdff; }
                        pre { background: #1c2631; }
                        blockquote { border-left-color: #727e89; }
                        th, td { border-color: #5c6874; }
                        hr { border-top-color: #5c6874; }
                    }
                </style>
            </head>
            <body>
            {{bodyHtml}}
            </body>
            </html>
            """;
    }
}
