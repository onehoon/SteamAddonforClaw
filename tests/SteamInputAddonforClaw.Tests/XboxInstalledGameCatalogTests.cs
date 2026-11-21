using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Xbox;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class XboxInstalledGameCatalogTests : IDisposable
{
    private const string ValidConfig = """
        <Game configVersion="1">
          <Identity Name="Sample.Game" Publisher="CN=Sample" ResourceId="pc" />
          <ShellVisuals DefaultDisplayName="Sample title" />
          <StoreId>9NABC123</StoreId>
          <TitleId>12345678</TitleId>
          <ExecutableList>
            <Executable Name="Sample.exe" Id="Sample" TargetDeviceFamily="PC" Architecture="x64" />
            <Executable Name="SampleLauncher.exe" />
          </ExecutableList>
        </Game>
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SteamAddon.XboxCatalog.{Guid.NewGuid():N}");

    public XboxInstalledGameCatalogTests() => AppLog.DirectoryOverride = Path.Combine(_directory, "logs");

    public void Dispose()
    {
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Config_parser_reads_required_identity_optional_ids_and_executable_attributes()
    {
        var parsed = MicrosoftGameConfigReader.Parse(ValidConfig);

        Assert.True(parsed.XmlParsed);
        Assert.True(parsed.RecognizedRoot);
        Assert.NotNull(parsed.Config);
        Assert.Equal("Sample title", parsed.Config.DefaultDisplayName);
        Assert.Equal("Sample.Game", parsed.Config.IdentityName);
        Assert.Equal("CN=Sample", parsed.Config.IdentityPublisher);
        Assert.Equal("pc", parsed.Config.IdentityResourceId);
        Assert.Equal("9NABC123", parsed.Config.StoreId);
        Assert.Equal("12345678", parsed.Config.TitleId);
        Assert.Equal(new XboxGameExecutable("Sample.exe", "Sample", "PC", "x64"), parsed.Config.Executables[0]);
        Assert.Equal(new XboxGameExecutable("SampleLauncher.exe", null, null, null), parsed.Config.Executables[1]);
    }

    [Theory]
    [InlineData("Game")]
    [InlineData("MicrosoftGame")]
    public void Config_parser_accepts_both_recognized_roots(string root)
    {
        var parsed = MicrosoftGameConfigReader.Parse(
            $"<{root}><Identity Name=\"Game\" Publisher=\"Publisher\"/><ExecutableList><Executable Name=\"Game.exe\"/></ExecutableList></{root}>");

        Assert.True(parsed.RecognizedRoot);
        Assert.Equal("Game.exe", Assert.Single(parsed.Config!.Executables).Name);
    }

    [Fact]
    public void Config_parser_accepts_xml_namespaces_by_local_name()
    {
        const string xml = "<Game xmlns=\"urn:sample\"><Identity Name=\"Game\" Publisher=\"Publisher\"/><ExecutableList><Executable Name=\"Game.exe\"/></ExecutableList></Game>";
        var parsed = MicrosoftGameConfigReader.Parse(xml);
        Assert.True(parsed.RecognizedRoot);
        Assert.Equal("Game", parsed.Config?.IdentityName);
    }

    [Theory]
    [InlineData("<Game><ExecutableList><Executable Name=\"Game.exe\"/></ExecutableList></Game>", true, "Required Identity")]
    [InlineData("<Game><Identity Name=\"Game\"/><ExecutableList><Executable Name=\"Game.exe\"/></ExecutableList></Game>", true, "Required Identity")]
    [InlineData("<Game><Identity Name=\"Game\" Publisher=\"Publisher\"/><ExecutableList><Executable /></ExecutableList></Game>", true, "usable Executable Name")]
    [InlineData("<Project><Identity Name=\"Game\" Publisher=\"Publisher\"/><ExecutableList><Executable Name=\"Game.exe\"/></ExecutableList></Project>", true, "Unrecognized root")]
    [InlineData("<Game>", false, "XmlException")]
    public void Config_parser_rejects_invalid_config_shapes(string xml, bool xmlParsed, string failure)
    {
        var parsed = MicrosoftGameConfigReader.Parse(xml);
        Assert.Equal(xmlParsed, parsed.XmlParsed);
        Assert.Null(parsed.Config);
        Assert.Contains(failure, parsed.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_parser_blocks_dtd_and_entity_expansion()
    {
        const string xml = "<!DOCTYPE Game [<!ENTITY name 'Game'>]><Game><Identity Name='&name;' Publisher='Publisher'/><ExecutableList><Executable Name='Game.exe'/></ExecutableList></Game>";
        var parsed = MicrosoftGameConfigReader.Parse(xml);
        Assert.False(parsed.XmlParsed);
        Assert.Null(parsed.Config);
    }

    [Fact]
    public void Canonical_key_prefers_store_id_then_package_family_then_identity_tuple()
    {
        var config = MicrosoftGameConfigReader.Parse(ValidConfig).Config!;
        Assert.Equal("store:9NABC123", XboxGameIdentity.CreateKey(" 9NABC123 ", "Sample_family", config));
        Assert.Equal("pfn:Sample_family", XboxGameIdentity.CreateKey(null, " Sample_family ", config));
        Assert.Equal("identity:SAMPLE.GAME|CN%3DSAMPLE|PC", XboxGameIdentity.CreateKey(null, null, config));
        Assert.DoesNotContain("12345678", XboxGameIdentity.CreateKey(null, null, config), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Valid_game_config_uses_registered_package_and_preserves_store_identity()
    {
        var folder = CreateGame("valid", ValidConfig);
        var package = Package("Sample_1.0.0.0_x64__test", "Sample_family", folder.GameDirectory);
        var packageSource = new FakePackageSource(_ => Success(package));

        var result = await ScanAsync(new FakeGameFolderSource([folder]), packageSource);

        var game = Assert.Single(result.Games);
        Assert.Equal(XboxInstalledGameCatalogOutcome.Completed, result.Outcome);
        Assert.Equal("store:9NABC123", game.Identity.Key);
        Assert.Equal("Sample title", game.Identity.DisplayName);
        Assert.Equal("9NABC123", game.Identity.StoreId);
        Assert.Equal("Sample_family", game.Identity.PackageFamilyName);
        Assert.Equal("Sample.Game", Assert.Single(packageSource.Configurations).IdentityName);
    }

    [Fact]
    public async Task Empty_folder_scan_completes_with_an_empty_catalog()
    {
        var result = await ScanAsync(new FakeGameFolderSource([]), new FakePackageSource(_ => Success()));
        Assert.Equal(XboxInstalledGameCatalogOutcome.Completed, result.Outcome);
        Assert.Empty(result.Games);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task Stale_folder_without_a_registered_package_is_not_reported_as_installed()
    {
        var folder = CreateGame("stale", ValidConfig);
        var result = await ScanAsync(new FakeGameFolderSource([folder]), new FakePackageSource(_ => Success()));

        Assert.Equal(XboxInstalledGameCatalogOutcome.Completed, result.Outcome);
        Assert.Empty(result.Games);
        Assert.Equal(1, result.SkippedGameCount);
    }

    [Fact]
    public async Task Successful_empty_targeted_lookup_accepts_verified_gdk_content_with_store_key_only()
    {
        var folder = CreateGame("flat-file-empty-registration", ValidConfig);
        WriteDeclaredExecutable(folder, "Sample.exe");
        var result = await ScanAsync(new FakeGameFolderSource([folder]), new FakePackageSource(_ => Success()));

        var game = Assert.Single(result.Games);
        Assert.Equal("store:9NABC123", game.Identity.Key);
        Assert.Equal("9NABC123", game.Identity.StoreId);
        Assert.Null(game.Identity.PackageFamilyName);
    }

    [Fact]
    public async Task Adjacent_manifest_can_verify_gdk_content_when_declared_executable_is_not_visible()
    {
        var folder = CreateGame("flat-file-manifest", ValidConfig);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(folder.ConfigPath)!, "appxmanifest.xml"), "<Package />");

        var result = await ScanAsync(new FakeGameFolderSource([folder]), new FakePackageSource(_ => Success()));

        var game = Assert.Single(result.Games);
        Assert.Equal("store:9NABC123", game.Identity.Key);
        Assert.Null(game.Identity.PackageFamilyName);
    }

    [Fact]
    public async Task Different_registered_location_uses_verified_gdk_fallback_without_attaching_package_family()
    {
        var folder = CreateGame("flat-file-mismatched-registration", ValidConfig);
        WriteDeclaredExecutable(folder, "Sample.exe");
        var unrelatedRoot = Path.Combine(_directory, "other-install");
        Directory.CreateDirectory(unrelatedRoot);
        var package = Package("Sample_1.0.0.0_x64__test", "Unrelated_family", unrelatedRoot);

        var result = await ScanAsync(new FakeGameFolderSource([folder]), new FakePackageSource(_ => Success(package)));

        var game = Assert.Single(result.Games);
        Assert.Equal("store:9NABC123", game.Identity.Key);
        Assert.Null(game.Identity.PackageFamilyName);
    }

    [Fact]
    public async Task Different_registered_location_without_local_artifact_is_still_skipped()
    {
        var folder = CreateGame("different-location-without-artifact", ValidConfig);
        var package = Package("Sample_1.0.0.0_x64__test", "Sample_family", Path.Combine(_directory, "other-install"));

        var result = await ScanAsync(new FakeGameFolderSource([folder]), new FakePackageSource(_ => Success(package)));

        Assert.Empty(result.Games);
        Assert.Equal(1, result.SkippedGameCount);
    }
    [Fact]
    public async Task No_store_id_never_uses_flat_file_fallback_without_exact_registered_path()
    {
        var config = ValidConfig.Replace("<StoreId>9NABC123</StoreId>", string.Empty, StringComparison.Ordinal);
        var emptyRegistrationFolder = CreateGame("no-store-empty-registration", config);
        var differentPathFolder = CreateGame("no-store-different-path", config);
        WriteDeclaredExecutable(emptyRegistrationFolder, "Sample.exe");
        WriteDeclaredExecutable(differentPathFolder, "Sample.exe");
        var package = Package("Sample_1.0.0.0_x64__test", "Sample_family", Path.Combine(_directory, "unrelated-root"));

        var result = await ScanAsync(new FakeGameFolderSource([emptyRegistrationFolder, differentPathFolder]),
            new FakePackageSource(_ => Success(package)));

        Assert.Empty(result.Games);
        Assert.Equal(2, result.SkippedGameCount);
    }

    [Fact]
    public async Task Unsafe_executable_path_and_contradictory_package_identity_are_rejected_even_with_artifacts()
    {
        Directory.CreateDirectory(_directory);
        var outsideExecutable = Path.Combine(_directory, "outside.exe");
        File.WriteAllText(outsideExecutable, "placeholder");
        var unsafeConfig = ValidConfig.Replace("Name=\"Sample.exe\"", "Name=\"..\\..\\outside.exe\"", StringComparison.Ordinal);
        var unsafeFolder = CreateGame("unsafe-executable", unsafeConfig);
        var normalizedTraversalConfig = ValidConfig.Replace("Name=\"Sample.exe\"", "Name=\"nested\\..\\Sample.exe\"", StringComparison.Ordinal);
        var normalizedTraversalFolder = CreateGame("normalized-traversal", normalizedTraversalConfig);
        WriteDeclaredExecutable(normalizedTraversalFolder, "Sample.exe");
        var conflictingFolder = CreateGame("identity-conflict", ValidConfig);
        WriteDeclaredExecutable(conflictingFolder, "Sample.exe");
        var contradictoryPackage = new XboxInstalledPackage("Other.Game", "CN=Other", "Other_1.0_x64", "Other_family",
            "Other", [new("Installed", conflictingFolder.GameDirectory)]);

        var result = await ScanAsync(new FakeGameFolderSource([unsafeFolder, normalizedTraversalFolder, conflictingFolder]),
            new FakePackageSource(_ => Success(contradictoryPackage)));

        Assert.Empty(result.Games);
        Assert.Equal(3, result.SkippedGameCount);
    }

    [Fact]
    public async Task Dtd_config_is_rejected_before_targeted_package_lookup()
    {
        const string dtdConfig = "<!DOCTYPE Game [<!ENTITY name 'Sample.Game'>]><Game><Identity Name='&name;' Publisher='CN=Sample'/><StoreId>9NABC123</StoreId><ExecutableList><Executable Name='Sample.exe'/></ExecutableList></Game>";
        var folder = CreateGame("dtd", dtdConfig);
        var packageSource = new FakePackageSource(_ => Success());

        var result = await ScanAsync(new FakeGameFolderSource([folder]), packageSource);

        Assert.Empty(result.Games);
        Assert.Equal(1, result.SkippedGameCount);
        Assert.Equal(0, packageSource.CallCount);
    }

    [Fact]
    public async Task Duplicate_store_keys_across_discovered_folders_are_deduplicated()
    {
        var first = CreateGame("same-store-first", ValidConfig);
        var second = CreateGame("same-store-second", ValidConfig);
        WriteDeclaredExecutable(first, "Sample.exe");
        WriteDeclaredExecutable(second, "Sample.exe");

        var result = await ScanAsync(new FakeGameFolderSource([first, second]), new FakePackageSource(_ => Success()));

        Assert.Equal("store:9NABC123", Assert.Single(result.Games).Identity.Key);
    }

    [Fact]
    public async Task Successful_empty_lookup_and_lookup_exception_have_distinct_diagnostics()
    {
        var previousLevel = AppLog.MinimumLevelOverride;
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
        try
        {
            var emptyFolder = CreateGame("diagnostic-empty", ValidConfig);
            WriteDeclaredExecutable(emptyFolder, "Sample.exe");
            await ScanAsync(new FakeGameFolderSource([emptyFolder]), new FakePackageSource(_ => Success()));

            var noPathFolder = CreateGame("diagnostic-no-path", ValidConfig);
            WriteDeclaredExecutable(noPathFolder, "Sample.exe");
            var noPathPackage = new XboxInstalledPackage("Sample.Game", "CN=Sample", "Sample_1.0_x64", "Sample_family",
                "Sample", []);
            await ScanAsync(new FakeGameFolderSource([noPathFolder]), new FakePackageSource(_ => Success(noPathPackage)));

            var differentPathFolder = CreateGame("diagnostic-different-path", ValidConfig);
            WriteDeclaredExecutable(differentPathFolder, "Sample.exe");
            var differentPathPackage = Package("Sample_1.0_x64", "Sample_family", Path.Combine(_directory, "elsewhere"));
            await ScanAsync(new FakeGameFolderSource([differentPathFolder]), new FakePackageSource(_ => Success(differentPathPackage)));

            var conflictFolder = CreateGame("diagnostic-conflict", ValidConfig);
            WriteDeclaredExecutable(conflictFolder, "Sample.exe");
            var conflictingPackage = new XboxInstalledPackage("Other.Game", "CN=Other", "Other_1.0_x64", "Other_family",
                "Other", [new("Installed", conflictFolder.GameDirectory)]);
            await ScanAsync(new FakeGameFolderSource([conflictFolder]), new FakePackageSource(_ => Success(conflictingPackage)));

            var failedFolder = CreateGame("diagnostic-failure", ValidConfig);
            var failedLookup = new XboxInstalledPackageEnumeration([], false, true, "Package lookup unavailable.");
            await ScanAsync(new FakeGameFolderSource([failedFolder]), new FakePackageSource(_ => failedLookup));
            AppLog.DrainForTests();
            var log = LogFileTestHelper.ReadAllText(AppLog.CurrentLogFilePath);

            Assert.Contains("Reason=TargetLookupReturnedZeroPackages", log, StringComparison.Ordinal);
            Assert.Contains("Reason=MatchingIdentityHasZeroOrUnavailablePaths", log, StringComparison.Ordinal);
            Assert.Contains("Reason=MatchingIdentityHasDifferentRegisteredLocations", log, StringComparison.Ordinal);
            Assert.Contains("Reason=ContradictoryPackageIdentity", log, StringComparison.Ordinal);
            Assert.Contains("Action=AcceptStoreIdFallback", log, StringComparison.Ordinal);
            Assert.Contains("Action=Reject", log, StringComparison.Ordinal);
            Assert.Contains("Targeted package lookup failed; retaining the existing Store ID identity.", log, StringComparison.Ordinal);
            Assert.Contains("TargetedPackageLookupFailureCount=1", log, StringComparison.Ordinal);
        }
        finally
        {
            AppLog.MinimumLevelOverride = previousLevel;
        }
    }

    [Fact]
    public async Task Targeted_package_lookup_failure_keeps_existing_store_key_without_inventing_family_key()
    {
        var folder = CreateGame("lookup-failure", ValidConfig);
        var failedLookup = new XboxInstalledPackageEnumeration([], false, true, "Package lookup unavailable.");
        var result = await ScanAsync(new FakeGameFolderSource([folder]), new FakePackageSource(_ => failedLookup));

        var game = Assert.Single(result.Games);
        Assert.Equal("store:9NABC123", game.Identity.Key);
        Assert.Null(game.Identity.PackageFamilyName);
        Assert.Equal(XboxInstalledGameCatalogOutcome.Completed, result.Outcome);
    }

    [Fact]
    public async Task Targeted_lookup_failure_without_store_id_does_not_create_an_unverified_identity()
    {
        var config = ValidConfig.Replace("<StoreId>9NABC123</StoreId>", string.Empty, StringComparison.Ordinal);
        var folder = CreateGame("no-store-id", config);
        var failedLookup = new XboxInstalledPackageEnumeration([], false, true, "Package lookup unavailable.");
        var result = await ScanAsync(new FakeGameFolderSource([folder]), new FakePackageSource(_ => failedLookup));

        Assert.Equal(XboxInstalledGameCatalogOutcome.Unavailable, result.Outcome);
        Assert.Empty(result.Games);
        Assert.Equal("Package lookup unavailable.", result.FailureReason);
    }

    [Fact]
    public async Task Registered_package_location_must_contain_the_discovered_config()
    {
        var folder = CreateGame("leftover", ValidConfig);
        var unrelatedRoot = Path.Combine(_directory, "different-install");
        Directory.CreateDirectory(unrelatedRoot);
        var result = await ScanAsync(new FakeGameFolderSource([folder]),
            new FakePackageSource(_ => Success(Package("Sample_1.0.0.0_x64__test", "Sample_family", unrelatedRoot))));

        Assert.Empty(result.Games);
        Assert.Equal(1, result.SkippedGameCount);
    }

    [Fact]
    public async Task Package_family_key_remains_stable_across_version_and_title_changes()
    {
        var firstFolder = CreateGame("version-one", ValidConfig.Replace("<StoreId>9NABC123</StoreId>", string.Empty, StringComparison.Ordinal));
        var secondFolder = CreateGame("version-two", ValidConfig.Replace("<StoreId>9NABC123</StoreId>", string.Empty, StringComparison.Ordinal)
            .Replace("Sample title", "Updated title", StringComparison.Ordinal));
        var first = await ScanAsync(new FakeGameFolderSource([firstFolder]), new FakePackageSource(_ =>
            Success(Package("Sample_1.0.0.0_x64__test", "Sample_family", firstFolder.GameDirectory))));
        var updated = await ScanAsync(new FakeGameFolderSource([secondFolder]), new FakePackageSource(_ =>
            Success(Package("Sample_2.0.0.0_x64__test", "Sample_family", secondFolder.GameDirectory))));

        var firstGame = Assert.Single(first.Games);
        var updatedGame = Assert.Single(updated.Games);
        Assert.Equal("pfn:Sample_family", firstGame.Identity.Key);
        Assert.Equal(firstGame.Identity.Key, updatedGame.Identity.Key);
        Assert.NotEqual(firstGame.Identity.DisplayName, updatedGame.Identity.DisplayName);
    }

    [Fact]
    public async Task Package_name_and_publisher_must_match_the_config_identity()
    {
        var folder = CreateGame("mismatched", ValidConfig);
        WriteDeclaredExecutable(folder, "Sample.exe");
        var package = new XboxInstalledPackage("Other.Game", "CN=Other", "Other_1.0_x64", "Other_family",
            "Other", [new("Installed", folder.GameDirectory)]);
        var result = await ScanAsync(new FakeGameFolderSource([folder]), new FakePackageSource(_ => Success(package)));
        Assert.Empty(result.Games);
    }

    [Fact]
    public async Task Malformed_or_oversize_config_does_not_abort_later_valid_game()
    {
        var malformed = CreateGame("malformed", "<Game>");
        var oversized = CreateGame("oversized", "<Game>" + new string(' ', 2 * 1024 * 1024) + "</Game>");
        var valid = CreateGame("valid-after-invalid", ValidConfig);
        var packageSource = new FakePackageSource(_ => Success(Package("Sample_1.0.0.0_x64__test", "Sample_family", valid.GameDirectory)));
        var result = await ScanAsync(new FakeGameFolderSource([malformed, oversized, valid]), packageSource);

        Assert.Single(result.Games);
        Assert.Equal("store:9NABC123", result.Games[0].Identity.Key);
        Assert.Equal(2, result.SkippedGameCount);
        Assert.Equal(1, packageSource.CallCount);
    }

    [Fact]
    public async Task Duplicate_folder_observations_and_profile_keys_do_not_duplicate_games()
    {
        var folder = CreateGame("duplicate", ValidConfig);
        var package = Package("Sample_1.0.0.0_x64__test", "Sample_family", folder.GameDirectory);
        var packageSource = new FakePackageSource(_ => Success(package, package));
        var result = await ScanAsync(new FakeGameFolderSource([folder, folder]), packageSource);

        Assert.Single(result.Games);
        Assert.Equal(1, packageSource.CallCount);
    }

    [Fact]
    public async Task Folder_discovery_failure_maps_to_global_catalog_outcome()
    {
        var failure = new XboxInstalledGameFolderEnumeration([], false, true, "folder discovery unavailable");
        var result = await ScanAsync(new FakeGameFolderSource(failure), new FakePackageSource(_ => Success()));
        Assert.Equal(XboxInstalledGameCatalogOutcome.Unavailable, result.Outcome);
        Assert.Empty(result.Games);
        Assert.Equal("folder discovery unavailable", result.FailureReason);
    }

    [Fact]
    public async Task Cancellation_is_observed_before_folder_discovery()
    {
        var source = new FakeGameFolderSource([]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ScanAsync(source, new FakePackageSource(_ => Success()), cancellation.Token));
        Assert.Equal(0, source.CallCount);
    }

    [Fact]
    public async Task Cancellation_during_folder_discovery_is_observed_before_game_scanning()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new FakeGameFolderSource([], _ => cancellation.Cancel());
        var packageSource = new FakePackageSource(_ => Success());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ScanAsync(source, packageSource, cancellation.Token));
        Assert.Equal(1, source.CallCount);
        Assert.Equal(0, packageSource.CallCount);
    }

    [Fact]
    public void Registered_locations_are_deduplicated_case_insensitively()
    {
        var locations = XboxInstalledGameCatalog.DistinctLocations(
            [new("Effective", _directory), new("Installed", _directory.ToUpperInvariant())]);
        Assert.Equal("Effective", Assert.Single(locations).Kind);
    }

    [Theory]
    [InlineData("Effective")]
    [InlineData("Installed")]
    public async Task Either_registered_package_location_can_confirm_the_discovered_game(string locationKind)
    {
        var folder = CreateGame("registered-location", ValidConfig);
        var package = new XboxInstalledPackage("Sample.Game", "CN=Sample", "Sample_1.0_x64", "Sample_family",
            "Registered display", [new(locationKind, folder.GameDirectory)]);

        var result = await ScanAsync(new FakeGameFolderSource([folder]), new FakePackageSource(_ => Success(package)));

        Assert.Single(result.Games);
    }

    [Fact]
    public void Normal_catalog_registration_uses_the_targeted_package_query_only()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "src", "SteamInputAddonforClaw", "Xbox", "XboxInstalledGameCatalog.cs"));
        var normalized = System.Text.RegularExpressions.Regex.Replace(source, @"\s+", " ");

        Assert.Contains("FindPackagesForUser( string.Empty, config.IdentityName, config.IdentityPublisher);", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("FindPackagesForUser( string.Empty )", normalized, StringComparison.Ordinal);
    }

    private async Task<XboxInstalledGameCatalogResult> ScanAsync(
        FakeGameFolderSource folderSource,
        FakePackageSource packageSource,
        CancellationToken cancellationToken = default) =>
        await new XboxInstalledGameCatalog(folderSource, packageSource).ScanAsync(cancellationToken);

    private XboxInstalledGameFolder CreateGame(string name, string config)
    {
        var gameDirectory = Path.Combine(_directory, name);
        var contentDirectory = Path.Combine(gameDirectory, "Content");
        Directory.CreateDirectory(contentDirectory);
        var configPath = Path.Combine(contentDirectory, "MicrosoftGame.config");
        File.WriteAllText(configPath, config);
        return new(gameDirectory, configPath);
    }

    private static void WriteDeclaredExecutable(XboxInstalledGameFolder folder, string executableName)
    {
        var contentDirectory = Path.GetDirectoryName(folder.ConfigPath)!;
        var executable = Path.Combine(contentDirectory, executableName.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, "test placeholder");
    }

    private static XboxInstalledPackage Package(string fullName, string familyName, string rootPath) =>
        new("Sample.Game", "CN=Sample", fullName, familyName, "Registered display", [new("Installed", rootPath)]);

    private static XboxInstalledPackageEnumeration Success(params XboxInstalledPackage[] packages) => new(packages, true, false, null);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private sealed class FakeGameFolderSource(
        XboxInstalledGameFolderEnumeration enumeration,
        Action<CancellationToken>? onEnumerate = null) : IXboxInstalledGameFolderSource
    {
        internal int CallCount { get; private set; }

        internal FakeGameFolderSource(IReadOnlyList<XboxInstalledGameFolder> folders, Action<CancellationToken>? onEnumerate = null)
            : this(new XboxInstalledGameFolderEnumeration(folders, true, false, null), onEnumerate) { }

        public XboxInstalledGameFolderEnumeration Enumerate(CancellationToken cancellationToken)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            onEnumerate?.Invoke(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return enumeration;
        }
    }

    private sealed class FakePackageSource(
        Func<MicrosoftGameConfig, XboxInstalledPackageEnumeration> resolve) : IXboxInstalledPackageSource
    {
        internal int CallCount { get; private set; }
        internal List<MicrosoftGameConfig> Configurations { get; } = [];

        public XboxInstalledPackageEnumeration FindForGame(MicrosoftGameConfig config, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Configurations.Add(config);
            return resolve(config);
        }
    }
}
