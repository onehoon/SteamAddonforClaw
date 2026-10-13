using Microsoft.UI.Xaml;
using SteamInputAddonforClaw.UI;
using System.Xml.Linq;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MainUiThemeTests
{
    [Theory]
    [InlineData("System")]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Preference_round_trips_all_supported_values(string themeName)
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var theme = Enum.Parse<MainUiTheme>(themeName);

        Assert.True(store.TrySave(theme, out var saveFailure), saveFailure?.ToString());
        Assert.Equal(theme, store.Load(out var loadFailure));
        Assert.Null(loadFailure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("automatic")]
    [InlineData("System/Dark")]
    public void Missing_or_invalid_preference_uses_system(string? storedValue)
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        if (storedValue is not null)
            File.WriteAllText(store.PreferenceFilePath, storedValue);

        Assert.Equal(MainUiTheme.System, store.Load(out var failure));
        Assert.Null(failure);
    }

    [Fact]
    public void Preference_is_saved_beside_the_canonical_log_directory()
    {
        using var directory = new TemporaryDirectory();
        var dataRoot = Path.Combine(directory.Path, "SteamInputAddonforClaw-Data");
        var store = new MainUiThemePreferenceStore(Path.Combine(dataRoot, "logs"));

        Assert.Equal(Path.Combine(dataRoot, "main-ui-theme.txt"), store.PreferenceFilePath);
    }

    [Fact]
    public void Failed_preference_write_returns_failure_without_throwing()
    {
        using var directory = new TemporaryDirectory();
        var dataRootBlocker = Path.Combine(directory.Path, "SteamInputAddonforClaw-Data");
        Directory.CreateDirectory(directory.Path);
        File.WriteAllText(dataRootBlocker, "not a directory");
        var store = new MainUiThemePreferenceStore(Path.Combine(dataRootBlocker, "logs"));

        Assert.False(store.TrySave(MainUiTheme.Dark, out var failure));
        Assert.NotNull(failure);
        Assert.Equal(MainUiTheme.System, store.Load(out _));
    }

    [Theory]
    [InlineData(1, ElementTheme.Light)]
    [InlineData(0, ElementTheme.Dark)]
    [InlineData(null, ElementTheme.Light)]
    public void System_theme_resolves_the_windows_app_preference_or_known_fallback(
        int? appsUseLightTheme,
        ElementTheme expected)
    {
        Assert.Equal(expected, MainUiThemePreferenceStore.ResolveSystemTheme(appsUseLightTheme, ElementTheme.Light));
    }

    [Fact]
    public void Settings_theme_card_is_between_updates_and_gaming_home_with_exact_choices()
    {
        var path = Source("src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml");
        var xaml = File.ReadAllText(path);
        var document = XDocument.Load(path);
        var stack = document.Descendants().Single(element => element.Name.LocalName == "StackPanel"
            && element.Elements().Any(child =>
                (string?)child.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "UpdateCard"));
        var cardOrder = stack.Elements()
            .Select(element => (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")))
            .ToArray();
        var themeCard = document.Descendants().Single(element =>
            element.Name.LocalName == "SettingsCard"
            && (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "AppThemeCard");
        var comboBox = themeCard.Descendants().Single(element => element.Name.LocalName == "ComboBox");

        Assert.True(Array.IndexOf(cardOrder, "UpdateCard") < Array.IndexOf(cardOrder, "AppThemeCard"));
        Assert.True(Array.IndexOf(cardOrder, "AppThemeCard") < Array.IndexOf(cardOrder, "GamingHomeExpander"));
        Assert.Equal("App theme", (string?)themeCard.Attribute("Header"));
        Assert.Equal("Choose the main app appearance.", (string?)themeCard.Attribute("Description"));
        var themeHeaderIcon = themeCard.Elements().Single(element => element.Name.LocalName == "SettingsCard.HeaderIcon");
        var themeFontIcon = themeHeaderIcon.Elements().Single(element => element.Name.LocalName == "FontIcon");
        Assert.Equal("\uE7A1", (string?)themeFontIcon.Attribute("Glyph"));
        Assert.Equal(new[] { "System", "Light", "Dark" }, comboBox.Elements()
            .Where(element => element.Name.LocalName == "ComboBoxItem")
            .Select(element => (string?)element.Attribute("Tag")));
        Assert.Contains("SelectionChanged=\"AppThemeComboBox_SelectionChanged\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Programmatic_theme_selection_is_suppressed_and_user_changes_are_forwarded()
    {
        var source = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "Views", "SettingsPage.xaml.cs"));
        var setterStart = source.IndexOf("internal void SetAppThemeSelection", StringComparison.Ordinal);
        var setterEnd = source.IndexOf("private void AppThemeComboBox_SelectionChanged", setterStart, StringComparison.Ordinal);
        var setter = source[setterStart..setterEnd];
        var handler = source[setterEnd..source.IndexOf("private async Task RefreshAppUpdateAsync", setterEnd, StringComparison.Ordinal)];

        Assert.Contains("_isApplyingAppThemeSelection = true", setter, StringComparison.Ordinal);
        Assert.Contains("AppThemeComboBox.SelectedItem =", setter, StringComparison.Ordinal);
        Assert.Contains("finally", setter, StringComparison.Ordinal);
        Assert.Contains("_isApplyingAppThemeSelection = false", setter, StringComparison.Ordinal);
        Assert.Contains("if (_isApplyingAppThemeSelection", handler, StringComparison.Ordinal);
        Assert.Contains("AppThemeSelectionRequested?.Invoke(theme)", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_themes_its_full_root_and_never_mutates_application_requested_theme()
    {
        var xamlPath = Source("src", "SteamInputAddonforClaw.UI", "MainWindow.xaml");
        var window = XDocument.Load(xamlPath);
        var root = window.Descendants().Single(element =>
            element.Name.LocalName == "Grid"
            && (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "MainAppRoot");
        var mainWindowCode = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "MainWindow.xaml.cs"));
        var themeMethodStart = mainWindowCode.IndexOf("private void ApplyMainUiTheme()", StringComparison.Ordinal);
        var themeMethodEnd = mainWindowCode.IndexOf("private ElementTheme ReadWindowsAppTheme()", themeMethodStart, StringComparison.Ordinal);
        var themeMethod = mainWindowCode[themeMethodStart..themeMethodEnd];

        Assert.Contains(root.Elements(), element => element.Name.LocalName == "NavigationView");
        Assert.Contains(root.Elements(), element =>
            element.Name.LocalName == "Grid"
            && (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "PrerequisiteSetupBusyOverlay");
        Assert.Contains("MainAppRoot.RequestedTheme =", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("AppsUseLightTheme", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("_systemUiSettings.ColorValuesChanged += SystemUiSettings_ColorValuesChanged", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("var theme = _mainUiThemePreference switch", themeMethod, StringComparison.Ordinal);
        Assert.Contains("MainAppRoot.RequestedTheme = theme;", themeMethod, StringComparison.Ordinal);
        Assert.Contains("AppWindow.TitleBar.PreferredTheme = theme == ElementTheme.Dark", themeMethod, StringComparison.Ordinal);
        Assert.Contains("? TitleBarTheme.Dark", themeMethod, StringComparison.Ordinal);
        Assert.Contains(": TitleBarTheme.Light;", themeMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Current.RequestedTheme =", mainWindowCode, StringComparison.Ordinal);
        Assert.DoesNotContain("App.Current.RequestedTheme =", mainWindowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Failed_save_restores_the_last_saved_selection_before_any_theme_change()
    {
        var source = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "MainWindow.xaml.cs"));
        var start = source.IndexOf("private void OnAppThemeSelectionRequested", StringComparison.Ordinal);
        var end = source.IndexOf("private void ApplyMainUiTheme", start, StringComparison.Ordinal);
        var handler = source[start..end];

        var saveAttempt = handler.IndexOf("TrySave(theme", StringComparison.Ordinal);
        var failureStart = handler.IndexOf("if (_mainUiThemePreferenceStore is null", StringComparison.Ordinal);
        var successStart = handler.IndexOf("_mainUiThemePreference = theme", failureStart, StringComparison.Ordinal);
        var failureBranch = handler[failureStart..successStart];
        var rollback = failureBranch.IndexOf("_settingsContent?.SetAppThemeSelection(", StringComparison.Ordinal);
        var savedSelectionRestore = failureBranch.IndexOf("_mainUiThemePreference", rollback, StringComparison.Ordinal);
        var apply = handler.IndexOf("ApplyMainUiTheme();", StringComparison.Ordinal);
        var successfulSelection = handler.IndexOf("_mainUiThemePreference = theme", StringComparison.Ordinal);

        Assert.True(saveAttempt >= 0);
        Assert.True(saveAttempt > failureStart);
        Assert.True(rollback >= 0);
        Assert.True(savedSelectionRestore > rollback);
        Assert.True(successfulSelection > saveAttempt);
        Assert.True(apply > successfulSelection);
        Assert.Contains("Theme could not be saved. The previous selection was kept.", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void How_to_use_webview_uses_white_default_background_before_attachment()
    {
        var source = File.ReadAllText(Source("src", "SteamInputAddonforClaw.UI", "Views", "HowToUsePage.xaml.cs"));
        var methodStart = source.IndexOf("private WebView2 EnsureDocumentationWebView()", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private void DocumentationWebView_Loaded", methodStart, StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];

        var background = method.IndexOf("DefaultBackgroundColor = Color.FromArgb(255, 255, 255, 255)", StringComparison.Ordinal);
        var attachment = method.IndexOf("DocumentationWebViewHost.Children.Add(webView)", StringComparison.Ordinal);
        Assert.True(background >= 0 && attachment > background);
    }

    private static MainUiThemePreferenceStore CreateStore(string root)
    {
        var logDirectory = Path.Combine(root, "SteamInputAddonforClaw-Data", "logs");
        Directory.CreateDirectory(logDirectory);
        return new MainUiThemePreferenceStore(logDirectory);
    }

    private static string Source(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
