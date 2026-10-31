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
        Assert.Empty(SteamGameArtworkResolver.ResolveArtworkCandidates(tree.Root, SteamPage(AppId), 1));
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
        var artworkWithoutManifest = tree.WriteArtwork(Path.Combine(
            tree.Root, "appcache", "librarycache", "570", "library_header_2x.jpg"));

        Assert.True(SteamGameArtworkResolver.HasNativeSteamManifest(tree.Root, 571));
        Assert.False(SteamGameArtworkResolver.HasNativeSteamManifest(tree.Root, 570));
        Assert.Equal([artworkWithoutManifest],
            SteamGameArtworkResolver.ResolveArtworkCandidates(tree.Root, SteamPage(570), null));
    }

    [Fact]
    public void Custom_wide_cover_precedes_library_header_and_hero_is_never_a_candidate()
    {
        using var tree = new SteamTree();
        tree.WriteManifest(AppId);
        var grid = Path.Combine(tree.Root, "userdata", "1", "config", "grid");
        var customGrid = new[] { $"{AppId}.png", $"{AppId}.jpg", $"{AppId}.jpeg" }
            .Select(name => tree.WriteArtwork(Path.Combine(grid, name))).ToArray();
        var builtInHero = tree.WriteArtwork(Path.Combine(tree.Root, "appcache", "librarycache", AppId.ToString(), "library_hero.jpg"));
        var builtInHeader = tree.WriteArtwork(Path.Combine(tree.Root, "appcache", "librarycache", AppId.ToString(), "library_header.jpg"));

        Assert.Equal(customGrid.Concat([builtInHeader]),
            SteamGameArtworkResolver.ResolveArtworkCandidates(tree.Root, SteamPage(AppId), 1));
        Assert.DoesNotContain(builtInHero, SteamGameArtworkResolver.ResolveArtworkCandidates(tree.Root, SteamPage(AppId), 1));
    }

    [Fact]
    public void Shortcut_custom_wide_art_includes_derived_launch_id_after_the_32_bit_app_id()
    {
        using var tree = new SteamTree();
        const uint shortcutAppId = 123456789;
        const uint accountId = 1;
        var grid = Path.Combine(tree.Root, "userdata", accountId.ToString(), "config", "grid");
        var appIdPng = tree.WriteArtwork(Path.Combine(grid, $"{shortcutAppId}.png"));
        var appIdJpeg = tree.WriteArtwork(Path.Combine(grid, $"{shortcutAppId}.jpeg"));
        var launchId = ((ulong)shortcutAppId << 32) | 0x02000000UL;
        var launchArt = tree.WriteArtwork(Path.Combine(grid, $"{launchId}.jpg"));

        Assert.False(SteamGameArtworkResolver.HasNativeSteamManifest(tree.Root, shortcutAppId));
        Assert.Equal([appIdPng, appIdJpeg, launchArt],
            SteamGameArtworkResolver.ResolveArtworkCandidates(tree.Root, SteamPage(shortcutAppId), accountId));
    }

    [Fact]
    public void Missing_custom_art_prefers_library_header_then_header_then_legacy_and_excludes_heroes()
    {
        using var tree = new SteamTree();
        tree.WriteManifest(AppId);
        var cache = Path.Combine(tree.Root, "appcache", "librarycache");
        var appDirectory = Path.Combine(cache, AppId.ToString());
        var directHero = tree.WriteArtwork(Path.Combine(appDirectory, "library_hero.jpg"));
        var hashHero = tree.WriteArtwork(Path.Combine(appDirectory, "aaa", "library_hero.jpg"));
        var flatHero = tree.WriteArtwork(Path.Combine(cache, $"{AppId}_library_hero.jpg"));
        var directLibraryHeaderTwoX = tree.WriteArtwork(Path.Combine(appDirectory, "library_header_2x.jpg"));
        var hashLibraryHeaderTwoX = tree.WriteArtwork(Path.Combine(appDirectory, "aaa", "library_header_2x.jpg"));
        var directLibraryHeader = tree.WriteArtwork(Path.Combine(appDirectory, "library_header.jpg"));
        var hashLibraryHeader = tree.WriteArtwork(Path.Combine(appDirectory, "aaa", "library_header.jpg"));
        var directWideCoverTwoX = tree.WriteArtwork(Path.Combine(appDirectory, "header_2x.jpg"));
        var hashWideCoverTwoX = tree.WriteArtwork(Path.Combine(appDirectory, "bbb", "header_2x.jpg"));
        var directWideCover = tree.WriteArtwork(Path.Combine(appDirectory, "header.jpg"));
        var hashWideCover = tree.WriteArtwork(Path.Combine(appDirectory, "bbb", "header.jpg"));
        var nestedOnly = tree.WriteArtwork(Path.Combine(appDirectory, "deep", "nested", "library_header.jpg"));
        var portrait = tree.WriteArtwork(Path.Combine(appDirectory, $"{AppId}p.jpg"));
        var otherApp = tree.WriteArtwork(Path.Combine(cache, "571", "library_header.jpg"));
        var flatHeader = tree.WriteArtwork(Path.Combine(cache, $"{AppId}_library_header.jpg"));

        var candidates = SteamGameArtworkResolver.ResolveArtworkCandidates(tree.Root, SteamPage(AppId), null);

        Assert.Equal([
            directLibraryHeaderTwoX, hashLibraryHeaderTwoX,
            directLibraryHeader, hashLibraryHeader,
            directWideCoverTwoX, hashWideCoverTwoX,
            directWideCover, hashWideCover, flatHeader
        ], candidates);
        Assert.DoesNotContain(directHero, candidates);
        Assert.DoesNotContain(hashHero, candidates);
        Assert.DoesNotContain(flatHero, candidates);
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
        tree.WriteArtwork(Path.Combine(tree.Root, "appcache", "librarycache", AppId.ToString(), "library_hero.jpg"));
        var builtIn = tree.WriteArtwork(Path.Combine(tree.Root, "appcache", "librarycache", AppId.ToString(), "library_header.jpg"));

        Assert.Equal([builtIn], SteamGameArtworkResolver.ResolveArtworkCandidates(tree.Root, SteamPage(AppId), null));
    }

    [Fact]
    public void Invalid_optional_metadata_and_missing_roots_are_silent_cache_misses()
    {
        using var tree = new SteamTree();
        tree.WriteManifest(AppId);
        File.WriteAllText(Path.Combine(tree.Root, "steamapps", "libraryfolders.vdf"), "malformed { metadata");
        var rootArtwork = tree.WriteArtwork(Path.Combine(tree.Root, "appcache", "librarycache", AppId.ToString(), "library_header.jpg"));

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
    public void Artwork_uses_a_separate_full_width_profile_row_above_scrollable_settings()
    {
        var xaml = XDocument.Load(ReadRepositoryFile("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml"));
        var rightColumn = xaml.Descendants().Single(element => element.Name.LocalName == "Grid"
            && Attribute(element, "Grid.Column") == "1" && Attribute(element, "Grid.Row") == "0");
        var visualChildren = rightColumn.Elements().Where(element => element.Name.LocalName != "RowDefinitions").ToArray();
        var title = visualChildren.Single(element => Attribute(element, "Name") == "PageTitle");
        var host = visualChildren.Single(element => Attribute(element, "Name") == "ProfileArtworkHost");
        var scroll = visualChildren.Single(element => Attribute(element, "Name") == "BodyScroll");

        Assert.True(Array.IndexOf(visualChildren, title) < Array.IndexOf(visualChildren, host));
        Assert.True(Array.IndexOf(visualChildren, host) < Array.IndexOf(visualChildren, scroll));
        Assert.Equal(["Auto", "Auto", "*"], rightColumn.Elements().Single(element => element.Name.LocalName == "Grid.RowDefinitions")
            .Elements().Select(element => Attribute(element, "Height")));
        Assert.Equal("Visible", Attribute(title, "Visibility"));
        Assert.Equal("0", Attribute(title, "Grid.Row"));
        Assert.Equal("1", Attribute(host, "Grid.Row"));
        Assert.Null(Attribute(host, "Grid.RowSpan"));
        Assert.Null(Attribute(host, "Margin"));
        Assert.Equal("Stretch", Attribute(host, "HorizontalAlignment"));
        Assert.Equal("Top", Attribute(host, "VerticalAlignment"));
        Assert.Equal("False", Attribute(host, "IsHitTestVisible"));
        Assert.Equal("Collapsed", Attribute(host, "Visibility"));
        Assert.Null(Attribute(host, "Width"));
        Assert.Null(Attribute(host, "Height"));
        Assert.Equal("2", Attribute(scroll, "Grid.Row"));
        Assert.Equal("Transparent", Attribute(scroll, "Background"));
        Assert.Equal("Hidden", Attribute(scroll, "VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", Attribute(scroll, "HorizontalScrollBarVisibility"));
        Assert.Equal("ProfileContentColumn_SizeChanged", Attribute(rightColumn, "SizeChanged"));
        Assert.Equal("{StaticResource QamBodyContentPadding}",
            Attribute(scroll.Elements().Single(element => element.Name.LocalName == "Border"), "Padding"));

        var image = host.Elements().Single(element => element.Name.LocalName == "Image");
        Assert.Equal("UniformToFill", Attribute(image, "Stretch"));
        var gradient = host.Descendants().Single(element => element.Name.LocalName == "LinearGradientBrush");
        Assert.Equal("0,0", Attribute(gradient, "StartPoint"));
        Assert.Equal("0,1", Attribute(gradient, "EndPoint"));
        Assert.Equal(
            [("#330E141B", "0"), ("#900E141B", "0.5"), ("#FF0E141B", "1")],
            gradient.Elements().Where(element => element.Name.LocalName == "GradientStop")
                .Select(element => (Attribute(element, "Color"), Attribute(element, "Offset"))));
        Assert.DoesNotContain(host.Descendants(), element =>
            element.Name.LocalName is "TextBlock" or "ToggleSwitch");

        var profile = File.ReadAllText(ReadRepositoryFile("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs"));
        var sizeChanged = ExtractMethod(profile, "private void ProfileContentColumn_SizeChanged(object sender, SizeChangedEventArgs e)");
        Assert.Contains("ProfileArtworkHost.Clip = new RectangleGeometry", sizeChanged);
        Assert.Contains("new Rect(0, 0, width, height)", sizeChanged);
        Assert.DoesNotContain("Height=\"240\"", File.ReadAllText(ReadRepositoryFile("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.xaml")));
        Assert.DoesNotContain("rightColumnWidth - 32", profile);
        Assert.DoesNotContain("ProfileArtwork", profile[profile.IndexOf("private FrameworkElement BuildProfilePage()", StringComparison.Ordinal)..
            profile.IndexOf("internal bool TryHandleBack()", StringComparison.Ordinal)]);
    }

    [Theory]
    [InlineData(400, 400)]
    [InlineData(32, 32)]
    [InlineData(0, 0)]
    [InlineData(-10, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    public void Artwork_size_uses_the_finite_full_content_column_width(double requestedWidth, double expectedWidth)
    {
        var artworkSize = OverlayWindow.CalculateProfileArtworkSize(requestedWidth);

        Assert.Equal(expectedWidth, artworkSize.Width);
        Assert.Equal(expectedWidth * 430.0 / 920.0, artworkSize.Height, 8);
    }

    [Fact]
    public void Artwork_visibility_and_retry_are_scoped_to_the_current_profile_entry()
    {
        var profile = File.ReadAllText(ReadRepositoryFile("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Profile.cs"));
        var entry = ExtractMethod(profile, "private void OnProfileTabSelectionChanged(bool selected)");
        var update = ExtractMethod(profile, "private void UpdateProfileArtwork(QuickSettingsPageSnapshot? page)");
        var resolve = ExtractMethod(profile, "private async Task ResolveAndApplyProfileArtworkAsync(");
        var loader = ExtractMethod(profile, "private async Task<BitmapImage?> TryLoadProfileArtworkAsync(");
        var clear = ExtractMethod(profile, "private void ClearProfileArtwork()");
        var presentation = File.ReadAllText(ReadRepositoryFile("src", "SteamInputAddonforClaw.Overlay", "OverlayWindow.Presentation.cs"));
        var hide = ExtractMethod(presentation, "internal Task HideForPocAsync()");
        const string source = "ProfileArtworkImage.Source = bitmap;";
        const string showHero = "ProfileArtworkHost.Visibility = Visibility.Visible;";
        const string hideTitle = "PageTitle.Visibility = Visibility.Collapsed;";

        Assert.Contains("if (!selected)", entry);
        Assert.Contains("ClearProfileArtwork();", entry);
        Assert.Contains("UpdateProfileArtwork(surface.Binding?.AuthoritativePage);", entry);
        Assert.Contains("if (_profileArtworkRequestedAppId == appId)", update);
        Assert.Contains("new CancellationTokenSource()", update);
        Assert.Contains("ProfileArtworkMaximumAttempts = 2", profile);
        Assert.Contains("ProfileArtworkRetryDelay = TimeSpan.FromMilliseconds(750)", profile);
        Assert.Contains("for (var attempt = 0; attempt < ProfileArtworkMaximumAttempts; attempt++)", resolve);
        Assert.Contains("Task.Run(() => SteamGameArtworkResolver.ResolveArtworkCandidates(page))", resolve);
        Assert.Contains("CandidateCount", resolve);
        Assert.Contains("\"NoCandidate\"", resolve);
        Assert.Contains("\"OpenOrDecodeFailed\"", resolve);
        Assert.Contains("\"StaleOrHidden\"", resolve);
        Assert.Contains("Task.Delay(ProfileArtworkRetryDelay, cancellationToken)", resolve);
        Assert.Contains("if (attempt == 0)", resolve);
        var currentRequest = ExtractMethod(profile, "private bool IsCurrentProfileArtworkRequest(");
        Assert.Contains("IsCurrentProfileArtworkRequest(appId, requestCancellation)", resolve);
        Assert.Contains("IsCurrentProfileArtworkTarget(appId)", currentRequest);
        Assert.Equal(1, profile.Split("PageTitle.Visibility = Visibility.Collapsed;", StringSplitOptions.None).Length - 1);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested();", loader);
        Assert.Contains("foreach (var path in paths)", loader);
        Assert.Contains("await bitmap.SetSourceAsync(stream);", loader);
        Assert.Contains("catch (Exception", loader);
        Assert.Contains("return null;", loader);
        var sourceIndex = resolve.IndexOf(source, StringComparison.Ordinal);
        var targetGuardIndex = resolve.LastIndexOf("if (!IsCurrentProfileArtworkRequest(appId, requestCancellation))", sourceIndex, StringComparison.Ordinal);
        Assert.True(resolve.IndexOf("if (bitmap is not null)", StringComparison.Ordinal) < sourceIndex);
        Assert.True(targetGuardIndex >= 0 && targetGuardIndex < sourceIndex);
        Assert.True(resolve.IndexOf(source, StringComparison.Ordinal)
            < resolve.IndexOf(showHero, StringComparison.Ordinal));
        Assert.True(resolve.IndexOf(showHero, StringComparison.Ordinal)
            < resolve.IndexOf(hideTitle, StringComparison.Ordinal));
        Assert.Contains("PageTitle.Visibility = Visibility.Visible;", clear);
        Assert.Contains("ProfileArtworkHost.Visibility = Visibility.Collapsed;", clear);
        Assert.Contains("ProfileArtworkImage.Source = null;", clear);
        Assert.Contains("_profileArtworkRequestedAppId = null;", clear);
        Assert.Contains("_profileArtworkRequestCancellation?.Cancel();", clear);
        Assert.Contains("ClearProfileArtwork();", hide);
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
