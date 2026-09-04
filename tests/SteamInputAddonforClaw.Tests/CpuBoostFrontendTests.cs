using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Contracts.Frontend;
using SteamInputAddonforClaw.Developer;
using SteamInputAddonforClaw.Frontend;
using SteamInputAddonforClaw.FrontendTransport;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Performance;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>
/// Covers the PR277 frontend projection of PR276's <see cref="CpuBoostRuntime"/> through
/// <see cref="InProcessAddonFrontendControl"/>: the frontend must never fold
/// <see cref="CpuBoostMutationOutcome"/> into a plain bool, must keep AC/DC independently mutable,
/// and must never gate CPU Boost on Routing/OEM1 (work order PR277 sections 1/6/7/10/18).
/// </summary>
[Collection("AppLog")]
public sealed class CpuBoostFrontendTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task Capture_preserves_an_unmanaged_known_current_state_exactly()
    {
        // CreateReconciledRuntime's StartupReconcile() bootstraps this Known current as the initial
        // Desired value too (PR277 addendum "CPU Boost First-Run Baseline Policy") -- current and
        // desired are the same value immediately after a fresh first-run bootstrap.
        var policy = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = CreateReconciledRuntime(policy);
        var control = CreateControl(runtime);

        var snapshot = await control.CaptureCpuBoostAsync();

        Assert.Equal(FrontendCpuBoostReadStatus.Known, snapshot.Ac.CurrentStatus);
        Assert.Equal(CpuBoostMode.Aggressive, snapshot.Ac.Current);
        Assert.Equal(CpuBoostMode.Aggressive, snapshot.Ac.Desired);
        Assert.Equal(FrontendCpuBoostReadStatus.Known, snapshot.Dc.CurrentStatus);
        Assert.Equal(CpuBoostMode.Disabled, snapshot.Dc.Current);
        Assert.Equal(CpuBoostMode.Disabled, snapshot.Dc.Desired);
    }

    [Fact]
    public async Task Capture_never_mutates_anything()
    {
        // Opening the Device page and capturing the snapshot must cause zero ADDITIONAL writes
        // beyond CreateReconciledRuntime's own one-time first-run bootstrap (work order section
        // 8/21) -- proven here by a policy that would record any write attempt, and by the
        // persisted file's content being unchanged by Capture.
        var policy = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = CreateReconciledRuntime(policy);
        var control = CreateControl(runtime);
        var profilesPath = Path.Combine(_testDirectory, "profiles.json");
        var contentsAfterBootstrap = File.ReadAllText(profilesPath);

        await control.CaptureCpuBoostAsync();
        await control.CaptureCpuBoostAsync();

        Assert.Equal(0, policy.AcWriteCount);
        Assert.Equal(0, policy.DcWriteCount);
        Assert.Equal(contentsAfterBootstrap, File.ReadAllText(profilesPath));
    }

    [Fact]
    public async Task AC_mutation_writes_only_AC()
    {
        var policy = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = CreateReconciledRuntime(policy);
        var control = CreateControl(runtime);

        var result = await control.SetDeviceCpuBoostAcAsync(CpuBoostMode.EfficientAggressive);

        Assert.True(result.Succeeded);
        Assert.Equal(1, policy.AcWriteCount);
        Assert.Equal(0, policy.DcWriteCount);
        Assert.Equal(CpuBoostMode.EfficientAggressive, result.Snapshot.Ac.Desired);
        Assert.Equal(CpuBoostMode.Disabled, result.Snapshot.Dc.Desired);
    }

    [Fact]
    public async Task DC_mutation_writes_only_DC()
    {
        var policy = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = CreateReconciledRuntime(policy);
        var control = CreateControl(runtime);

        var result = await control.SetDeviceCpuBoostDcAsync(CpuBoostMode.Disabled);

        Assert.True(result.Succeeded);
        Assert.Equal(0, policy.AcWriteCount);
        Assert.Equal(1, policy.DcWriteCount);
        Assert.Equal(CpuBoostMode.Enabled, result.Snapshot.Ac.Desired);
        Assert.Equal(CpuBoostMode.Disabled, result.Snapshot.Dc.Desired);
    }

    [Fact]
    public async Task Apply_failure_remains_visible_to_the_frontend_and_is_never_reported_as_success()
    {
        var policy = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = CreateReconciledRuntime(policy);
        var control = CreateControl(runtime);
        policy.FailNextApply = true;

        var result = await control.SetDeviceCpuBoostAcAsync(CpuBoostMode.Aggressive);

        Assert.False(result.Succeeded);
        Assert.Equal(FrontendCpuBoostMutationOutcome.ApplyFailed, result.Outcome);
        Assert.NotNull(result.FailureMessage);
        // The new desired value IS now authoritative (persistence succeeded) -- the snapshot must
        // NOT roll back to the old (null) desired value (work order section 7).
        Assert.Equal(CpuBoostMode.Aggressive, result.Snapshot.Ac.Desired);
    }

    [Fact]
    public async Task Active_game_feature_toggle_reports_apply_failure_after_persistence()
    {
        var policy = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var profilesPath = Path.Combine(_testDirectory, "profiles.json");
        var store = new ProfileStore(profilesPath);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings { Performance = new DevicePerformanceSettings { CpuBoost = new DeviceCpuBoostSettings { Enabled = true, Ac = CpuBoostMode.Enabled, Dc = CpuBoostMode.Disabled } } },
            Games = new Dictionary<string, GameProfile>
            {
                ["12345"] = new GameProfile
                {
                    Enabled = true,
                    Performance = new GamePerformanceOverrides
                    {
                        CpuBoost = new GameCpuBoostSettings { Enabled = false, Ac = CpuBoostMode.Aggressive, Dc = CpuBoostMode.EfficientEnabled },
                        Tdp = new GameTdpSettings { Enabled = true, Ac = new() { Pl1Watts = 20, Pl2Watts = 22 }, Dc = new() { Pl1Watts = 20, Pl2Watts = 22 } }
                    }
                }
            }
        });
        var runtime = new CpuBoostRuntime(store, policy);
        runtime.SetActualAppIdSource(() => 12345);
        runtime.StartupReconcile(12345);
        var mutations = new GameProfileMutations(store);
        Assert.Equal(GameProfileMutations.MutationOutcome.Succeeded, mutations.SetCpuBoostEnabled(12345, false));
        var control = CreateControl(runtime, mutations, () => 12345);
        policy.FailNextApply = true;

        var result = await control.SetGameProfileCpuBoostEnabledAsync(12345, true);

        Assert.Equal(FrontendGameProfileMutationOutcome.ApplyFailed, result.Outcome);
        Assert.Contains("apply", result.FailureMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.True(store.Load().Document.Games["12345"].Performance.CpuBoost!.Enabled);
    }

    [Fact]
    public async Task Persistence_failure_remains_a_mutation_failure_and_is_never_hidden()
    {
        // No StartupReconcile() was ever called, so the runtime's persistence-writable flag is still
        // false -- every mutation must fail closed without touching the fake Windows policy at all.
        var policy = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(new ProfileStore(Path.Combine(_testDirectory, "profiles.json")), policy);
        var control = CreateControl(runtime);

        var result = await control.SetDeviceCpuBoostAcAsync(CpuBoostMode.Aggressive);

        Assert.False(result.Succeeded);
        Assert.Equal(FrontendCpuBoostMutationOutcome.PersistenceFailed, result.Outcome);
        Assert.Equal(0, policy.AcWriteCount);
        Assert.False(result.Snapshot.PersistenceWritable);
    }

    [Fact]
    public async Task Unknown_current_value_remains_Unknown()
    {
        var policy = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.UnknownValue() };
        var runtime = CreateReconciledRuntime(policy);
        var control = CreateControl(runtime);

        var snapshot = await control.CaptureCpuBoostAsync();

        Assert.Equal(FrontendCpuBoostReadStatus.Unknown, snapshot.Ac.CurrentStatus);
        Assert.Null(snapshot.Ac.Current);
    }

    [Fact]
    public async Task Unavailable_current_value_remains_Unavailable()
    {
        var policy = new FakeCpuBoostPowerPolicy { FailRead = true };
        var runtime = CreateReconciledRuntime(policy);
        var control = CreateControl(runtime);

        var snapshot = await control.CaptureCpuBoostAsync();

        Assert.Equal(FrontendCpuBoostReadStatus.Unavailable, snapshot.Ac.CurrentStatus);
        Assert.Equal(FrontendCpuBoostReadStatus.Unavailable, snapshot.Dc.CurrentStatus);
    }

    [Fact]
    public async Task CPU_Boost_frontend_works_with_no_routing_composition_at_all()
    {
        // Architectural invariant (work order section 1): construct the control with runtime: null
        // (no AddonRuntimeHost/routing composition whatsoever) and prove CPU Boost still works.
        var policy = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = CreateReconciledRuntime(policy);
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _testDirectory;
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), store, new FakeStartupManager());
        var control = new InProcessAddonFrontendControl(coordinator, new ThrowingSystemStatusProvider(), null, new DeveloperTestModeState(),
            cpuBoostRuntime: runtime);

        var result = await control.SetDeviceCpuBoostAcAsync(CpuBoostMode.Aggressive);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Disabling_reports_Enabled_false_and_performs_no_restoration()
    {
        var policy = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = CreateReconciledRuntime(policy);
        var control = CreateControl(runtime);

        var result = await control.SetDeviceCpuBoostEnabledAsync(false);

        Assert.True(result.Succeeded);
        Assert.False(result.Snapshot.Enabled);
        Assert.Equal(0, policy.AcWriteCount);
        Assert.Equal(0, policy.DcWriteCount);
        // Saved selections remain visible while disabled.
        Assert.Equal(CpuBoostMode.Aggressive, result.Snapshot.Ac.Desired);
        Assert.Equal(CpuBoostMode.Disabled, result.Snapshot.Dc.Desired);
    }

    [Fact]
    public async Task Re_enabling_applies_saved_values_and_reports_Enabled_true()
    {
        var policy = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = CreateReconciledRuntime(policy);
        var control = CreateControl(runtime);
        await control.SetDeviceCpuBoostEnabledAsync(false);

        var result = await control.SetDeviceCpuBoostEnabledAsync(true);

        Assert.True(result.Succeeded);
        Assert.True(result.Snapshot.Enabled);
        Assert.Equal(1, policy.AcWriteCount);
        Assert.Equal(1, policy.DcWriteCount);
    }

    [Fact]
    public async Task Shutdown_barrier_rejects_AC_mutation_with_zero_writes_and_unchanged_persistence()
    {
        var policy = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = CreateReconciledRuntime(policy);
        var control = CreateControl(runtime);
        var profilesPath = Path.Combine(_testDirectory, "profiles.json");
        var contentsBeforeShutdown = File.ReadAllText(profilesPath);

        control.BeginProcessShutdown();

        var exception = await Assert.ThrowsAsync<FrontendProtocolException>(() => control.SetDeviceCpuBoostAcAsync(CpuBoostMode.Enabled));
        Assert.Contains("shutting down", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, policy.AcWriteCount);
        Assert.Equal(0, policy.DcWriteCount);
        Assert.Equal(contentsBeforeShutdown, File.ReadAllText(profilesPath));
    }

    [Fact]
    public async Task Shutdown_barrier_rejects_Enabled_mutation_with_Enabled_unchanged_and_zero_writes()
    {
        var policy = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = CreateReconciledRuntime(policy);
        var control = CreateControl(runtime);

        control.BeginProcessShutdown();

        var exception = await Assert.ThrowsAsync<FrontendProtocolException>(() => control.SetDeviceCpuBoostEnabledAsync(false));
        Assert.Contains("shutting down", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(runtime.Snapshot.Enabled);
        Assert.Equal(0, policy.AcWriteCount);
        Assert.Equal(0, policy.DcWriteCount);
    }

    [Fact]
    public async Task Shutdown_barrier_rejects_game_profile_mutation_without_persistence()
    {
        var policy = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = CreateReconciledRuntime(policy);
        var profilesPath = Path.Combine(_testDirectory, "profiles.json");
        var control = CreateControl(runtime, new GameProfileMutations(new ProfileStore(profilesPath)));
        var contentsBeforeShutdown = File.ReadAllText(profilesPath);

        control.BeginProcessShutdown();

        var exception = await Assert.ThrowsAsync<FrontendProtocolException>(() => control.SetGameProfileEnabledAsync(12345, true, "Game"));
        Assert.Contains("shutting down", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(contentsBeforeShutdown, File.ReadAllText(profilesPath));
    }

    private CpuBoostRuntime CreateReconciledRuntime(ICpuBoostPowerPolicy policy)
    {
        var runtime = new CpuBoostRuntime(new ProfileStore(Path.Combine(_testDirectory, "profiles.json")), policy);
        runtime.StartupReconcile();
        return runtime;
    }

    private InProcessAddonFrontendControl CreateControl(CpuBoostRuntime cpuBoostRuntime)
        => CreateControl(cpuBoostRuntime, null);

    private InProcessAddonFrontendControl CreateControl(CpuBoostRuntime cpuBoostRuntime, GameProfileMutations? gameProfileMutations, Func<uint>? actualRunningAppIdSource = null)
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = _testDirectory;
        var store = new SettingsStore(Path.Combine(_testDirectory, "settings.json"));
        var coordinator = new StartupSettingsCoordinator(new AppSettings(), store, new FakeStartupManager());
        return new InProcessAddonFrontendControl(
            coordinator,
            new ThrowingSystemStatusProvider(),
            null,
            new DeveloperTestModeState(),
            cpuBoostRuntime: cpuBoostRuntime, gameProfileMutations: gameProfileMutations, actualRunningAppIdSource: actualRunningAppIdSource);
    }

    public void Dispose()
    {
        SteamInputAddonforClaw.Diagnostics.AppLog.DirectoryOverride = null;
        if (Directory.Exists(_testDirectory)) Directory.Delete(_testDirectory, recursive: true);
    }

    private sealed class FakeStartupManager : IWindowsStartupManager
    {
        public StartupRegistrationResult Synchronize(bool enabled) => StartupRegistrationResult.Enabled();
    }

    private sealed class ThrowingSystemStatusProvider : ISystemStatusProvider
    {
        public Task<SystemStatusSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Status capture is not part of these tests.");
    }
}
