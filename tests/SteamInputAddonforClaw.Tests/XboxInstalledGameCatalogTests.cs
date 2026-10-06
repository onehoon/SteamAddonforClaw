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
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
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
    public async Task Package_family_key_ignores_version_display_name_and_config_path_changes()
    {
        var originalConfig = ValidConfig.Replace("<StoreId>9NABC123</StoreId>", string.Empty, StringComparison.Ordinal);
        var updatedConfig = originalConfig.Replace("Sample title", "Updated title", StringComparison.Ordinal);
        var originalRoot = CreatePackage("original", originalConfig);
        var updatedRoot = CreatePackage("updated", updatedConfig);
        var original = await ScanAsync(new FakePackageSource(true, null,
            Package("Sample_1.0.0.0_x64__test", "Sample_family", "Old title", Location("Effective", originalRoot))));
        var updated = await ScanAsync(new FakePackageSource(true, null,
            Package("Sample_2.0.0.0_x64__test", "Sample_family", "New title", Location("Effective", updatedRoot))));

        var originalGame = Assert.Single(original.Games);
        var updatedGame = Assert.Single(updated.Games);
        Assert.Equal("pfn:Sample_family", originalGame.Identity.Key);
        Assert.Equal(originalGame.Identity.Key, updatedGame.Identity.Key);
        Assert.NotEqual(originalGame.ConfigPath, updatedGame.ConfigPath);
        Assert.NotEqual(originalGame.Identity.DisplayName, updatedGame.Identity.DisplayName);
    }

    [Fact]
    public void Title_id_is_metadata_and_never_becomes_key_authority()
    {
        var config = MicrosoftGameConfigReader.Parse(ValidConfig.Replace("<StoreId>9NABC123</StoreId>", string.Empty, StringComparison.Ordinal)).Config!;

        Assert.Equal("12345678", config.TitleId);
        Assert.StartsWith("identity:", XboxGameIdentity.CreateKey(null, null, config), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zero_packages_completes_with_an_empty_catalog()
    {
        var result = await ScanAsync(new FakePackageSource(true, null));

        Assert.Equal(XboxInstalledGameCatalogOutcome.Completed, result.Outcome);
        Assert.Empty(result.Games);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task Valid_game_config_produces_a_production_catalog_entry()
    {
        var root = CreatePackage("valid", ValidConfig);

        var result = await ScanAsync(new FakePackageSource(true, null,
            Package("Sample_1.0.0.0_x64__test", "Sample_family", "Sample package", Location("Installed", root))));

        var game = Assert.Single(result.Games);
        Assert.Equal(XboxInstalledGameCatalogOutcome.Completed, result.Outcome);
        Assert.Equal("store:9NABC123", game.Identity.Key);
        Assert.Equal("Sample title", game.Identity.DisplayName);
        Assert.Equal("Sample_1.0.0.0_x64__test", game.PackageFullName);
        Assert.Equal(Path.Combine(root, "MicrosoftGame.config"), game.ConfigPath);
        Assert.Equal("12345678", game.Identity.TitleId);
        Assert.Equal(2, game.Identity.Executables.Count);
    }

    [Fact]
    public async Task Ordinary_package_without_config_is_skipped_without_scan_failure()
    {
        var root = CreatePackage("ordinary", null);

        var result = await ScanAsync(new FakePackageSource(true, null,
            Package("Ordinary_1.0.0.0_x64__test", "Ordinary_family", "Ordinary app", Location("Installed", root))));

        Assert.Equal(XboxInstalledGameCatalogOutcome.Completed, result.Outcome);
        Assert.Empty(result.Games);
        Assert.Equal(0, result.SkippedPackageCount);
    }

    [Fact]
    public async Task Malformed_config_does_not_abort_a_later_valid_package()
    {
        var malformedRoot = CreatePackage("malformed", "<Game>");
        var validRoot = CreatePackage("later-valid", ValidConfig);

        var result = await ScanAsync(new FakePackageSource(true, null,
            Package("Malformed_1.0.0.0_x64__test", "Malformed_family", "Malformed", Location("Installed", malformedRoot)),
            Package("Valid_1.0.0.0_x64__test", "Valid_family", "Valid", Location("Installed", validRoot))));

        Assert.Equal(XboxInstalledGameCatalogOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.SkippedPackageCount);
        Assert.Equal("store:9NABC123", Assert.Single(result.Games).Identity.Key);
    }

    [Fact]
    public async Task Duplicate_package_and_game_observations_do_not_duplicate_catalog_entries()
    {
        var root = CreatePackage("duplicate", ValidConfig);
        var first = Package("Duplicate_1.0.0.0_x64__test", "Duplicate_family", "Duplicate", Location("Installed", root));
        var secondVersion = Package("Duplicate_2.0.0.0_x64__test", "Duplicate_family", "Duplicate", Location("Installed", root));

        var result = await ScanAsync(new FakePackageSource(true, null, first, first, secondVersion));

        Assert.Single(result.Games);
    }

    [Fact]
    public async Task Effective_and_installed_locations_with_the_same_root_are_deduplicated_case_insensitively()
    {
        var root = CreatePackage("same-root", ValidConfig);
        var distinctLocations = XboxInstalledGameCatalog.DistinctLocations(
            [Location("Effective", root), Location("Installed", root.ToUpperInvariant())]);

        Assert.Equal("Effective", Assert.Single(distinctLocations).Kind);

        var result = await ScanAsync(new FakePackageSource(true, null,
            Package("Sample_1.0.0.0_x64__test", "Sample_family", "Sample", Location("Effective", root), Location("Installed", root.ToUpperInvariant()))));

        var game = Assert.Single(result.Games);
        Assert.Equal(Path.Combine(root, "MicrosoftGame.config"), game.ConfigPath);
    }

    [Fact]
    public async Task Config_in_installed_location_is_used_when_effective_location_has_no_config()
    {
        var effectiveRoot = CreatePackage("effective-empty", null);
        var installedRoot = CreatePackage("installed-config", ValidConfig);

        var result = await ScanAsync(new FakePackageSource(true, null,
            Package("Sample_1.0.0.0_x64__test", "Sample_family", "Sample", Location("Effective", effectiveRoot), Location("Installed", installedRoot))));

        Assert.Equal(Path.Combine(installedRoot, "MicrosoftGame.config"), Assert.Single(result.Games).ConfigPath);
    }

    [Fact]
    public async Task Effective_location_has_precedence_when_both_locations_contain_a_valid_config()
    {
        var effectiveRoot = CreatePackage("effective-config", ValidConfig.Replace("9NABC123", "9NEFFECTIVE", StringComparison.Ordinal));
        var installedRoot = CreatePackage("installed-config", ValidConfig.Replace("9NABC123", "9NINSTALLED", StringComparison.Ordinal));

        var result = await ScanAsync(new FakePackageSource(true, null,
            Package("Sample_1.0.0.0_x64__test", "Sample_family", "Sample", Location("Effective", effectiveRoot), Location("Installed", installedRoot))));

        var game = Assert.Single(result.Games);
        Assert.Equal("store:9NEFFECTIVE", game.Identity.Key);
        Assert.Equal(Path.Combine(effectiveRoot, "MicrosoftGame.config"), game.ConfigPath);
    }

    [Fact]
    public async Task Package_metadata_config_path_is_used_without_payload_path_assumptions()
    {
        var metadataRoot = CreatePackage("package-metadata-on-c", ValidConfig);

        var result = await ScanAsync(new FakePackageSource(true, null,
            Package("Sample_1.0.0.0_x64__test", "Sample_family", "Sample", Location("Installed", metadataRoot))));

        Assert.Equal(Path.Combine(metadataRoot, "MicrosoftGame.config"), Assert.Single(result.Games).ConfigPath);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public async Task Enumeration_source_failure_maps_to_global_outcome_without_fallback(bool unavailable, int expected)
    {
        var source = new FakePackageSource(new XboxInstalledPackageEnumeration([], false, unavailable, "synthetic enumeration failure"));

        var result = await ScanAsync(source);

        Assert.Equal(1, source.CallCount);
        Assert.Equal((XboxInstalledGameCatalogOutcome)expected, result.Outcome);
        Assert.Empty(result.Games);
        Assert.Equal("synthetic enumeration failure", result.FailureReason);
    }

    [Fact]
    public async Task Package_metadata_failure_does_not_abort_later_valid_packages()
    {
        var root = CreatePackage("after-metadata-failure", ValidConfig);
        var badPackage = new XboxInstalledPackage("broken", "broken_full", "broken_family", "Broken", [], MetadataFailure: "metadata failure");

        var result = await ScanAsync(new FakePackageSource(true, null,
            badPackage,
            Package("Valid_1.0.0.0_x64__test", "Valid_family", "Valid", Location("Installed", root))));

        Assert.Equal(1, result.SkippedPackageCount);
        Assert.Single(result.Games);
    }

    [Fact]
    public async Task Package_location_failure_does_not_abort_other_packages()
    {
        var root = CreatePackage("after-location-failure", ValidConfig);
        var badPackage = new XboxInstalledPackage("broken", "broken_full", "broken_family", "Broken", [], LocationFailure: "location failure");

        var result = await ScanAsync(new FakePackageSource(true, null,
            badPackage,
            Package("Valid_1.0.0.0_x64__test", "Valid_family", "Valid", Location("Installed", root))));

        Assert.Equal(1, result.SkippedPackageCount);
        Assert.Single(result.Games);
    }

    [Fact]
    public async Task Config_over_limit_is_skipped_and_later_packages_are_still_scanned()
    {
        var largeRoot = CreatePackage("large", "<Game>" + new string(' ', 2 * 1024 * 1024) + "</Game>");
        var validRoot = CreatePackage("after-large", ValidConfig);

        var result = await ScanAsync(new FakePackageSource(true, null,
            Package("Large_1.0.0.0_x64__test", "Large_family", "Large", Location("Installed", largeRoot)),
            Package("Valid_1.0.0.0_x64__test", "Valid_family", "Valid", Location("Installed", validRoot))));

        Assert.Equal(1, result.SkippedPackageCount);
        Assert.Single(result.Games);
    }

    [Fact]
    public async Task Cancellation_is_observed_before_package_enumeration()
    {
        var source = new FakePackageSource(true, null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ScanAsync(source, cancellation.Token));
        Assert.Equal(0, source.CallCount);
    }

    [Fact]
    public async Task Cancellation_during_package_enumeration_is_observed_before_scan()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new FakePackageSource(new([], true, false, null), token => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ScanAsync(source, cancellation.Token));
        Assert.Equal(1, source.CallCount);
    }

    private async Task<XboxInstalledGameCatalogResult> ScanAsync(FakePackageSource source, CancellationToken cancellationToken = default) =>
        await new XboxInstalledGameCatalog(source).ScanAsync(cancellationToken);

    private string CreatePackage(string name, string? config)
    {
        var path = Path.Combine(_directory, name);
        Directory.CreateDirectory(path);
        if (config is not null)
            File.WriteAllText(Path.Combine(path, "MicrosoftGame.config"), config);
        return path;
    }

    private static XboxInstalledPackageLocation Location(string kind, string rootPath) => new(kind, rootPath);

    private static XboxInstalledPackage Package(string fullName, string familyName, string displayName, params XboxInstalledPackageLocation[] locations) =>
        new("PackageName", fullName, familyName, displayName, locations);

    private sealed class FakePackageSource(
        XboxInstalledPackageEnumeration enumeration,
        Action<CancellationToken>? onEnumerate = null) : IXboxInstalledPackageSource
    {
        public int CallCount { get; private set; }

        public FakePackageSource(bool succeeded, string? failureReason, params XboxInstalledPackage[] packages)
            : this(new(packages, succeeded, false, failureReason)) { }

        public XboxInstalledPackageEnumeration Enumerate(CancellationToken cancellationToken)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            onEnumerate?.Invoke(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return enumeration;
        }
    }
}
