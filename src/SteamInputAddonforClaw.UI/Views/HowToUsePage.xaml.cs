using Microsoft.UI.Xaml.Controls;
using System.Globalization;

namespace SteamInputAddonforClaw.Views;

public sealed partial class HowToUsePage : UserControl
{
    internal const string EnglishDocumentationUrl =
        "https://github.com/onehoon/SteamInputAddonforClaw#readme";
    internal const string KoreanDocumentationUrl =
        "https://github.com/onehoon/SteamInputAddonforClaw/wiki/Korean-User-Guide";

    private bool _activated;

    public HowToUsePage()
    {
        InitializeComponent();
    }

    internal void Activate()
    {
        if (_activated) return;

        _activated = true;
        var documentationUrl = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko"
            ? KoreanDocumentationUrl
            : EnglishDocumentationUrl;
        DocumentationWebView.Source = new Uri(documentationUrl);
    }
}
