using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Diagnostics.XboxCatalog;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class XboxCatalogDiagnosticTests : IDisposable
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

    public XboxCatalogDiagnosticTests() => AppLog.DirectoryOverride = Path.Combine(_directory, "logs");

    [Fact]
    public void Config_parser_reads_identity_optional_ids_and_all_executable_attributes()
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
        Assert.Equal(new XboxCatalogExecutable("Sample.exe", "Sample", "PC", "x64"), parsed.Config.Executables[0]);
        Assert.Equal(new XboxCatalogExecutable("SampleLauncher.exe", null, null, null), parsed.Config.Executables[1]);
    }

    [Fact]
    public void Config_parser_accepts_a_namespaced_Game_root_and_optional_store_or_title_ids()
    {
        const string xml = "<Game xmlns=\"urn:sample\"><Identity Name=\"Game\" Publisher=\"Publisher\"/><ExecutableList><Executable Name=\"Game.exe\"/></ExecutableList></Game>";

        var parsed = MicrosoftGameConfigReader.Parse(xml);

        Assert.True(parsed.XmlParsed);
        Assert.True(parsed.RecognizedRoot);
        Assert.Null(parsed.FailureReason);
        Assert.Null(parsed.Config!.StoreId);
        Assert.Null(parsed.Config.TitleId);
    }

    [Theory]
    [InlineData("<Game><ExecutableList><Executable Name=\"Game.exe\"/></ExecutableList></Game>", true, "Required Identity")]
    [InlineData("<Game><Identity Name=\"Game\" Publisher=\"Publisher\"/><ExecutableList><Executable /></ExecutableList></Game>", true, "usable Executable Name")]
    [InlineData("<Project><Identity Name=\"Game\" Publisher=\"Publisher\"/><ExecutableList><Executable Name=\"Game.exe\"/></ExecutableList></Project>", true, "Unrecognized root")]
    [InlineData("<Game>", false, "XmlException")]
    public void Config_parser_fails_closed_for_invalid_config_shapes(string xml, bool xmlParsed, string expectedFailure)
    {
        var parsed = MicrosoftGameConfigReader.Parse(xml);

        Assert.Equal(xmlParsed, parsed.XmlParsed);
        Assert.Null(parsed.Config);
        Assert.Contains(expectedFailure, parsed.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Candidate_key_uses_store_then_package_family_then_normalized_identity()
    {
        var config = MicrosoftGameConfigReader.Parse(ValidConfig).Config!;

        Assert.Equal("store:9NABC123", XboxCatalogDiagnostic.CreateCandidateKey(" 9NABC123 ", "Sample_family", config));
        Assert.Equal("pfn:Sample_family", XboxCatalogDiagnostic.CreateCandidateKey(null, " Sample_family ", config));
        Assert.Equal("identity:SAMPLE.GAME|CN%3DSAMPLE|PC", XboxCatalogDiagnostic.CreateCandidateKey(null, null, config));
    }

    [Fact]
    public async Task Package_family_candidate_key_is_stable_when_package_version_full_name_display_name_and_path_change()
    {
        var configWithoutStoreId = ValidConfig.Replace("<StoreId>9NABC123</StoreId>", string.Empty, StringComparison.Ordinal);
        var originalRoot = CreatePackage("original", configWithoutStoreId);
        var changedRoot = CreatePackage("changed", configWithoutStoreId);
        var timestamp = new DateTimeOffset(2026, 10, 5, 12, 30, 0, TimeSpan.Zero);
        var original = await new XboxCatalogDiagnostic(
            new FakePackageSource(true, null, Package("Sample_1.0.0.0_x64__test", "Sample_family", "Original display name", originalRoot)),
            Path.Combine(_directory, "logs"), () => timestamp).RunAsync();
        var changed = await new XboxCatalogDiagnostic(
            new FakePackageSource(true, null, Package("Sample_2.0.0.0_x64__test", "Sample_family", "Changed display name", changedRoot)),
            Path.Combine(_directory, "logs"), () => timestamp).RunAsync();

        Assert.Equal("pfn:Sample_family", Assert.Single(original.Games).CandidateKey);
        Assert.Equal(Assert.Single(original.Games).CandidateKey, Assert.Single(changed.Games).CandidateKey);
    }

    [Fact]
    public async Task Zero_packages_completes_with_zero_games()
    {
        var result = await new XboxCatalogDiagnostic(
            new FakePackageSource(true, null),
            Path.Combine(_directory, "logs")).RunAsync();

        Assert.Equal(FrontendXboxCatalogDiagnosticOutcome.Completed, result.Outcome);
        Assert.Equal(0, result.EnumeratedPackageCount);
        Assert.Equal(0, result.ValidGameCount);
        Assert.Empty(result.Games);
        Assert.Equal(0, result.SkippedOrFailedCount);
    }

    [Fact]
    public async Task Scan_reports_package_counts_candidates_identity_and_a_durable_text_report()
    {
        var gameRoot = CreatePackage("sample", ValidConfig);
        var ordinaryRoot = CreatePackage("ordinary", config: null);
        var source = new FakePackageSource(true, null,
            Package("Sample_1.0.0.0_x64__test", "Sample_family", "Sample", gameRoot),
            Package("Ordinary_1.0.0.0_x64__test", "Ordinary_family", "Ordinary app", ordinaryRoot));
        var diagnostic = new XboxCatalogDiagnostic(source, Path.Combine(_directory, "logs"), () => new DateTimeOffset(2026, 10, 5, 12, 30, 0, TimeSpan.Zero));

        var result = await diagnostic.RunAsync();

        Assert.Equal(FrontendXboxCatalogDiagnosticOutcome.Completed, result.Outcome);
        Assert.Equal(2, result.EnumeratedPackageCount);
        Assert.Equal(2, result.AccessiblePackageCount);
        Assert.Equal(1, result.ConfigCandidateCount);
        Assert.Equal(1, result.ParsedConfigCount);
        Assert.Equal(1, result.ValidGameCount);
        Assert.Equal("store:9NABC123", Assert.Single(result.Games).CandidateKey);
        Assert.Equal("Sample title", result.Games[0].DisplayName);
        Assert.Equal("Installed", result.Games[0].PackageLocationKind);
        Assert.NotNull(result.ReportPath);
        var report = await File.ReadAllTextAsync(result.ReportPath!);
        Assert.Contains("FindPackagesForUser", report, StringComparison.Ordinal);
        Assert.Contains("Identity Publisher: CN=Sample", report, StringComparison.Ordinal);
        Assert.Contains("Name=SampleLauncher.exe", report, StringComparison.Ordinal);
        Assert.Contains("StoreId: 9NABC123", report, StringComparison.Ordinal);
        Assert.Contains("TitleId: 12345678", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_package_config_does_not_abort_later_packages()
    {
        var malformedRoot = CreatePackage("malformed", "<Game>");
        var validRoot = CreatePackage("valid", ValidConfig);
        var source = new FakePackageSource(true, null,
            Package("Malformed_1.0.0.0_x64__test", "Malformed_family", "Malformed", malformedRoot),
            Package("Valid_1.0.0.0_x64__test", "Valid_family", "Valid", validRoot));

        var result = await new XboxCatalogDiagnostic(source, Path.Combine(_directory, "logs")).RunAsync();

        Assert.Equal(FrontendXboxCatalogDiagnosticOutcome.Completed, result.Outcome);
        Assert.Equal(2, result.ConfigCandidateCount);
        Assert.Equal(1, result.ParsedConfigCount);
        Assert.Equal(1, result.ValidGameCount);
        Assert.Equal(1, result.SkippedOrFailedCount);
        Assert.Contains(result.Failures, failure => failure.Stage == "ConfigParse");
    }

    [Fact]
    public async Task Enumeration_failure_is_reported_without_using_a_fallback_source()
    {
        var source = new FakePackageSource(false, "UnauthorizedAccessException (HRESULT 0x80070005): Access denied.");

        var result = await new XboxCatalogDiagnostic(source, Path.Combine(_directory, "logs")).RunAsync();

        Assert.Equal(FrontendXboxCatalogDiagnosticOutcome.Failed, result.Outcome);
        Assert.Equal(0, result.EnumeratedPackageCount);
        Assert.Equal("PackageEnumeration", Assert.Single(result.Failures).Stage);
        Assert.NotNull(result.ReportPath);
        Assert.Contains("Succeeded: False", await File.ReadAllTextAsync(result.ReportPath!));
    }

    [Fact]
    public async Task Duplicate_observations_do_not_emit_duplicate_game_rows()
    {
        var gameRoot = CreatePackage("duplicate", ValidConfig);
        var package = Package("Duplicate_1.0.0.0_x64__test", "Duplicate_family", "Duplicate", gameRoot);
        var source = new FakePackageSource(true, null, package, package);

        var result = await new XboxCatalogDiagnostic(source, Path.Combine(_directory, "logs")).RunAsync();

        Assert.Equal(2, result.EnumeratedPackageCount);
        Assert.Equal(1, result.ValidGameCount);
        Assert.Single(result.Games);
    }

    [Fact]
    public async Task Cancellation_is_observed_before_package_enumeration()
    {
        var source = new FakePackageSource(true, null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new XboxCatalogDiagnostic(source, Path.Combine(_directory, "logs")).RunAsync(cancellation.Token));
        Assert.Equal(0, source.CallCount);
    }

    public void Dispose()
    {
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private string CreatePackage(string name, string? config)
    {
        var path = Path.Combine(_directory, name);
        Directory.CreateDirectory(path);
        if (config is not null)
            File.WriteAllText(Path.Combine(path, "MicrosoftGame.config"), config);
        return path;
    }

    private static XboxCatalogPackage Package(string fullName, string familyName, string displayName, string root) =>
        new("PackageName", fullName, familyName, displayName, [new XboxCatalogPackageLocation("Installed", root)]);

    private sealed class FakePackageSource(bool succeeded, string? failureReason, params XboxCatalogPackage[] packages) : IXboxCatalogPackageSource
    {
        public int CallCount { get; private set; }

        public XboxCatalogPackageEnumeration Enumerate(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return new(packages, succeeded, false, failureReason);
        }
    }
}
