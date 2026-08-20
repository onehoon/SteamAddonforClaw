using SteamInputAddonforClaw.Contracts.DeviceProfiles;
using SteamInputAddonforClaw.Profiles;
using SteamInputAddonforClaw.Profiles.Performance;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

/// <summary>Fake CPU Boost backend -- never touches the real machine's power scheme. Mirrors the
/// AC/DC-independent, no-implicit-normalization semantics <see cref="ICpuBoostPowerPolicy"/>
/// documents.</summary>
internal sealed class FakeCpuBoostPowerPolicy : ICpuBoostPowerPolicy
{
    public CpuBoostSideReading Ac { get; set; } = CpuBoostSideReading.Unavailable;
    public CpuBoostSideReading Dc { get; set; } = CpuBoostSideReading.Unavailable;
    public int AcWriteCount { get; private set; }
    public int DcWriteCount { get; private set; }
    public bool FailNextApply { get; set; }
    public bool FailRead { get; set; }

    /// <summary>Invoked synchronously at the start of every <see cref="Apply"/> call, before any
    /// gate wait -- lets a test know precisely when a caller has entered Apply.</summary>
    public Action? OnApplyEntered { get; set; }

    /// <summary>When set, the next <see cref="Apply"/> call blocks on this gate before doing
    /// anything else, then clears the field so only that one call blocks.</summary>
    public ManualResetEventSlim? ApplyGate { get; set; }

    public CpuBoostSystemState Read() =>
        FailRead ? CpuBoostSystemState.Failure("simulated read failure") : new CpuBoostSystemState(true, Ac, Dc, null);

    public CpuBoostApplyResult Apply(CpuBoostMode? ac, CpuBoostMode? dc)
    {
        OnApplyEntered?.Invoke();
        var gate = ApplyGate;
        if (gate is not null)
        {
            ApplyGate = null;
            gate.Wait();
        }

        if (FailNextApply)
        {
            FailNextApply = false;
            return new CpuBoostApplyResult(ac is null, dc is null, "simulated apply failure");
        }

        if (ac is { } acMode)
        {
            AcWriteCount++;
            Ac = CpuBoostSideReading.Known(acMode);
        }

        if (dc is { } dcMode)
        {
            DcWriteCount++;
            Dc = CpuBoostSideReading.Known(dcMode);
        }

        return new CpuBoostApplyResult(true, true, null);
    }
}

public sealed class CpuBoostRuntimeTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    private string ProfilesPath => Path.Combine(_testDirectory, "profiles.json");

    // ---- Unmanaged: existing Windows values are never touched ----

    [Fact]
    public void StartupReconcile_FirstRun_AdoptsCurrentWindowsValuesAsInitialDeviceValues()
    {
        // PR277 addendum "CPU Boost First-Run Baseline Policy": with no persisted CPU Boost value
        // yet, the current Windows AC/DC values are read once and adopted as-is as the initial
        // persisted Device values -- not left "unmanaged forever" as PR276 originally modeled it.
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive), Dc = CpuBoostSideReading.Known(CpuBoostMode.EfficientEnabled) };
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();

        // Adopting the exact current values requires no additional Windows write -- they already
        // match.
        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
        Assert.Equal(CpuBoostMode.Aggressive, runtime.Snapshot.AcCurrent.Mode);
        Assert.Equal(CpuBoostMode.EfficientEnabled, runtime.Snapshot.DcCurrent.Mode);
        Assert.Equal(CpuBoostMode.Aggressive, runtime.Snapshot.AcDesired);
        Assert.Equal(CpuBoostMode.EfficientEnabled, runtime.Snapshot.DcDesired);
        Assert.True(File.Exists(ProfilesPath));
        var loaded = store.Load();
        Assert.Equal(CpuBoostMode.Aggressive, loaded.Document.Device.Performance.CpuBoost?.Ac);
        Assert.Equal(CpuBoostMode.EfficientEnabled, loaded.Document.Device.Performance.CpuBoost?.Dc);
    }

    [Fact]
    public void StartupReconcile_FirstRun_WindowsReadFailure_DoesNotPersistOrInventAFallback()
    {
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { FailRead = true };
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();

        Assert.False(File.Exists(ProfilesPath));
        Assert.Null(runtime.Snapshot.AcDesired);
        Assert.Null(runtime.Snapshot.DcDesired);
        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
    }

    [Fact]
    public void StartupReconcile_FirstRun_UnknownWindowsValue_DoesNotPersistOrNormalize()
    {
        // Neither side maps to the supported 0..6 mode set -- must not invent a value, must not
        // normalize, must not persist a partial/guessed result.
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.UnknownValue(), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();

        Assert.False(File.Exists(ProfilesPath));
        Assert.Null(runtime.Snapshot.AcDesired);
        Assert.Null(runtime.Snapshot.DcDesired);
    }

    [Fact]
    public void StartupReconcile_AfterFirstRun_DoesNotReplacePersistedValuesFromAFreshWindowsRead()
    {
        // Once a CPU Boost value is persisted, a later startup must keep using it -- Windows having
        // since been changed by another application must never become the new app default again.
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile(); // first run: adopts Aggressive/Disabled

        // Another application changes Windows in between.
        backend.Ac = CpuBoostSideReading.Known(CpuBoostMode.EfficientEnabled);
        backend.Dc = CpuBoostSideReading.Known(CpuBoostMode.Enabled);
        var secondRuntime = new CpuBoostRuntime(store, backend);

        secondRuntime.StartupReconcile();

        Assert.Equal(CpuBoostMode.Aggressive, secondRuntime.Snapshot.AcDesired);
        Assert.Equal(CpuBoostMode.Disabled, secondRuntime.Snapshot.DcDesired);
    }

    // ---- Device CPU Boost Toggle addendum ----

    [Fact]
    public void StartupReconcile_FirstRun_EnabledDefaultsOn()
    {
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();

        Assert.True(runtime.Snapshot.Enabled);
        Assert.True(store.Load().Document.Device.Performance.CpuBoost?.Enabled);
    }

    [Fact]
    public void StartupReconcile_Disabled_PerformsZeroWrites()
    {
        var store = new ProfileStore(ProfilesPath);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings { Performance = new DevicePerformanceSettings { CpuBoost = new DeviceCpuBoostSettings { Enabled = false, Ac = CpuBoostMode.Aggressive, Dc = CpuBoostMode.Disabled } } }
        });
        var backend = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();

        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
        Assert.False(runtime.Snapshot.Enabled);
        // Saved selections remain visible even while disabled -- OFF never nulls them out.
        Assert.Equal(CpuBoostMode.Aggressive, runtime.Snapshot.AcDesired);
        Assert.Equal(CpuBoostMode.Disabled, runtime.Snapshot.DcDesired);
    }

    [Fact]
    public void SetDeviceCpuBoostEnabled_Disable_PerformsNoRestorationAndLeavesWindowsUntouched()
    {
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile(); // first-run bootstrap: Enabled=true, Ac=Aggressive, Dc=Disabled

        var result = runtime.SetDeviceCpuBoostEnabled(false);

        Assert.True(result.Succeeded);
        Assert.False(store.Load().Document.Device.Performance.CpuBoost?.Enabled);
        // Zero CPU Boost restoration writes -- current Windows values remain exactly as they were.
        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
        Assert.Equal(CpuBoostMode.Aggressive, backend.Ac.Mode);
        Assert.Equal(CpuBoostMode.Disabled, backend.Dc.Mode);
    }

    [Fact]
    public void SetDeviceCpuBoostEnabled_ReEnable_AppliesSavedValuesNotFreshWindowsRead()
    {
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile(); // Enabled=true, Ac=Aggressive, Dc=Disabled
        runtime.SetDeviceCpuBoostEnabled(false);

        // While OFF, another tool changes Windows.
        backend.Ac = CpuBoostSideReading.Known(CpuBoostMode.EfficientEnabled);
        backend.Dc = CpuBoostSideReading.Known(CpuBoostMode.Enabled);

        var result = runtime.SetDeviceCpuBoostEnabled(true);

        Assert.True(result.Succeeded);
        Assert.True(store.Load().Document.Device.Performance.CpuBoost?.Enabled);
        // Applies the SAVED Aggressive/Disabled, never re-bootstrapped from the current
        // EfficientEnabled/Enabled Windows values.
        Assert.Equal(CpuBoostMode.Aggressive, backend.Ac.Mode);
        Assert.Equal(CpuBoostMode.Disabled, backend.Dc.Mode);
    }

    [Fact]
    public void Disabling_PreservesTheSavedAcDcSelections()
    {
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile();

        runtime.SetDeviceCpuBoostEnabled(false);
        var reloaded = store.Load();

        Assert.False(reloaded.Document.Device.Performance.CpuBoost?.Enabled);
        Assert.Equal(CpuBoostMode.Aggressive, reloaded.Document.Device.Performance.CpuBoost?.Ac);
        Assert.Equal(CpuBoostMode.Disabled, reloaded.Document.Device.Performance.CpuBoost?.Dc);
    }

    [Fact]
    public void Mutation_WhileDisabled_PersistsSelectionButDoesNotApplyToWindows()
    {
        var store = new ProfileStore(ProfilesPath);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings { Performance = new DevicePerformanceSettings { CpuBoost = new DeviceCpuBoostSettings { Enabled = false, Ac = CpuBoostMode.Aggressive } } }
        });
        var backend = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile();

        var result = runtime.SetDeviceCpuBoostAc(CpuBoostMode.EfficientAggressive);

        Assert.True(result.Succeeded);
        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(CpuBoostMode.EfficientAggressive, store.Load().Document.Device.Performance.CpuBoost?.Ac);
    }

    // ---- AC-only managed startup ----

    [Fact]
    public void StartupReconcile_AcOnlyManaged_WritesAcOnly()
    {
        var store = new ProfileStore(ProfilesPath);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings { Performance = new DevicePerformanceSettings { CpuBoost = new DeviceCpuBoostSettings { Enabled = true, Ac = CpuBoostMode.EfficientAggressive } } }
        });
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();

        Assert.Equal(1, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
        Assert.Equal(CpuBoostMode.EfficientAggressive, backend.Ac.Mode);
        Assert.Equal(CpuBoostMode.Disabled, backend.Dc.Mode);
    }

    // ---- DC-only managed startup ----

    [Fact]
    public void StartupReconcile_DcOnlyManaged_WritesDcOnly()
    {
        var store = new ProfileStore(ProfilesPath);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings { Performance = new DevicePerformanceSettings { CpuBoost = new DeviceCpuBoostSettings { Enabled = true, Dc = CpuBoostMode.Disabled } } }
        });
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Aggressive) };
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();

        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(1, backend.DcWriteCount);
        Assert.Equal(CpuBoostMode.Enabled, backend.Ac.Mode);
        Assert.Equal(CpuBoostMode.Disabled, backend.Dc.Mode);
    }

    // ---- Both sides managed ----

    [Fact]
    public void StartupReconcile_BothManaged_WritesBothIndependently()
    {
        var store = new ProfileStore(ProfilesPath);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings { Performance = new DevicePerformanceSettings { CpuBoost = new DeviceCpuBoostSettings { Enabled = true, Ac = CpuBoostMode.Aggressive, Dc = CpuBoostMode.Disabled } } }
        });
        var backend = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();

        Assert.Equal(1, backend.AcWriteCount);
        Assert.Equal(1, backend.DcWriteCount);
        Assert.Equal(CpuBoostMode.Aggressive, backend.Ac.Mode);
        Assert.Equal(CpuBoostMode.Disabled, backend.Dc.Mode);
    }

    // ---- Explicit Disabled is not confused with unmanaged/null ----

    [Fact]
    public void StartupReconcile_ExplicitDisabled_IsAppliedAsManagedNotUnmanaged()
    {
        var store = new ProfileStore(ProfilesPath);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings { Performance = new DevicePerformanceSettings { CpuBoost = new DeviceCpuBoostSettings { Enabled = true, Ac = CpuBoostMode.Disabled } } }
        });
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive) };
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();

        Assert.Equal(1, backend.AcWriteCount);
        Assert.Equal(CpuBoostMode.Disabled, backend.Ac.Mode);
        Assert.Equal(CpuBoostMode.Disabled, runtime.Snapshot.AcDesired);
    }

    // ---- All seven modes round-trip through persistence ----

    [Theory]
    [InlineData(CpuBoostMode.Disabled)]
    [InlineData(CpuBoostMode.Enabled)]
    [InlineData(CpuBoostMode.Aggressive)]
    [InlineData(CpuBoostMode.EfficientEnabled)]
    [InlineData(CpuBoostMode.EfficientAggressive)]
    [InlineData(CpuBoostMode.AggressiveAtGuaranteed)]
    [InlineData(CpuBoostMode.EfficientAggressiveAtGuaranteed)]
    public void SaveAndLoad_RoundTripsEachCpuBoostMode(CpuBoostMode mode)
    {
        var store = new ProfileStore(ProfilesPath);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings { Performance = new DevicePerformanceSettings { CpuBoost = new DeviceCpuBoostSettings { Ac = mode, Dc = mode } } }
        });

        var loaded = store.Load();

        Assert.Equal(ProfileLoadStatus.Loaded, loaded.Status);
        Assert.Equal(mode, loaded.Document.Device.Performance.CpuBoost?.Ac);
        Assert.Equal(mode, loaded.Document.Device.Performance.CpuBoost?.Dc);
    }

    // ---- Profile load safety: NotFound ----

    [Fact]
    public void StartupReconcile_NotFound_BootstrapsFromWindowsAndPersists()
    {
        // A missing profiles.json (NotFound) is CanSafelyReplace=true, so it is a legitimate
        // first-run case just like an existing-but-empty CpuBoost value -- it must bootstrap, not
        // stay read-only forever.
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Enabled) };
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();

        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
        Assert.True(File.Exists(ProfilesPath));
        Assert.Equal(CpuBoostMode.Enabled, runtime.Snapshot.AcDesired);
        Assert.Equal(CpuBoostMode.Enabled, runtime.Snapshot.DcDesired);
    }

    // ---- Profile load safety: Malformed ----

    [Fact]
    public void StartupReconcile_MalformedProfile_NoCpuBoostWritesAndFilePreserved()
    {
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(ProfilesPath, "{ not valid json");
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled) };
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();

        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
        Assert.Equal("{ not valid json", File.ReadAllText(ProfilesPath));
    }

    // ---- Profile load safety: unsupported schema ----

    [Fact]
    public void StartupReconcile_UnsupportedSchema_NoCpuBoostWrites()
    {
        Directory.CreateDirectory(_testDirectory);
        var original = "{\"schemaVersion\":" + (ProfileDocument.CurrentSchemaVersion + 1) + ",\"device\":{},\"games\":{}}";
        File.WriteAllText(ProfilesPath, original);
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled) };
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();

        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(original, File.ReadAllText(ProfilesPath));
    }

    // ---- Profile load safety: read failure ----

    [Fact]
    public void StartupReconcile_ProfileReadFailure_NoCpuBoostWrites()
    {
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(ProfilesPath, """{"schemaVersion":1,"device":{},"games":{}}""");
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled) };
        var runtime = new CpuBoostRuntime(store, backend);

        using var handle = new FileStream(ProfilesPath, FileMode.Open, FileAccess.Read, FileShare.None);
        runtime.StartupReconcile();

        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
    }

    // ---- Mutation after an unreliable startup load must not overwrite the unsafe-to-replace file ----

    [Fact]
    public void Mutation_AfterMalformedStartup_FailsAndNeverWritesTheProfileOrWindows()
    {
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(ProfilesPath, "{ not valid json");
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile();

        var result = runtime.SetDeviceCpuBoostAc(CpuBoostMode.Aggressive);

        Assert.Equal(CpuBoostMutationOutcome.PersistenceFailed, result.Outcome);
        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
        // The original malformed file must survive untouched -- a mutation must never replace it
        // with a default document derived from the unreliable load.
        Assert.Equal("{ not valid json", File.ReadAllText(ProfilesPath));
    }

    [Fact]
    public void Mutation_AfterUnsupportedSchemaStartup_FailsAndNeverWritesTheProfileOrWindows()
    {
        Directory.CreateDirectory(_testDirectory);
        var original = "{\"schemaVersion\":" + (ProfileDocument.CurrentSchemaVersion + 1) + ",\"device\":{},\"games\":{}}";
        File.WriteAllText(ProfilesPath, original);
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile();

        var result = runtime.SetDeviceCpuBoostDc(CpuBoostMode.Disabled);

        Assert.Equal(CpuBoostMutationOutcome.PersistenceFailed, result.Outcome);
        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
        Assert.Equal(original, File.ReadAllText(ProfilesPath));
    }

    // ---- AC mutation ----

    [Fact]
    public void SetDeviceCpuBoostAc_PersistsAndAppliesAcOnly()
    {
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile();

        var result = runtime.SetDeviceCpuBoostAc(CpuBoostMode.Aggressive);

        Assert.True(result.Succeeded);
        Assert.Equal(1, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
        var loaded = store.Load();
        Assert.Equal(CpuBoostMode.Aggressive, loaded.Document.Device.Performance.CpuBoost?.Ac);
        Assert.Null(loaded.Document.Device.Performance.CpuBoost?.Dc);
    }

    // ---- DC mutation ----

    [Fact]
    public void SetDeviceCpuBoostDc_PersistsAndAppliesDcOnly()
    {
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile();

        var result = runtime.SetDeviceCpuBoostDc(CpuBoostMode.EfficientEnabled);

        Assert.True(result.Succeeded);
        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(1, backend.DcWriteCount);
        var loaded = store.Load();
        Assert.Equal(CpuBoostMode.EfficientEnabled, loaded.Document.Device.Performance.CpuBoost?.Dc);
        Assert.Null(loaded.Document.Device.Performance.CpuBoost?.Ac);
    }

    // ---- Persistence failure before hardware apply ----

    [Fact]
    public void Mutation_PersistenceFailure_NeverAppliesToWindowsAndKeepsPreviousDesiredState()
    {
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(store, backend);

        // NotFound -> safe/writable, previous desired state is null. This must happen BEFORE the
        // path is sabotaged below, or the mutation would exit at the _persistenceWritable guard
        // without ever reaching ProfileStore.Save() -- which would not exercise a persistence
        // failure at all.
        runtime.StartupReconcile();

        // A directory in place of the profiles.json path makes File.Move/File.WriteAllText fail
        // reliably without relying on OS-specific permission APIs.
        Directory.CreateDirectory(ProfilesPath);

        var result = runtime.SetDeviceCpuBoostAc(CpuBoostMode.Aggressive);

        Assert.Equal(CpuBoostMutationOutcome.PersistenceFailed, result.Outcome);
        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
        Assert.Null(runtime.Snapshot.AcDesired);
    }

    // ---- Hardware apply failure after persistence succeeds ----

    [Fact]
    public void Mutation_ApplyFailure_KeepsPersistedDesiredStateAndReportsFailure_AndRetriesOnNewRuntime()
    {
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { FailNextApply = true };
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile();

        var result = runtime.SetDeviceCpuBoostAc(CpuBoostMode.EfficientAggressive);

        Assert.Equal(CpuBoostMutationOutcome.ApplyFailed, result.Outcome);
        var loaded = store.Load();
        Assert.Equal(CpuBoostMode.EfficientAggressive, loaded.Document.Device.Performance.CpuBoost?.Ac);
        Assert.Equal(0, backend.AcWriteCount); // the failed attempt did not count as a successful write

        // A newly created Runtime instance (as at a later process start) can attempt the persisted
        // desired value again.
        var secondBackend = new FakeCpuBoostPowerPolicy();
        var secondRuntime = new CpuBoostRuntime(store, secondBackend);
        secondRuntime.StartupReconcile();

        Assert.Equal(1, secondBackend.AcWriteCount);
        Assert.Equal(CpuBoostMode.EfficientAggressive, secondBackend.Ac.Mode);
    }

    // ---- Unexpected current Windows value must not be silently normalized ----

    [Fact]
    public void StartupReconcile_UnexpectedCurrentWindowsValue_IsNotNormalizedOrWrittenWhenUnmanaged()
    {
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.UnknownValue(), Dc = CpuBoostSideReading.Known(CpuBoostMode.Enabled) };
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();

        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(CpuBoostReadStatus.Unknown, runtime.Snapshot.AcCurrent.Status);
        Assert.Null(runtime.Snapshot.AcCurrent.Mode);
    }

    // ---- WindowsCpuBoostPowerPolicy.ResolveApplyResult native-backend decision logic ----

    [Fact]
    public void ResolveApplyResult_AcOnlyRequested_WriteAndActivationSucceed_ReportsSucceeded()
    {
        var result = WindowsCpuBoostPowerPolicy.ResolveApplyResult(CpuBoostMode.Aggressive, 0, null, null, () => 0);

        Assert.True(result.AcSucceeded);
        Assert.True(result.DcSucceeded);
        Assert.True(result.Succeeded);
        Assert.Null(result.FailureMessage);
    }

    [Fact]
    public void ResolveApplyResult_AcOnlyRequested_WriteSucceedsButActivationFails_ReportsAcFailedNotSucceeded()
    {
        var result = WindowsCpuBoostPowerPolicy.ResolveApplyResult(CpuBoostMode.Aggressive, 0, null, null, () => 5);

        Assert.False(result.AcSucceeded);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.FailureMessage);
    }

    [Fact]
    public void ResolveApplyResult_AcOnlyRequested_WriteFails_NeverInvokesActivation()
    {
        var activateInvoked = false;
        uint Activate() { activateInvoked = true; return 0; }

        var result = WindowsCpuBoostPowerPolicy.ResolveApplyResult(CpuBoostMode.Aggressive, 87, null, null, Activate);

        Assert.False(activateInvoked);
        Assert.False(result.AcSucceeded);
        // DC was never requested, so it must remain trivially "succeeded" and never observed as a failure.
        Assert.True(result.DcSucceeded);
    }

    [Fact]
    public void ResolveApplyResult_BothRequested_AcFailsDcSucceeds_ActivationRunsAndDcSurvivesActivationSuccess()
    {
        var result = WindowsCpuBoostPowerPolicy.ResolveApplyResult(CpuBoostMode.Aggressive, 87, CpuBoostMode.Disabled, 0, () => 0);

        Assert.False(result.AcSucceeded);
        Assert.True(result.DcSucceeded);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void ResolveApplyResult_BothRequestedAndWritesSucceed_ActivationFails_BothSidesReportFailure()
    {
        var result = WindowsCpuBoostPowerPolicy.ResolveApplyResult(CpuBoostMode.Aggressive, 0, CpuBoostMode.Disabled, 0, () => 5);

        Assert.False(result.AcSucceeded);
        Assert.False(result.DcSucceeded);
        Assert.False(result.Succeeded);
    }

    // ---- Startup reconcile must not clobber a newer mutation that lands while it is applying ----

    [Fact]
    public async Task StartupReconcile_BlockedMidApply_DoesNotOverwriteANewerConcurrentMutation()
    {
        // Seed a persisted managed AC value for startup reconcile to reapply.
        var seedStore = new ProfileStore(ProfilesPath);
        var seedRuntime = new CpuBoostRuntime(seedStore, new FakeCpuBoostPowerPolicy());
        seedRuntime.StartupReconcile();
        seedRuntime.SetDeviceCpuBoostAc(CpuBoostMode.Enabled);

        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy();
        var startupEnteredApply = new ManualResetEventSlim(false);
        var releaseStartupApply = new ManualResetEventSlim(false);
        backend.OnApplyEntered = () => startupEnteredApply.Set();
        backend.ApplyGate = releaseStartupApply;

        var runtime = new CpuBoostRuntime(store, backend);

        var startupTask = Task.Run(() => runtime.StartupReconcile());
        // Deterministic: StartupReconcile holds _mutationSync and is blocked inside Apply(Enabled,
        // null) once this returns, so the mutation below is guaranteed to block acquiring the same
        // gate rather than racing to complete first.
        startupEnteredApply.Wait();

        var mutationTask = Task.Run(() => runtime.SetDeviceCpuBoostAc(CpuBoostMode.Aggressive));

        releaseStartupApply.Set();
        await startupTask;
        var mutationResult = await mutationTask;

        Assert.True(mutationResult.Succeeded);

        var loaded = store.Load();
        Assert.Equal(CpuBoostMode.Aggressive, loaded.Document.Device.Performance.CpuBoost?.Ac);
        Assert.Equal(CpuBoostSideReading.Known(CpuBoostMode.Aggressive), backend.Ac);
    }

    // ---- Concurrent AC/DC mutations must not lose either change ----

    [Fact]
    public async Task ConcurrentAcAndDcMutations_BothChangesSurviveInTheFinalPersistedDocument()
    {
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile();

        var acTask = Task.Run(() => runtime.SetDeviceCpuBoostAc(CpuBoostMode.Aggressive));
        var dcTask = Task.Run(() => runtime.SetDeviceCpuBoostDc(CpuBoostMode.Disabled));
        var acResult = await acTask;
        var dcResult = await dcTask;

        Assert.True(acResult.Succeeded);
        Assert.True(dcResult.Succeeded);

        var loaded = store.Load();
        Assert.Equal(CpuBoostMode.Aggressive, loaded.Document.Device.Performance.CpuBoost?.Ac);
        Assert.Equal(CpuBoostMode.Disabled, loaded.Document.Device.Performance.CpuBoost?.Dc);
    }

    [Fact]
    public async Task CpuBoostRuntime_initializes_and_mutates_with_no_frontend_object_ever_constructed()
    {
        // PR277 headless regression (work order section 32): CPU Boost must not become something
        // that requires WinUI. No IAddonFrontendControl/InProcessAddonFrontendControl/NamedPipe type
        // appears anywhere in this test -- only the same CpuBoostRuntime -> ProfileStore ->
        // ICpuBoostPowerPolicy chain the headless Runtime process itself uses.
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();
        var result = runtime.SetDeviceCpuBoostAc(CpuBoostMode.Aggressive);

        Assert.True(result.Succeeded);
        Assert.Equal(CpuBoostMode.Aggressive, runtime.Snapshot.AcDesired);
        Assert.Equal(1, backend.AcWriteCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
