using Microsoft.UI.Xaml.Controls;
using System.Globalization;

namespace SteamInputAddonforClaw.Views;

public sealed partial class HowToUsePage : UserControl
{
    internal const string EnglishDocumentationUrl =
        "https://github.com/onehoon/SteamAddonforClaw#readme";
    internal const string KoreanDocumentationUrl =
        "https://github.com/onehoon/SteamAddonforClaw/blob/main/docs/howtouse/README_KO.md";

    private bool _activated;

    public HowToUsePage()
    {
        InitializeComponent();
    }

    internal void Activate()
    {
        if (_activated) return;

        _activated = true;
        var documentationUrl = ResolveDocumentationUrl(CultureInfo.CurrentUICulture);
        DocumentationWebView.Source = new Uri(documentationUrl);
    }

    internal static string ResolveDocumentationUrl(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return string.Equals(culture.TwoLetterISOLanguageName, "ko", StringComparison.OrdinalIgnoreCase)
            ? KoreanDocumentationUrl
            : EnglishDocumentationUrl;
    }
}
