using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.GameDetection.Windows;
using SteamInputAddonforClaw.Xbox;
using SteamInputAddonforClaw.Xbox.Session;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class XboxGameSessionRuntimeTests : IAsyncLifetime
{
    private const string ValidConfig = """
        <MicrosoftGame>
          <Identity Name="Sample.Game" Publisher="CN=Sample" ResourceId="pc" />
          <ShellVisuals DefaultDisplayName="Sample Display" />
          <ExecutableList><Executable Name="Sample.exe" /></ExecutableList>
        </MicrosoftGame>
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SteamAddon.XboxSession.{Guid.NewGuid():N}");

    public Task InitializeAsync()
    {
        AppLog.DirectoryOverride = Path.Combine(_directory, "logs");
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Start_arms_hooks_and_recovers_an_already_running_foreground_game_once()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 100 };
        var probe = new FakeProcessProbe();
        probe.Set(100, 1, MatchedInspection(100));
        await using var runtime = CreateRuntime(windows, probe);
        var changes = new List<ActiveXboxGame?>();
        runtime.ActiveGameChanged += changes.Add;

        await runtime.StartAsync();

        Assert.True(runtime.CanReliablyReportNoActiveGame);
        Assert.Equal(new ActiveXboxGame("store:9NABC123", "Sample Display"), runtime.ActiveGame);
        Assert.Equal([runtime.ActiveGame], changes);
        Assert.Equal(1, windows.StartCount);
        Assert.Equal(0, windows.EnumerationCount);
        Assert.Equal(1, probe.InspectionCount);
    }

    [Fact]
    public async Task Startup_uses_one_bounded_window_reconcile_when_foreground_is_not_the_game()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 11, ProcessIds = [11, 22, 22] };
        var probe = new FakeProcessProbe();
        probe.Set(11, 1, Negative(XboxGameProcessInspectionDisposition.NoPackage));
        probe.Set(22, 1, MatchedInspection(22));
        await using var runtime = CreateRuntime(windows, probe);

        await runtime.StartAsync();

        Assert.Equal("store:9NABC123", runtime.ActiveGame?.Key);
        Assert.Equal(1, windows.EnumerationCount);
        Assert.Equal(512, windows.LastEnumerationLimit);
        Assert.Equal(2, probe.InspectionCount);
    }

    [Fact]
    public async Task Successful_startup_reconcile_can_confirm_idle_without_a_game()
    {
        await using var runtime = CreateRuntime(new FakeWindowSource(), new FakeProcessProbe());

        await runtime.StartAsync();

        Assert.Null(runtime.ActiveGame);
        Assert.True(runtime.CanReliablyReportNoActiveGame);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Show")]
    [InlineData("Foreground")]
    public async Task Window_observations_activate_a_positive_candidate(string eventKind)
    {
        var windows = new FakeWindowSource();
        var probe = new FakeProcessProbe();
        probe.Set(30, 1, MatchedInspection(30));
        await using var runtime = CreateRuntime(windows, probe);
        await runtime.StartAsync();

        windows.Raise(Enum.Parse<GameWindowEventKind>(eventKind), 30);
        await WaitUntilAsync(() => runtime.ActiveGame is not null);

        Assert.Equal("store:9NABC123", runtime.ActiveGame?.Key);
        Assert.Equal("Sample Display", runtime.ActiveGame?.DisplayName);
        Assert.Equal(1, probe.InspectionCount);
    }

    [Fact]
    public async Task Duplicate_events_do_not_reinspect_or_republish_a_classified_generation()
    {
        var windows = new FakeWindowSource();
        var probe = new FakeProcessProbe();
        probe.Set(30, 1, MatchedInspection(30));
        await using var runtime = CreateRuntime(windows, probe);
        var changes = new List<ActiveXboxGame?>();
        runtime.ActiveGameChanged += changes.Add;
        await runtime.StartAsync();

        windows.Raise(GameWindowEventKind.Create, 30);
        await WaitUntilAsync(() => runtime.ActiveGame is not null);
        windows.Raise(GameWindowEventKind.Show, 30);
        windows.Raise(GameWindowEventKind.Foreground, 30);
        await WaitUntilAsync(() => probe.OpenCount >= 3);

        Assert.Equal(1, probe.InspectionCount);
        Assert.Single(changes);
    }

    [Fact]
    public async Task Alt_tab_does_not_clear_the_active_game()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 40 };
        var probe = new FakeProcessProbe();
        probe.Set(40, 1, MatchedInspection(40));
        probe.Set(41, 1, Negative(XboxGameProcessInspectionDisposition.NoPackage));
        await using var runtime = CreateRuntime(windows, probe);
        await runtime.StartAsync();

        windows.Raise(GameWindowEventKind.Foreground, 41);
        await WaitUntilAsync(() => probe.InspectionCount == 2);

        Assert.Equal("store:9NABC123", runtime.ActiveGame?.Key);
    }

    [Fact]
    public async Task Active_process_exit_clears_the_fact_once()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 50 };
        var probe = new FakeProcessProbe();
        probe.Set(50, 1, MatchedInspection(50));
        await using var runtime = CreateRuntime(windows, probe);
        var changes = new List<ActiveXboxGame?>();
        runtime.ActiveGameChanged += changes.Add;
        await runtime.StartAsync();
        var generation = Assert.Single(probe.Generations, item => item.Key == new GameProcessGenerationKey(50, 1));

        generation.Signal();
        await WaitUntilAsync(() => runtime.ActiveGame is null);
        generation.RaiseStaleExit();
        await Task.Delay(20);

        Assert.Null(runtime.ActiveGame);
        Assert.Equal(2, changes.Count);
        Assert.Null(changes[^1]);
    }

    [Fact]
    public async Task New_process_generation_replaces_pid_reuse_and_stale_exit_cannot_clear_it()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 60 };
        var probe = new FakeProcessProbe();
        probe.Set(60, 1, MatchedInspection(60, "store:old"));
        await using var runtime = CreateRuntime(windows, probe);
        await runtime.StartAsync();
        var oldGeneration = Assert.Single(probe.Generations, item => item.Key == new GameProcessGenerationKey(60, 1));

        probe.Set(60, 2, MatchedInspection(60, "store:new"));
        windows.Raise(GameWindowEventKind.Foreground, 60);
        await WaitUntilAsync(() => runtime.ActiveGame?.Key == "store:new");
        oldGeneration.RaiseStaleExit();
        await Task.Delay(20);

        Assert.Equal("store:new", runtime.ActiveGame?.Key);
        Assert.Equal(new GameProcessGenerationKey(60, 2), probe.ActiveKey(60));
    }

    [Fact]
    public async Task Second_positive_live_process_does_not_replace_current_active_game()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 61 };
        var probe = new FakeProcessProbe();
        probe.Set(61, 1, MatchedInspection(61, "store:first"));
        probe.Set(62, 1, MatchedInspection(62, "store:second"));
        await using var runtime = CreateRuntime(windows, probe);
        await runtime.StartAsync();

        windows.Raise(GameWindowEventKind.Create, 62);
        await WaitUntilAsync(() => probe.InspectionCount == 2);

        Assert.Equal("store:first", runtime.ActiveGame?.Key);
    }

    [Fact]
    public async Task Failed_initial_hook_start_is_feature_local_and_a_later_resume_can_rearm()
    {
        var windows = new FakeWindowSource { FailOnStartNumber = 1, ForegroundProcessId = 70 };
        var probe = new FakeProcessProbe();
        probe.Set(70, 1, MatchedInspection(70));
        await using var runtime = CreateRuntime(windows, probe);

        await runtime.StartAsync();
        Assert.Null(runtime.ActiveGame);
        Assert.False(runtime.CanReliablyReportNoActiveGame);
        Assert.Equal(1, windows.StartCount);

        await runtime.ReconcileAfterResumeAsync();

        Assert.True(runtime.CanReliablyReportNoActiveGame);
        Assert.Equal(2, windows.StartCount);
        Assert.Equal("store:9NABC123", runtime.ActiveGame?.Key);
    }

    [Fact]
    public async Task Failed_initial_bounded_reconcile_stays_unready_until_a_later_resume_reconcile_succeeds()
    {
        var windows = new FakeWindowSource { EnumerationException = new InvalidOperationException("window enumeration failed") };
        await using var runtime = CreateRuntime(windows, new FakeProcessProbe());

        await runtime.StartAsync();

        Assert.False(runtime.CanReliablyReportNoActiveGame);
        windows.EnumerationException = null;
        await runtime.ReconcileAfterResumeAsync();

        Assert.True(runtime.CanReliablyReportNoActiveGame);
    }

    [Fact]
    public async Task Resume_rearms_hooks_retires_non_active_cache_and_retains_a_live_active_generation()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 100 };
        var probe = new FakeProcessProbe();
        probe.Set(100, 1, MatchedInspection(100));
        probe.Set(101, 1, Negative(XboxGameProcessInspectionDisposition.NoPackage));
        await using var runtime = CreateRuntime(windows, probe);
        await runtime.StartAsync();
        var activeGeneration = Assert.Single(probe.Generations, item => item.ProcessId == 100);
        windows.Raise(GameWindowEventKind.Create, 101);
        await WaitUntilAsync(() => probe.InspectionCount == 2);
        var retiredCandidate = Assert.Single(probe.Generations, item => item.ProcessId == 101);

        await runtime.ReconcileAfterResumeAsync();
        windows.Raise(GameWindowEventKind.Show, 101);
        await WaitUntilAsync(() => probe.InspectionCount == 3);

        Assert.Equal("store:9NABC123", runtime.ActiveGame?.Key);
        Assert.False(activeGeneration.IsDisposed);
        Assert.True(retiredCandidate.IsDisposed);
        Assert.Equal(2, windows.StartCount);
        Assert.Equal(1, windows.StopCount);
    }

    [Fact]
    public async Task Resume_with_signaled_active_generation_clears_it_and_hook_failure_keeps_runtime_available()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 102, FailOnStartNumber = 2 };
        var probe = new FakeProcessProbe();
        probe.Set(102, 1, MatchedInspection(102));
        await using var runtime = CreateRuntime(windows, probe);
        var changes = new List<ActiveXboxGame?>();
        runtime.ActiveGameChanged += changes.Add;
        await runtime.StartAsync();
        var activeGeneration = Assert.Single(probe.Generations);
        activeGeneration.IsSignaled = true;

        await runtime.ReconcileAfterResumeAsync();

        Assert.False(runtime.CanReliablyReportNoActiveGame);
        Assert.Null(runtime.ActiveGame);
        Assert.Equal(2, windows.StartCount);
        Assert.Null(changes[^1]);
    }

    [Fact]
    public async Task Event_source_failure_retains_a_live_active_game_until_its_handle_exits()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 103 };
        var probe = new FakeProcessProbe();
        probe.Set(103, 1, MatchedInspection(103));
        await using var runtime = CreateRuntime(windows, probe);
        await runtime.StartAsync();
        var generation = Assert.Single(probe.Generations);

        windows.Fail(new InvalidOperationException("WinEvent message loop failed."));
        await WaitUntilAsync(() => windows.StopCount == 1);
        Assert.False(runtime.CanReliablyReportNoActiveGame);
        Assert.Equal("store:9NABC123", runtime.ActiveGame?.Key);

        generation.Signal();
        await WaitUntilAsync(() => runtime.ActiveGame is null);
        Assert.Null(runtime.ActiveGame);
        Assert.False(runtime.CanReliablyReportNoActiveGame);
    }

    [Fact]
    public async Task Shutdown_unregisters_waits_and_disposes_retained_process_handles()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 104 };
        var probe = new FakeProcessProbe();
        probe.Set(104, 1, MatchedInspection(104));
        var runtime = CreateRuntime(windows, probe);
        var changes = new List<ActiveXboxGame?>();
        runtime.ActiveGameChanged += changes.Add;
        await runtime.StartAsync();

        await runtime.DisposeAsync();

        Assert.All(probe.Generations, generation => Assert.True(generation.IsDisposed));
        Assert.Equal(1, windows.StopCount);
        Assert.Null(runtime.ActiveGame);
        Assert.Null(changes[^1]);
    }

    [Fact]
    public async Task Identity_evaluator_fails_closed_for_image_package_config_and_exact_executable_evidence()
    {
        var root = Path.Combine(_directory, "package");
        Directory.CreateDirectory(root);
        var generation = new FakeProcessGeneration(90, 1);

        var imageFailure = await EvaluateAsync(generation, Evidence(imageResultCode: 5));
        var noPackage = await EvaluateAsync(generation, Evidence(packageFullNameResultCode: 15700));
        var packageFailure = await EvaluateAsync(generation, Evidence(packageFullNameResultCode: 5));
        var configLocationFailure = await EvaluateAsync(generation, Evidence(configLocationFailure: "PackageManager failed."));
        var missingConfig = await EvaluateAsync(generation, Evidence(configLocations: [new("Effective", root)]));
        await File.WriteAllTextAsync(Path.Combine(root, "MicrosoftGame.config"), "<Game>");
        var malformedConfig = await EvaluateAsync(generation, Evidence(configLocations: [new("Effective", root)]));
        await File.WriteAllTextAsync(Path.Combine(root, "MicrosoftGame.config"), new string('x', 2 * 1024 * 1024 + 1));
        var oversizedConfig = await EvaluateAsync(generation, Evidence(configLocations: [new("Effective", root)]));
        await File.WriteAllTextAsync(Path.Combine(root, "MicrosoftGame.config"), ValidConfig);
        var mismatch = await EvaluateAsync(generation, Evidence(runningPath: @"D:\Games\Launcher.exe", configLocations: [new("Effective", root)]));

        Assert.Equal(XboxGameProcessInspectionDisposition.ProcessImageFailure, imageFailure.Disposition);
        Assert.Equal(XboxGameProcessInspectionDisposition.NoPackage, noPackage.Disposition);
        Assert.Equal(XboxGameProcessInspectionDisposition.PackageIdentityFailure, packageFailure.Disposition);
        Assert.Equal(XboxGameProcessInspectionDisposition.ConfigNegative, configLocationFailure.Disposition);
        Assert.Equal(XboxGameProcessInspectionDisposition.ConfigNegative, missingConfig.Disposition);
        Assert.Equal(XboxGameProcessInspectionDisposition.ConfigNegative, malformedConfig.Disposition);
        Assert.Equal(XboxGameProcessInspectionDisposition.ConfigNegative, oversizedConfig.Disposition);
        Assert.Equal(XboxGameProcessInspectionDisposition.ExecutableMismatch, mismatch.Disposition);
        Assert.Null(imageFailure.Match);
        Assert.Null(mismatch.Match);
    }

    [Fact]
    public async Task Exact_executable_match_accepts_D_drive_process_with_C_drive_package_config_and_uses_production_identity()
    {
        var root = Path.Combine(_directory, "metadata-on-c");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "MicrosoftGame.config"), ValidConfig);

        var inspection = await EvaluateAsync(new FakeProcessGeneration(91, 1), Evidence(
            runningPath: @"D:\InstalledGames\sample.EXE",
            packageDisplayName: "Package Display",
            packageName: "Sample.Package",
            configLocations: [new("Effective", root)]));

        Assert.Equal(XboxGameProcessInspectionDisposition.Matched, inspection.Disposition);
        Assert.Equal("pfn:Sample_family", inspection.Match?.Identity.Key);
        Assert.Equal("Sample Display", inspection.Match?.Identity.DisplayName);
        Assert.Equal((uint)91, inspection.Match?.ProcessId);
        Assert.Equal("sample.EXE", inspection.Match?.RunningExecutableName);
    }

    [Fact]
    public async Task Package_display_name_and_executable_basename_are_fallbacks_for_display_name()
    {
        var root = Path.Combine(_directory, "display-fallback");
        Directory.CreateDirectory(root);
        const string config = "<Game><Identity Name=\"Sample.Game\" Publisher=\"CN=Sample\"/><ExecutableList><Executable Name=\"Sample.exe\"/></ExecutableList></Game>";
        await File.WriteAllTextAsync(Path.Combine(root, "MicrosoftGame.config"), config);

        var packageName = await EvaluateAsync(new FakeProcessGeneration(92, 1), Evidence(
            packageDisplayName: "Package Display", packageName: "Sample.Package", configLocations: [new("Effective", root)]));
        var executableName = await EvaluateAsync(new FakeProcessGeneration(93, 1), Evidence(
            packageDisplayName: null, packageName: null, configLocations: [new("Effective", root)]));

        Assert.Equal("Package Display", packageName.Match?.Identity.DisplayName);
        Assert.Equal("Sample.exe", executableName.Match?.Identity.DisplayName);
    }

    [Fact]
    public async Task Package_location_resolver_uses_exact_current_user_package_and_effective_or_installed_locations()
    {
        var effectiveRoot = Path.Combine(_directory, "effective");
        var installedRoot = Path.Combine(_directory, "installed");
        Directory.CreateDirectory(installedRoot);
        await File.WriteAllTextAsync(Path.Combine(installedRoot, "MicrosoftGame.config"), ValidConfig);
        const string packageFullName = "Sample_1.0.0.0_x64__test";
        var lookupCount = 0;

        var resolution = XboxGamePackageConfigLocationResolver.ResolveCurrentUserPackage(
            packageFullName,
            (userSecurityId, resolvedPackageFullName) =>
            {
                lookupCount++;
                Assert.Equal(string.Empty, userSecurityId);
                Assert.Equal(packageFullName, resolvedPackageFullName);
                return new(() => effectiveRoot, () => installedRoot)
                {
                    DisplayName = () => "Package Display",
                    Name = () => "Sample.Package",
                };
            });
        var inspection = await EvaluateAsync(new FakeProcessGeneration(94, 1), Evidence(
            packageFullName: packageFullName,
            packageDisplayName: resolution.PackageDisplayName,
            packageName: resolution.PackageName,
            configLocations: resolution.Locations,
            configLocationFailure: resolution.FailureReason));

        Assert.Equal(1, lookupCount);
        Assert.Equal(["Effective", "Installed"], resolution.Locations.Select(location => location.Kind));
        Assert.Equal(XboxGameProcessInspectionDisposition.Matched, inspection.Disposition);
        Assert.Equal("Sample Display", inspection.Match?.Identity.DisplayName);
    }

    [Fact]
    public async Task Optional_package_display_metadata_failures_do_not_block_config_identity_match()
    {
        var root = Path.Combine(_directory, "optional-package-metadata-failure");
        Directory.CreateDirectory(root);
        const string config = "<Game><Identity Name=\"Sample.Game\" Publisher=\"CN=Sample\"/><ExecutableList><Executable Name=\"Sample.exe\"/></ExecutableList></Game>";
        await File.WriteAllTextAsync(Path.Combine(root, "MicrosoftGame.config"), config);
        const string packageFullName = "Sample_1.0.0.0_x64__test";

        var resolution = XboxGamePackageConfigLocationResolver.ResolveCurrentUserPackage(
            packageFullName,
            (_, _) => new(() => root, () => null)
            {
                DisplayName = static () => throw new InvalidOperationException("Display metadata unavailable."),
                Name = static () => throw new InvalidOperationException("Package name metadata unavailable."),
            });
        var inspection = await EvaluateAsync(new FakeProcessGeneration(95, 1), Evidence(
            packageFullName: packageFullName,
            packageDisplayName: resolution.PackageDisplayName,
            packageName: resolution.PackageName,
            configLocations: resolution.Locations,
            configLocationFailure: resolution.FailureReason));

        Assert.Single(resolution.Locations);
        Assert.Null(resolution.PackageDisplayName);
        Assert.Null(resolution.PackageName);
        Assert.Equal(XboxGameProcessInspectionDisposition.Matched, inspection.Disposition);
        Assert.Equal("Sample.exe", inspection.Match?.Identity.DisplayName);
    }

    [Fact]
    public void Package_location_resolver_deduplicates_and_keeps_a_valid_installed_location_if_effective_fails()
    {
        var root = Path.Combine(_directory, "same-location");
        var deduped = XboxGamePackageConfigLocationResolver.ResolveCurrentUserPackage(
            "Sample_1.0.0.0_x64__test", (_, _) => new(() => root, () => root.ToUpperInvariant()));
        var failedEffective = XboxGamePackageConfigLocationResolver.ResolveCurrentUserPackage(
            "Sample_1.0.0.0_x64__test",
            (_, _) => new(() => throw new UnauthorizedAccessException("effective unavailable"), () => root));

        Assert.Equal("Effective", Assert.Single(deduped.Locations).Kind);
        Assert.Contains("Effective location:", failedEffective.FailureReason, StringComparison.Ordinal);
        Assert.Equal(root, Assert.Single(failedEffective.Locations).RootPath);
    }

    [Fact]
    public async Task Canonical_key_uses_store_id_then_package_family_then_normalized_config_identity()
    {
        var root = Path.Combine(_directory, "identity-keys");
        Directory.CreateDirectory(root);
        const string config = "<Game><Identity Name=\"Sample.Game\" Publisher=\"CN=Sample\" ResourceId=\"PC\"/><StoreId>Store-123</StoreId><ExecutableList><Executable Name=\"Sample.exe\"/></ExecutableList></Game>";
        await File.WriteAllTextAsync(Path.Combine(root, "MicrosoftGame.config"), config);

        var store = await EvaluateAsync(new FakeProcessGeneration(95, 1), Evidence(configLocations: [new("Effective", root)]));
        var noStore = config.Replace("<StoreId>Store-123</StoreId>", string.Empty, StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(root, "MicrosoftGame.config"), noStore);
        var family = await EvaluateAsync(new FakeProcessGeneration(96, 1), Evidence(configLocations: [new("Effective", root)]));
        var identity = await EvaluateAsync(new FakeProcessGeneration(97, 1), Evidence(
            packageFamilyName: null, packageFamilyNameResultCode: 0, configLocations: [new("Effective", root)]));

        Assert.Equal("store:Store-123", store.Match?.Identity.Key);
        Assert.Equal("pfn:Sample_family", family.Match?.Identity.Key);
        Assert.Equal("identity:SAMPLE.GAME|CN%3DSAMPLE|PC", identity.Match?.Identity.Key);
    }

    [Fact]
    public void Production_session_source_keeps_the_event_and_process_lifetime_contract_without_diagnostic_evidence()
    {
        var sourceRoot = Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw");
        var sessionRoot = Path.Combine(sourceRoot, "Xbox", "Session");
        var foundationRoot = Path.Combine(sourceRoot, "GameDetection", "Windows");
        var sources = string.Join("\n", Directory.GetFiles(sessionRoot, "*.cs").Select(File.ReadAllText));
        var hookSource = File.ReadAllText(Path.Combine(foundationRoot, "WindowsGameWindowEventSource.cs"));
        var processSource = File.ReadAllText(Path.Combine(foundationRoot, "WindowsGameProcess.cs"));
        var callbackStart = hookSource.IndexOf("private void OnWinEvent", StringComparison.Ordinal);
        var callbackEnd = hookSource.IndexOf("private delegate void WinEventProc", callbackStart, StringComparison.Ordinal);
        var callback = hookSource[callbackStart..callbackEnd];
        var runtime = File.ReadAllText(Path.Combine(sessionRoot, "XboxGameSessionRuntime.cs"));
        var probe = File.ReadAllText(Path.Combine(sessionRoot, "XboxGameProcessIdentityProbe.cs"));

        foreach (var eventName in new[] { "EventSystemForeground", "EventObjectCreate", "EventObjectShow" })
            Assert.Contains(eventName, hookSource, StringComparison.Ordinal);
        Assert.Contains("RegisterWaitForSingleObject", processSource, StringComparison.Ordinal);
        Assert.Contains("_observation?.Invoke(new GameWindowObservation", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenProcess", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("InspectAsync", callback, StringComparison.Ordinal);
        Assert.Contains("ProcessQueryLimitedInformation | Synchronize", processSource, StringComparison.Ordinal);
        Assert.Contains("generation.QueryImagePath()", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("QueryFullProcessImageNameW", probe, StringComparison.Ordinal);
        Assert.Contains("GetPackageFullName", probe, StringComparison.Ordinal);
        Assert.Contains("GetPackageFamilyName", probe, StringComparison.Ordinal);
        Assert.Contains("new PackageManager().FindPackageForUser(userSecurityId, packageFullName)", probe, StringComparison.Ordinal);
        Assert.Contains("package.EffectiveLocation?.Path", probe, StringComparison.Ordinal);
        Assert.Contains("package.InstalledLocation?.Path", probe, StringComparison.Ordinal);
        Assert.Contains("XboxGameIdentity.CreateKey", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("GetApplicationUserModelId", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPackageId", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPackagePathByFullName2", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("FrontendXboxSessionDiagnostic", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("ProfileStore", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("CpuBoostRuntime", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestControllerPresentationReconcile", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("PeriodicTimer", sources + hookSource + processSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", sources + hookSource + processSource, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Threading.Timer", sources + hookSource + processSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", sources + hookSource + processSource, StringComparison.Ordinal);
    }

    public async Task DisposeAsync()
    {
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory))
            await Task.Run(() => Directory.Delete(_directory, recursive: true));
    }

    private XboxGameSessionRuntime CreateRuntime(FakeWindowSource windows, FakeProcessProbe probe) => new(windows, probe);

    private async Task<XboxGameProcessInspection> EvaluateAsync(
        FakeProcessGeneration generation,
        XboxGameProcessIdentityEvidence evidence) =>
        await XboxGameProcessIdentityEvaluator.InspectAsync(generation, evidence, CancellationToken.None);

    private static XboxGameProcessInspection MatchedInspection(uint processId, string key = "store:9NABC123")
    {
        var identity = new XboxGameIdentity(key, "Sample Display", "9NABC123", "Sample_family");
        return new(XboxGameProcessInspectionDisposition.Matched, null,
            new(identity, processId, "Sample.exe"));
    }

    private static XboxGameProcessInspection Negative(XboxGameProcessInspectionDisposition disposition) =>
        new(disposition, "Expected negative evidence.", null);

    private static XboxGameProcessIdentityEvidence Evidence(
        int imageResultCode = 0,
        string? runningPath = @"D:\Games\Sample.exe",
        int packageFullNameResultCode = 0,
        string? packageFullName = "Sample_1.0.0.0_x64__test",
        int packageFamilyNameResultCode = 0,
        string? packageFamilyName = "Sample_family",
        string? packageDisplayName = "Package Display",
        string? packageName = "Sample.Package",
        IReadOnlyList<XboxGamePackageConfigLocation>? configLocations = null,
        string? configLocationFailure = null) =>
        new(imageResultCode, runningPath, packageFullNameResultCode, packageFullName,
            packageFamilyNameResultCode, packageFamilyName, packageDisplayName, packageName,
            configLocations ?? [], configLocationFailure);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (predicate())
                return;
            await Task.Delay(10);
        }
        throw new Xunit.Sdk.XunitException("The production XBOX game-session runtime did not reach the expected state.");
    }

    private sealed class FakeWindowSource : IGameWindowEventSource
    {
        private Action<GameWindowObservation>? _observation;
        private Action<Exception>? _failure;
        public uint ForegroundProcessId { get; set; }
        public IReadOnlyList<uint> ProcessIds { get; set; } = [];
        public int FailOnStartNumber { get; set; }
        public Exception? EnumerationException { get; set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int EnumerationCount { get; private set; }
        public int LastEnumerationLimit { get; private set; }

        public Task StartAsync(Action<GameWindowObservation> observation, Action<Exception> failure, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            if (StartCount == FailOnStartNumber)
                throw new InvalidOperationException("WinEvent hook start failed.");
            _observation = observation;
            _failure = failure;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            _observation = null;
            _failure = null;
            StopCount++;
            return Task.CompletedTask;
        }

        public uint GetForegroundProcessId() => ForegroundProcessId;

        public IReadOnlyList<uint> EnumerateTopLevelProcessIds(int maximumProcessCount)
        {
            EnumerationCount++;
            LastEnumerationLimit = maximumProcessCount;
            if (EnumerationException is not null) throw EnumerationException;
            return ProcessIds.Take(maximumProcessCount).ToArray();
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        internal void Raise(GameWindowEventKind kind, uint processId) => _observation?.Invoke(new(kind, (nint)processId, processId));
        internal void Fail(Exception exception) => _failure?.Invoke(exception);
    }

    private sealed class FakeProcessProbe : IXboxGameProcessIdentityProbe
    {
        private readonly Dictionary<uint, (long CreationTime, XboxGameProcessInspection Inspection)> _current = [];
        private readonly Dictionary<(uint ProcessId, long CreationTime), FakeProcessGeneration> _generations = [];
        internal List<FakeProcessGeneration> Generations { get; } = [];
        internal int InspectionCount { get; private set; }
        internal int OpenCount { get; private set; }

        internal void Set(uint processId, long creationTime, XboxGameProcessInspection inspection) =>
            _current[processId] = (creationTime, inspection);

        internal GameProcessGenerationKey ActiveKey(uint processId) =>
            new(processId, _current[processId].CreationTime);

        public GameProcessOpenResult Open(uint processId)
        {
            OpenCount++;
            if (!_current.TryGetValue(processId, out var configured))
                return new(null, 5);
            var generation = new FakeProcessGeneration(processId, configured.CreationTime);
            Generations.Add(generation);
            _generations.TryAdd((processId, configured.CreationTime), generation);
            return new(generation, 0);
        }

        public Task<XboxGameProcessInspection> InspectAsync(IGameProcessGeneration generation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectionCount++;
            var key = (generation.ProcessId, generation.Key.CreationTime);
            return Task.FromResult(_current[key.ProcessId].Inspection);
        }
    }

    private sealed class FakeProcessGeneration(uint processId, long creationTime) : IGameProcessGeneration
    {
        private Action<IGameProcessGeneration>? _exited;
        private Action<IGameProcessGeneration>? _lastExitHandler;

        public uint ProcessId { get; } = processId;
        public GameProcessGenerationKey Key { get; } = new(processId, creationTime);
        public bool IsSignaled { get; set; }
        public bool IsDisposed { get; private set; }
        public GameProcessImageQueryResult QueryImagePath() => new(true, @"D:\Games\Sample.exe", 0);
        public event Action<IGameProcessGeneration>? Exited
        {
            add => _exited += value;
            remove
            {
                _lastExitHandler = _exited;
                _exited -= value;
            }
        }

        public void Dispose() => IsDisposed = true;

        internal void Signal()
        {
            IsSignaled = true;
            _lastExitHandler = _exited;
            _exited?.Invoke(this);
        }

        internal void RaiseStaleExit() => _lastExitHandler?.Invoke(this);
    }
}
