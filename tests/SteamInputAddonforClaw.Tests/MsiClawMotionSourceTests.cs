using System.Diagnostics;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;
using SteamInputAddonforClaw.Hosting;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class MsiClawMotionSourceTests
{
    private const string MotionType = "E83AF229-8640-4D18-A213-E22675EBB2C3";
    private const string A2VmGyroDeviceId = @"\\?\ACPI#INTC0AC2#VID_8087&PID_0AC2#0";

    [Fact]
    public void Motion_readers_are_off_without_healthy_owned_Steam_Deck_presentation()
    {
        Assert.False(AddonProcessHost.ShouldRunMotionReaders(false, AddonPresentationKind.SteamDeck, false));
        Assert.False(AddonProcessHost.ShouldRunMotionReaders(true, AddonPresentationKind.Xbox360, false));
        Assert.False(AddonProcessHost.ShouldRunMotionReaders(true, null, false));
        Assert.False(AddonProcessHost.ShouldRunMotionReaders(true, AddonPresentationKind.SteamDeck, true));
    }

    [Fact]
    public void Bpm_only_Steam_Deck_presentation_runs_motion_readers_without_a_running_game()
    {
        // BPM has RunningAppId == 0; the committed Steam Deck presentation is the activation fact.
        Assert.True(AddonProcessHost.ShouldRunMotionReaders(true, AddonPresentationKind.SteamDeck, false));
    }

    [Fact]
    public void Active_Steam_game_Steam_Deck_presentation_runs_motion_readers()
    {
        Assert.True(AddonProcessHost.ShouldRunMotionReaders(true, AddonPresentationKind.SteamDeck, false));
    }

    [Fact]
    public void Transition_back_to_Xbox360_disables_motion_readers()
    {
        Assert.False(AddonProcessHost.ShouldRunMotionReaders(true, AddonPresentationKind.Xbox360, false));
    }

    [Theory]
    [InlineData("msi.claw.a2vm.7")]
    [InlineData("msi.claw.a2vm.8")]
    public void A2VM_selects_the_device_matched_WinRT_gyro_and_Min_legacy_accelerometer(string modelId)
    {
        var gyro = WinRtGyro();
        var accel = Legacy("Physical Accelerometer", "{71000000-0000-0000-0000-000000000001}", "Min");

        var selected = MsiClawMotionSource.TrySelectSources(
            new HandheldDeviceModelId(modelId), [gyro, accel], out var actualGyro, out var actualAccel, out var reason);

        Assert.True(selected);
        Assert.Same(gyro, actualGyro);
        Assert.Same(accel, actualAccel);
        Assert.Equal("A2VmWinRtGyrometerAndLegacyAccelerometer", reason);
    }

    [Fact]
    public void CG3EM_selects_two_distinct_legacy_sensor_ids_and_deduplicates_enumeration_evidence()
    {
        var gyro = Legacy("Physical Gyrometer", "{76000001-0012-0002-0000-000000000001}");
        var accel = Legacy("Physical Accelerometer", "{73000001-0012-0002-0000-000000000001}");
        var duplicateGyro = gyro with { SelectionReason = "Second enumeration of same sensor" };

        var selected = MsiClawMotionSource.TrySelectSources(
            new HandheldDeviceModelId("msi.claw.cg3em"), [gyro, accel, duplicateGyro],
            out var actualGyro, out var actualAccel, out var reason);

        Assert.True(selected);
        Assert.Equal(gyro.SensorId, actualGyro?.SensorId);
        Assert.Equal(accel.SensorId, actualAccel?.SensorId);
        Assert.Equal("Cg3EmSeparateLegacyGyrometerAndAccelerometer", reason);
    }

    [Fact]
    public void Ambiguous_or_unverified_sensor_roles_fail_closed()
    {
        var gyro = Legacy("Physical Gyrometer", "{76000001-0012-0002-0000-000000000001}");
        var accel = Legacy("Physical Accelerometer", "{73000001-0012-0002-0000-000000000001}");

        Assert.False(MsiClawMotionSource.TrySelectSources(
            new HandheldDeviceModelId("msi.claw.cg3em"), [gyro, accel, Legacy("Physical Gyrometer", "{76000001-0012-0002-0000-000000000002}")],
            out _, out _, out var ambiguousReason));
        Assert.Equal("MotionRoleAmbiguous", ambiguousReason);

        Assert.False(MsiClawMotionSource.TrySelectSources(
            new HandheldDeviceModelId("msi.claw.cg3em"), [gyro, accel with { SupportsZ = false }],
            out _, out _, out var supportReason));
        Assert.Equal("RequiredMotionRoleMissing", supportReason);

        Assert.False(MsiClawMotionSource.TrySelectSources(
            new HandheldDeviceModelId("msi.claw.cg3em"), [gyro, accel with { FriendlyName = "Simple DMD" }],
            out _, out _, out var roleReason));
        Assert.Equal("RequiredMotionRoleMissing", roleReason);

        Assert.False(MsiClawMotionSource.TrySelectSources(
            new HandheldDeviceModelId("msi.claw.a2vm.8"),
            [WinRtGyro(), Legacy("Physical Accelerometer", "{73000001-0012-0002-0000-000000000001}") with { Manufacturer = "Unavailable", Model = "Unavailable" }],
            out _, out _, out var identityReason));
        Assert.Equal("A2VmAccelerometerIdentityUnverified", identityReason);

        Assert.False(MsiClawMotionSource.TrySelectSources(
            new HandheldDeviceModelId("unknown"), [gyro, accel], out _, out _, out var unsupportedReason));
        Assert.Equal("UnsupportedModel", unsupportedReason);
    }

    [Fact]
    public void Application_axis_transform_is_applied_once_and_nonfinite_samples_are_rejected()
    {
        Assert.True(MsiClawMotionState.TryNormalizeSensorAxes(1, 2, 3, out var gyroX, out var gyroY, out var gyroZ));
        Assert.Equal((1d, 3d, -2d), (gyroX, gyroY, gyroZ));

        Assert.True(MsiClawMotionState.TryNormalizeSensorAxes(0.1, 0.2, 0.9, out var accelX, out var accelY, out var accelZ));
        Assert.Equal((0.1d, 0.9d, -0.2d), (accelX, accelY, accelZ));

        Assert.False(MsiClawMotionState.TryNormalizeSensorAxes(double.NaN, 0, 0, out var x, out var y, out var z));
        Assert.Equal((0d, 0d, 0d), (x, y, z));
        Assert.True(MsiClawMotionState.TryNormalizeSensorAxes(0, 0, 0, out x, out y, out z));
        Assert.Equal((0d, 0d, 0d), (x, y, z));
    }

    [Fact]
    public void Gyro_and_accelerometer_freshness_are_independent_and_accept_boundary_and_true_zero()
    {
        var state = new MsiClawMotionState(
            0, 0, 0, 0.1, 0.9, -0.2,
            750, 500, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            true, true, "gyro", "accel");

        var atBothBoundaries = state.WithFreshness(1000, 1000);
        Assert.True(atBothBoundaries.HasGyro);
        Assert.True(atBothBoundaries.HasAccelerometer);
        Assert.Equal(0, atBothBoundaries.GyroXDegPerSecond);
        Assert.Equal(0.9, atBothBoundaries.AccelYG);
        Assert.True(atBothBoundaries.IsUsableForSteamDeckImu);

        var staleGyroOnly = state with { GyroReceiveTicks = 749 };
        var independentlyStale = staleGyroOnly.WithFreshness(1000, 1000);
        Assert.False(independentlyStale.HasGyro);
        Assert.Equal(0, independentlyStale.GyroYDegPerSecond);
        Assert.True(independentlyStale.HasAccelerometer);
        Assert.False(independentlyStale.IsUsableForSteamDeckImu);
    }

    [Fact]
    public void Duplicate_sensor_timestamps_are_rejected_but_missing_timestamps_are_not_fabricated()
    {
        var timestamp = DateTimeOffset.UnixEpoch.AddSeconds(10);
        Assert.True(MsiClawMotionSource.IsDuplicateSensorTimestamp(timestamp, timestamp));
        Assert.False(MsiClawMotionSource.IsDuplicateSensorTimestamp(timestamp, timestamp.AddTicks(1)));
        Assert.False(MsiClawMotionSource.IsDuplicateSensorTimestamp(null, null));
    }

    [Fact]
    public async Task Blocking_reader_does_not_block_latest_snapshot_and_releases_handle_on_its_reader_thread()
    {
        var gyro = Legacy("Physical Gyrometer", "{76000001-0012-0002-0000-000000000001}");
        var accel = Legacy("Physical Accelerometer", "{73000001-0012-0002-0000-000000000001}");
        using var releaseBlockedRead = new ManualResetEventSlim();
        var handles = new Dictionary<string, FakeSourceHandle>(StringComparer.OrdinalIgnoreCase)
        {
            [gyro.SensorId] = new(gyro, (1, 2, 3), releaseBlockedRead),
            [accel.SensorId] = new(accel, (0.1, 0.2, 0.9), null)
        };
        var discovery = new ClawSensorDiscovery([gyro, accel], null, null, []);
        var clock = new FakeMonotonicClock();
        await using var source = new MsiClawMotionSource(
            new HandheldDeviceModelId("msi.claw.cg3em"),
            () => discovery,
            candidate => handles[candidate.SensorId],
            clock.Read,
            clock.Frequency);

        Assert.True(await source.StartAsync("Test"));
        await WaitUntilAsync(() => handles.Values.All(x => x.FirstReadCompleted) && source.LatestState.IsUsableForSteamDeckImu);
        Assert.True(handles[gyro.SensorId].BlockedReadStarted.Wait(TimeSpan.FromSeconds(2)));

        var snapshot = await Task.Run(() => source.LatestState).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(snapshot.HasGyro);
        Assert.True(snapshot.HasAccelerometer);
        Assert.Equal((1d, 3d, -2d), (snapshot.GyroXDegPerSecond, snapshot.GyroYDegPerSecond, snapshot.GyroZDegPerSecond));
        Assert.Equal((0.1d, 0.9d, -0.2d), (snapshot.AccelXG, snapshot.AccelYG, snapshot.AccelZG));

        releaseBlockedRead.Set();
        Assert.True(await source.StopAsync("Test"));
        Assert.Equal(handles[gyro.SensorId].FirstReadThreadId, handles[gyro.SensorId].DisposeThreadId);
        Assert.Equal(handles[accel.SensorId].FirstReadThreadId, handles[accel.SensorId].DisposeThreadId);
        Assert.NotEqual(handles[gyro.SensorId].FirstReadThreadId, handles[accel.SensorId].FirstReadThreadId);
    }

    [Fact]
    public async Task Nonfinite_sample_invalidates_only_its_sensor_role()
    {
        var gyro = Legacy("Physical Gyrometer", "{76000001-0012-0002-0000-000000000001}");
        var accel = Legacy("Physical Accelerometer", "{73000001-0012-0002-0000-000000000001}");
        var handles = new Dictionary<string, FakeSourceHandle>(StringComparer.OrdinalIgnoreCase)
        {
            [gyro.SensorId] = new(gyro, (1, 2, 3), null, nonFiniteFirstSample: true),
            [accel.SensorId] = new(accel, (0.1, 0.2, 0.9), null)
        };
        var clock = new FakeMonotonicClock();
        await using var source = new MsiClawMotionSource(
            new HandheldDeviceModelId("msi.claw.cg3em"),
            () => new ClawSensorDiscovery([gyro, accel], null, null, []),
            candidate => handles[candidate.SensorId],
            clock.Read,
            clock.Frequency);

        Assert.True(await source.StartAsync("Test"));
        await WaitUntilAsync(() => handles[gyro.SensorId].IsDisposed && source.LatestState.HasAccelerometer);

        var snapshot = source.LatestState;
        Assert.False(snapshot.HasGyro);
        Assert.Equal((0d, 0d, 0d), (snapshot.GyroXDegPerSecond, snapshot.GyroYDegPerSecond, snapshot.GyroZDegPerSecond));
        Assert.True(snapshot.HasAccelerometer);
        Assert.True(await source.StopAsync("Test"));
    }

    [Fact]
    public async Task Blocked_reader_shutdown_is_bounded_and_defers_handle_release_to_its_worker()
    {
        var gyro = Legacy("Physical Gyrometer", "{76000001-0012-0002-0000-000000000001}");
        var accel = Legacy("Physical Accelerometer", "{73000001-0012-0002-0000-000000000001}");
        using var releaseBlockedRead = new ManualResetEventSlim();
        var handles = new Dictionary<string, FakeSourceHandle>(StringComparer.OrdinalIgnoreCase)
        {
            [gyro.SensorId] = new(gyro, (1, 2, 3), releaseBlockedRead),
            [accel.SensorId] = new(accel, (0.1, 0.2, 0.9), null)
        };
        var clock = new FakeMonotonicClock();
        await using var source = new MsiClawMotionSource(
            new HandheldDeviceModelId("msi.claw.cg3em"),
            () => new ClawSensorDiscovery([gyro, accel], null, null, []),
            candidate => handles[candidate.SensorId],
            clock.Read,
            clock.Frequency,
            TimeSpan.FromMilliseconds(40));

        Assert.True(await source.StartAsync("Test"));
        await WaitUntilAsync(() => source.LatestState.IsUsableForSteamDeckImu);
        Assert.True(handles[gyro.SensorId].BlockedReadStarted.Wait(TimeSpan.FromSeconds(2)));

        Assert.False(await source.StopAsync("BlockedRead"));
        Assert.False(source.LatestState.IsUsableForSteamDeckImu);
        Assert.False(handles[gyro.SensorId].IsDisposed);
        Assert.True(handles[accel.SensorId].IsDisposed);

        releaseBlockedRead.Set();
        await WaitUntilAsync(() => handles[gyro.SensorId].IsDisposed);
        Assert.Equal(handles[gyro.SensorId].FirstReadThreadId, handles[gyro.SensorId].DisposeThreadId);
    }

    [Fact]
    public async Task Unsupported_model_does_not_discover_or_open_any_sensor()
    {
        var discoveryCalls = 0;
        await using var source = new MsiClawMotionSource(
            new HandheldDeviceModelId("unknown"),
            () =>
            {
                Interlocked.Increment(ref discoveryCalls);
                return new ClawSensorDiscovery([], null, null, []);
            },
            _ => throw new Xunit.Sdk.XunitException("Unsupported models must not open a sensor."),
            Stopwatch.GetTimestamp,
            Stopwatch.Frequency);

        Assert.False(await source.StartAsync("Test"));
        Assert.Equal(0, discoveryCalls);
        Assert.False(source.LatestState.IsUsableForSteamDeckImu);
    }

    [Fact]
    public async Task Stop_is_idempotent_and_restart_opens_fresh_reader_handles()
    {
        var gyro = Legacy("Physical Gyrometer", "{76000001-0012-0002-0000-000000000001}");
        var accel = Legacy("Physical Accelerometer", "{73000001-0012-0002-0000-000000000001}");
        var handles = new List<FakeSourceHandle>();
        var handlesGate = new object();
        var clock = new FakeMonotonicClock();
        await using var source = new MsiClawMotionSource(
            new HandheldDeviceModelId("msi.claw.cg3em"),
            () => new ClawSensorDiscovery([gyro, accel], null, null, []),
            candidate =>
            {
                var sample = candidate.FriendlyName == "Physical Gyrometer" ? (1d, 2d, 3d) : (0.1d, 0.2d, 0.9d);
                var handle = new FakeSourceHandle(candidate, sample, null);
                lock (handlesGate)
                    handles.Add(handle);
                return handle;
            },
            clock.Read,
            clock.Frequency);

        Assert.True(await source.StartAsync("FirstStart"));
        await WaitUntilAsync(() => source.LatestState.IsUsableForSteamDeckImu);
        Assert.True(await source.StopAsync("FirstStop"));
        Assert.False(source.LatestState.IsUsableForSteamDeckImu);
        Assert.True(await source.StopAsync("RepeatedStop"));

        Assert.True(await source.StartAsync("Restart"));
        await WaitUntilAsync(() => source.LatestState.IsUsableForSteamDeckImu);
        FakeSourceHandle[] opened;
        lock (handlesGate)
            opened = handles.ToArray();
        Assert.Equal(4, opened.Length);
        Assert.All(opened[..2], handle => Assert.True(handle.IsDisposed));
        Assert.All(opened[2..], handle => Assert.False(handle.IsDisposed));
        Assert.True(await source.StopAsync("FinalStop"));
        Assert.All(opened, handle => Assert.True(handle.IsDisposed));
    }

    [Fact]
    public void Host_starts_motion_only_after_a_committed_Steam_Deck_presentation_and_stops_at_lifecycle_boundaries()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SteamInputAddonforClaw.slnx"))) dir = dir.Parent;
        var host = File.ReadAllText(Path.Combine(dir!.FullName, "src/SteamInputAddonforClaw/Hosting/AddonProcessHost.cs"));

        var owned = host.IndexOf("if (!acquired.IsOwned)", StringComparison.Ordinal);
        var live = host.IndexOf("if (source is null || !source.IsRunning)", owned, StringComparison.Ordinal);
        var sourceCreated = host.IndexOf("_motionSource = new MsiClawMotionSource(motionModel)", live, StringComparison.Ordinal);
        var firstPresentation = host.IndexOf("AttachInitialAsync", sourceCreated, StringComparison.Ordinal);
        var motionReconcile = host.IndexOf("await ReconcileMotionReadersAsync(\"StartupPresentation\")", firstPresentation, StringComparison.Ordinal);
        Assert.True(owned >= 0 && live > owned && sourceCreated > live && firstPresentation > sourceCreated && motionReconcile > firstPresentation);
        Assert.DoesNotContain("await StartMotionSourceAsync(\"Startup\")", host, StringComparison.Ordinal);
        Assert.True(
            host.IndexOf("startupResult.DisabledBootAdmission?.IsReady != true", StringComparison.Ordinal)
            < host.IndexOf("var acquired = await owner.AcquireAsync", StringComparison.Ordinal),
            "motion startup must remain behind the existing Disabled-boot admission and owned acquisition gates");
        Assert.Contains("startupResult.HardwareDeviceModel is { } motionModel && MsiClawMotionSource.IsSupportedModel(motionModel)", host, StringComparison.Ordinal);
        Assert.Contains("physical.OwnedPhysicalIdentity is not { Confidence: MsiClawIdentityConfidence.Strong }", host, StringComparison.Ordinal);

        var loss = host.IndexOf("private void OnOwnedControllerPhysicalInputCompleted", StringComparison.Ordinal);
        var lossInvalidation = host.IndexOf("_motionSource?.InvalidateAndCancel();", loss, StringComparison.Ordinal);
        var lossRecovery = host.IndexOf("RequestOwnedControllerRecovery(physical, \"UnexpectedDirectInputCompletion\")", loss, StringComparison.Ordinal);
        Assert.True(loss >= 0 && lossInvalidation > loss && lossRecovery > lossInvalidation);

        var suspend = host.IndexOf("private async Task<bool> QuiesceFull1902PresentationForSuspendAsync", StringComparison.Ordinal);
        var suspendInvalidation = host.IndexOf("_motionSource?.InvalidateAndCancel();", suspend, StringComparison.Ordinal);
        var presentationPause = host.IndexOf("PauseForSuspendAsync", suspend, StringComparison.Ordinal);
        var suspendStop = host.IndexOf("await StopMotionSourceAsync(\"Suspend\")", suspend, StringComparison.Ordinal);
        Assert.True(suspend >= 0 && suspendInvalidation > suspend && presentationPause > suspendInvalidation && suspendStop > presentationPause);

        var resume = host.IndexOf("private void OnPowerResumeObserved()", StringComparison.Ordinal);
        var resumeStop = host.IndexOf("StopMotionSourceAsync(\"PowerResume\")", resume, StringComparison.Ordinal);
        var presentationReconcile = host.IndexOf("RequestControllerPresentationReconcile(\"PowerResume\")", resume, StringComparison.Ordinal);
        Assert.True(resume >= 0 && resumeStop > resume && presentationReconcile > resumeStop);
        Assert.DoesNotContain("RestartMotionSourceAfterResumeAsync", host, StringComparison.Ordinal);

        var stockRelease = host.IndexOf("await DisposeMotionSourceAsync(\"CenterMAuthorityRelease\")", StringComparison.Ordinal);
        var stockOwnerRelease = host.IndexOf("return await owner.ReleaseForCenterMEnableAsync(token)", stockRelease, StringComparison.Ordinal);
        Assert.True(stockRelease >= 0 && stockOwnerRelease > stockRelease);

        var shutdownDispose = host.IndexOf("await DisposeMotionSourceAsync(\"ProcessShutdown\")", StringComparison.Ordinal);
        Assert.True(shutdownDispose >= 0);
        Assert.True(
            shutdownDispose < host.IndexOf("await _physicalOwnership.DisposeAsync()", shutdownDispose, StringComparison.Ordinal));

        var sourcePath = Path.Combine(dir.FullName, "src/SteamInputAddonforClaw/Devices/MSI/Claw/MsiClawMotionSource.cs");
        var source = File.ReadAllText(sourcePath);
        foreach (var forbidden in new[]
        {
            "SteamDeckDeviceStateMapper", "CanonicalSteamDeckInputPublisher", "ControllerState",
            "Xbox360DeviceState", "XInputSetState", "ControllerVibration"
        })
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
    }

    private static ClawSensorProbeCandidate WinRtGyro() => new(
        "WinRT Gyrometer", A2VmGyroDeviceId, "Unavailable", "Unavailable",
        Backend: ClawSensorProbeBackend.WinRtGyrometer,
        State: "Ready",
        DevicePath: A2VmGyroDeviceId,
        UnitBasis: ClawSensorProbeUnitBasis.DegreesPerSecond);

    private static ClawSensorProbeCandidate Legacy(string role, string sensorId, string state = "Ready") => new(
        role, sensorId, MotionType, "C317C286-C468-4288-9975-D4C4587C442C",
        Manufacturer: "ST_MICRO",
        Model: "LSM6DSO",
        MinimumReportInterval: "10",
        Backend: ClawSensorProbeBackend.LegacySensorApi,
        State: state,
        DevicePath: @"\\?\USB#VID_0483&PID_XXXX",
        IsDirectTypeMatch: true,
        SupportsX: true,
        SupportsY: true,
        SupportsZ: true);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(3))
                throw new TimeoutException("The expected physical motion sample was not published.");
            await Task.Delay(5);
        }
    }

    private sealed class FakeMonotonicClock
    {
        private long _ticks;
        internal long Frequency => 1000;
        internal long Read() => Interlocked.Increment(ref _ticks);
    }

    private sealed class FakeSourceHandle(
        ClawSensorProbeCandidate candidate,
        (double X, double Y, double Z) values,
        ManualResetEventSlim? blockAfterFirstRead,
        bool nonFiniteFirstSample = false) : IClawSensorProbeSourceHandle
    {
        private int _readCount;
        private int _firstReadThreadId;
        private int _disposeThreadId;
        private int _isDisposed;

        internal bool FirstReadCompleted => Volatile.Read(ref _readCount) >= 1;
        internal bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;
        internal int FirstReadThreadId => Volatile.Read(ref _firstReadThreadId);
        internal int DisposeThreadId => Volatile.Read(ref _disposeThreadId);
        internal ManualResetEventSlim BlockedReadStarted { get; } = new();
        public ClawSensorProbeSourceConfiguration Configuration { get; } = new(candidate.Backend, null, null, null);

        public ClawSensorReportReadResult Read()
        {
            var count = Interlocked.Increment(ref _readCount);
            Interlocked.CompareExchange(ref _firstReadThreadId, Environment.CurrentManagedThreadId, 0);
            if (count == 1)
            {
                if (nonFiniteFirstSample)
                    return ClawSensorReportReadResult.Data(double.NaN, values.Y, values.Z, DateTimeOffset.UnixEpoch.AddTicks(count));
                return ClawSensorReportReadResult.Data(values.X, values.Y, values.Z, DateTimeOffset.UnixEpoch.AddTicks(count));
            }

            if (blockAfterFirstRead is not null)
            {
                BlockedReadStarted.Set();
                blockAfterFirstRead.Wait();
            }

            return ClawSensorReportReadResult.Data(values.X, values.Y, values.Z, DateTimeOffset.UnixEpoch.AddTicks(count));
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _isDisposed, 1);
            Interlocked.Exchange(ref _disposeThreadId, Environment.CurrentManagedThreadId);
            BlockedReadStarted.Dispose();
        }
    }
}
