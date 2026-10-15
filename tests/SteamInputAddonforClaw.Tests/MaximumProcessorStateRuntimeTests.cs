using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Performance;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class MaximumProcessorStateRuntimeTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    public MaximumProcessorStateRuntimeTests() => AppLog.DirectoryOverride = _testDirectory;

    [Fact]
    public void Legacy_profile_capture_and_startup_leave_nonstandard_windows_values_untouched()
    {
        var path = ProfilePath;
        Directory.CreateDirectory(_testDirectory);
        const string legacy = "{\"schemaVersion\":1,\"device\":{\"performance\":{},\"display\":{},\"battery\":{}},\"games\":{},\"xboxGames\":{}}";
        File.WriteAllText(path, legacy);
        var policy = new FakePowerPolicy { Ac = 85, Dc = 90 };
        var runtime = CreateRuntime(policy);

        runtime.StartupReconcile();
        var snapshot = runtime.Capture();

        Assert.Equal(85u, snapshot.AcCurrent.Percent);
        Assert.Equal(90u, snapshot.DcCurrent.Percent);
        Assert.False(snapshot.Enabled);
        Assert.Equal(100, snapshot.AcDesired);
        Assert.Equal(100, snapshot.DcDesired);
        Assert.Empty(policy.Applied);
        Assert.Equal(legacy, File.ReadAllText(path));
    }

    [Fact]
    public void First_enable_off_and_reenable_follow_explicit_ownership_policy()
    {
        var policy = new FakePowerPolicy();
        var runtime = CreateRuntime(policy);

        Assert.True(runtime.SetEnabled(true).Succeeded);
        Assert.Equal(((int? Ac, int? Dc))(100, 100), Assert.Single(policy.Applied));
        Assert.True(runtime.SetAc(99).Succeeded);
        Assert.True(runtime.SetDc(99).Succeeded);
        policy.Applied.Clear();

        Assert.True(runtime.SetEnabled(false).Succeeded);
        Assert.Empty(policy.Applied);
        Assert.False(runtime.Snapshot.Enabled);
        Assert.Equal(99, runtime.Snapshot.AcDesired);
        Assert.Equal(99, runtime.Snapshot.DcDesired);
        Assert.Equal(99u, policy.Ac);
        Assert.Equal(99u, policy.Dc);

        Assert.True(runtime.SetEnabled(true).Succeeded);
        Assert.Equal((99, 99), Assert.Single(policy.Applied));
    }

    [Fact]
    public void Side_updates_apply_only_the_requested_side_and_accept_only_99_or_100()
    {
        var policy = new FakePowerPolicy();
        var runtime = CreateRuntime(policy);
        Assert.True(runtime.SetEnabled(true).Succeeded);
        policy.Applied.Clear();

        Assert.True(runtime.SetAc(99).Succeeded);
        Assert.True(runtime.SetDc(99).Succeeded);
        Assert.True(runtime.SetAc(100).Succeeded);

        Assert.Equal(new (int? Ac, int? Dc)[] { (99, null), (null, 99), (100, null) }, policy.Applied);
        foreach (var invalid in new[] { 98, 0, 150 })
        {
            Assert.Equal(MaximumProcessorStateMutationOutcome.InvalidTarget, runtime.SetDc(invalid).Outcome);
        }
        Assert.Equal(3, policy.Applied.Count);
        Assert.Equal(100, runtime.Snapshot.AcDesired);
        Assert.Equal(99, runtime.Snapshot.DcDesired);
    }

    [Fact]
    public void Invalid_saved_pair_is_not_coerced_or_written_and_original_document_is_preserved()
    {
        var store = new ProfileStore(ProfilePath);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings
            {
                Performance = new DevicePerformanceSettings
                {
                    MaximumProcessorState = new DeviceMaximumProcessorStateSettings { Enabled = true, Ac = 50, Dc = 150 }
                }
            }
        });
        var original = File.ReadAllText(ProfilePath);
        var policy = new FakePowerPolicy();
        var runtime = CreateRuntime(policy);

        runtime.StartupReconcile();
        var result = runtime.SetEnabled(true);

        Assert.Equal(MaximumProcessorStateMutationOutcome.InvalidTarget, result.Outcome);
        Assert.False(runtime.Snapshot.ConfigurationValid);
        Assert.Null(runtime.Snapshot.AcDesired);
        Assert.Null(runtime.Snapshot.DcDesired);
        Assert.Empty(policy.Applied);
        Assert.Equal(original, File.ReadAllText(ProfilePath));
    }

    [Fact]
    public void Persistence_failure_prevents_every_windows_write()
    {
        Directory.CreateDirectory(ProfilePath);
        var policy = new FakePowerPolicy();
        var runtime = CreateRuntime(policy);

        var result = runtime.SetEnabled(true);

        Assert.Equal(MaximumProcessorStateMutationOutcome.PersistenceFailed, result.Outcome);
        Assert.Empty(policy.Applied);
    }

    [Fact]
    public void Apply_failure_is_reported_after_desired_pair_is_persisted()
    {
        var policy = new FakePowerPolicy { NextApplyResult = new(false, false, "PowerSetActiveScheme failed.") };
        var runtime = CreateRuntime(policy);

        var result = runtime.SetEnabled(true);

        Assert.Equal(MaximumProcessorStateMutationOutcome.ApplyFailed, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Equal(100, new ProfileStore(ProfilePath).Load().Document.Device.Performance.MaximumProcessorState?.Ac);
        Assert.Equal(100, new ProfileStore(ProfilePath).Load().Document.Device.Performance.MaximumProcessorState?.Dc);
        Assert.True(runtime.Snapshot.Enabled);
    }

    [Fact]
    public void Startup_and_normal_reconcile_apply_only_an_enabled_saved_pair()
    {
        var store = new ProfileStore(ProfilePath);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings
            {
                Performance = new DevicePerformanceSettings
                {
                    MaximumProcessorState = new DeviceMaximumProcessorStateSettings { Enabled = true, Ac = 99, Dc = 100 }
                }
            }
        });
        var policy = new FakePowerPolicy();
        var runtime = CreateRuntime(policy);

        runtime.StartupReconcile();
        runtime.Reconcile();

        Assert.Equal(new (int? Ac, int? Dc)[] { (99, 100), (99, 100) }, policy.Applied);
    }

    [Fact]
    public void Startup_and_normal_reconcile_leave_a_saved_disabled_pair_untouched()
    {
        var store = new ProfileStore(ProfilePath);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings
            {
                Performance = new DevicePerformanceSettings
                {
                    MaximumProcessorState = new DeviceMaximumProcessorStateSettings { Enabled = false, Ac = 99, Dc = 100 }
                }
            }
        });
        var policy = new FakePowerPolicy { Ac = 85, Dc = 90 };
        var runtime = CreateRuntime(policy);

        runtime.StartupReconcile();
        runtime.Reconcile();

        Assert.Empty(policy.Applied);
        Assert.Equal(85u, policy.Ac);
        Assert.Equal(90u, policy.Dc);
    }

    [Fact]
    public void Policy_apply_reports_write_and_active_scheme_failures()
    {
        var activated = false;
        var writeFailure = WindowsMaximumProcessorStatePowerPolicy.ResolveApplyResult(
            99, 5, null, null, () => { activated = true; return 0; });
        Assert.False(writeFailure.Succeeded);
        Assert.False(activated);

        var activationFailure = WindowsMaximumProcessorStatePowerPolicy.ResolveApplyResult(
            99, 0, 100, 0, () => 5);
        Assert.False(activationFailure.Succeeded);
        Assert.False(activationFailure.AcSucceeded);
        Assert.False(activationFailure.DcSucceeded);
        Assert.Contains("PowerSetActiveScheme", activationFailure.FailureMessage, StringComparison.Ordinal);
    }

    private string ProfilePath => Path.Combine(_testDirectory, "profiles.json");

    private MaximumProcessorStateRuntime CreateRuntime(FakePowerPolicy policy)
        => new(new ProfileStore(ProfilePath), policy, new ProfileMutationGate());

    public void Dispose()
    {
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_testDirectory)) Directory.Delete(_testDirectory, recursive: true);
    }

    private sealed class FakePowerPolicy : IMaximumProcessorStatePowerPolicy
    {
        public uint Ac { get; set; } = 100;
        public uint Dc { get; set; } = 100;
        public MaximumProcessorStateApplyResult? NextApplyResult { get; set; }
        public List<(int? Ac, int? Dc)> Applied { get; } = [];

        public MaximumProcessorStateSystemState Read()
        {
            return new(MaximumProcessorStateSideReading.Known(Ac), MaximumProcessorStateSideReading.Known(Dc), null);
        }

        public MaximumProcessorStateApplyResult Apply(int? ac, int? dc)
        {
            Applied.Add((ac, dc));
            if (NextApplyResult is { } failure)
            {
                NextApplyResult = null;
                return failure;
            }
            if (ac is { } acValue) Ac = (uint)acValue;
            if (dc is { } dcValue) Dc = (uint)dcValue;
            return new(true, true, null);
        }
    }
}
