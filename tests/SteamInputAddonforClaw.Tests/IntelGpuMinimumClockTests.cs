using System.Text.Json;
using SteamInputAddonforClaw.Hosting;
using SteamInputAddonforClaw.Profiles.Performance;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class IntelGpuMinimumClockTests
{
    private static readonly double[] SelectableClocks = [1500, 1600, 1700, 1800, 1900, 2000, 2100];

    [Fact]
    public void Available_clocks_are_sanitized_and_bounds_use_actual_driver_entries()
    {
        var selection = IntelGpuMinimumClockPolicy.SelectAvailableClocks(
            [2300, 2000, double.NaN, 0, 2100, 2100.05, -1, 2250, 1500]);

        Assert.True(selection.Available);
        Assert.Equal(new[] { 1500d, 2000d, 2100d, 2250d, 2300d }, selection.AvailableClocksMhz);
        Assert.Equal(new[] { 1500d, 2000d, 2100d }, selection.SelectableClocksMhz);
        Assert.Equal(1500, selection.SelectableMinMhz);
        Assert.Equal(2100, selection.SelectableMaxMhz);
        Assert.Equal(selection.SelectableMaxMhz, selection.RecommendedDefaultMhz);
    }

    [Fact]
    public void Missing_1500_uses_first_actual_supported_clock_above_threshold()
    {
        var selection = IntelGpuMinimumClockPolicy.SelectAvailableClocks([1000, 1475, 1525, 1600, 1650]);

        Assert.True(selection.Available);
        Assert.Equal(new[] { 1525d }, selection.SelectableClocksMhz);
        Assert.Equal(1525, selection.SelectableMinMhz);
        Assert.Equal(1525, selection.SelectableMaxMhz);
        Assert.Equal(1525, selection.RecommendedDefaultMhz);
    }

    [Fact]
    public void Different_integrated_gpu_tables_produce_dynamic_bounds_without_model_constants()
    {
        var b390 = IntelGpuMinimumClockPolicy.SelectAvailableClocks([900, 1500, 1600, 1700, 1800, 1900, 2000, 2100, 2200, 2250, 2300]);
        var arc140V = IntelGpuMinimumClockPolicy.SelectAvailableClocks([500, 800, 1300, 1525, 1625, 1725, 1825, 1925, 1975, 2025]);

        Assert.True(b390.Available);
        Assert.Equal(2200, b390.RecommendedDefaultMhz);
        Assert.True(arc140V.Available);
        Assert.Equal(1525, arc140V.SelectableMinMhz);
        Assert.Equal(1925, arc140V.RecommendedDefaultMhz);
    }

    [Fact]
    public void Invalid_or_insufficient_tables_fail_closed()
    {
        Assert.False(IntelGpuMinimumClockPolicy.SelectAvailableClocks([1500, 1600]).Available);
        Assert.False(IntelGpuMinimumClockPolicy.SelectAvailableClocks([1000, 1400, 1450, 1475]).Available);
        Assert.False(IntelGpuMinimumClockPolicy.SelectAvailableClocks([double.NaN, double.PositiveInfinity, 0, -10]).Available);
    }

    [Fact]
    public void Target_validation_accepts_only_current_driver_entries_with_small_tolerance()
    {
        Assert.True(IntelGpuMinimumClockPolicy.TryGetCanonicalTarget(SelectableClocks, 1800.05, out var canonical));
        Assert.Equal(1800, canonical);
        Assert.False(IntelGpuMinimumClockPolicy.TryGetCanonicalTarget(SelectableClocks, 1850, out _));
        Assert.False(IntelGpuMinimumClockPolicy.TryGetCanonicalTarget(SelectableClocks, 1499, out _));
        Assert.False(IntelGpuMinimumClockPolicy.TryGetCanonicalTarget(SelectableClocks, 2200, out _));
        Assert.False(IntelGpuMinimumClockPolicy.TryGetCanonicalTarget(SelectableClocks, double.NaN, out _));
    }

    [Fact]
    public void Minimum_request_preserves_current_max_and_refuses_an_explicit_conflict()
    {
        Assert.True(IntelGpuMinimumClockPolicy.TryCreateMinimumRequest(
            SelectableClocks, 1800, new(-1, 1950), out var target, out var explicitMaxRequest, out _));
        Assert.Equal(1800, target);
        Assert.Equal(new IntelGpuFrequencyRange(1800, 1950), explicitMaxRequest);

        Assert.True(IntelGpuMinimumClockPolicy.TryCreateMinimumRequest(
            SelectableClocks, 1800, new(-1, -2), out _, out var unmanagedMaxRequest, out _));
        Assert.Equal(new IntelGpuFrequencyRange(1800, -1), unmanagedMaxRequest);

        Assert.False(IntelGpuMinimumClockPolicy.TryCreateMinimumRequest(
            SelectableClocks, 1800, new(-1, 1750), out _, out _, out var failure));
        Assert.Contains("explicit maximum", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicit_max_conflict_refuses_apply_without_creating_ownership_marker()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(-1, 1750));
        using var runtime = CreateRuntime(temp.MarkerPath, fake);
        runtime.InitializeReadOnly();

        var result = runtime.ApplyMinimum(1800, "UnitTest");

        Assert.False(result.Succeeded);
        Assert.Equal(0, fake.SetCalls);
        Assert.False(File.Exists(temp.MarkerPath));
    }

    [Fact]
    public void Readback_verifies_minimum_and_preserved_max_semantics()
    {
        Assert.True(IntelGpuMinimumClockPolicy.MatchesReadback(new(1800, 1950), new(-1, 1950), new(1800.05, 1949.95)));
        Assert.False(IntelGpuMinimumClockPolicy.MatchesReadback(new(1800, 1950), new(-1, 1950), new(1700, 1950)));
        Assert.True(IntelGpuMinimumClockPolicy.MatchesReadback(new(1800, -1), new(-1, -2), new(1800, -3)));
        Assert.False(IntelGpuMinimumClockPolicy.MatchesReadback(new(1800, 1950), new(-1, 1950), new(1800, 1900)));
    }

    [Fact]
    public void Marker_is_persisted_before_first_set_and_successful_apply_keeps_it()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(-1, -1));
        using var runtime = CreateRuntime(temp.MarkerPath, fake);
        runtime.InitializeReadOnly();
        fake.BeforeSet = _ => Assert.True(File.Exists(temp.MarkerPath));

        var result = runtime.ApplyMinimum(1800, "UnitTest");

        Assert.True(result.Succeeded);
        Assert.True(result.Verified);
        Assert.True(result.OwnershipMarkerPresent);
        Assert.True(File.Exists(temp.MarkerPath));
        var marker = JsonSerializer.Deserialize<IntelGpuMinimumClockOwnershipMarker>(
            File.ReadAllText(temp.MarkerPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(marker);
        Assert.Equal(0x8086u, marker.VendorId);
        Assert.Equal(0x1234u, marker.DeviceId);
        Assert.Equal(-1, marker.OriginalMinMhz);
        Assert.Equal(new IntelGpuFrequencyRange(1800, -1), fake.LastSetRange);
    }

    [Fact]
    public void Marker_persistence_failure_prevents_native_set()
    {
        using var temp = new TemporaryDirectory();
        var parentBlocker = Path.Combine(temp.Root, "not-a-directory");
        Directory.CreateDirectory(temp.Root);
        File.WriteAllText(parentBlocker, "block");
        var fake = new FakeControl(new(-1, -1));
        using var runtime = CreateRuntime(Path.Combine(parentBlocker, "marker.json"), fake);
        runtime.InitializeReadOnly();

        var result = runtime.ApplyMinimum(1800, "UnitTest");

        Assert.False(result.Succeeded);
        Assert.Equal(0, fake.SetCalls);
        Assert.False(File.Exists(Path.Combine(parentBlocker, "marker.json")));
    }

    [Fact]
    public void Successful_set_with_readback_failure_retains_marker()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(-1, -1)) { ThrowOnGetCall = 2 };
        using var runtime = CreateRuntime(temp.MarkerPath, fake);
        runtime.InitializeReadOnly();

        var result = runtime.ApplyMinimum(1800, "UnitTest");

        Assert.False(result.Succeeded);
        Assert.Equal(0u, result.NativeSetResult);
        Assert.Equal(1, fake.SetCalls);
        Assert.True(result.OwnershipMarkerPresent);
        Assert.True(File.Exists(temp.MarkerPath));
    }

    [Fact]
    public void Readback_mismatch_after_successful_set_retains_marker()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(-1, -1))
        {
            SetRangeReadback = requested => requested with { Min = requested.Min - 100 }
        };
        using var runtime = CreateRuntime(temp.MarkerPath, fake);
        runtime.InitializeReadOnly();

        var result = runtime.ApplyMinimum(1800, "UnitTest");

        Assert.False(result.Succeeded);
        Assert.False(result.Verified);
        Assert.Equal(1700, result.ReadbackRange!.Value.Min);
        Assert.True(File.Exists(temp.MarkerPath));
    }

    [Fact]
    public void Failed_set_retains_conservative_marker_for_later_restore()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(-1, -1)) { SetResult = 0x40000001 };
        using var runtime = CreateRuntime(temp.MarkerPath, fake);
        runtime.InitializeReadOnly();

        var result = runtime.ApplyMinimum(1800, "UnitTest");

        Assert.False(result.Succeeded);
        Assert.Equal(0x40000001u, result.NativeSetResult);
        Assert.True(File.Exists(temp.MarkerPath));
        Assert.True(result.OwnershipMarkerPresent);
    }

    [Fact]
    public void Verified_restore_deletes_marker_and_preserves_current_max()
    {
        using var temp = new TemporaryDirectory();
        WriteMarker(temp.MarkerPath, originalMinMhz: -1);
        var fake = new FakeControl(new(1800, 1950));
        using var runtime = CreateRuntime(temp.MarkerPath, fake);
        runtime.InitializeReadOnly();

        var result = runtime.RestoreOriginalMinimum("UnitTest");

        Assert.True(result.Succeeded);
        Assert.True(result.Verified);
        Assert.False(File.Exists(temp.MarkerPath));
        Assert.False(result.OwnershipMarkerPresent);
        Assert.Equal(new IntelGpuFrequencyRange(-1, 1950), fake.LastSetRange);
    }

    [Fact]
    public void Verified_restore_restores_an_explicit_original_minimum_exactly()
    {
        using var temp = new TemporaryDirectory();
        WriteMarker(temp.MarkerPath, originalMinMhz: 950);
        var fake = new FakeControl(new(1800, -2));
        using var runtime = CreateRuntime(temp.MarkerPath, fake);
        runtime.InitializeReadOnly();

        var result = runtime.RestoreOriginalMinimum("UnitTest");

        Assert.True(result.Verified);
        Assert.Equal(new IntelGpuFrequencyRange(950, -1), fake.LastSetRange);
        Assert.False(File.Exists(temp.MarkerPath));
    }

    [Fact]
    public void Failed_restore_keeps_marker_for_retry()
    {
        using var temp = new TemporaryDirectory();
        WriteMarker(temp.MarkerPath, originalMinMhz: 900);
        var fake = new FakeControl(new(1800, -1)) { SetResult = 0x40000001 };
        using var runtime = CreateRuntime(temp.MarkerPath, fake);
        runtime.InitializeReadOnly();

        var result = runtime.RestoreOriginalMinimum("UnitTest");

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(temp.MarkerPath));
        Assert.True(result.OwnershipMarkerPresent);
    }

    [Fact]
    public void New_runtime_instance_adopts_original_baseline_without_overwriting_it()
    {
        using var temp = new TemporaryDirectory();
        var firstControl = new FakeControl(new(-1, -1));
        using (var firstRuntime = CreateRuntime(temp.MarkerPath, firstControl))
        {
            firstRuntime.InitializeReadOnly();
            Assert.True(firstRuntime.ApplyMinimum(1800, "FirstProcess").Succeeded);
        }

        var nextControl = new FakeControl(new(1800, 1950));
        using var nextRuntime = CreateRuntime(temp.MarkerPath, nextControl);
        var capability = nextRuntime.InitializeReadOnly();
        var result = nextRuntime.RestoreOriginalMinimum("ReplacementProcess");

        Assert.True(capability.OwnershipMarkerPresent);
        Assert.True(result.Verified);
        Assert.Equal(new IntelGpuFrequencyRange(-1, 1950), nextControl.LastSetRange);
        Assert.False(File.Exists(temp.MarkerPath));
    }

    [Fact]
    public void Startup_discovery_is_read_only_and_does_not_create_ownership_marker()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(-1, -1));
        using var runtime = CreateRuntime(temp.MarkerPath, fake);

        var capability = runtime.InitializeReadOnly();

        Assert.True(capability.Available);
        Assert.Equal(1, fake.InitializeCalls);
        Assert.Equal(0, fake.SetCalls);
        Assert.False(File.Exists(temp.MarkerPath));
        Assert.False(capability.OwnershipMarkerPresent);
    }

    [Fact]
    public void Capability_without_control_permission_fails_closed_without_a_write()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(-1, -1))
        {
            Capability = CreateNativeCapability(SelectableClocks) with { CanControl = false }
        };
        using var runtime = CreateRuntime(temp.MarkerPath, fake);
        var capability = runtime.InitializeReadOnly();

        var result = runtime.ApplyMinimum(1800, "UnitTest");

        Assert.False(capability.Available);
        Assert.False(result.Succeeded);
        Assert.Equal(0, fake.SetCalls);
        Assert.False(File.Exists(temp.MarkerPath));
    }

    [Fact]
    public void Invalid_hardware_frequency_properties_make_capability_unavailable()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(-1, -1))
        {
            Capability = CreateNativeCapability(SelectableClocks) with { HardwareMaxMhz = double.NaN }
        };
        using var runtime = CreateRuntime(temp.MarkerPath, fake);

        Assert.False(runtime.InitializeReadOnly().Available);
    }

    [Fact]
    public async Task Host_startup_discovery_does_not_write_gpu_range_marker_or_profile_settings()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(-1, -1));
        var host = new AddonProcessHost(
            testOnlyDataRoot: temp.Root,
            testIntelGpuMinimumClockControlFactory: _ => fake);

        host.InitializeIntelGpuMinimumClockReadOnlyForStartup();

        Assert.Equal(1, fake.InitializeCalls);
        Assert.Equal(0, fake.SetCalls);
        Assert.False(File.Exists(temp.MarkerPath));
        Assert.False(File.Exists(Path.Combine(temp.Root, "profiles.json")));
        await host.DisposeAsync();
    }

    [Theory]
    [InlineData(IntelGpuMinimumClockRuntime.DeviceLostResult)]
    [InlineData(IntelGpuMinimumClockRuntime.DeviceUnavailableResult)]
    public void Session_reinitialize_is_bounded_to_one_discovery_for_device_loss_results(uint resultCode)
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(-1, -1));
        using var runtime = CreateRuntime(temp.MarkerPath, fake);
        runtime.InitializeReadOnly();
        fake.ReinitializedCapability = CreateNativeCapability([1400, 1525, 1625, 1725, 1825]);

        Assert.True(runtime.TryReinitializeSession(resultCode));

        Assert.Equal(1, fake.ReinitializeCalls);
        Assert.Equal(1525, runtime.Capability!.SelectableMinMhz);
        Assert.Equal(0, fake.SetCalls);
    }

    [Fact]
    public void Session_is_not_reinitialized_for_unrelated_native_results()
    {
        using var temp = new TemporaryDirectory();
        var fake = new FakeControl(new(-1, -1));
        using var runtime = CreateRuntime(temp.MarkerPath, fake);
        runtime.InitializeReadOnly();

        Assert.False(runtime.TryReinitializeSession(0x40000001));

        Assert.Equal(0, fake.ReinitializeCalls);
    }

    [Fact]
    public void Production_igcl_frequency_abi_and_available_clock_delegate_match_x64_header()
        => Assert.True(IntelGpuMinimumClockControl.NativeAbiIsExpectedForTests());

    private static IntelGpuMinimumClockRuntime CreateRuntime(string markerPath, FakeControl control) => new(control, markerPath);

    private static IntelGpuMinimumClockNativeCapability CreateNativeCapability(IReadOnlyList<double> clocks) => new(
        true, null, "Intel Integrated GPU", 0x8086, 0x1234, true, 300, 2300, clocks);

    private static void WriteMarker(string path, double originalMinMhz)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new IntelGpuMinimumClockOwnershipMarker(0x8086, 0x1234, originalMinMhz)));
    }

    private sealed class FakeControl(IntelGpuFrequencyRange currentRange) : IIntelGpuMinimumClockControl
    {
        private int _getCalls;
        private IntelGpuFrequencyRange _currentRange = currentRange;

        public IntelGpuMinimumClockNativeCapability Capability { get; set; } = CreateNativeCapability(SelectableClocks);
        public IntelGpuMinimumClockNativeCapability? ReinitializedCapability { get; set; }
        public int InitializeCalls { get; private set; }
        public int ReinitializeCalls { get; private set; }
        public int SetCalls { get; private set; }
        public int? ThrowOnGetCall { get; set; }
        public uint SetResult { get; set; }
        public IntelGpuFrequencyRange? LastSetRange { get; private set; }
        public Func<IntelGpuFrequencyRange, IntelGpuFrequencyRange>? SetRangeReadback { get; set; }
        public Action<IntelGpuFrequencyRange>? BeforeSet { get; set; }

        public IntelGpuMinimumClockNativeCapability Initialize()
        {
            InitializeCalls++;
            return Capability;
        }

        public IntelGpuMinimumClockNativeCapability Reinitialize()
        {
            ReinitializeCalls++;
            return Capability = ReinitializedCapability ?? Capability;
        }

        public IntelGpuFrequencyRange GetRange()
        {
            _getCalls++;
            if (_getCalls == ThrowOnGetCall) throw new IgclMinimumClockException("ctlFrequencyGetRange", 0x40000027);
            return _currentRange;
        }

        public uint SetRange(IntelGpuFrequencyRange range)
        {
            SetCalls++;
            LastSetRange = range;
            BeforeSet?.Invoke(range);
            if (SetResult == 0) _currentRange = SetRangeReadback?.Invoke(range) ?? range;
            return SetResult;
        }

        public void Dispose() { }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), $"IntelGpuMinimumClock-{Guid.NewGuid():N}");
        internal string MarkerPath => Path.Combine(Root, "intel-gpu-minimum-clock-ownership.json");

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
