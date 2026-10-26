using System.Xml.Linq;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Overlay;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class OverlaySteamGameArtworkTests
{
    private const uint AppId = 570;

    [Fact]
    public void Artwork_requires_an_available_profile_for_a_native_steam_target()
    {
        using var tree = new SteamTree();
        tree.WriteManifest(AppId);
        tree.WriteArtwork(Path.Combine(tree.Root, "userdata", "1", "config", "grid", $"{AppId}_hero.png"));

        Assert.Equal(AppId, SteamGameArtworkResolver.EligibleSteamAppId(SteamPage(AppId)));
        Assert.Empty(SteamGameArtworkResolver.ResolveArtworkCandidates(tree.Root,
            new QuickSettingsPageSnapshot(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForXbox("xbox:game"), true, null, [], []), 1));
        Assert.Empty(SteamGameArtworkResolver.ResolveArtworkCandidates(tree.Root,
            QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, null, "No game"), 1));
        Assert.Empty(SteamGameArtworkResolver.ResolveArtworkCandidates(tree.Root,
            QuickSettingsPageSnapshot.Unavailable(QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(AppId), "Loading"), 1));
        Assert.Empty(SteamGameArtworkResolver.ResolveArtworkCandidates(tree.Root,
            new QuickSettingsPageSnapshot(QuickSettingsPageId.Device, QuickSettingsProfileTarget.ForSteam(AppId), true, null, [], []), 1));
        Assert.Empty(SteamGameArtworkResolver.ResolveArtworkCandidates(tree.Root,
            new QuickSettingsPageSnapshot(QuickSettingsPageId.Profile, new(QuickSettingsProfileTargetKind.Steam, SteamAppId: 0), true, null, [], []), 1));

        // Steam shortcut targets can carry an AppID, but without the exact native app manifest
        // they must not expose either account art or the shared Steam cache.
        Assert.Empty(SteamGameArtworkResolver.ResolveArtworkCandidates(tree.Root, SteamPage(571), 1));
    }

    [Fact]
    public void Exact_native_manifest_in_an_external_library_qualifies_but_a_prefix_does_not()
    {
        using var tree = new SteamTree();
        var externalLibrary = Path.Combine(tree.Root, "..", "Library");
        Directory.CreateDirectory(Path.Combine(externalLibrary, "steamapps"));
        tree.WriteLibraryFolders(externalLibrary);
        tree.WriteManifest(571, externalLibrary);
        tree.WriteManifest(5701);

        Assert.True(SteamGameArtworkResolver.HasNativeSteamManifest(tree.Root, 571));
        Assert.False(SteamGameArtworkResolver.HasNativeSteamManifest(tree.Root, 570));
        Assert.Empty(SteamGameArtworkResolver.ResolveArtworkCandidates(tree.Root, SteamPage(570), null));
    }

    [Fact]
    public void Custom_hero_then_wide_cover_grid_art_precede_all_builtin_art()
    {
        using var tree = new SteamTree();
        tree.WriteManifest(AppId);
        var grid = Path.Combine(tree.Root, "userdata", "1", "config", "grid");
        var customHero = new[] { $"{AppId}_hero.png", $"{AppId}_hero.jpg", $"{AppId}_hero.jpeg" }
            .Select(name => tree.WriteArtwork(Path.Combine(grid, name))).ToArray();
        var customGrid = new[] { $"{AppId}.png", $"{AppId}.jpg", $"{AppId}.jpeg" }
            .Select(name => tree.WriteArtwork(Path.Combine(grid, name))).ToArray();
        var builtInHero = tree.WriteArtwork(Path.Combine(tree.Root, "appcache", "librarycache", AppId.ToString(), "library_hero.jpg"));
        var builtInHeader = tree.WriteArtwork(Path.Combine(tree.Root, "appcache", "librarycache", AppId.ToString(), "library_header.jpg"));

        Assert.Equal(customHero.Concat(customGrid).Concat([builtInHero, builtInHeader]),
            SteamGameArtworkResolver.ResolveArtworkCandidates(tree.Root, SteamPage(AppId), 1));
    }

    [Fact]
    public void Missing_custom_art_falls_back_to_direct_hash_and_legacy_library_cache_layouts()
    {
        using var tree = new SteamTree();
        tree.WriteManifest(AppId);
        var cache = Path.Combine(tree.Root, "appcache", "librarycache");
        var appDirectory = Path.Combine(cache, AppId.ToString());
        var directHero = tree.WriteArtwork(Path.Combine(appDirectory, "library_hero.jpg"));
        var hashA = tree.WriteArtwork(Path.Combine(appDirectory, "aaa", "library_hero.jpg"));
        var hashB = tree.WriteArtwork(Path.Combine(appDirectory, "bbb", "library_hero.jpg"));
        var flatHero = tree.WriteArtwork(Path.Combine(cache, $"{AppId}_library_hero.jpg"));
        var directLibraryHeader = tree.WriteArtwork(Path.Combine(appDirectory, "library_header.jpg"));
        var hashLibraryHeader = tree.WriteArtwork(Path.Combine(appDirectory, "aaa", "library_header.jpg"));
        var directWideCover = tree.WriteArtwork(Path.Combine(appDirectory, "header.jpg"));
        var hashWideCover = tree.WriteArtwork(Path.Combine(appDirectory, "bbb", "header.jpg"));
        var nestedOnly = tree.WriteArtwork(Path.Combine(appDirectory, "deep", "nested", "library_hero.jpg"));
        var portrait = tree.WriteArtwork(Path.Combine(appDirectory, $"{AppId}p.jpg"));
        var otherApp = tree.WriteArtwork(Path.Combine(cache, "571", "library_hero.jpg"));
        var flatHeader = tree.WriteArtwork(Path.Combine(cache, $"{AppId}_library_header.jpg"));

        var candidates = SteamGameArtworkResolver.ResolveArtworkCandidates(tree.Root, SteamPage(AppId), null);

        Assert.Equal([
            directHero, hashA, hashB, flatHero,
            directLibraryHeader, hashLibraryHeader, directWideCover, hashWideCover, flatHeader
        ], candidates);
        Assert.DoesNotContain(nestedOnly, candidates);
        Assert.DoesNotContain(portrait, candidates);
        Assert.DoesNotContain(otherApp, candidates);
    }

    [Fact]
    public void Account_selection_uses_active_then_most_recent_then_only_numeric_directory()
    {
        using var tree = new SteamTree();
        var userdata = Path.Combine(tree.Root, "userdata");
        Directory.CreateDirectory(Path.Combine(userdata, "1"));
        Directory.CreateDirectory(Path.Combine(userdata, "2"));
        var loginUsers = Path.Combine(tree.Root, "config", "loginusers.vdf");
        Directory.CreateDirectory(Path.GetDirectoryName(loginUsers)!);
        File.WriteAllText(loginUsers, "\"users\" { \"76561197960265730\" { \"MostRecent\" \"1\" } }");

        Assert.Equal(1u, SteamGameArtworkResolver.ResolveUserdataAccountId(userdata, loginUsers, 1));
        Assert.Equal(2u, SteamGameArtworkResolver.ResolveUserdataAccountId(userdata, loginUsers, null));

        File.WriteAllText(loginUsers, "\"users\" { \"76561197960265729\" { \"MostRecent\" \"0\" } }");
        Assert.Null(SteamGameArtworkResolver.ResolveUserdataAccountId(userdata, loginUsers, null));

        Directory.Delete(Path.Combine(userdata, "2"));
        Assert.Equal(1u, SteamGameArtworkResolver.ResolveUserdataAccountId(userdata, loginUsers, null));
    }

    [Fact]
    public void Unknown_account_never_mixes_custom_art_from_an_arbitrary_userdata_folder()
    {
        using var tree = new SteamTree();
        tree.WriteManifest(AppId);
        tree.WriteArtwork(Path.Combine(tree.Root, "userdata", "1", "config", "grid", $"{AppId}_hero.png"));
        tree.WriteArtwork(Path.Combine(tree.Root, "userdata", "2", "config", "grid", $"{AppId}_hero.png"));
        var builtIn = tree.WriteArtwork(Path.Combine(tree.Root, "appcache", "librarycache", AppId.ToString(), "library_hero.jpg"));

        Assert.Equal([builtIn], SteamGameArtworkResolver.ResolveArtworkCandidates(tree.Root, SteamPage(AppId), null));
    }

    [Fact]
    public void Invalid_optional_metadata_and_missing_roots_are_silent_cache_misses()
    {
        using var tree = new SteamTree();
        tree.WriteManifest(AppId);
        File.WriteAllText(Path.Combine(tree.Root, "steamapps", "libraryfolders.vdf"), "malformed { metadata");
        var rootArtwork = tree.WriteArtwork(Path.Combine(tree.Root, "appcache", "librarycache", AppId.ToString(), "library_hero.jpg"));

        Assert.Equal([rootArtwork], SteamGameArtworkResolver.ResolveArtworkCandidates(tree.Root, SteamPage(AppId), null));
        Assert.Empty(SteamGameArtworkResolver.ResolveArtworkCandidates(null, SteamPage(AppId), null));
        Assert.False(SteamGameArtworkResolver.HasNativeSteamManifest(tree.Root, 571));
    }

    [Fact]
    public void Steam_root_selection_preserves_the_existing_registry_precedence_and_requires_steam_exe()
    {
        using var tree = new SteamTree();
        var first = Path.Combine(tree.Root, "first");
        var second = Path.Combine(tree.Root, "second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(second, "steam.exe"), string.Empty);

        Assert.Equal(Path.GetFullPath(second), SteamGameArtworkResolver.SelectValidSteamRoot([first, second]));
        File.WriteAllText(Path.Combine(first, "steam.exe"), string.Empty);
        Assert.Equal(Path.GetFullPath(first), SteamGameArtworkResolver.SelectValidSteamRoot([first, second]));
    }

    [Fact]
    public void Artwork_layer_is_right_column_only_behind_title_and_scroll_and_does_not_extend_page_content()
    {
        var xaml = XDocument.Load(ReadRepositoryFile("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml"));
        var rightColumn = xaml.Descendants().Single(element => element.Name.LocalName == "Grid"
            && Attribute(element, "Grid.Column") == "1" && Attribute(element, "Grid.Row") == "0");
        var visualChildren = rightColumn.Elements().Where(element => element.Name.LocalName != "RowDefinitions").ToArray();
        var artworkIndex = Array.FindIndex(visualChildren, element => Attribute(element, "Name") == "ProfileArtworkHost");
        var titleIndex = Array.FindIndex(visualChildren, element => Attribute(element, "Name") == "PageTitle");
        var scrollIndex = Array.FindIndex(visualChildren, element => Attribute(element, "Name") == "BodyScroll");
        Assert.True(artworkIndex >= 0 && titleIndex >= 0 && scrollIndex >= 0);
        var host = visualChildren[artworkIndex];

        Assert.True(artworkIndex >= 0 && artworkIndex < titleIndex && titleIndex < scrollIndex);
        Assert.Equal("2", Attribute(host, "Grid.RowSpan"));
        Assert.Equal("240", Attribute(host, "Height"));
        Assert.Equal("Stretch", Attribute(host, "HorizontalAlignment"));
        Assert.Equal("Top", Attribute(host, "VerticalAlignment"));
        Assert.Equal("False", Attribute(host, "IsHitTestVisible"));
        Assert.Equal("Collapsed", Attribute(host, "Visibility"));
        Assert.Null(Attribute(host, "Width"));
        Assert.Equal("Transparent", Attribute(visualChildren[scrollIndex], "Background"));

        var image = host.Elements().Single(element => element.Name.LocalName == "Image");
        Assert.Equal("UniformToFill", Attribute(image, "Stretch"));
        var profile = File.ReadAllText(ReadRepositoryFile("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs"));
        Assert.DoesNotContain("ProfileArtwork", profile[profile.IndexOf("private FrameworkElement BuildProfilePage()", StringComparison.Ordinal)..
            profile.IndexOf("internal bool TryHandleBack()", StringComparison.Ordinal)]);
        Assert.Contains("Grid.Row=\"1\"", File.ReadAllText(ReadRepositoryFile("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml")));
    }

    [Fact]
    public void Artwork_refresh_reuses_same_target_and_clears_on_tab_change_hide_and_stale_async_completion()
    {
        var profile = File.ReadAllText(ReadRepositoryFile("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs"));
        var entry = ExtractMethod(profile, "private void OnProfileTabSelectionChanged(bool selected)");
        var update = ExtractMethod(profile, "private void UpdateProfileArtwork(QuickSettingsPageSnapshot? page)");
        var resolve = ExtractMethod(profile, "private async Task ResolveAndApplyProfileArtworkAsync(uint appId, QuickSettingsPageSnapshot page)");
        var loader = ExtractMethod(profile, "private async Task<BitmapImage?> TryLoadProfileArtworkAsync(IReadOnlyList<string> paths)");
        var presentation = File.ReadAllText(ReadRepositoryFile("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Presentation.cs"));
        var hide = ExtractMethod(presentation, "internal async Task HideForPocAsync()");

        Assert.Contains("if (!selected)", entry);
        Assert.Contains("ClearProfileArtwork();", entry);
        Assert.Contains("UpdateProfileArtwork(surface.Binding?.AuthoritativePage);", entry);
        Assert.Contains("if (_profileArtworkRequestedAppId == appId)", update);
        Assert.Contains("Task.Run(() => SteamGameArtworkResolver.ResolveArtworkCandidates(page))", resolve);
        Assert.Contains("IsCurrentProfileArtworkTarget(appId)", resolve);
        Assert.Contains("foreach (var path in paths)", loader);
        Assert.Contains("await bitmap.SetSourceAsync(stream);", loader);
        Assert.Contains("catch (Exception)", loader);
        Assert.Contains("ClearProfileArtwork();", hide);
        Assert.Contains("ProfileArtworkImage.Source = null;", profile);
    }

    private static QuickSettingsPageSnapshot SteamPage(uint appId) => new(
        QuickSettingsPageId.Profile, QuickSettingsProfileTarget.ForSteam(appId), true, null, [], []);

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName)?.Value;

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method signature not found: {signature}");
        var openBrace = source.IndexOf('{', start);
        var depth = 0;
        var index = openBrace;
        for (; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) break;
        }

        return source[start..(index + 1)];
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }

    private sealed class SteamTree : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ClawSteamArtworkTests", Guid.NewGuid().ToString("N"));

        internal SteamTree()
        {
            Directory.CreateDirectory(Path.Combine(Root, "steamapps"));
            File.WriteAllText(Path.Combine(Root, "steam.exe"), string.Empty);
        }

        internal void WriteManifest(uint appId, string? library = null)
        {
            var steamApps = Path.Combine(library ?? Root, "steamapps");
            Directory.CreateDirectory(steamApps);
            File.WriteAllText(Path.Combine(steamApps, $"appmanifest_{appId}.acf"),
                $"\"AppState\" {{ \"appid\" \"{appId}\" }}");
        }

        internal string WriteArtwork(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF]);
            return path;
        }

        internal void WriteLibraryFolders(params string[] libraries)
        {
            var entries = libraries.Select((library, index) =>
                $"\"{index}\" {{ \"path\" \"{library.Replace("\\", "\\\\", StringComparison.Ordinal)}\" }}");
            File.WriteAllText(Path.Combine(Root, "steamapps", "libraryfolders.vdf"),
                $"\"libraryfolders\" {{ {string.Join(' ', entries)} }}");
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
