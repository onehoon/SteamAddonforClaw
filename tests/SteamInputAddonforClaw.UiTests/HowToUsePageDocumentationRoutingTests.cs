using System.Globalization;
using SteamInputAddonforClaw.Views;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class HowToUsePageDocumentationRoutingTests
{
    [Fact]
    public void Korean_ui_routes_to_the_repository_korean_guide()
    {
        Assert.Equal(
            "https://github.com/onehoon/SteamAddonforClaw/blob/main/docs/howtouse/README_KO.md",
            HowToUsePage.ResolveDocumentationUrl(CultureInfo.GetCultureInfo("ko-KR")));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ja-JP")]
    public void Non_korean_ui_routes_to_the_readme(string cultureName)
    {
        Assert.Equal(
            HowToUsePage.EnglishDocumentationUrl,
            HowToUsePage.ResolveDocumentationUrl(CultureInfo.GetCultureInfo(cultureName)));
    }
}
