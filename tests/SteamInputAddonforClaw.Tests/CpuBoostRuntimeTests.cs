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
    public int ReadCount { get; private set; }
    public bool FailNextApply { get; set; }
    public bool FailRead { get; set; }

    /// <summary>Invoked synchronously at the start of every <see cref="Apply"/> call, before any
    /// gate wait -- lets a test know precisely when a caller has entered Apply.</summary>
    public Action? OnApplyEntered { get; set; }

    /// <summary>When set, the next <see cref="Apply"/> call blocks on this gate before doing
    /// anything else, then clears the field so only that one call blocks.</summary>
    public ManualResetEventSlim? ApplyGate { get; set; }

    public CpuBoostSystemState Read()
    {
        ReadCount++;
        return FailRead ? CpuBoostSystemState.Failure("simulated read failure") : new CpuBoostSystemState(true, Ac, Dc, null);
    }

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

[Collection("AppLog")]
public sealed class CpuBoostRuntimeTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.Tests.{Guid.NewGuid():N}");

    private string ProfilesPath => Path.Combine(_testDirectory, "profiles.json");

    // ---- Uninitialized baseline: do not invent or normalize Windows values ----

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
        // First-run bootstrap policy is "read AC/DC once and adopt it" -- the successful read that
        // established the baseline must be reused for the snapshot, not re-read a second time.
        Assert.Equal(1, backend.ReadCount);
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
            Device = new DeviceSettings { Performance = new DevicePerformanceSettings { CpuBoost = new DeviceCpuBoostSettings { Enabled = false, Ac = CpuBoostMode.Aggressive, Dc = CpuBoostMode.Disabled } } }
        });
        var backend = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile();

        var result = runtime.SetDeviceCpuBoostAc(CpuBoostMode.EfficientAggressive);

        Assert.True(result.Succeeded);
        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
        Assert.Equal(CpuBoostMode.EfficientAggressive, store.Load().Document.Device.Performance.CpuBoost?.Ac);
        Assert.Equal(CpuBoostMode.Disabled, store.Load().Document.Device.Performance.CpuBoost?.Dc);
    }

    [Fact]
    public void PreToggle_Pr276Document_WithNoEnabledProperty_LoadsAsEnabledTrueAndApplies()
    {
        // Review fix (MAJOR): a PR276-era schema-v1 document persisted ac/dc with no enabled
        // property at all (the property didn't exist yet). The missing property must deserialize
        // as ON -- preserving that document's previously-active behavior -- not silently become
        // Device CPU Boost OFF merely because this PR added the property additively.
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(ProfilesPath, "{\"schemaVersion\":1,\"device\":{\"performance\":{\"cpuBoost\":{\"ac\":\"aggressive\",\"dc\":\"disabled\"}}},\"games\":{}}");
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();

        Assert.True(runtime.Snapshot.Enabled);
        Assert.Equal(1, backend.AcWriteCount);
        Assert.Equal(1, backend.DcWriteCount);
        Assert.Equal(CpuBoostMode.Aggressive, backend.Ac.Mode);
        Assert.Equal(CpuBoostMode.Disabled, backend.Dc.Mode);
    }

    [Fact]
    public void SetDeviceCpuBoostEnabled_TrueWithoutAnyPersistedBaseline_ReadsWindowsFirst()
    {
        // Review fix (MAJOR): enabling an uninitialized Device CPU Boost (first-run bootstrap never
        // succeeded) must obtain concrete AC/DC values before committing Enabled=true -- never an
        // enabled-but-null/null document.
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy(); // Ac/Dc default Unavailable -> bootstrap fails
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile();
        Assert.False(store.Load().Document.Device.Performance.CpuBoost is { Ac: not null, Dc: not null });

        var result = runtime.SetDeviceCpuBoostEnabled(true);

        Assert.False(result.Succeeded);
        Assert.Equal(CpuBoostMutationOutcome.ApplyFailed, result.Outcome);
        Assert.Null(store.Load().Document.Device.Performance.CpuBoost);
    }

    [Fact]
    public void SetDeviceCpuBoostEnabled_TrueWithoutBaseline_AdoptsWindowsWhenReadable()
    {
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy(); // bootstrap fails at StartupReconcile time
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile();

        // Windows becomes readable by the time the user tries to enable it.
        backend.Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive);
        backend.Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled);

        var result = runtime.SetDeviceCpuBoostEnabled(true);

        Assert.True(result.Succeeded);
        var loaded = store.Load();
        Assert.True(loaded.Document.Device.Performance.CpuBoost?.Enabled);
        Assert.Equal(CpuBoostMode.Aggressive, loaded.Document.Device.Performance.CpuBoost?.Ac);
        Assert.Equal(CpuBoostMode.Disabled, loaded.Document.Device.Performance.CpuBoost?.Dc);
    }

    // ---- Incomplete legacy/partial baseline at startup: completed from Windows, not applied as a
    // partial policy (current product policy section 3.4) ----

    [Fact]
    public void StartupReconcile_IncompleteBaseline_AcOnly_CompletesDcFromWindowsAndPreservesAc()
    {
        // The already-known Ac side must be preserved (not re-adopted from Windows), while the
        // missing Dc side is completed from the current Windows read.
        var store = new ProfileStore(ProfilesPath);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings { Performance = new DevicePerformanceSettings { CpuBoost = new DeviceCpuBoostSettings { Enabled = true, Ac = CpuBoostMode.EfficientAggressive } } }
        });
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();

        Assert.Equal(CpuBoostMode.EfficientAggressive, runtime.Snapshot.AcDesired);
        Assert.Equal(CpuBoostMode.Disabled, runtime.Snapshot.DcDesired);
        var loaded = store.Load();
        Assert.Equal(CpuBoostMode.EfficientAggressive, loaded.Document.Device.Performance.CpuBoost?.Ac);
        Assert.Equal(CpuBoostMode.Disabled, loaded.Document.Device.Performance.CpuBoost?.Dc);

        // The persisted AC (EfficientAggressive) differs from the current Windows AC (Enabled): an
        // enabled Device policy must reconcile Windows to the now-complete persisted baseline, not
        // leave the stale effective Windows value in place for the whole session.
        Assert.Equal(1, backend.AcWriteCount);
        Assert.Equal(CpuBoostSideReading.Known(CpuBoostMode.EfficientAggressive), backend.Ac);
    }

    [Fact]
    public void StartupReconcile_IncompleteBaseline_PreservesUnknownExtensionDataAcrossCompletion()
    {
        // An incomplete legacy document with an unknown/future field must survive baseline
        // completion -- TryCompleteBaseline must rebuild from the existing record, not construct a
        // fresh DeviceCpuBoostSettings that silently drops PR275's additive ExtensionData contract.
        var path = ProfilesPath;
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(path, """{"schemaVersion":1,"device":{"performance":{"cpuBoost":{"enabled":true,"ac":"efficientAggressive","futureCpuBoostField":42}},"display":{}},"games":{}}""");
        var store = new ProfileStore(path);
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();

        Assert.Contains("futureCpuBoostField", File.ReadAllText(path));
    }

    [Fact]
    public void StartupReconcile_IncompleteBaseline_DcOnly_CompletesAcFromWindowsAndPreservesDc()
    {
        var store = new ProfileStore(ProfilesPath);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings { Performance = new DevicePerformanceSettings { CpuBoost = new DeviceCpuBoostSettings { Enabled = true, Dc = CpuBoostMode.Disabled } } }
        });
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Aggressive) };
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();

        Assert.Equal(CpuBoostMode.Enabled, runtime.Snapshot.AcDesired);
        Assert.Equal(CpuBoostMode.Disabled, runtime.Snapshot.DcDesired);
        var loaded = store.Load();
        Assert.Equal(CpuBoostMode.Enabled, loaded.Document.Device.Performance.CpuBoost?.Ac);
        Assert.Equal(CpuBoostMode.Disabled, loaded.Document.Device.Performance.CpuBoost?.Dc);

        // The persisted DC (Disabled) differs from the current Windows DC (Aggressive): reconcile.
        Assert.Equal(1, backend.DcWriteCount);
        Assert.Equal(CpuBoostSideReading.Known(CpuBoostMode.Disabled), backend.Dc);
    }

    [Fact]
    public void StartupReconcile_IncompleteBaseline_WindowsUnreadable_LeavesCpuBoostIncomplete()
    {
        var store = new ProfileStore(ProfilesPath);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings { Performance = new DevicePerformanceSettings { CpuBoost = new DeviceCpuBoostSettings { Enabled = true, Ac = CpuBoostMode.EfficientAggressive } } }
        });
        var backend = new FakeCpuBoostPowerPolicy { FailRead = true };
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();

        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
        // Not persisted/normalized as complete -- the original incomplete document survives untouched.
        Assert.Equal(CpuBoostMode.EfficientAggressive, store.Load().Document.Device.Performance.CpuBoost?.Ac);
        Assert.Null(store.Load().Document.Device.Performance.CpuBoost?.Dc);
    }

    // ---- Both sides initialized ----

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

    // ---- Explicit Disabled is not confused with an uninitialized/incomplete side ----

    [Fact]
    public void StartupReconcile_ExplicitDisabled_IsAppliedAsAConcreteValueNotUninitialized()
    {
        var store = new ProfileStore(ProfilesPath);
        store.Save(new ProfileDocument
        {
            Device = new DeviceSettings { Performance = new DevicePerformanceSettings { CpuBoost = new DeviceCpuBoostSettings { Enabled = true, Ac = CpuBoostMode.Disabled, Dc = CpuBoostMode.Disabled } } }
        });
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Aggressive), Dc = CpuBoostSideReading.Known(CpuBoostMode.Aggressive) };
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
        // Already-complete baseline (established during StartupReconcile's first-run bootstrap): a
        // single-side mutation must still write/change only the requested side.
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile();

        var result = runtime.SetDeviceCpuBoostAc(CpuBoostMode.Aggressive);

        Assert.True(result.Succeeded);
        Assert.Equal(1, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
        var loaded = store.Load();
        Assert.Equal(CpuBoostMode.Aggressive, loaded.Document.Device.Performance.CpuBoost?.Ac);
        Assert.Equal(CpuBoostMode.Disabled, loaded.Document.Device.Performance.CpuBoost?.Dc);
    }

    // ---- DC mutation ----

    [Fact]
    public void SetDeviceCpuBoostDc_PersistsAndAppliesDcOnly()
    {
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile();

        var result = runtime.SetDeviceCpuBoostDc(CpuBoostMode.EfficientEnabled);

        Assert.True(result.Succeeded);
        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(1, backend.DcWriteCount);
        var loaded = store.Load();
        Assert.Equal(CpuBoostMode.EfficientEnabled, loaded.Document.Device.Performance.CpuBoost?.Dc);
        Assert.Equal(CpuBoostMode.Enabled, loaded.Document.Device.Performance.CpuBoost?.Ac);
    }

    // ---- Fresh single-side mutation completes the baseline from Windows (current product policy
    // section 3.2: no persisted CpuBoost yet, requesting one side must never persist Enabled=true
    // with the other side left null) ----

    [Fact]
    public void SetDeviceCpuBoostAc_NoBaseline_CompletesDcFromWindowsAndWritesAcOnly()
    {
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy(); // Windows unreadable at startup -> bootstrap fails, no baseline persisted yet
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile();
        Assert.Null(store.Load().Document.Device.Performance.CpuBoost);

        // Windows becomes readable by the time the user requests the AC mutation.
        backend.Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled);
        backend.Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled);

        var result = runtime.SetDeviceCpuBoostAc(CpuBoostMode.Aggressive);

        Assert.True(result.Succeeded);
        Assert.Equal(1, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
        var loaded = store.Load();
        Assert.True(loaded.Document.Device.Performance.CpuBoost?.Enabled);
        Assert.Equal(CpuBoostMode.Aggressive, loaded.Document.Device.Performance.CpuBoost?.Ac);
        Assert.Equal(CpuBoostMode.Disabled, loaded.Document.Device.Performance.CpuBoost?.Dc);
    }

    [Fact]
    public void SetDeviceCpuBoostDc_NoBaseline_CompletesAcFromWindowsAndWritesDcOnly()
    {
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile();
        Assert.Null(store.Load().Document.Device.Performance.CpuBoost);

        backend.Ac = CpuBoostSideReading.Known(CpuBoostMode.EfficientEnabled);
        backend.Dc = CpuBoostSideReading.Known(CpuBoostMode.Enabled);

        var result = runtime.SetDeviceCpuBoostDc(CpuBoostMode.Disabled);

        Assert.True(result.Succeeded);
        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(1, backend.DcWriteCount);
        var loaded = store.Load();
        Assert.True(loaded.Document.Device.Performance.CpuBoost?.Enabled);
        Assert.Equal(CpuBoostMode.EfficientEnabled, loaded.Document.Device.Performance.CpuBoost?.Ac);
        Assert.Equal(CpuBoostMode.Disabled, loaded.Document.Device.Performance.CpuBoost?.Dc);
    }

    [Fact]
    public void SetDeviceCpuBoostAc_NoBaselineAndWindowsUnreadable_FailsWithoutPartialPersistenceOrWrites()
    {
        // Current product policy section 3.3: if the missing side cannot be established from
        // Windows, the mutation must fail closed -- zero persistence, zero Windows writes, and the
        // previous (here: absent) authoritative state remains untouched.
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy(); // Ac/Dc default Unavailable -> baseline cannot be completed
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile(); // NotFound -> writable, but bootstrap itself also fails to read Windows

        var result = runtime.SetDeviceCpuBoostAc(CpuBoostMode.Aggressive);

        // ApplyFailed, not PersistenceFailed: no ProfileStore.Save() was ever attempted here, so
        // PersistenceFailed's specific "persistence failed before any Windows write" contract would
        // be untruthful for a Windows-read/initialization failure.
        Assert.Equal(CpuBoostMutationOutcome.ApplyFailed, result.Outcome);
        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
        Assert.Null(store.Load().Document.Device.Performance.CpuBoost);
        Assert.Null(runtime.Snapshot.AcDesired);
    }

    // ---- Persistence failure before hardware apply ----

    [Fact]
    public void Mutation_PersistenceFailure_NeverAppliesToWindowsAndKeepsPreviousDesiredState()
    {
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = new CpuBoostRuntime(store, backend);

        // NotFound -> safe/writable; StartupReconcile's own first-run bootstrap already establishes
        // a complete baseline here. This must happen BEFORE the path is sabotaged below, or the
        // mutation would exit at the _persistenceWritable guard without ever reaching
        // ProfileStore.Save() -- which would not exercise a persistence failure at all.
        runtime.StartupReconcile();

        // A directory in place of the profiles.json path makes File.Move/File.WriteAllText fail
        // reliably without relying on OS-specific permission APIs. The bootstrap above already
        // created the file, so it must be removed first before a directory can take its place.
        File.Delete(ProfilesPath);
        Directory.CreateDirectory(ProfilesPath);

        var result = runtime.SetDeviceCpuBoostAc(CpuBoostMode.Aggressive);

        Assert.Equal(CpuBoostMutationOutcome.PersistenceFailed, result.Outcome);
        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
        Assert.Equal(CpuBoostMode.Enabled, runtime.Snapshot.AcDesired);
    }

    // ---- Hardware apply failure after persistence succeeds ----

    [Fact]
    public void Mutation_ApplyFailure_KeepsPersistedDesiredStateAndReportsFailure_AndRetriesOnNewRuntime()
    {
        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled), FailNextApply = true };
        var runtime = new CpuBoostRuntime(store, backend);
        runtime.StartupReconcile();

        var result = runtime.SetDeviceCpuBoostAc(CpuBoostMode.EfficientAggressive);

        Assert.Equal(CpuBoostMutationOutcome.ApplyFailed, result.Outcome);
        var loaded = store.Load();
        Assert.Equal(CpuBoostMode.EfficientAggressive, loaded.Document.Device.Performance.CpuBoost?.Ac);
        Assert.Equal(0, backend.AcWriteCount); // the failed attempt did not count as a successful write

        // A newly created Runtime instance (as at a later process start) can attempt the persisted
        // desired value again.
        var secondBackend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var secondRuntime = new CpuBoostRuntime(store, secondBackend);
        secondRuntime.StartupReconcile();

        Assert.Equal(1, secondBackend.AcWriteCount);
        Assert.Equal(CpuBoostMode.EfficientAggressive, secondBackend.Ac.Mode);
    }

    // ---- Unexpected current Windows value must not be silently normalized ----

    [Fact]
    public void StartupReconcile_UnexpectedCurrentWindowsValue_IsNotNormalizedOrWrittenWhenUninitialized()
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
        // Seed a persisted complete AC/DC baseline for startup reconcile to reapply.
        var seedStore = new ProfileStore(ProfilesPath);
        var seedRuntime = new CpuBoostRuntime(seedStore, new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) });
        seedRuntime.StartupReconcile();
        seedRuntime.SetDeviceCpuBoostAc(CpuBoostMode.Enabled);

        var store = new ProfileStore(ProfilesPath);
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
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
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
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
        var backend = new FakeCpuBoostPowerPolicy { Ac = CpuBoostSideReading.Known(CpuBoostMode.Enabled), Dc = CpuBoostSideReading.Known(CpuBoostMode.Disabled) };
        var runtime = new CpuBoostRuntime(store, backend);

        runtime.StartupReconcile();
        var result = runtime.SetDeviceCpuBoostAc(CpuBoostMode.Aggressive);

        Assert.True(result.Succeeded);
        Assert.Equal(CpuBoostMode.Aggressive, runtime.Snapshot.AcDesired);
        Assert.Equal(1, backend.AcWriteCount);
    }

    [Fact]
    public void StartupReconcile_EnabledGameWinsEvenWhenDeviceCpuIsOff()
    {
        SaveProfile(deviceEnabled: false, gameAppId: 123, gameEnabled: true);
        var backend = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(new ProfileStore(ProfilesPath), backend);

        runtime.StartupReconcile(123);

        Assert.Equal(CpuBoostMode.Aggressive, backend.Ac.Mode);
        Assert.Equal(CpuBoostMode.EfficientEnabled, backend.Dc.Mode);
        Assert.False(runtime.Snapshot.Enabled);
        Assert.Equal(CpuBoostMode.Enabled, runtime.Snapshot.AcDesired);
        Assert.Equal(CpuBoostMode.Disabled, runtime.Snapshot.DcDesired);
    }

    [Fact]
    public void Reconcile_MissingOrDisabledGameFallsBackToEnabledDevice()
    {
        SaveProfile(deviceEnabled: true, gameAppId: 123, gameEnabled: false);
        var backend = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(new ProfileStore(ProfilesPath), backend);

        runtime.StartupReconcile(123);

        Assert.Equal(CpuBoostMode.Enabled, backend.Ac.Mode);
        Assert.Equal(CpuBoostMode.Disabled, backend.Dc.Mode);
    }

    [Fact]
    public void Reconcile_DeviceOffWithoutGameDoesNotWrite()
    {
        SaveProfile(deviceEnabled: false, gameAppId: null, gameEnabled: false);
        var backend = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(new ProfileStore(ProfilesPath), backend);

        runtime.StartupReconcile(123);

        Assert.Equal(0, backend.AcWriteCount);
        Assert.Equal(0, backend.DcWriteCount);
    }

    [Fact]
    public void Reconcile_DirectGameSwitchAndExitResolveCurrentActualAppId()
    {
        SaveProfile(deviceEnabled: true, gameAppId: 123, gameEnabled: true, secondGameAppId: 456);
        var backend = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(new ProfileStore(ProfilesPath), backend);

        runtime.StartupReconcile(123);
        runtime.Reconcile(456);
        Assert.Equal(CpuBoostMode.Disabled, backend.Ac.Mode);
        runtime.Reconcile(0);
        Assert.Equal(CpuBoostMode.Enabled, backend.Ac.Mode);
    }

    [Fact]
    public void DeviceMutationWhileGameIsActive_PersistsDeviceValueButKeepsGameApplied()
    {
        SaveProfile(deviceEnabled: true, gameAppId: 123, gameEnabled: true);
        var backend = new FakeCpuBoostPowerPolicy();
        var runtime = new CpuBoostRuntime(new ProfileStore(ProfilesPath), backend);
        runtime.SetActualAppIdSource(() => 123);
        runtime.StartupReconcile(123);

        Assert.True(runtime.SetDeviceCpuBoostAc(CpuBoostMode.EfficientAggressive).Succeeded);

        Assert.Equal(CpuBoostMode.Aggressive, backend.Ac.Mode);
        Assert.Equal(CpuBoostMode.EfficientAggressive, new ProfileStore(ProfilesPath).Load().Document.Device.Performance.CpuBoost!.Ac);
    }

    private void SaveProfile(bool deviceEnabled, uint? gameAppId, bool gameEnabled, uint? secondGameAppId = null)
    {
        var games = new Dictionary<string, GameProfile>();
        if (gameAppId is { } appId)
            games[appId.ToString()] = new GameProfile { Enabled = gameEnabled, Performance = new() { CpuBoost = new() { Ac = CpuBoostMode.Aggressive, Dc = CpuBoostMode.EfficientEnabled }, Tdp = new() { Ac = new() { Pl1Watts = 20, Pl2Watts = 22 }, Dc = new() { Pl1Watts = 20, Pl2Watts = 22 } } } };
        if (secondGameAppId is { } second)
            games[second.ToString()] = new GameProfile { Enabled = true, Performance = new() { CpuBoost = new() { Ac = CpuBoostMode.Disabled, Dc = CpuBoostMode.EfficientAggressive }, Tdp = new() { Ac = new() { Pl1Watts = 20, Pl2Watts = 22 }, Dc = new() { Pl1Watts = 20, Pl2Watts = 22 } } } };
        new ProfileStore(ProfilesPath).Save(new ProfileDocument
        {
            Device = new() { Performance = new() { CpuBoost = new() { Enabled = deviceEnabled, Ac = CpuBoostMode.Enabled, Dc = CpuBoostMode.Disabled } } },
            Games = games
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
