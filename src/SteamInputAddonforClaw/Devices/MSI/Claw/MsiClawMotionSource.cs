using System.Diagnostics;
using System.Globalization;
using SteamInputAddonforClaw.Devices.Abstractions;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;

namespace SteamInputAddonforClaw.Devices.MSI.Claw;

internal sealed class MsiClawMotionSource : IAsyncDisposable
{
    private const string A2VmGyroDevicePathToken = "VID_8087&PID_0AC2";
    private const string LegacyMotionSensorType = "E83AF229-8640-4D18-A213-E22675EBB2C3";
    private const string LegacyMotionDataFormat = "B14C764F-07CF-41E8-9D82-EBE3D0776A6F";
    private static readonly TimeSpan ReaderShutdownLimit = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan UnavailableWarningInterval = TimeSpan.FromSeconds(30);

    private readonly HandheldDeviceModelId _modelId;
    private readonly Func<ClawSensorDiscovery> _discover;
    private readonly Func<ClawSensorProbeCandidate, IClawSensorProbeSourceHandle> _openSource;
    private readonly Func<long> _getTimestamp;
    private readonly long _timestampFrequency;
    private readonly TimeSpan _readerShutdownLimit;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _snapshotGate = new();
    private CancellationTokenSource? _stop;
    private Task _runTask = Task.CompletedTask;
    private MsiClawMotionState _latestState = MsiClawMotionState.Unavailable;
    private int _acceptingSamples;
    private int _disposed;
    private int _shutdownTimedOut;
    private int _shutdownTimeoutLogged;
    private long _lastUnavailableWarningTicks;

    internal MsiClawMotionSource(HandheldDeviceModelId modelId)
        : this(modelId, DiscoverSensors, OpenSource, Stopwatch.GetTimestamp, Stopwatch.Frequency)
    {
    }

    internal MsiClawMotionSource(
        HandheldDeviceModelId modelId,
        Func<ClawSensorDiscovery> discover,
        Func<ClawSensorProbeCandidate, IClawSensorProbeSourceHandle> openSource,
        Func<long> getTimestamp,
        long timestampFrequency,
        TimeSpan? readerShutdownLimit = null)
    {
        _modelId = modelId;
        _discover = discover;
        _openSource = openSource;
        _getTimestamp = getTimestamp;
        _timestampFrequency = timestampFrequency;
        _readerShutdownLimit = readerShutdownLimit ?? ReaderShutdownLimit;
    }

    internal bool IsRunning => Volatile.Read(ref _acceptingSamples) != 0;

    internal MsiClawMotionState LatestState
    {
        get => Volatile.Read(ref _latestState).WithFreshness(_getTimestamp(), _timestampFrequency);
    }

    internal async Task<bool> StartAsync(string trigger)
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
                return false;

            if (Volatile.Read(ref _acceptingSamples) != 0
                && _stop is { IsCancellationRequested: false }
                && !_runTask.IsCompleted)
                return true;

            if (Volatile.Read(ref _shutdownTimedOut) != 0 && !_runTask.IsCompleted)
            {
                LogShutdownTimeoutOnce(trigger);
                return false;
            }

            if (_stop is { } previousStop)
            {
                previousStop.Cancel();
                if (!await WaitForPreviousRunAsync(_runTask, trigger).ConfigureAwait(false))
                {
                    Volatile.Write(ref _shutdownTimedOut, 1);
                    DeferStopSourceDisposal(previousStop, _runTask);
                    return false;
                }
                DisposeStopSource(previousStop);
            }

            if (!IsSupportedModel(_modelId))
            {
                PublishUnavailable();
                AppLog.Warn("ControllerMotion", "Motion acquisition skipped for an unsupported exact model.", null,
                    ("Event", "MotionSourceUnavailable"), ("Model", _modelId.Value), ("Trigger", trigger));
                return false;
            }

            var stop = new CancellationTokenSource();
            Volatile.Write(ref _stop, stop);
            PublishUnavailable();
            Volatile.Write(ref _acceptingSamples, 1);
            Volatile.Write(ref _shutdownTimedOut, 0);
            Volatile.Write(ref _shutdownTimeoutLogged, 0);
            _runTask = Task.Run(() => RunGenerationAsync(stop.Token), CancellationToken.None);
            return true;
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _acceptingSamples, 0);
            PublishUnavailable();
            AppLog.Warn("ControllerMotion", "Motion source start failed; Full1902 controller operation is unaffected.", exception,
                ("Event", "MotionSourceStartFailed"), ("Model", _modelId.Value), ("Trigger", trigger));
            return false;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    internal async Task<bool> StopAsync(string trigger)
    {
        InvalidateAndCancel();
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Start may have installed a new reader generation after the first invalidation but
            // before this stop acquired the lifecycle gate. Cancel again while serialized.
            InvalidateAndCancel();
            var stop = _stop;
            if (stop is null)
                return true;

            var runTask = _runTask;
            if (Volatile.Read(ref _shutdownTimedOut) != 0 && !runTask.IsCompleted)
            {
                LogShutdownTimeoutOnce(trigger);
                return false;
            }

            if (!await WaitForPreviousRunAsync(runTask, trigger).ConfigureAwait(false))
            {
                Volatile.Write(ref _shutdownTimedOut, 1);
                DeferStopSourceDisposal(stop, runTask);
                return false;
            }

            DisposeStopSource(stop);
            Volatile.Write(ref _acceptingSamples, 0);
            return true;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    internal void InvalidateAndCancel()
    {
        Volatile.Write(ref _acceptingSamples, 0);
        PublishUnavailable();
        var stop = Volatile.Read(ref _stop);
        if (stop is null)
            return;

        try { stop.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _ = await StopAsync("Dispose").ConfigureAwait(false);
    }

    internal static bool TrySelectSources(
        HandheldDeviceModelId modelId,
        IReadOnlyList<ClawSensorProbeCandidate> sensors,
        out ClawSensorProbeCandidate? gyroscope,
        out ClawSensorProbeCandidate? accelerometer,
        out string reason)
    {
        gyroscope = null;
        accelerometer = null;
        reason = "UnsupportedModel";

        if (modelId.Value is "msi.claw.a2vm.7" or "msi.claw.a2vm.8")
        {
            var gyros = sensors.Where(IsA2VmWinRtGyrometer).ToArray();
            var accels = DistinctLegacyCandidates(sensors.Where(x => IsLegacyRoleCandidate(x, "Physical Accelerometer")));
            if (gyros.Length != 1 || accels.Length != 1)
            {
                reason = DescribeMissingOrAmbiguous(gyros.Length, accels.Length);
                return false;
            }

            gyroscope = gyros[0];
            accelerometer = accels[0];
            if (!HasA2VmIntelSensorIdentity(accelerometer))
            {
                gyroscope = null;
                accelerometer = null;
                reason = "A2VmAccelerometerIdentityUnverified";
                return false;
            }

            reason = "A2VmWinRtGyrometerAndLegacyAccelerometer";
            return true;
        }

        if (modelId.Value == "msi.claw.cg3em")
        {
            var gyros = DistinctLegacyCandidates(sensors.Where(x => IsLegacyRoleCandidate(x, "Physical Gyrometer")));
            var accels = DistinctLegacyCandidates(sensors.Where(x => IsLegacyRoleCandidate(x, "Physical Accelerometer")));
            if (gyros.Length != 1 || accels.Length != 1)
            {
                reason = DescribeMissingOrAmbiguous(gyros.Length, accels.Length);
                return false;
            }

            if (string.Equals(gyros[0].SensorId, accels[0].SensorId, StringComparison.OrdinalIgnoreCase))
            {
                reason = "LegacyMotionRolesShareSensorId";
                return false;
            }

            if (!HasCg3EmSensorIdentity(gyros[0]) || !HasCg3EmSensorIdentity(accels[0]))
            {
                reason = "Cg3EmSensorIdentityUnverified";
                return false;
            }

            gyroscope = gyros[0];
            accelerometer = accels[0];
            reason = "Cg3EmSeparateLegacyGyrometerAndAccelerometer";
            return true;
        }

        return false;
    }

    internal static bool IsDuplicateSensorTimestamp(DateTimeOffset? previous, DateTimeOffset? current) =>
        current is not null && previous == current;

    internal static bool IsSupportedModel(HandheldDeviceModelId modelId) =>
        modelId.Value is "msi.claw.a2vm.7" or "msi.claw.a2vm.8" or "msi.claw.cg3em";

    private async Task<bool> WaitForPreviousRunAsync(Task runTask, string trigger)
    {
        try
        {
            await runTask.WaitAsync(_readerShutdownLimit).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            AppLog.Warn("ControllerMotion", "A motion reader remains in a blocking sensor call; its handle will be released by its owning worker when the call returns.", null,
                ("Event", "MotionReaderShutdownDeferred"), ("Trigger", trigger), ("BoundedWaitMs", _readerShutdownLimit.TotalMilliseconds));
            return false;
        }
    }

    private async Task RunGenerationAsync(CancellationToken cancellationToken)
    {
        var readerTasksStarted = false;
        try
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var discovery = _discover();
            if (cancellationToken.IsCancellationRequested)
                return;

            if (!TrySelectSources(_modelId, discovery.Sensors, out var gyro, out var accel, out var reason)
                || gyro is null || accel is null)
            {
                Volatile.Write(ref _acceptingSamples, 0);
                PublishUnavailable();
                if (ShouldLogUnavailableWarning())
                {
                    AppLog.Warn("ControllerMotion", "No unambiguous validated motion source pair was found; motion remains unavailable.", null,
                        ("Event", "MotionSourceUnavailable"), ("Model", _modelId.Value), ("Reason", reason));
                }
                return;
            }

            AppLog.Info("ControllerMotion", "Validated physical motion sources selected.",
                ("Event", "MotionSourcesSelected"), ("Model", _modelId.Value),
                ("GyroBackend", gyro.Backend), ("GyroSensorId", gyro.SensorId),
                ("AccelerometerBackend", accel.Backend), ("AccelerometerSensorId", accel.SensorId),
                ("GyroUnits", "deg/s"),
                ("GyroUnitsBasis", gyro.Backend == ClawSensorProbeBackend.WinRtGyrometer ? "WinRtApiDeclared" : "ReferenceBasedLegacyInterpretation"),
                ("AccelerometerUnits", "g"), ("AccelerometerUnitsBasis", "ObservedGScale"),
                ("LegacyDataFormat", LegacyMotionDataFormat));

            var readers = new[]
            {
                Task.Run(() => ReadRoleAsync(MotionRole.Gyroscope, gyro, cancellationToken), CancellationToken.None),
                Task.Run(() => ReadRoleAsync(MotionRole.Accelerometer, accel, cancellationToken), CancellationToken.None)
            };
            readerTasksStarted = true;
            await Task.WhenAll(readers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _acceptingSamples, 0);
            PublishUnavailable();
            if (ShouldLogUnavailableWarning())
            {
                AppLog.Warn("ControllerMotion", "Motion discovery failed; Full1902 controller operation is unaffected.", exception,
                    ("Event", "MotionSourceDiscoveryFailed"), ("Model", _modelId.Value));
            }
        }
        finally
        {
            if (!readerTasksStarted)
                Volatile.Write(ref _acceptingSamples, 0);
            else if (!cancellationToken.IsCancellationRequested)
                Volatile.Write(ref _acceptingSamples, 0);
        }
    }

    private void ReadRoleAsync(MotionRole role, ClawSensorProbeCandidate candidate, CancellationToken cancellationToken)
    {
        IClawSensorProbeSourceHandle? handle = null;
        DateTimeOffset? previousSensorTimestamp = null;
        var pollDelayMs = PollDelayMilliseconds(candidate);
        try
        {
            handle = _openSource(candidate);
            while (!cancellationToken.IsCancellationRequested)
            {
                var sample = handle.Read();
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (!sample.HasData)
                {
                    WaitForNextRead(cancellationToken, pollDelayMs);
                    continue;
                }

                DateTimeOffset? sensorTimestamp = sample.SensorTimestamp == default ? null : sample.SensorTimestamp;
                if (IsDuplicateSensorTimestamp(previousSensorTimestamp, sensorTimestamp))
                {
                    WaitForNextRead(cancellationToken, pollDelayMs);
                    continue;
                }

                previousSensorTimestamp = sensorTimestamp;
                if (!PublishSample(role, candidate, sample, sensorTimestamp))
                    return;
                WaitForNextRead(cancellationToken, pollDelayMs);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            InvalidateRole(role);
            AppLog.Warn("ControllerMotion", "A physical motion reader failed; only that motion role is disabled.", exception,
                ("Event", "MotionReaderFailed"), ("Model", _modelId.Value), ("Role", role),
                ("Backend", candidate.Backend), ("SensorId", candidate.SensorId), ("HResult", exception.HResult));
        }
        finally
        {
            try { handle?.Dispose(); }
            catch (Exception exception)
            {
                InvalidateRole(role);
                AppLog.Warn("ControllerMotion", "A sensor handle could not be released by its owning reader.", exception,
                    ("Event", "MotionReaderDisposeFailed"), ("Model", _modelId.Value), ("Role", role),
                    ("Backend", candidate.Backend), ("SensorId", candidate.SensorId));
            }
        }
    }

    private bool PublishSample(
        MotionRole role,
        ClawSensorProbeCandidate candidate,
        ClawSensorReportReadResult sample,
        DateTimeOffset? sensorTimestamp)
    {
        if (!MsiClawMotionState.TryNormalizeSensorAxes(sample.X, sample.Y, sample.Z, out var x, out var y, out var z))
        {
            InvalidateRole(role);
            AppLog.Warn("ControllerMotion", "A non-finite motion sample was rejected.", null,
                ("Event", "MotionSampleRejected"), ("Model", _modelId.Value), ("Role", role),
                ("Backend", candidate.Backend), ("SensorId", candidate.SensorId));
            return false;
        }

        var receiveTicks = _getTimestamp();
        var source = $"{candidate.Backend}:{candidate.SensorId}";
        lock (_snapshotGate)
        {
            if (Volatile.Read(ref _acceptingSamples) == 0)
                return false;

            var current = _latestState;
            var next = role switch
            {
                MotionRole.Gyroscope => current with
                {
                    GyroXDegPerSecond = x,
                    GyroYDegPerSecond = y,
                    GyroZDegPerSecond = z,
                    GyroReceiveTicks = receiveTicks,
                    GyroSensorTimestamp = sensorTimestamp,
                    HasGyro = true,
                    GyroSource = source
                },
                _ => current with
                {
                    AccelXG = x,
                    AccelYG = y,
                    AccelZG = z,
                    AccelReceiveTicks = receiveTicks,
                    AccelSensorTimestamp = sensorTimestamp,
                    HasAccelerometer = true,
                    AccelerometerSource = source
                }
            };
            Volatile.Write(ref _latestState, next);
            return true;
        }
    }

    private void InvalidateRole(MotionRole role)
    {
        lock (_snapshotGate)
        {
            var current = _latestState;
            var next = role == MotionRole.Gyroscope
                ? current with
                {
                    GyroXDegPerSecond = 0,
                    GyroYDegPerSecond = 0,
                    GyroZDegPerSecond = 0,
                    GyroReceiveTicks = 0,
                    GyroSensorTimestamp = null,
                    HasGyro = false
                }
                : current with
                {
                    AccelXG = 0,
                    AccelYG = 0,
                    AccelZG = 0,
                    AccelReceiveTicks = 0,
                    AccelSensorTimestamp = null,
                    HasAccelerometer = false
                };
            Volatile.Write(ref _latestState, next);
        }
    }

    private void PublishUnavailable()
    {
        lock (_snapshotGate)
            Volatile.Write(ref _latestState, MsiClawMotionState.Unavailable);
    }

    private static ClawSensorDiscovery DiscoverSensors()
    {
        using var api = new ClawSensorProbeSensorApi();
        return api.Discover();
    }

    private static IClawSensorProbeSourceHandle OpenSource(ClawSensorProbeCandidate candidate)
    {
        if (candidate.Backend == ClawSensorProbeBackend.WinRtGyrometer)
            return ClawSensorProbeWinRtSourceHandle.OpenGyrometer();

        if (candidate.Backend != ClawSensorProbeBackend.LegacySensorApi
            || !Guid.TryParse(candidate.SensorId, out var sensorId))
            throw new InvalidOperationException("The selected motion source has an unsupported backend or invalid SensorId.");

        using var api = new ClawSensorProbeSensorApi();
        return new ClawSensorProbeLegacySourceHandle(api.GetSensorById(sensorId));
    }

    private static ClawSensorProbeCandidate[] DistinctLegacyCandidates(IEnumerable<ClawSensorProbeCandidate> candidates) =>
        candidates
            .GroupBy(x => x.SensorId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.FirstOrDefault(x => x.IsDirectTypeMatch) ?? group.First())
            .ToArray();

    private static bool IsA2VmWinRtGyrometer(ClawSensorProbeCandidate candidate) =>
        candidate.Backend == ClawSensorProbeBackend.WinRtGyrometer
        && string.Equals(candidate.FriendlyName, "WinRT Gyrometer", StringComparison.OrdinalIgnoreCase)
        && string.Equals(candidate.State, "Ready", StringComparison.OrdinalIgnoreCase)
        && candidate.UnitBasis == ClawSensorProbeUnitBasis.DegreesPerSecond
        && candidate.SensorId.Contains(A2VmGyroDevicePathToken, StringComparison.OrdinalIgnoreCase)
        && candidate.DevicePath.Contains(A2VmGyroDevicePathToken, StringComparison.OrdinalIgnoreCase)
        && string.Equals(candidate.SensorId, candidate.DevicePath, StringComparison.OrdinalIgnoreCase);

    private static bool IsLegacyRoleCandidate(ClawSensorProbeCandidate candidate, string friendlyName) =>
        candidate.Backend == ClawSensorProbeBackend.LegacySensorApi
        && string.Equals(candidate.FriendlyName, friendlyName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(candidate.TypeGuid, LegacyMotionSensorType, StringComparison.OrdinalIgnoreCase)
        && Guid.TryParse(candidate.SensorId, out _)
        && candidate.IsDirectTypeMatch
        && candidate.SupportsX == true
        && candidate.SupportsY == true
        && candidate.SupportsZ == true
        && IsUsableLegacyState(candidate.State);

    private static bool IsUsableLegacyState(string state) =>
        string.Equals(state, "Ready", StringComparison.OrdinalIgnoreCase)
        || string.Equals(state, "Min", StringComparison.OrdinalIgnoreCase)
        || string.Equals(state, "Unavailable", StringComparison.OrdinalIgnoreCase);

    private static bool HasA2VmIntelSensorIdentity(ClawSensorProbeCandidate candidate) =>
        candidate.DevicePath.Contains(A2VmGyroDevicePathToken, StringComparison.OrdinalIgnoreCase)
        || HasStMicroLsm6dsoIdentity(candidate);

    private static bool HasCg3EmSensorIdentity(ClawSensorProbeCandidate candidate) =>
        HasStMicroLsm6dsoIdentity(candidate)
        || candidate.DevicePath.Contains(A2VmGyroDevicePathToken, StringComparison.OrdinalIgnoreCase);

    private static bool HasStMicroLsm6dsoIdentity(ClawSensorProbeCandidate candidate) =>
        string.Equals(candidate.Manufacturer, "ST_MICRO", StringComparison.OrdinalIgnoreCase)
        && string.Equals(candidate.Model, "LSM6DSO", StringComparison.OrdinalIgnoreCase);

    private static string DescribeMissingOrAmbiguous(int gyroCount, int accelCount)
    {
        if (gyroCount == 0 || accelCount == 0)
            return "RequiredMotionRoleMissing";
        return "MotionRoleAmbiguous";
    }

    private static int PollDelayMilliseconds(ClawSensorProbeCandidate candidate) =>
        int.TryParse(candidate.MinimumReportInterval, NumberStyles.None, CultureInfo.InvariantCulture, out var interval)
        && interval > 0
            ? interval
            : 10;

    private static void WaitForNextRead(CancellationToken cancellationToken, int delayMilliseconds) =>
        cancellationToken.WaitHandle.WaitOne(delayMilliseconds);

    private void DisposeStopSource(CancellationTokenSource stop)
    {
        if (Interlocked.CompareExchange(ref _stop, null, stop) != stop)
            return;

        try { stop.Dispose(); }
        catch (ObjectDisposedException) { }
    }

    private void DeferStopSourceDisposal(CancellationTokenSource stop, Task runTask)
    {
        _ = runTask.ContinueWith(
            _ => DisposeStopSource(stop),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void LogShutdownTimeoutOnce(string trigger)
    {
        if (Interlocked.Exchange(ref _shutdownTimeoutLogged, 1) != 0)
            return;

        AppLog.Warn("ControllerMotion", "A prior motion reader is still blocked; a new reader generation will not be started.", null,
            ("Event", "MotionReaderShutdownDeferred"), ("Trigger", trigger),
            ("BoundedWaitMs", _readerShutdownLimit.TotalMilliseconds));
    }

    private bool ShouldLogUnavailableWarning()
    {
        var now = _getTimestamp();
        var intervalTicks = (long)(UnavailableWarningInterval.TotalSeconds * _timestampFrequency);
        while (true)
        {
            var previous = Volatile.Read(ref _lastUnavailableWarningTicks);
            if (previous != 0 && now >= previous && now - previous < intervalTicks)
                return false;
            if (Interlocked.CompareExchange(ref _lastUnavailableWarningTicks, now == 0 ? 1 : now, previous) == previous)
                return true;
        }
    }

    private enum MotionRole
    {
        Gyroscope,
        Accelerometer
    }
}
