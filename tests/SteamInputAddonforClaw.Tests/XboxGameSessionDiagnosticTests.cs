using System.Runtime.InteropServices;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Diagnostics.XboxCatalog;
using SteamInputAddonforClaw.Diagnostics.XboxSession;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class XboxGameSessionDiagnosticTests : IAsyncLifetime
{
    private const string ValidConfig = """
        <MicrosoftGame>
          <Identity Name="Sample.Game" Publisher="CN=Sample" ResourceId="pc" />
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
    public async Task Start_reconciles_an_already_running_foreground_game_once()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 100 };
        var probe = new FakeProcessProbe();
        probe.Set(100, 1, MatchedInspection(100));
        await using var diagnostic = CreateDiagnostic(windows, probe);

        var snapshot = await diagnostic.StartAsync();

        Assert.Equal(FrontendXboxSessionDiagnosticState.Running, snapshot.State);
        Assert.Equal(100u, snapshot.ActiveGame?.ProcessId);
        Assert.Equal(1, windows.StartCount);
        Assert.Equal(0, windows.EnumerationCount);
        Assert.Equal(0, windows.LastEnumerationLimit);
        Assert.Equal(1, probe.InspectionCount);
        Assert.Contains(snapshot.LifecycleEvents, item => item.Event == "ActiveGameDetected");
    }

    [Fact]
    public async Task Start_uses_a_bounded_window_reconcile_when_foreground_is_not_the_game()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 11, ProcessIds = [11, 22, 22] };
        var probe = new FakeProcessProbe();
        probe.Set(11, 1, Negative(XboxGameProcessInspectionDisposition.NoPackage));
        probe.Set(22, 1, MatchedInspection(22));
        await using var diagnostic = CreateDiagnostic(windows, probe);

        var snapshot = await diagnostic.StartAsync();

        Assert.Equal(22u, snapshot.ActiveGame?.ProcessId);
        Assert.Equal(1, windows.EnumerationCount);
        Assert.Equal(512, windows.LastEnumerationLimit);
        Assert.Equal(2, probe.InspectionCount);
        Assert.Contains(snapshot.LifecycleEvents, item => item.Event == "ActiveGameDetected");
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Show")]
    [InlineData("Foreground")]
    public async Task Window_observations_activate_a_positive_candidate(string eventKind)
    {
        var kind = Enum.Parse<XboxGameWindowEventKind>(eventKind);
        var windows = new FakeWindowSource();
        var probe = new FakeProcessProbe();
        probe.Set(30, 1, MatchedInspection(30));
        await using var diagnostic = CreateDiagnostic(windows, probe);
        await diagnostic.StartAsync();

        windows.Raise(kind, 30);
        var snapshot = await CaptureUntilAsync(diagnostic, state => state.ActiveGame is not null);

        Assert.Equal(30u, snapshot.ActiveGame?.ProcessId);
        Assert.Equal(1, snapshot.Counters.UniqueProcessGenerationsInspected);
        Assert.Equal(1, snapshot.Counters.PositiveMatches);
        Assert.Equal(1, snapshot.Counters.AcceptedWinEvents);
    }

    [Fact]
    public async Task Duplicate_events_do_not_reinspect_a_classified_live_generation()
    {
        var windows = new FakeWindowSource();
        var probe = new FakeProcessProbe();
        probe.Set(30, 1, Negative(XboxGameProcessInspectionDisposition.NoPackage));
        await using var diagnostic = CreateDiagnostic(windows, probe);
        await diagnostic.StartAsync();

        windows.Raise(XboxGameWindowEventKind.Create, 30);
        _ = await CaptureUntilAsync(diagnostic, snapshot => snapshot.Counters.AcceptedWinEvents == 1);
        windows.Raise(XboxGameWindowEventKind.Show, 30);
        var snapshot = await CaptureUntilAsync(diagnostic, state => state.Counters.AcceptedWinEvents == 2);

        Assert.Equal(1, probe.InspectionCount);
        Assert.Equal(1, snapshot.Counters.UniqueProcessGenerationsInspected);
        Assert.Equal(1, snapshot.Counters.NoPackageCandidates);
    }

    [Fact]
    public async Task Alt_tab_keeps_the_active_game_and_records_foreground_loss_and_return()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 40 };
        var probe = new FakeProcessProbe();
        probe.Set(40, 1, MatchedInspection(40));
        probe.Set(41, 1, Negative(XboxGameProcessInspectionDisposition.NoPackage));
        await using var diagnostic = CreateDiagnostic(windows, probe);
        await diagnostic.StartAsync();

        windows.Raise(XboxGameWindowEventKind.Foreground, 41);
        var away = await CaptureUntilAsync(diagnostic, state => state.LifecycleEvents.Any(item => item.Event == "ForegroundLeft"));
        Assert.Equal(40u, away.ActiveGame?.ProcessId);

        windows.Raise(XboxGameWindowEventKind.Foreground, 40);
        var returned = await CaptureUntilAsync(diagnostic, state => state.LifecycleEvents.Any(item => item.Event == "ForegroundReturned"));

        Assert.Equal(40u, returned.ActiveGame?.ProcessId);
        Assert.Contains(returned.LifecycleEvents, item => item.Event == "ForegroundLeft");
        Assert.Contains(returned.LifecycleEvents, item => item.Event == "ForegroundReturned");
    }

    [Fact]
    public async Task Matched_process_exit_clears_active_game()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 50 };
        var probe = new FakeProcessProbe();
        probe.Set(50, 1, MatchedInspection(50));
        await using var diagnostic = CreateDiagnostic(windows, probe);
        await diagnostic.StartAsync();
        var generation = Assert.Single(probe.Generations, item => item.Key == new XboxGameProcessGenerationKey(50, 1));

        generation.Signal();
        var snapshot = await CaptureUntilAsync(diagnostic, state => state.ActiveGame is null && state.Counters.ProcessExits == 1);

        Assert.Null(snapshot.ActiveGame);
        Assert.Equal(1, snapshot.Counters.ProcessExits);
        Assert.Contains(snapshot.LifecycleEvents, item => item.Event == "ProcessExited");
    }

    [Fact]
    public async Task Stale_exit_from_old_process_generation_cannot_clear_pid_reuse()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 60 };
        var probe = new FakeProcessProbe();
        probe.Set(60, 1, MatchedInspection(60, candidateKey: "old"));
        await using var diagnostic = CreateDiagnostic(windows, probe);
        await diagnostic.StartAsync();
        var oldGeneration = Assert.Single(probe.Generations, item => item.Key == new XboxGameProcessGenerationKey(60, 1));

        probe.Set(60, 2, MatchedInspection(60, candidateKey: "new"));
        windows.Raise(XboxGameWindowEventKind.Foreground, 60);
        var newer = await CaptureUntilAsync(diagnostic, state => state.ActiveGame?.CandidateKey == "new");
        oldGeneration.RaiseStaleExit();
        var afterStale = await diagnostic.CaptureAsync();

        Assert.Equal("new", newer.ActiveGame?.CandidateKey);
        Assert.Equal("new", afterStale.ActiveGame?.CandidateKey);
        Assert.Equal(new XboxGameProcessGenerationKey(60, 2), probe.ActiveKey(60));
    }

    [Fact]
    public async Task Second_positive_live_game_is_logged_without_arbitration()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 61 };
        var probe = new FakeProcessProbe();
        probe.Set(61, 1, MatchedInspection(61, "first"));
        probe.Set(62, 1, MatchedInspection(62, "second"));
        await using var diagnostic = CreateDiagnostic(windows, probe);
        await diagnostic.StartAsync();

        windows.Raise(XboxGameWindowEventKind.Create, 62);
        var snapshot = await CaptureUntilAsync(diagnostic,
            state => state.LifecycleEvents.Any(item => item.Event == "SecondPositiveLiveGameConflict"));

        Assert.Equal("first", snapshot.ActiveGame?.CandidateKey);
        Assert.Equal("second", snapshot.LastPositiveGame?.CandidateKey);
    }

    [Fact]
    public async Task Process_open_failure_is_counted_without_failing_the_observer()
    {
        var windows = new FakeWindowSource();
        var probe = new FakeProcessProbe();
        await using var diagnostic = CreateDiagnostic(windows, probe);
        await diagnostic.StartAsync();

        windows.Raise(XboxGameWindowEventKind.Show, 99);
        var snapshot = await CaptureUntilAsync(diagnostic, state => state.Counters.ProcessOpenFailures == 1);

        Assert.Equal(FrontendXboxSessionDiagnosticState.Running, snapshot.State);
        Assert.Null(snapshot.ActiveGame);
        Assert.Equal(0, probe.InspectionCount);
    }

    [Fact]
    public async Task Resume_rearms_hooks_and_reconciles_the_foreground_game()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 100 };
        var probe = new FakeProcessProbe();
        probe.Set(100, 1, MatchedInspection(100));
        await using var diagnostic = CreateDiagnostic(windows, probe);
        await diagnostic.StartAsync();

        await diagnostic.ReconcileAfterResumeAsync();
        var snapshot = await diagnostic.CaptureAsync();

        Assert.Equal(FrontendXboxSessionDiagnosticState.Running, snapshot.State);
        Assert.Equal(100u, snapshot.ActiveGame?.ProcessId);
        Assert.Equal(2, windows.StartCount);
        Assert.Equal(1, windows.StopCount);
        Assert.Contains(snapshot.LifecycleEvents, item => item.Event == "ResumeObserved");
    }

    [Fact]
    public async Task Event_source_failure_preserves_active_identity_until_the_matched_process_exits()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 101 };
        var probe = new FakeProcessProbe();
        probe.Set(101, 1, MatchedInspection(101));
        await using var diagnostic = CreateDiagnostic(windows, probe);
        await diagnostic.StartAsync();
        var generation = Assert.Single(probe.Generations, item => item.Key == new XboxGameProcessGenerationKey(101, 1));

        windows.Fail(new InvalidOperationException("WinEvent message loop failed."));
        var failed = await CaptureUntilAsync(diagnostic, state => state.State == FrontendXboxSessionDiagnosticState.Failed);

        Assert.Equal(101u, failed.ActiveGame?.ProcessId);
        Assert.False(failed.ForegroundHookInstalled);
        generation.Signal();
        var exited = await CaptureUntilAsync(diagnostic, state => state.ActiveGame is null && state.Counters.ProcessExits == 1);

        Assert.Null(exited.ActiveGame);
        Assert.Equal(FrontendXboxSessionDiagnosticState.Failed, exited.State);
    }

    [Fact]
    public async Task Start_is_idempotent_and_stop_unhooks_and_clears_active_state()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 70 };
        var probe = new FakeProcessProbe();
        probe.Set(70, 1, MatchedInspection(70));
        await using var diagnostic = CreateDiagnostic(windows, probe);
        var started = await diagnostic.StartAsync();
        var repeated = await diagnostic.StartAsync();

        Assert.Equal(1, windows.StartCount);
        Assert.Equal(started.ActiveGame, repeated.ActiveGame);
        Assert.True(repeated.ForegroundHookInstalled);

        var stopped = await diagnostic.StopAsync();

        Assert.Equal(FrontendXboxSessionDiagnosticState.Stopped, stopped.State);
        Assert.Null(stopped.ActiveGame);
        Assert.False(stopped.ForegroundHookInstalled);
        Assert.False(stopped.CreateHookInstalled);
        Assert.False(stopped.ShowHookInstalled);
        Assert.Equal(1, windows.StopCount);
    }

    [Fact]
    public async Task Report_is_written_with_separate_running_and_config_paths()
    {
        var windows = new FakeWindowSource { ForegroundProcessId = 80 };
        var probe = new FakeProcessProbe();
        probe.Set(80, 1, MatchedInspection(80));
        await using var diagnostic = CreateDiagnostic(windows, probe);
        await diagnostic.StartAsync();

        var result = await diagnostic.GenerateReportAsync();

        Assert.Equal(FrontendXboxSessionDiagnosticReportOutcome.Created, result.Outcome);
        Assert.True(File.Exists(result.ReportPath));
        var report = await File.ReadAllTextAsync(result.ReportPath!);
        Assert.Contains("Running process path: D:\\Games\\Sample.exe", report, StringComparison.Ordinal);
        Assert.Contains("Config path: C:\\PackageMetadata\\MicrosoftGame.config", report, StringComparison.Ordinal);
        Assert.Contains("Install: result=0", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0x8000, 0, 0, 123, true, true)]
    [InlineData(0x8002, 0, 0, 123, true, true)]
    [InlineData(0x0003, -1, -1, 123, true, true)]
    [InlineData(0x8000, 1, 0, 123, true, false)]
    [InlineData(0x8002, 0, 1, 123, true, false)]
    [InlineData(0x8000, 0, 0, 0, true, false)]
    [InlineData(0x8000, 0, 0, 123, false, false)]
    [InlineData(0x8001, 0, 0, 123, true, false)]
    public void WinEvent_filter_requires_allowed_event_top_level_window_and_self_object(
        int eventType, int objectId, int childId, int window, bool isTopLevel, bool expected)
    {
        Assert.Equal(expected, WindowsXboxGameWindowEventSource.IsRelevantObservation(
            checked((uint)eventType), objectId, childId, window, isTopLevel));
    }

    [Fact]
    public async Task Process_identity_evaluator_fails_closed_for_image_package_config_and_executable_evidence()
    {
        var root = Path.Combine(_directory, "package");
        Directory.CreateDirectory(root);
        var generation = new FakeProcessGeneration(90, 1);

        var imageFailure = await EvaluateAsync(generation, Evidence(imageResultCode: 5));
        var noPackage = await EvaluateAsync(generation, Evidence(packageFullNameResultCode: 15700));
        var packageFailure = await EvaluateAsync(generation, Evidence(packageFullNameResultCode: 5));
        var missingConfig = await EvaluateAsync(generation, Evidence(configLocations: [new("Effective", root)]));
        await File.WriteAllTextAsync(Path.Combine(root, "MicrosoftGame.config"), "<Game>");
        var malformedConfig = await EvaluateAsync(generation, Evidence(configLocations: [new("Effective", root)]));
        await File.WriteAllTextAsync(Path.Combine(root, "MicrosoftGame.config"), ValidConfig);
        var mismatch = await EvaluateAsync(generation, Evidence(runningPath: @"D:\Games\Launcher.exe", configLocations: [new("Effective", root)]));

        Assert.Equal(XboxGameProcessInspectionDisposition.ProcessImageFailure, imageFailure.Disposition);
        Assert.Equal(XboxGameProcessInspectionDisposition.NoPackage, noPackage.Disposition);
        Assert.Equal(XboxGameProcessInspectionDisposition.PackageIdentityFailure, packageFailure.Disposition);
        Assert.Equal(XboxGameProcessInspectionDisposition.ConfigNegative, missingConfig.Disposition);
        Assert.Equal(XboxGameProcessInspectionDisposition.ConfigNegative, malformedConfig.Disposition);
        Assert.Equal(XboxGameProcessInspectionDisposition.ExecutableMismatch, mismatch.Disposition);
        Assert.Null(imageFailure.Game);
        Assert.Null(mismatch.Game);
    }

    [Fact]
    public async Task Process_identity_accepts_case_insensitive_exact_executable_and_independent_paths()
    {
        var root = Path.Combine(_directory, "metadata");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "MicrosoftGame.config"), ValidConfig);
        var game = await EvaluateAsync(new FakeProcessGeneration(91, 1), Evidence(
            runningPath: @"D:\InstalledGames\sample.EXE",
            applicationUserModelId: null,
            applicationUserModelIdResultCode: 87,
            packageIdPresent: false,
            configLocations: [new("Effective", root)],
            paths: [new("EffectiveExternal", 0, @"F:\NativePackagePathEvidence")]));

        Assert.Equal(XboxGameProcessInspectionDisposition.Matched, game.Disposition);
        Assert.Equal(@"D:\InstalledGames\sample.EXE", game.Game?.RunningProcessPath);
        Assert.Equal(Path.Combine(root, "MicrosoftGame.config"), game.Game?.ConfigPath);
        Assert.Equal("pfn:Sample_family", game.Game?.CandidateKey);
        Assert.Equal("Sample.exe", game.Game?.MatchedExecutableName);
        Assert.Null(game.Game?.ApplicationUserModelId);
        Assert.Null(game.Game?.PackageIdentityName);
        Assert.Equal([new FrontendXboxSessionDiagnosticPackagePath("EffectiveExternal", 0, @"F:\NativePackagePathEvidence")], game.Game?.PackagePaths);
    }

    [Fact]
    public async Task Package_config_location_resolver_uses_the_exact_current_user_package()
    {
        var effectiveRoot = Path.Combine(_directory, "effective");
        Directory.CreateDirectory(effectiveRoot);
        await File.WriteAllTextAsync(Path.Combine(effectiveRoot, "MicrosoftGame.config"), ValidConfig);
        const string packageFullName = "Sample_1.0.0.0_x64__test";
        var lookupCount = 0;

        var resolution = XboxGamePackageConfigLocationResolver.ResolveCurrentUserPackage(
            packageFullName,
            (userSecurityId, resolvedPackageFullName) =>
            {
                lookupCount++;
                Assert.Equal(string.Empty, userSecurityId);
                Assert.Equal(packageFullName, resolvedPackageFullName);
                return new(() => effectiveRoot, () => null);
            });
        var game = await EvaluateAsync(new FakeProcessGeneration(93, 1), Evidence(
            packageFullName: packageFullName,
            configLocations: resolution.Locations,
            configLocationFailure: resolution.FailureReason));

        Assert.Equal(1, lookupCount);
        Assert.Equal(XboxGameProcessInspectionDisposition.Matched, game.Disposition);
        Assert.Equal(Path.Combine(effectiveRoot, "MicrosoftGame.config"), game.Game?.ConfigPath);
    }

    [Fact]
    public async Task Package_config_location_resolver_uses_installed_when_effective_has_no_config()
    {
        var effectiveRoot = Path.Combine(_directory, "effective-missing");
        var installedRoot = Path.Combine(_directory, "installed");
        Directory.CreateDirectory(effectiveRoot);
        Directory.CreateDirectory(installedRoot);
        await File.WriteAllTextAsync(Path.Combine(installedRoot, "MicrosoftGame.config"), ValidConfig);

        var resolution = XboxGamePackageConfigLocationResolver.ResolveCurrentUserPackage(
            "Sample_1.0.0.0_x64__test",
            (_, _) => new(() => effectiveRoot, () => installedRoot));
        var game = await EvaluateAsync(new FakeProcessGeneration(94, 1), Evidence(
            configLocations: resolution.Locations,
            configLocationFailure: resolution.FailureReason));

        Assert.Equal(["Effective", "Installed"], resolution.Locations.Select(location => location.Kind));
        Assert.Equal(XboxGameProcessInspectionDisposition.Matched, game.Disposition);
        Assert.Equal(Path.Combine(installedRoot, "MicrosoftGame.config"), game.Game?.ConfigPath);
    }

    [Fact]
    public void Package_config_location_resolver_deduplicates_effective_and_installed_paths_case_insensitively()
    {
        var root = Path.Combine(_directory, "same-location");
        var resolution = XboxGamePackageConfigLocationResolver.ResolveCurrentUserPackage(
            "Sample_1.0.0.0_x64__test",
            (_, _) => new(() => root, () => root.ToUpperInvariant()));

        var location = Assert.Single(resolution.Locations);
        Assert.Equal("Effective", location.Kind);
        Assert.Equal(root, location.RootPath);
    }

    [Fact]
    public async Task Package_config_location_resolver_continues_when_effective_location_throws()
    {
        var installedRoot = Path.Combine(_directory, "installed-after-effective-error");
        Directory.CreateDirectory(installedRoot);
        await File.WriteAllTextAsync(Path.Combine(installedRoot, "MicrosoftGame.config"), ValidConfig);
        var resolution = XboxGamePackageConfigLocationResolver.ResolveCurrentUserPackage(
            "Sample_1.0.0.0_x64__test",
            (_, _) => new(
                () => throw new UnauthorizedAccessException("effective unavailable"),
                () => installedRoot));

        var game = await EvaluateAsync(new FakeProcessGeneration(95, 1), Evidence(
            configLocations: resolution.Locations,
            configLocationFailure: resolution.FailureReason));

        Assert.Contains("Effective location:", resolution.FailureReason, StringComparison.Ordinal);
        Assert.Equal(XboxGameProcessInspectionDisposition.Matched, game.Disposition);
        Assert.Equal(Path.Combine(installedRoot, "MicrosoftGame.config"), game.Game?.ConfigPath);
    }

    [Fact]
    public async Task Package_config_location_lookup_null_is_a_config_negative()
    {
        var resolution = XboxGamePackageConfigLocationResolver.ResolveCurrentUserPackage("missing-package", (_, _) => null);
        var result = await EvaluateAsync(new FakeProcessGeneration(96, 1), Evidence(
            configLocations: resolution.Locations,
            configLocationFailure: resolution.FailureReason));

        Assert.Empty(resolution.Locations);
        Assert.Equal(XboxGameProcessInspectionDisposition.ConfigNegative, result.Disposition);
        Assert.Contains("PackageManager did not resolve", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Package_without_effective_or_installed_location_is_a_config_negative()
    {
        var resolution = XboxGamePackageConfigLocationResolver.ResolveCurrentUserPackage(
            "locationless-package",
            (_, _) => new(() => null, () => null));
        var result = await EvaluateAsync(new FakeProcessGeneration(97, 1), Evidence(
            configLocations: resolution.Locations,
            configLocationFailure: resolution.FailureReason));

        Assert.Empty(resolution.Locations);
        Assert.Equal(XboxGameProcessInspectionDisposition.ConfigNegative, result.Disposition);
        Assert.Contains("no usable Effective/Installed location", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Config_absent_from_both_package_locations_is_a_config_negative()
    {
        var effectiveRoot = Path.Combine(_directory, "effective-empty");
        var installedRoot = Path.Combine(_directory, "installed-empty");
        Directory.CreateDirectory(effectiveRoot);
        Directory.CreateDirectory(installedRoot);
        var resolution = XboxGamePackageConfigLocationResolver.ResolveCurrentUserPackage(
            "Sample_1.0.0.0_x64__test",
            (_, _) => new(() => effectiveRoot, () => installedRoot));
        var result = await EvaluateAsync(new FakeProcessGeneration(98, 1), Evidence(
            configLocations: resolution.Locations,
            configLocationFailure: resolution.FailureReason));

        Assert.Equal(XboxGameProcessInspectionDisposition.ConfigNegative, result.Disposition);
        Assert.Contains("Package object's Effective/Installed locations", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Package_path_type_evidence_neither_supplies_nor_blocks_config_location()
    {
        var configRoot = Path.Combine(_directory, "metadata-on-c");
        Directory.CreateDirectory(configRoot);
        await File.WriteAllTextAsync(Path.Combine(configRoot, "MicrosoftGame.config"), ValidConfig);
        FrontendXboxSessionDiagnosticPackagePath[] packagePaths =
        [
            new("Install", 2, @"D:\NativeInstall"),
            new("Effective", 2, @"D:\NativeEffective"),
            new("Mutable", 2, @"D:\NativeMutable"),
            new("MachineExternal", 2, @"D:\NativeMachineExternal"),
            new("UserExternal", 2, @"D:\NativeUserExternal"),
            new("EffectiveExternal", 2, @"D:\NativeEffectiveExternal"),
        ];
        var matched = await EvaluateAsync(new FakeProcessGeneration(99, 1), Evidence(
            runningPath: @"D:\Games\Sample.exe",
            configLocations: [new("Effective", configRoot)],
            paths: packagePaths));
        var notSynthesized = await EvaluateAsync(new FakeProcessGeneration(100, 1), Evidence(
            configLocations: [],
            paths: [new("Install", 0, configRoot)]));

        Assert.Equal(XboxGameProcessInspectionDisposition.Matched, matched.Disposition);
        Assert.Equal(@"D:\Games\Sample.exe", matched.Game?.RunningProcessPath);
        Assert.Equal(Path.Combine(configRoot, "MicrosoftGame.config"), matched.Game?.ConfigPath);
        Assert.Equal(packagePaths, matched.Game?.PackagePaths);
        Assert.Equal(XboxGameProcessInspectionDisposition.ConfigNegative, notSynthesized.Disposition);
    }

    [Fact]
    public void Package_id_native_layout_and_projection_match_appmodel_contract()
    {
        Assert.Equal(0, Marshal.OffsetOf<WindowsXboxGameProcessIdentityProbe.PackageVersion>(nameof(WindowsXboxGameProcessIdentityProbe.PackageVersion.Revision)).ToInt32());
        Assert.Equal(2, Marshal.OffsetOf<WindowsXboxGameProcessIdentityProbe.PackageVersion>(nameof(WindowsXboxGameProcessIdentityProbe.PackageVersion.Build)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<WindowsXboxGameProcessIdentityProbe.PackageVersion>(nameof(WindowsXboxGameProcessIdentityProbe.PackageVersion.Minor)).ToInt32());
        Assert.Equal(6, Marshal.OffsetOf<WindowsXboxGameProcessIdentityProbe.PackageVersion>(nameof(WindowsXboxGameProcessIdentityProbe.PackageVersion.Major)).ToInt32());

        const int pointerOffset = 16;
        Assert.Equal(0, Marshal.OffsetOf<WindowsXboxGameProcessIdentityProbe.PackageIdNative>(nameof(WindowsXboxGameProcessIdentityProbe.PackageIdNative.Reserved)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<WindowsXboxGameProcessIdentityProbe.PackageIdNative>(nameof(WindowsXboxGameProcessIdentityProbe.PackageIdNative.ProcessorArchitecture)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<WindowsXboxGameProcessIdentityProbe.PackageIdNative>(nameof(WindowsXboxGameProcessIdentityProbe.PackageIdNative.Version)).ToInt32());
        Assert.Equal(pointerOffset, Marshal.OffsetOf<WindowsXboxGameProcessIdentityProbe.PackageIdNative>(nameof(WindowsXboxGameProcessIdentityProbe.PackageIdNative.Name)).ToInt32());
        Assert.Equal(pointerOffset + IntPtr.Size, Marshal.OffsetOf<WindowsXboxGameProcessIdentityProbe.PackageIdNative>(nameof(WindowsXboxGameProcessIdentityProbe.PackageIdNative.Publisher)).ToInt32());
        Assert.Equal(pointerOffset + 2 * IntPtr.Size, Marshal.OffsetOf<WindowsXboxGameProcessIdentityProbe.PackageIdNative>(nameof(WindowsXboxGameProcessIdentityProbe.PackageIdNative.ResourceId)).ToInt32());
        Assert.Equal(pointerOffset + 3 * IntPtr.Size, Marshal.OffsetOf<WindowsXboxGameProcessIdentityProbe.PackageIdNative>(nameof(WindowsXboxGameProcessIdentityProbe.PackageIdNative.PublisherId)).ToInt32());

        var name = Marshal.StringToHGlobalUni("Example.Package");
        var publisher = Marshal.StringToHGlobalUni("CN=Example");
        var resourceId = Marshal.StringToHGlobalUni("resource-id");
        var publisherId = Marshal.StringToHGlobalUni("publisher-id");
        try
        {
            var projected = WindowsXboxGameProcessIdentityProbe.ProjectPackageId(
                new WindowsXboxGameProcessIdentityProbe.PackageIdNative
                {
                    ProcessorArchitecture = 9,
                    Version = new() { Revision = 4, Build = 3, Minor = 2, Major = 1 },
                    Name = name,
                    Publisher = publisher,
                    ResourceId = resourceId,
                    PublisherId = publisherId,
                },
                0);

            Assert.Equal("Example.Package", projected.Name);
            Assert.Equal("CN=Example", projected.Publisher);
            Assert.Equal("publisher-id", projected.PublisherId);
            Assert.Equal("resource-id", projected.ResourceId);
            Assert.Equal("X64", projected.Architecture);
            Assert.Equal("1.2.3.4", projected.Version);
            Assert.Equal(0, projected.ResultCode);
        }
        finally
        {
            Marshal.FreeHGlobal(name);
            Marshal.FreeHGlobal(publisher);
            Marshal.FreeHGlobal(resourceId);
            Marshal.FreeHGlobal(publisherId);
        }
    }

    [Fact]
    public async Task Candidate_key_uses_normalized_identity_when_store_and_family_are_absent()
    {
        var root = Path.Combine(_directory, "identity");
        Directory.CreateDirectory(root);
        const string config = "<Game><Identity Name=\"Sample.Game\" Publisher=\"CN=Sample\" ResourceId=\"PC\"/><ExecutableList><Executable Name=\"Sample.exe\"/></ExecutableList></Game>";
        await File.WriteAllTextAsync(Path.Combine(root, "MicrosoftGame.config"), config);

        var result = await EvaluateAsync(new FakeProcessGeneration(92, 1), Evidence(
            packageFamilyName: null,
            packageFamilyNameResultCode: 15700,
            paths: [new("Install", 0, root)]));

        Assert.Equal(XboxGameProcessInspectionDisposition.PackageIdentityFailure, result.Disposition);
        var parsedConfig = MicrosoftGameConfigReader.Parse(config).Config!;
        Assert.Equal("identity:SAMPLE.GAME|CN%3DSAMPLE|PC", XboxCatalogDiagnostic.CreateCandidateKey(null, null, parsedConfig));
    }

    [Fact]
    public void Runtime_diagnostic_uses_win_events_and_process_exit_without_recurring_polling()
    {
        var sourceRoot = Path.Combine(RepositoryRoot(), "src", "SteamInputAddonforClaw", "Diagnostics", "XboxSession");
        var files = Directory.GetFiles(sourceRoot, "*.cs");
        var sources = string.Join("\n", files.Select(File.ReadAllText));
        var hookSource = File.ReadAllText(Path.Combine(sourceRoot, "XboxGameWindowEventSource.cs"));
        var callbackStart = hookSource.IndexOf("private void OnWinEvent", StringComparison.Ordinal);
        var callbackEnd = hookSource.IndexOf("private delegate void WinEventProc", callbackStart, StringComparison.Ordinal);
        var callback = hookSource[callbackStart..callbackEnd];
        var probeSource = File.ReadAllText(Path.Combine(sourceRoot, "XboxGameProcessIdentityProbe.cs"));

        Assert.Contains("EventSystemForeground", sources, StringComparison.Ordinal);
        Assert.Contains("EventObjectCreate", sources, StringComparison.Ordinal);
        Assert.Contains("EventObjectShow", sources, StringComparison.Ordinal);
        Assert.Contains("RegisterWaitForSingleObject", sources, StringComparison.Ordinal);
        Assert.Contains("_observation?.Invoke(new XboxGameWindowObservation", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenProcess", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("InspectAsync", callback, StringComparison.Ordinal);
        Assert.Contains("ProcessQueryLimitedInformation | Synchronize", probeSource, StringComparison.Ordinal);
        Assert.Contains("QueryFullProcessImageNameW", probeSource, StringComparison.Ordinal);
        Assert.Contains("GetPackageFullName", probeSource, StringComparison.Ordinal);
        Assert.Contains("GetPackageFamilyName", probeSource, StringComparison.Ordinal);
        Assert.Contains("GetApplicationUserModelId", probeSource, StringComparison.Ordinal);
        Assert.Contains("GetPackageId", probeSource, StringComparison.Ordinal);
        Assert.Contains("GetPackagePathByFullName2", probeSource, StringComparison.Ordinal);
        Assert.Contains("[DllImport(\"kernelbase.dll\", EntryPoint = \"GetPackagePathByFullName2\"", probeSource, StringComparison.Ordinal);
        Assert.Contains("new PackageManager().FindPackageForUser(userSecurityId, packageFullName)", probeSource, StringComparison.Ordinal);
        Assert.Contains("package.EffectiveLocation?.Path", probeSource, StringComparison.Ordinal);
        Assert.Contains("package.InstalledLocation?.Path", probeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var packagePath in evidence.PackagePaths)", File.ReadAllText(Path.Combine(sourceRoot, "XboxGameProcessIdentityEvaluator.cs")), StringComparison.Ordinal);
        foreach (var pathType in new[] { "Install", "Effective", "Mutable", "MachineExternal", "UserExternal", "EffectiveExternal" })
            Assert.Contains($"(\"{pathType}\"", probeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchOption.AllDirectories", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("PeriodicTimer", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Threading.Timer", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", sources, StringComparison.Ordinal);
    }

    public async Task DisposeAsync()
    {
        AppLog.DrainForTests();
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory))
            await Task.Run(() => Directory.Delete(_directory, recursive: true));
    }

    private XboxGameSessionDiagnostic CreateDiagnostic(FakeWindowSource windows, FakeProcessProbe probe) =>
        new(windows, probe, Path.Combine(_directory, "logs"), () => new DateTimeOffset(2026, 10, 5, 12, 30, 0, TimeSpan.Zero));

    private static async Task<FrontendXboxSessionDiagnosticSnapshot> CaptureUntilAsync(
        XboxGameSessionDiagnostic diagnostic,
        Func<FrontendXboxSessionDiagnosticSnapshot, bool> predicate)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var snapshot = await diagnostic.CaptureAsync();
            if (predicate(snapshot))
                return snapshot;
            await Task.Delay(10);
        }
        throw new Xunit.Sdk.XunitException("The XBOX session diagnostic did not reach the expected state.");
    }

    private async Task<XboxGameProcessInspection> EvaluateAsync(
        FakeProcessGeneration generation,
        XboxGameProcessIdentityEvidence evidence) =>
        await XboxGameProcessIdentityEvaluator.InspectAsync(generation, evidence, CancellationToken.None);

    private static XboxGameProcessInspection MatchedInspection(uint processId, string candidateKey = "store:9NABC123") =>
        new(XboxGameProcessInspectionDisposition.Matched, null, new FrontendXboxSessionDiagnosticGame(
            candidateKey, processId, @"D:\Games\Sample.exe", "Sample.exe", "Sample_1.0.0.0_x64__test", "Sample_family",
            "Sample!App", 0, "Sample.Game", "CN=Sample", "publisher", null, "X64", "1.0.0.0",
            "Sample.Game", "CN=Sample", null, "9NABC123", "1234", "Sample.exe",
            @"C:\PackageMetadata\MicrosoftGame.config", [new("Install", 0, @"C:\PackageMetadata") ]));

    private static XboxGameProcessInspection Negative(XboxGameProcessInspectionDisposition disposition) =>
        new(disposition, "Expected negative evidence.", null);

    private static XboxGameProcessIdentityEvidence Evidence(
        int imageResultCode = 0,
        string? runningPath = @"D:\Games\Sample.exe",
        int packageFullNameResultCode = 0,
        string? packageFullName = "Sample_1.0.0.0_x64__test",
        int packageFamilyNameResultCode = 0,
        string? packageFamilyName = "Sample_family",
        string? applicationUserModelId = "Sample!App",
        int applicationUserModelIdResultCode = 0,
        bool packageIdPresent = true,
        IReadOnlyList<XboxGamePackageConfigLocation>? configLocations = null,
        string? configLocationFailure = null,
        IReadOnlyList<FrontendXboxSessionDiagnosticPackagePath>? paths = null) =>
        new(imageResultCode, runningPath,
            packageFullNameResultCode, packageFullName,
            packageFamilyNameResultCode, packageFamilyName,
            applicationUserModelId, applicationUserModelIdResultCode,
            packageIdPresent ? "Sample.Game" : null,
            packageIdPresent ? "CN=Sample" : null,
            packageIdPresent ? "publisher" : null,
            null,
            packageIdPresent ? "X64" : null,
            packageIdPresent ? "1.0.0.0" : null,
            configLocations ?? [],
            configLocationFailure,
            paths ?? [new("Install", 0, Path.Combine(Path.GetTempPath(), "missing-xbox-package"))]);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SteamInputAddonforClaw.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class FakeWindowSource : IXboxGameWindowEventSource
    {
        private Action<XboxGameWindowObservation>? _observation;
        private Action<Exception>? _failure;
        public uint ForegroundProcessId { get; set; }
        public IReadOnlyList<uint> ProcessIds { get; set; } = [];
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int EnumerationCount { get; private set; }
        public int LastEnumerationLimit { get; private set; }

        public Task StartAsync(Action<XboxGameWindowObservation> observation, Action<Exception> failure, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _observation = observation;
            _failure = failure;
            StartCount++;
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
            return ProcessIds.Take(maximumProcessCount).ToArray();
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal void Raise(XboxGameWindowEventKind kind, uint processId) =>
            _observation?.Invoke(new(kind, (nint)processId, processId));

        internal void Fail(Exception exception) => _failure?.Invoke(exception);
    }

    private sealed class FakeProcessProbe : IXboxGameProcessIdentityProbe
    {
        private readonly Dictionary<uint, (long CreationTime, XboxGameProcessInspection Inspection)> _current = [];
        private readonly Dictionary<(uint ProcessId, long CreationTime), FakeProcessGeneration> _generations = [];
        internal List<FakeProcessGeneration> Generations { get; } = [];
        internal int InspectionCount { get; private set; }

        internal void Set(uint processId, long creationTime, XboxGameProcessInspection inspection) =>
            _current[processId] = (creationTime, inspection);

        internal XboxGameProcessGenerationKey ActiveKey(uint processId) =>
            new(processId, _current[processId].CreationTime);

        public XboxGameProcessOpenResult Open(uint processId)
        {
            if (!_current.TryGetValue(processId, out var configured))
                return new(null, 5);
            var generation = new FakeProcessGeneration(processId, configured.CreationTime);
            Generations.Add(generation);
            _generations.TryAdd((processId, configured.CreationTime), generation);
            return new(generation, 0);
        }

        public Task<XboxGameProcessInspection> InspectAsync(IXboxGameProcessGeneration generation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectionCount++;
            var key = (generation.ProcessId, generation.Key.CreationTime);
            return Task.FromResult(_current[key.ProcessId].Inspection);
        }
    }

    private sealed class FakeProcessGeneration(uint processId, long creationTime) : IXboxGameProcessGeneration
    {
        private Action<IXboxGameProcessGeneration>? _exited;
        private Action<IXboxGameProcessGeneration>? _lastExitHandler;

        public uint ProcessId { get; } = processId;
        public XboxGameProcessGenerationKey Key { get; } = new(processId, creationTime);
        public bool IsSignaled { get; private set; }
        public bool IsDisposed { get; private set; }
        public event Action<IXboxGameProcessGeneration>? Exited
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
