using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.BackButtons;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Display;
using SteamInputAddonforClaw.Profiles.Performance;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class XboxGameProfileFrontendTests : IDisposable
{
    private const string Key = "store:9PK8PHLCQDF6";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"XboxProfileFrontend-{Guid.NewGuid():N}");
    private string ProfilePath => Path.Combine(_directory, "profiles.json");

    [Fact]
    public async Task XBOX_capture_projects_existing_TDP_limits_and_Intel_capability()
    {
        var store = new ProfileStore(ProfilePath);
        var gate = new ProfileMutationGate();
        var mutations = new XboxGameProfileMutations(store, gate, Model());
        var tdp = new TdpRuntime(store, gate, Model(), new MsiClawTdpHardware(new RecordingTdpTransport()));
        var limiter = new RecordingFrameLimiter();
        var fps = new IntelFrameLimiterRuntime(store, gate, limiter, marker: Path.Combine(_directory, "fps-marker.json"));
        var control = CreateControl(mutations, tdp, fps);

        var snapshot = await control.CaptureXboxGameProfileAsync(Key);

        Assert.Equal(Key, snapshot.Key);
        Assert.True(snapshot.PersistenceWritable);
        Assert.Equal(new FrontendTdpLimits(8, 30, 8, 37), snapshot.Limits);
        Assert.True(snapshot.FpsLimit!.Available);
        Assert.False(snapshot.Exists);
        await tdp.DisposeAsync();
        fps.Dispose();
    }

    [Fact]
    public async Task Active_XBOX_overlay_uses_live_title_for_first_profile_and_persists_it_when_enabled()
    {
        Directory.CreateDirectory(_directory);
        var store = new ProfileStore(ProfilePath);
        var mutations = new XboxGameProfileMutations(store, new ProfileMutationGate(), Model());
        const string liveTitle = "Xbox Live Title";
        var target = QuickSettingsProfileTarget.ForXbox(Key);
        var control = CreateControl(
            mutations,
            activeProfileTargetSource: () => ActiveProfileTarget.ForXbox(Key),
            activeXboxDisplayNameSource: requestedKey => string.Equals(requestedKey, Key, StringComparison.Ordinal)
                ? liveTitle
                : null);

        var page = await control.CaptureQuickSettingsPageAsync(QuickSettingsPageId.Profile, target);
        var initialSnapshot = await control.CaptureXboxGameProfileAsync(Key);
        Assert.True(page.Available);
        Assert.False(initialSnapshot.Exists);
        Assert.Equal(liveTitle, page.Sections.Single(section => section.SectionId == QuickSettingsSectionId.ProfileGeneral).Label);

        var intent = new QuickSettingsMutationIntent(QuickSettingsPageId.Profile, target, QuickSettingsRowId.ProfileEnabled,
            [new(QuickSettingsRowId.ProfileEnabled, QuickSettingsValue.Boolean(true))]);
        var result = await QuickSettingsMutationAdapter.MutateAsync(control, intent, CancellationToken.None);

        var persisted = store.Load().Document.XboxGames[Key];
        Assert.True(persisted.Enabled);
        Assert.Equal(liveTitle, persisted.DisplayName);
        Assert.Equal(liveTitle, result.Page.Sections.Single(section => section.SectionId == QuickSettingsSectionId.ProfileGeneral).Label);
    }

    [Fact]
    public async Task XBOX_profile_mutations_for_an_offline_target_persist_without_live_apply()
    {
        Directory.CreateDirectory(_directory);
        var store = new ProfileStore(ProfilePath);
        var gate = new ProfileMutationGate();
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings
            {
                Performance = new DevicePerformanceSettings
                {
                    Tdp = new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 24), Dc = Pair(18, 22) },
                    PowerMode = new DevicePowerModeSettings { Ac = WindowsPowerMode.Balanced, Dc = WindowsPowerMode.BestPowerEfficiency }
                }
            }
        });
        var mutations = new XboxGameProfileMutations(store, gate, Model());
        var transport = new RecordingTdpTransport();
        var tdp = new TdpRuntime(store, gate, Model(), new MsiClawTdpHardware(transport));
        var limiter = new RecordingFrameLimiter();
        var fps = new IntelFrameLimiterRuntime(store, gate, limiter, marker: Path.Combine(_directory, "fps-marker.json"));
        var control = CreateControl(mutations, tdp, fps, actualRunningAppIdSource: () => throw new InvalidOperationException("XBOX profile edits must not consult the Steam active AppID."));
        var invalidations = 0;
        control.StateInvalidated += (_, _) => invalidations++;

        Assert.True((await control.SetXboxGameProfileFavoriteAsync(Key, true, "Game")).Succeeded);
        Assert.True((await control.SetXboxGameProfileEnabledAsync(Key, true, "Game")).Succeeded);
        Assert.True((await control.SetXboxGameProfileCpuBoostAcAsync(Key, CpuBoostMode.Aggressive)).Succeeded);
        Assert.True((await control.SetXboxGameProfileCpuBoostDcAsync(Key, CpuBoostMode.Disabled)).Succeeded);
        Assert.True((await control.SetXboxGameProfileCpuBoostEnabledAsync(Key, true)).Succeeded);
        Assert.True((await control.SetXboxGameProfileTdpAsync(Key,
            new FrontendGameTdpConfiguration(true, new(25, 30), new(18, 24)))).Succeeded);
        Assert.True((await control.SetXboxGameProfileTdpEnabledAsync(Key, true)).Succeeded);
        Assert.True((await control.SetXboxGameProfilePowerModeAcAsync(Key, WindowsPowerMode.BestPerformance)).Succeeded);
        Assert.True((await control.SetXboxGameProfilePowerModeDcAsync(Key, WindowsPowerMode.Balanced)).Succeeded);
        Assert.True((await control.SetXboxGameProfilePowerModeEnabledAsync(Key, true)).Succeeded);
        Assert.True((await control.SetXboxGameProfileFpsLimitAcAsync(Key, 90)).Succeeded);
        Assert.True((await control.SetXboxGameProfileFpsLimitDcAsync(Key, 60)).Succeeded);
        Assert.True((await control.SetXboxGameProfileFpsLimitEnabledAsync(Key, true)).Succeeded);
        Assert.True((await control.SetXboxGameProfileResolutionAsync(Key, new(1920, 1200), "Game")).Succeeded);

        var capture = await control.CaptureXboxGameProfileAsync(Key);
        var saved = store.Load().Document.XboxGames[Key];
        Assert.True(capture.Exists);
        Assert.True(capture.Enabled);
        Assert.True(capture.CpuBoost.Enabled);
        Assert.Equal(CpuBoostMode.Aggressive, capture.CpuBoost.Ac);
        Assert.Equal(new FrontendTdpPowerPair(25, 30), capture.Tdp.Ac);
        Assert.Equal(WindowsPowerMode.BestPerformance, capture.PowerMode!.Ac);
        Assert.True(capture.FpsLimit!.Enabled);
        Assert.Equal(90, capture.FpsLimit.AcFps);
        Assert.Equal(new FrontendGameResolution(1920, 1200), capture.Resolution);
        Assert.True(saved.Favorite);
        Assert.Equal(14, invalidations);
        Assert.Equal(0, transport.OperationCount);
        Assert.Equal(0, limiter.ApplyCount);
        await tdp.DisposeAsync();
        fps.Dispose();
    }

    [Fact]
    public async Task Active_XBOX_profile_mutation_applies_through_the_five_shared_runtime_owners()
    {
        Directory.CreateDirectory(_directory);
        var store = new ProfileStore(ProfilePath);
        var gate = new ProfileMutationGate();
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings
            {
                Performance = new DevicePerformanceSettings
                {
                    CpuBoost = new DeviceCpuBoostSettings { Enabled = true, Ac = CpuBoostMode.Enabled, Dc = CpuBoostMode.Disabled },
                    Tdp = new DeviceTdpSettings { Enabled = true, Ac = Pair(20, 24), Dc = Pair(18, 22) },
                    PowerMode = new DevicePowerModeSettings { Enabled = true, Ac = WindowsPowerMode.Balanced, Dc = WindowsPowerMode.Balanced }
                }
            },
            XboxGames = new()
            {
                [Key] = new XboxGameProfile
                {
                    Enabled = false,
                    Performance = new GamePerformanceOverrides
                    {
                        CpuBoost = new GameCpuBoostSettings { Enabled = true, Ac = CpuBoostMode.Aggressive, Dc = CpuBoostMode.EfficientEnabled },
                        Tdp = new GameTdpSettings { Enabled = true, Ac = Pair(25, 30), Dc = Pair(18, 24) },
                        PowerMode = new GamePowerModeSettings { Enabled = true, Ac = WindowsPowerMode.BestPerformance, Dc = WindowsPowerMode.BestPowerEfficiency },
                        FpsLimit = new GameFpsLimitSettings { Enabled = true, AcFps = 90, DcFps = 60 }
                    },
                    Display = new GameDisplayOverrides { Resolution = new GameDisplayResolution { Width = 1600, Height = 900 } }
                }
            }
        });

        var target = ActiveProfileTarget.ForXbox(Key);
        Func<ProfileDocument, ResolvedActiveProfile?> resolver = document => ActiveProfileResolver.Resolve(target, document);
        var cpuPolicy = new FakeCpuBoostPowerPolicy
        {
            Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled),
            Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled)
        };
        var cpu = new CpuBoostRuntime(store, cpuPolicy, gate);
        cpu.SetActiveProfileResolver(resolver);
        var powerPolicy = new FakePowerModePolicy();
        var power = new PowerModeRuntime(store, powerPolicy, gate);
        power.SetActiveProfileResolver(resolver);
        var transport = new RecordingTdpTransport();
        await using var tdp = new TdpRuntime(store, gate, Model(), new MsiClawTdpHardware(transport),
            powerSource: () => TdpPowerSource.AC);
        tdp.SetActiveProfileResolver(resolver);
        var limiter = new RecordingFrameLimiter();
        var fps = new IntelFrameLimiterRuntime(store, gate, limiter,
            () => AcDcPowerSource.AC, marker: Path.Combine(_directory, "fps-marker.json"));
        fps.SetActiveProfileResolver(resolver);
        var display = new RecordingDisplayResolutionService();
        var resolution = new GameDisplayResolutionRuntime(store, gate, _directory, display);
        resolution.SetActiveProfileResolver(resolver);
        var mutations = new XboxGameProfileMutations(store, gate, Model());
        var mappingReconciles = 0;
        var control = CreateControl(mutations, tdp, fps,
            activeProfileTargetSource: () => target,
            reconcileXboxBackButtonMapping: key => { Assert.Equal(Key, key); mappingReconciles++; return true; },
            cpu: cpu,
            power: power,
            resolution: resolution);

        var result = await control.SetXboxGameProfileEnabledAsync(Key, true, "Game");

        Assert.Equal(FrontendGameProfileMutationOutcome.Succeeded, result.Outcome);
        Assert.Equal(CpuBoostMode.Aggressive, cpuPolicy.Ac.Mode);
        Assert.Equal(CpuBoostMode.EfficientEnabled, cpuPolicy.Dc.Mode);
        Assert.Equal((WindowsPowerMode.BestPerformance, WindowsPowerMode.BestPowerEfficiency), powerPolicy.LastApplied);
        Assert.Contains(transport.Operations, operation => operation.StartsWith("SetData(", StringComparison.Ordinal));
        Assert.Equal(90, limiter.LastEnabledFps);
        Assert.Equal(new DisplayModeSnapshot(1600, 900, 120, 32), display.Current);
        Assert.Equal(1, mappingReconciles);

        var tdpOperations = transport.OperationCount;
        var powerApplies = powerPolicy.ApplyCount;
        var fpsCalls = limiter.ApplyCount;
        var resolutionCalls = display.ApplyCalls;
        var cpuEdit = await control.SetXboxGameProfileCpuBoostAcAsync(Key, CpuBoostMode.Disabled);
        Assert.Equal(FrontendGameProfileMutationOutcome.Succeeded, cpuEdit.Outcome);
        Assert.Equal(CpuBoostMode.Disabled, cpuPolicy.Ac.Mode);
        Assert.Equal(tdpOperations, transport.OperationCount);
        Assert.Equal(powerApplies, powerPolicy.ApplyCount);
        Assert.Equal(fpsCalls, limiter.ApplyCount);
        Assert.Equal(resolutionCalls, display.ApplyCalls);
        Assert.Equal(1, mappingReconciles);

        var cpuWrites = cpuPolicy.AcWriteCount + cpuPolicy.DcWriteCount;
        powerApplies = powerPolicy.ApplyCount;
        fpsCalls = limiter.ApplyCount;
        resolutionCalls = display.ApplyCalls;
        var tdpEdit = await control.SetXboxGameProfileTdpAsync(Key,
            new FrontendGameTdpConfiguration(true, new(23, 30), new(17, 25)));
        Assert.Equal(FrontendGameProfileMutationOutcome.Succeeded, tdpEdit.Outcome);
        Assert.True(transport.OperationCount > tdpOperations);
        Assert.Equal(cpuWrites, cpuPolicy.AcWriteCount + cpuPolicy.DcWriteCount);
        Assert.Equal(powerApplies, powerPolicy.ApplyCount);
        Assert.Equal(fpsCalls, limiter.ApplyCount);
        Assert.Equal(resolutionCalls, display.ApplyCalls);
        Assert.Equal(1, mappingReconciles);

        tdpOperations = transport.OperationCount;
        cpuWrites = cpuPolicy.AcWriteCount + cpuPolicy.DcWriteCount;
        fpsCalls = limiter.ApplyCount;
        resolutionCalls = display.ApplyCalls;
        var powerEdit = await control.SetXboxGameProfilePowerModeAcAsync(Key, WindowsPowerMode.BestPowerEfficiency);
        Assert.Equal(FrontendGameProfileMutationOutcome.Succeeded, powerEdit.Outcome);
        Assert.Equal((WindowsPowerMode.BestPowerEfficiency, WindowsPowerMode.BestPowerEfficiency), powerPolicy.LastApplied);
        Assert.Equal(tdpOperations, transport.OperationCount);
        Assert.Equal(cpuWrites, cpuPolicy.AcWriteCount + cpuPolicy.DcWriteCount);
        Assert.Equal(1, mappingReconciles);
        Assert.Equal(fpsCalls, limiter.ApplyCount);
        Assert.Equal(resolutionCalls, display.ApplyCalls);
        Assert.Equal(1, mappingReconciles);

        powerApplies = powerPolicy.ApplyCount;
        tdpOperations = transport.OperationCount;
        cpuWrites = cpuPolicy.AcWriteCount + cpuPolicy.DcWriteCount;
        resolutionCalls = display.ApplyCalls;
        var fpsEdit = await control.SetXboxGameProfileFpsLimitAcAsync(Key, 75);
        Assert.Equal(FrontendGameProfileMutationOutcome.Succeeded, fpsEdit.Outcome);
        Assert.Equal(75, limiter.LastEnabledFps);
        Assert.Equal(powerApplies, powerPolicy.ApplyCount);
        Assert.Equal(tdpOperations, transport.OperationCount);
        Assert.Equal(cpuWrites, cpuPolicy.AcWriteCount + cpuPolicy.DcWriteCount);
        Assert.Equal(resolutionCalls, display.ApplyCalls);
        Assert.Equal(1, mappingReconciles);

        fpsCalls = limiter.ApplyCount;
        powerApplies = powerPolicy.ApplyCount;
        tdpOperations = transport.OperationCount;
        cpuWrites = cpuPolicy.AcWriteCount + cpuPolicy.DcWriteCount;
        var resolutionEdit = await control.SetXboxGameProfileResolutionAsync(Key, new(1440, 900), "Game");
        Assert.Equal(FrontendGameProfileMutationOutcome.Succeeded, resolutionEdit.Outcome);
        Assert.Equal(new DisplayModeSnapshot(1440, 900, 120, 32), display.Current);
        Assert.Equal(fpsCalls, limiter.ApplyCount);
        Assert.Equal(powerApplies, powerPolicy.ApplyCount);
        Assert.Equal(tdpOperations, transport.OperationCount);
        Assert.Equal(cpuWrites, cpuPolicy.AcWriteCount + cpuPolicy.DcWriteCount);

        cpuWrites = cpuPolicy.AcWriteCount + cpuPolicy.DcWriteCount;
        tdpOperations = transport.OperationCount;
        fpsCalls = limiter.ApplyCount;
        powerApplies = powerPolicy.ApplyCount;
        resolutionCalls = display.ApplyCalls;
        var favorite = await control.SetXboxGameProfileFavoriteAsync(Key, true, "Game");
        Assert.Equal(FrontendGameProfileMutationOutcome.Succeeded, favorite.Outcome);
        Assert.Equal(cpuWrites, cpuPolicy.AcWriteCount + cpuPolicy.DcWriteCount);
        Assert.Equal(tdpOperations, transport.OperationCount);
        Assert.Equal(fpsCalls, limiter.ApplyCount);
        Assert.Equal(powerApplies, powerPolicy.ApplyCount);
        Assert.Equal(resolutionCalls, display.ApplyCalls);

        fps.BeginShutdown();
        fps.Dispose();
        resolution.Shutdown();
    }

    [Fact]
    public async Task XBOX_edit_persists_without_apply_while_Steam_is_the_effective_target()
    {
        Directory.CreateDirectory(_directory);
        var store = new ProfileStore(ProfilePath);
        var gate = new ProfileMutationGate();
        var mutations = new XboxGameProfileMutations(store, gate, Model());
        var transport = new RecordingTdpTransport();
        await using var tdp = new TdpRuntime(store, gate, Model(), new MsiClawTdpHardware(transport));
        var limiter = new RecordingFrameLimiter();
        var fps = new IntelFrameLimiterRuntime(store, gate, limiter, marker: Path.Combine(_directory, "fps-marker.json"));
        var mappingReconciles = 0;
        var control = CreateControl(mutations, tdp, fps,
            activeProfileTargetSource: () => ActiveProfileTarget.ForSteam(123),
            reconcileXboxBackButtonMapping: _ => { mappingReconciles++; return true; });
        mutations.SetEnabled(Key, true, "Game");

        var result = await control.SetXboxGameProfileTdpAsync(Key,
            new FrontendGameTdpConfiguration(true, new(25, 30), new(18, 24)));

        Assert.Equal(FrontendGameProfileMutationOutcome.Succeeded, result.Outcome);
        Assert.Equal(25, store.Load().Document.XboxGames[Key].Performance.Tdp!.Ac.Pl1Watts);
        Assert.Equal(0, transport.OperationCount);
        Assert.Equal(0, limiter.ApplyCount);
        var mapping = new BackButtonMappingSettings(Xbox360BackButtonTarget.X, Xbox360BackButtonTarget.Y);
        Assert.True((await control.SetXboxGameProfileBackButtonMappingAsync(Key, mapping)).Succeeded);
        Assert.Equal(mapping, store.Load().Document.XboxGames[Key].Controller.BackButtonMapping);
        Assert.Equal(0, mappingReconciles);
        await tdp.DisposeAsync();
        fps.Dispose();
    }

    [Fact]
    public async Task Active_XBOX_apply_failure_keeps_the_saved_profile_change()
    {
        Directory.CreateDirectory(_directory);
        var store = new ProfileStore(ProfilePath);
        var gate = new ProfileMutationGate();
        store.Save(new ProfileDocument
        {
            XboxGames = new()
            {
                [Key] = new XboxGameProfile
                {
                    Enabled = true,
                    Performance = new GamePerformanceOverrides
                    {
                        CpuBoost = new GameCpuBoostSettings { Enabled = true, Ac = CpuBoostMode.Enabled, Dc = CpuBoostMode.Disabled },
                        Tdp = new GameTdpSettings { Enabled = true, Ac = Pair(20, 24), Dc = Pair(18, 22) }
                    }
                }
            }
        });
        var mutations = new XboxGameProfileMutations(store, gate, Model());
        var cpuPolicy = new FakeCpuBoostPowerPolicy
        {
            Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled),
            Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled),
            FailNextApply = true
        };
        var target = ActiveProfileTarget.ForXbox(Key);
        var resolver = ActiveProfileTestResolver.ForXbox(() => Key);
        var cpu = new CpuBoostRuntime(store, cpuPolicy, gate);
        cpu.SetActiveProfileResolver(resolver);
        var control = CreateControl(mutations, activeProfileTargetSource: () => target, cpu: cpu);

        var result = await control.SetXboxGameProfileCpuBoostAcAsync(Key, CpuBoostMode.Aggressive);

        Assert.Equal(FrontendGameProfileMutationOutcome.ApplyFailed, result.Outcome);
        Assert.Equal(CpuBoostMode.Aggressive, store.Load().Document.XboxGames[Key].Performance.CpuBoost!.Ac);
    }

    [Fact]
    public async Task Invalid_XBOX_TDP_and_FPS_values_do_not_write_the_profile()
    {
        var mutations = new XboxGameProfileMutations(new ProfileStore(ProfilePath), new ProfileMutationGate(), Model());
        mutations.SetEnabled(Key, true, "Game");
        var control = CreateControl(mutations);
        var before = File.ReadAllText(ProfilePath);

        var tdp = await control.SetXboxGameProfileTdpAsync(Key,
            new FrontendGameTdpConfiguration(true, new(7, 25), new(18, 22)));
        var fps = await control.SetXboxGameProfileFpsLimitAcAsync(Key, 39);

        Assert.Equal(FrontendGameProfileMutationOutcome.InvalidTarget, tdp.Outcome);
        Assert.Equal(FrontendGameProfileMutationOutcome.InvalidTarget, fps.Outcome);
        Assert.Equal(before, File.ReadAllText(ProfilePath));
    }

    [Fact]
    public async Task XBOX_mapping_snapshot_shows_current_global_fallback_and_explicit_override()
    {
        var global = new BackButtonMappingSettings(Xbox360BackButtonTarget.A, Xbox360BackButtonTarget.B);
        var mutations = new XboxGameProfileMutations(new ProfileStore(ProfilePath), new ProfileMutationGate(), Model());
        var control = CreateControl(mutations, globalBackButtonMapping: global);

        var initial = await control.CaptureXboxGameProfileAsync(Key);
        Assert.True(initial.BackButtonMapping!.UseGlobalMapping);
        Assert.Equal(global, initial.BackButtonMapping.Mapping);

        var explicitMapping = new BackButtonMappingSettings(Xbox360BackButtonTarget.X, Xbox360BackButtonTarget.Y);
        Assert.True((await control.SetXboxGameProfileBackButtonMappingAsync(Key, explicitMapping)).Succeeded);
        var explicitSnapshot = await control.CaptureXboxGameProfileAsync(Key);
        Assert.False(explicitSnapshot.BackButtonMapping!.UseGlobalMapping);
        Assert.Equal(explicitMapping, explicitSnapshot.BackButtonMapping.Mapping);

        Assert.True((await control.SetXboxGameProfileBackButtonMappingAsync(Key, null)).Succeeded);
        var globalSnapshot = await control.CaptureXboxGameProfileAsync(Key);
        Assert.True(globalSnapshot.BackButtonMapping!.UseGlobalMapping);
        Assert.Equal(global, globalSnapshot.BackButtonMapping.Mapping);
    }

    [Fact]
    public async Task Active_XBOX_mapping_and_profile_enable_changes_reconcile_only_after_persistence()
    {
        var store = new ProfileStore(ProfilePath);
        var mutations = new XboxGameProfileMutations(store, new ProfileMutationGate(), Model());
        var target = ActiveProfileTarget.ForXbox(Key);
        var mapping = new BackButtonMappingSettings(Xbox360BackButtonTarget.X, Xbox360BackButtonTarget.Y);
        BackButtonMappingSettings? expectedPersistedMapping = mapping;
        var reconciles = 0;
        var control = CreateControl(mutations,
            activeProfileTargetSource: () => target,
            reconcileXboxBackButtonMapping: key =>
            {
                Assert.Equal(Key, key);
                Assert.Equal(expectedPersistedMapping, store.Load().Document.XboxGames[Key].Controller.BackButtonMapping);
                reconciles++;
                return true;
            });

        Assert.True((await control.SetXboxGameProfileBackButtonMappingAsync(Key, mapping)).Succeeded);
        Assert.Equal(mapping, store.Load().Document.XboxGames[Key].Controller.BackButtonMapping);
        Assert.False(store.Load().Document.XboxGames[Key].Enabled);
        expectedPersistedMapping = null;
        Assert.True((await control.SetXboxGameProfileBackButtonMappingAsync(Key, null)).Succeeded);
        Assert.Null(store.Load().Document.XboxGames[Key].Controller.BackButtonMapping);
        expectedPersistedMapping = mapping;
        Assert.True((await control.SetXboxGameProfileBackButtonMappingAsync(Key, mapping)).Succeeded);
        Assert.Equal(FrontendGameProfileMutationOutcome.ApplyFailed,
            (await control.SetXboxGameProfileEnabledAsync(Key, true, "Game")).Outcome);
        Assert.Equal(FrontendGameProfileMutationOutcome.ApplyFailed,
            (await control.SetXboxGameProfileEnabledAsync(Key, false, "Game")).Outcome);
        Assert.False(store.Load().Document.XboxGames[Key].Enabled);
        Assert.Equal(mapping, store.Load().Document.XboxGames[Key].Controller.BackButtonMapping);
        Assert.Equal(FrontendGameProfileMutationOutcome.ApplyFailed,
            (await control.SetXboxGameProfileEnabledAsync(Key, true, "Game")).Outcome);
        Assert.True(store.Load().Document.XboxGames[Key].Enabled);
        Assert.Equal(mapping, store.Load().Document.XboxGames[Key].Controller.BackButtonMapping);
        Assert.Equal(6, reconciles);
    }

    [Fact]
    public async Task Active_mapping_reconcile_failure_keeps_the_persisted_change_and_reports_apply_failed()
    {
        var store = new ProfileStore(ProfilePath);
        var mutations = new XboxGameProfileMutations(store, new ProfileMutationGate(), Model());
        var control = CreateControl(mutations,
            activeProfileTargetSource: () => ActiveProfileTarget.ForXbox(Key),
            reconcileXboxBackButtonMapping: _ => false);
        var mapping = new BackButtonMappingSettings(Xbox360BackButtonTarget.LeftBumper, Xbox360BackButtonTarget.RightBumper);

        var result = await control.SetXboxGameProfileBackButtonMappingAsync(Key, mapping);

        Assert.Equal(FrontendGameProfileMutationOutcome.ApplyFailed, result.Outcome);
        Assert.Equal(mapping, store.Load().Document.XboxGames[Key].Controller.BackButtonMapping);
    }

    private InProcessAddonFrontendControl CreateControl(
        XboxGameProfileMutations mutations,
        TdpRuntime? tdp = null,
        IntelFrameLimiterRuntime? fps = null,
        Func<uint>? actualRunningAppIdSource = null,
        Func<ActiveProfileTarget>? activeProfileTargetSource = null,
        Func<string, string?>? activeXboxDisplayNameSource = null,
        Func<string, bool>? reconcileXboxBackButtonMapping = null,
        BackButtonMappingSettings? globalBackButtonMapping = null,
        CpuBoostRuntime? cpu = null,
        PowerModeRuntime? power = null,
        GameDisplayResolutionRuntime? resolution = null)
    {
        var settings = new StartupSettingsCoordinator(new AppSettings { BackButtonMapping = globalBackButtonMapping ?? BackButtonMappingSettings.Default },
            new SettingsStore(Path.Combine(_directory, "settings.json")), new NoOpStartupManager());
        return new InProcessAddonFrontendControl(settings, new ThrowingStatusProvider(), null,
            cpuBoostRuntime: cpu, tdpRuntime: tdp, powerModeRuntime: power,
            displayResolutionRuntime: resolution, intelFpsRuntime: fps,
            xboxGameProfileMutations: mutations, actualRunningAppIdSource: actualRunningAppIdSource,
            activeProfileTargetSource: activeProfileTargetSource,
            reconcileXboxBackButtonMapping: reconcileXboxBackButtonMapping,
            activeXboxDisplayNameSource: activeXboxDisplayNameSource);
    }

    private static HandheldDeviceModelId Model() => new("msi.claw.a2vm.7");
    private static TdpPowerPair Pair(int pl1, int pl2) => new() { Pl1Watts = pl1, Pl2Watts = pl2 };

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class NoOpStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class ThrowingStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Status is not used by these tests.");
    }

    private sealed class RecordingTdpTransport : IMsiClawTdpTransport
    {
        public List<string> Operations { get; } = [];
        public int OperationCount { get; private set; }
        public bool TryGetAp(int index, out byte[] payload) { OperationCount++; Operations.Add($"GetAp({index})"); payload = [0, 0, 0xC0]; return true; }
        public bool TrySetData(int block, byte value) { OperationCount++; Operations.Add($"SetData({block},{value})"); return true; }
    }

    private sealed class RecordingDisplayResolutionService : IDisplayResolutionService
    {
        public DisplayModeSnapshot Current { get; private set; } = new(1920, 1200, 120, 32);
        public int ApplyCalls { get; private set; }
        public bool TryCapture(out DisplayModeSnapshot snapshot) { snapshot = Current; return true; }
        public bool TryApply(DisplayModeSnapshot current, int width, int height)
        {
            ApplyCalls++;
            Current = current with { Width = width, Height = height };
            return true;
        }
        public bool TryRestore(DisplayModeSnapshot original) { Current = original; return true; }
    }

    private sealed class RecordingFrameLimiter : IIntelFrameLimiter
    {
        public int ApplyCount { get; private set; }
        public int LastEnabledFps { get; private set; }
        public void Initialize() { }
        public bool Available => true;
        public string? UnavailableReason => null;
        public IntelFpsCapability? Capability => null;
        public IntelFpsApplyOutcome Enable(int fps, AcDcPowerSource source) { ApplyCount++; LastEnabledFps = fps; return IntelFpsApplyOutcome.Succeeded; }
        public bool Disable(AcDcPowerSource? source) { ApplyCount++; return true; }
        public void Dispose() { }
    }
}
