using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlayStartupClawHudSettingsTests
{
    [Fact]
    public void Bootstrap_reads_the_canonical_sibling_settings_and_user_ini_without_writing_them()
    {
        using var paths = new TemporaryPaths();
        paths.WriteAddonSettings("{\"ClawHudEnabled\":true}");
        paths.WriteHudIni("[HUD]\r\nEnabled=false\r\nFont=SegoeUIVariable\r\nSize=1\r\n");
        var settingsBefore = File.ReadAllBytes(paths.SettingsPath);
        var iniBefore = File.ReadAllBytes(paths.IniPath);

        var result = OverlayStartupClawHudSettings.Load(paths.LogDirectory, paths.LocalAppData);

        Assert.True(result.AddonDesiredEnabled);
        Assert.Equal(FrontendClawHudFont.SegoeUiVariable, result.Font);
        Assert.Equal(1, result.SizeOffset);
        Assert.Equal(settingsBefore, File.ReadAllBytes(paths.SettingsPath));
        Assert.Equal(iniBefore, File.ReadAllBytes(paths.IniPath));
    }

    [Theory]
    [InlineData("{\"ClawHudEnabled\":true}", true)]
    [InlineData("{\"ClawHudEnabled\":false}", false)]
    [InlineData("{}", false)]
    [InlineData("{\"ClawHudEnabled\":\"true\"}", false)]
    [InlineData("[]", false)]
    [InlineData("{", false)]
    public void Addon_enablement_requires_a_valid_json_boolean(string json, bool expected)
    {
        using var paths = new TemporaryPaths();
        paths.WriteAddonSettings(json);

        var result = OverlayStartupClawHudSettings.Load(paths.LogDirectory, paths.LocalAppData);

        Assert.Equal(expected, result.AddonDesiredEnabled);
    }

    [Theory]
    [InlineData("Unispace", -2, FrontendClawHudFont.Unispace, -2)]
    [InlineData("Unispace", -1, FrontendClawHudFont.Unispace, -1)]
    [InlineData("Unispace", 0, FrontendClawHudFont.Unispace, 0)]
    [InlineData("Unispace", 1, FrontendClawHudFont.Unispace, 1)]
    [InlineData("Unispace", 2, FrontendClawHudFont.Unispace, 2)]
    [InlineData("SegoeUIVariable", -2, FrontendClawHudFont.SegoeUiVariable, -2)]
    [InlineData("SegoeUIVariable", -1, FrontendClawHudFont.SegoeUiVariable, -1)]
    [InlineData("SegoeUIVariable", 0, FrontendClawHudFont.SegoeUiVariable, 0)]
    [InlineData("SegoeUIVariable", 1, FrontendClawHudFont.SegoeUiVariable, 1)]
    [InlineData("SegoeUIVariable", 2, FrontendClawHudFont.SegoeUiVariable, 2)]
    public void Ini_font_and_all_supported_size_offsets_are_read(
        string fontToken,
        int size,
        FrontendClawHudFont expectedFont,
        int expectedSize)
    {
        using var paths = new TemporaryPaths();
        paths.WriteAddonSettings("{\"ClawHudEnabled\":true}");
        paths.WriteHudIni($"[HUD]\r\nEnabled=false\r\nFont={fontToken}\r\nSize={size}\r\n");

        var result = OverlayStartupClawHudSettings.Load(paths.LogDirectory, paths.LocalAppData);

        Assert.True(result.AddonDesiredEnabled);
        Assert.Equal(expectedFont, result.Font);
        Assert.Equal(expectedSize, result.SizeOffset);
    }

    [Theory]
    [InlineData("99", 2)]
    [InlineData("-99", -2)]
    [InlineData("invalid", 0)]
    [InlineData("", 0)]
    public void Ini_size_is_defaulted_or_clamped_like_the_clawhud_setting(string rawSize, int expected)
    {
        using var paths = new TemporaryPaths();
        paths.WriteHudIni($"[HUD]\r\nFont=unknown-font\r\nSize={rawSize}\r\n");

        var result = OverlayStartupClawHudSettings.Load(paths.LogDirectory, paths.LocalAppData);

        Assert.False(result.AddonDesiredEnabled);
        Assert.Equal(FrontendClawHudFont.Unispace, result.Font);
        Assert.Equal(expected, result.SizeOffset);
    }

    [Fact]
    public void Missing_files_and_noncanonical_log_paths_use_safe_defaults()
    {
        using var paths = new TemporaryPaths();

        var missingFiles = OverlayStartupClawHudSettings.Load(paths.LogDirectory, paths.LocalAppData);
        var noncanonicalLogDirectory = Path.Combine(paths.Root, "overlay", "logs");
        Directory.CreateDirectory(noncanonicalLogDirectory);
        File.WriteAllText(Path.Combine(paths.Root, "overlay", "settings.json"), "{\"ClawHudEnabled\":true}");
        var invalidDataRoot = OverlayStartupClawHudSettings.Load(noncanonicalLogDirectory, paths.LocalAppData);

        Assert.False(missingFiles.AddonDesiredEnabled);
        Assert.Equal(FrontendClawHudFont.SegoeUiVariable, missingFiles.Font);
        Assert.Equal(0, missingFiles.SizeOffset);
        Assert.False(invalidDataRoot.AddonDesiredEnabled);
        Assert.False(File.Exists(Path.Combine(paths.Root, "overlay", "settings.ini")));
    }

    [Fact]
    public void Incomplete_ipc_keeps_bootstrap_geometry_until_authoritative_values_arrive()
    {
        var bootstrap = new OverlayStartupClawHudSettings(
            true, FrontendClawHudFont.Unispace, 0);

        Assert.Equal(34, bootstrap.ResolveTopInsetPx(authoritativeEnabled: null, authoritativeSettings: null));
        Assert.Equal(34, bootstrap.ResolveTopInsetPx(authoritativeEnabled: true, authoritativeSettings: null));
        Assert.Equal(0, bootstrap.ResolveTopInsetPx(authoritativeEnabled: false, authoritativeSettings: null));

        var ipcSettings = new FrontendClawHudSettingsSnapshot(
            FrontendClawHudDisplayMode.Always,
            2,
            FrontendClawHudFont.SegoeUiVariable,
            FrontendClawHudAlignment.Left,
            FrontendClawHudBackgroundMode.FullWidth,
            50,
            false,
            null);
        Assert.Equal(40, bootstrap.ResolveTopInsetPx(authoritativeEnabled: true, ipcSettings));
        Assert.Equal(0, bootstrap.ResolveTopInsetPx(authoritativeEnabled: false, ipcSettings));

        var acceptedLocalPreview = ipcSettings with { Font = FrontendClawHudFont.Unispace, HudSizeOffset = -2 };
        Assert.Equal(32, bootstrap.ResolveTopInsetPx(authoritativeEnabled: true, acceptedLocalPreview));
    }

    [Fact]
    public void App_installs_the_geometry_bootstrap_before_hidden_window_preparation_and_first_show_reuses_it()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "App.xaml.cs"));
        var clawHud = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.ClawHud.cs"));
        var presentation = File.ReadAllText(Path.Combine(root, "src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Presentation.cs"));
        var show = Method(presentation, "internal async Task ShowForPocAsync()", "internal Task HideForPocAsync()");

        var load = app.IndexOf("OverlayStartupClawHudSettings.Load(OverlayLog.DirectoryPath)", StringComparison.Ordinal);
        var prepareHidden = app.IndexOf("_window.PrepareHidden();", StringComparison.Ordinal);
        Assert.True(load >= 0 && prepareHidden > load);
        Assert.Contains("startupClawHudSettings", app, StringComparison.Ordinal);
        Assert.Contains("_startupClawHudSettings.ResolveTopInsetPx(", clawHud, StringComparison.Ordinal);
        Assert.Contains("_window.PrepareHidden();", app, StringComparison.Ordinal);
        Assert.Contains("ConfigureWindow();", presentation, StringComparison.Ordinal);
        Assert.True(show.IndexOf("ConfigureWindow();", StringComparison.Ordinal)
            < show.IndexOf("WindowInterop.ShowWithoutActivation(this)", StringComparison.Ordinal));
        Assert.Contains("topInsetPx == _lastAppliedClawHudTopInsetPx", presentation, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static string Method(string source, string startSignature, string nextSignature)
    {
        var start = source.IndexOf(startSignature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method was not found: {startSignature}");
        var end = source.IndexOf(nextSignature, start + startSignature.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Method end was not found: {nextSignature}");
        return source[start..end];
    }

    private sealed class TemporaryPaths : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ClawHudBootstrapTests", Guid.NewGuid().ToString("N"));
        internal string DataRoot => Path.Combine(Root, "SteamInputAddonforClaw-Data");
        internal string LogDirectory => Path.Combine(DataRoot, "logs");
        internal string SettingsPath => Path.Combine(DataRoot, "settings.json");
        internal string LocalAppData => Path.Combine(Root, "LocalAppData");
        internal string IniPath => Path.Combine(LocalAppData, "ClawHUD", "settings.ini");

        internal void WriteAddonSettings(string json)
        {
            Directory.CreateDirectory(DataRoot);
            File.WriteAllText(SettingsPath, json);
        }

        internal void WriteHudIni(string contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IniPath)!);
            File.WriteAllText(IniPath, contents);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
