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
}
