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
    private const int NonFreshReadBackoffMilliseconds = 1;
    private static readonly TimeSpan ReaderShutdownLimit = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan UnavailableWarningInterval = TimeSpan.FromSeconds(30);

    private readonly HandheldDeviceModelId _modelId;
    private readonly Func<ClawSensorDiscovery> _discover;
    private readonly Func<ClawSensorProbeCandidate, IClawSensorProbeSourceHandle> _openSource;
    private readonly Func<long> _getTimestamp;
    private readonly long _timestampFrequency;
    private readonly TimeSpan _readerShutdownLimit;
    private readonly Action<CancellationToken, int> _waitForNextRead;
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
        TimeSpan? readerShutdownLimit = null,
        Action<CancellationToken, int>? waitForNextRead = null)
    {
        _modelId = modelId;
        _discover = discover;
        _openSource = openSource;
        _getTimestamp = getTimestamp;
        _timestampFrequency = timestampFrequency;
        _readerShutdownLimit = readerShutdownLimit ?? ReaderShutdownLimit;
        _waitForNextRead = waitForNextRead ?? WaitForNextRead;
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
        var diagnostics = default(MsiClawMotionReaderDiagnostics);
        var diagnosticsActive = false;
        var diagnosticsConfigurationLogged = false;
        try
        {
            handle = _openSource(candidate);
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    SynchronizeReaderDiagnostics(
                        role, candidate, handle,
                        ref diagnostics, ref diagnosticsActive, ref diagnosticsConfigurationLogged);
                }
                catch
                {
                    diagnostics.Reset();
                    diagnosticsActive = false;
                }

                var measureRead = diagnosticsActive;
                long readStartedTicks = 0;
                if (measureRead)
                {
                    try { readStartedTicks = _getTimestamp(); }
                    catch
                    {
                        diagnostics.Reset();
                        diagnosticsActive = false;
                        measureRead = false;
                    }
                }

                var sample = handle.Read();
                long readCompletedTicks = 0;
                if (measureRead)
                {
                    try { readCompletedTicks = _getTimestamp(); }
                    catch
                    {
                        diagnostics.Reset();
                        diagnosticsActive = false;
                        measureRead = false;
                    }
                }
                var debugStillEnabled = AppLog.IsEnabled(AppLogLevel.Debug);
                try
                {
                    if (measureRead && debugStillEnabled && diagnosticsActive)
                        diagnostics.ObserveRead(readCompletedTicks - readStartedTicks, _timestampFrequency);
                    else if (!debugStillEnabled && diagnosticsActive)
                    {
                        diagnostics.Reset();
                        diagnosticsActive = false;
                    }
                    else if (debugStillEnabled && !diagnosticsActive)
                    {
                        StartReaderDiagnostics(
                            role, candidate, handle,
                            ref diagnostics, ref diagnosticsActive, ref diagnosticsConfigurationLogged);
                    }
                }
                catch
                {
                    diagnostics.Reset();
                    diagnosticsActive = false;
                    measureRead = false;
                }

                var recordThisRead = measureRead && debugStillEnabled && diagnosticsActive;
                if (cancellationToken.IsCancellationRequested)
                    break;

                if (!sample.HasData)
                {
                    if (recordThisRead)
                        diagnostics.ObserveNoData();
                    WaitForReadOutcome(cancellationToken, hasData: false, duplicateSensorTimestamp: false);
                    EmitReaderDiagnosticsIfDue(role, candidate, ref diagnostics, ref diagnosticsActive, ref diagnosticsConfigurationLogged, handle);
                    continue;
                }

                DateTimeOffset? sensorTimestamp = sample.SensorTimestamp == default ? null : sample.SensorTimestamp;
                if (IsDuplicateSensorTimestamp(previousSensorTimestamp, sensorTimestamp))
                {
                    if (recordThisRead)
                        diagnostics.ObserveDuplicateTimestamp();
                    WaitForReadOutcome(cancellationToken, hasData: true, duplicateSensorTimestamp: true);
                    EmitReaderDiagnosticsIfDue(role, candidate, ref diagnostics, ref diagnosticsActive, ref diagnosticsConfigurationLogged, handle);
                    continue;
                }

                previousSensorTimestamp = sensorTimestamp;
                if (recordThisRead && sensorTimestamp is null)
                    diagnostics.ObserveMissingSensorTimestamp();
                if (!PublishSample(role, candidate, sample, sensorTimestamp))
                    return;

                if (recordThisRead)
                {
                    try
                    {
                        var published = Volatile.Read(ref _latestState);
                        var sensorAgeNow = sensorTimestamp is null ? (DateTimeOffset?)null : DateTimeOffset.UtcNow;
                        if (role == MotionRole.Gyroscope)
                        {
                            diagnostics.ObserveFresh(
                                published.GyroXDegPerSecond, published.GyroYDegPerSecond, published.GyroZDegPerSecond,
                                published.GyroReceiveTicks, published.GyroSensorTimestamp, sensorAgeNow,
                                _timestampFrequency);
                        }
                        else
                        {
                            diagnostics.ObserveFresh(
                                published.AccelXG, published.AccelYG, published.AccelZG,
                                published.AccelReceiveTicks, published.AccelSensorTimestamp, sensorAgeNow,
                                _timestampFrequency);
                        }
                    }
                    catch
                    {
                        diagnostics.Reset();
                        diagnosticsActive = false;
                    }
                }

                WaitForReadOutcome(cancellationToken, hasData: true, duplicateSensorTimestamp: false);
                EmitReaderDiagnosticsIfDue(role, candidate, ref diagnostics, ref diagnosticsActive, ref diagnosticsConfigurationLogged, handle);
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

    private void SynchronizeReaderDiagnostics(
        MotionRole role,
        ClawSensorProbeCandidate candidate,
        IClawSensorProbeSourceHandle handle,
        ref MsiClawMotionReaderDiagnostics diagnostics,
        ref bool diagnosticsActive,
        ref bool diagnosticsConfigurationLogged)
    {
        if (!AppLog.IsEnabled(AppLogLevel.Debug))
        {
            if (diagnosticsActive)
            {
                diagnostics.Reset();
                diagnosticsActive = false;
            }
            return;
        }

        if (!diagnosticsActive)
            StartReaderDiagnostics(role, candidate, handle, ref diagnostics, ref diagnosticsActive, ref diagnosticsConfigurationLogged);
    }

    private void StartReaderDiagnostics(
        MotionRole role,
        ClawSensorProbeCandidate candidate,
        IClawSensorProbeSourceHandle handle,
        ref MsiClawMotionReaderDiagnostics diagnostics,
        ref bool diagnosticsActive,
        ref bool diagnosticsConfigurationLogged)
    {
        try
        {
            if (!AppLog.IsEnabled(AppLogLevel.Debug))
                return;

            diagnostics.StartWindow(_getTimestamp());
            diagnosticsActive = true;
            if (diagnosticsConfigurationLogged)
                return;

            var configuration = handle.Configuration;
            var isWinRt = configuration.Backend is ClawSensorProbeBackend.WinRtGyrometer or ClawSensorProbeBackend.WinRtAccelerometer;
            uint? candidateMinimum = uint.TryParse(candidate.MinimumReportInterval, NumberStyles.None, CultureInfo.InvariantCulture, out var minimum)
                ? minimum
                : null;
            AppLog.Debug("ControllerMotion", "MotionReaderDiagnosticsStarted",
                ("Event", "MotionReaderDiagnosticsStarted"),
                ("Model", _modelId.Value),
                ("Role", role),
                ("Backend", configuration.Backend),
                ("SensorId", candidate.SensorId),
                ("CandidateMinimumReportIntervalMs", candidateMinimum),
                ("WinRtMinimumReportIntervalMs", isWinRt ? configuration.MinimumReportIntervalMs : null),
                ("WinRtRequestedReportIntervalMs", isWinRt ? configuration.RequestedReportIntervalMs : null),
                ("WinRtEffectiveReportIntervalMs", isWinRt ? configuration.EffectiveReportIntervalMs : null),
                ("NonFreshBackoffMs", NonFreshReadBackoffMilliseconds),
                ("FreshAcceptedExtraWaitMs", 0));
            diagnosticsConfigurationLogged = true;
        }
        catch
        {
            diagnostics.Reset();
            diagnosticsActive = false;
        }
    }

    private void EmitReaderDiagnosticsIfDue(
        MotionRole role,
        ClawSensorProbeCandidate candidate,
        ref MsiClawMotionReaderDiagnostics diagnostics,
        ref bool diagnosticsActive,
        ref bool diagnosticsConfigurationLogged,
        IClawSensorProbeSourceHandle handle)
    {
        try
        {
            if (!AppLog.IsEnabled(AppLogLevel.Debug))
            {
                if (diagnosticsActive)
                {
                    diagnostics.Reset();
                    diagnosticsActive = false;
                }
                return;
            }

            if (!diagnosticsActive)
            {
                StartReaderDiagnostics(role, candidate, handle, ref diagnostics, ref diagnosticsActive, ref diagnosticsConfigurationLogged);
                return;
            }

            var nowTicks = _getTimestamp();
            if (!diagnostics.IsSummaryDue(nowTicks, _timestampFrequency))
                return;

            var summary = diagnostics.CreateSummary(nowTicks, _timestampFrequency);
            AppLog.Debug("ControllerMotion", "MotionReaderDiagnosticsSummary",
                ("Event", "MotionReaderDiagnosticsSummary"),
                ("Model", _modelId.Value),
                ("Role", role),
                ("Backend", candidate.Backend),
                ("SensorId", candidate.SensorId),
                ("WindowMs", summary.WindowMs),
                ("ReadAttempts", summary.ReadAttempts),
                ("FreshAcceptedCount", summary.FreshAcceptedCount),
                ("DuplicateTimestampCount", summary.DuplicateTimestampCount),
                ("NoDataCount", summary.NoDataCount),
                ("MissingSensorTimestampCount", summary.MissingSensorTimestampCount),
                ("ActualFreshHz", summary.ActualFreshHz),
                ("NonFreshBackoffMs", NonFreshReadBackoffMilliseconds),
                ("FreshAcceptedExtraWaitMs", 0),
                ("ReadDurationMeanMs", summary.ReadDurationMeanMs),
                ("ReadDurationMaxMs", summary.ReadDurationMaxMs),
                ("FreshReceiveIntervalMeanMs", summary.FreshReceiveIntervalMeanMs),
                ("FreshReceiveIntervalMaxMs", summary.FreshReceiveIntervalMaxMs),
                ("GapOver20MsCount", summary.GapOver20MsCount),
                ("GapOver50MsCount", summary.GapOver50MsCount),
                ("GapOver100MsCount", summary.GapOver100MsCount),
                ("SensorTimestampIntervalMeanMs", summary.SensorTimestampIntervalMeanMs),
                ("SensorTimestampIntervalMaxMs", summary.SensorTimestampIntervalMaxMs),
                ("SensorTimestampNonMonotonicCount", summary.SensorTimestampNonMonotonicCount),
                ("SensorAgeSampleCount", summary.SensorAgeSampleCount),
                ("SensorAgeMeanMs", summary.SensorAgeMeanMs),
                ("SensorAgeMaxMs", summary.SensorAgeMaxMs),
                ("SensorAgeNegativeCount", summary.SensorAgeNegativeCount),
                ("AxisSampleCount", summary.AxisSampleCount),
                ("AxisVariationStage", "NormalizedRawInputBeforeBiasOrFilter"),
                ("AxisUnits", role == MotionRole.Gyroscope ? "deg/s" : "g"),
                ("MeanX", summary.MeanX), ("StdDevX", summary.StdDevX),
                ("MeanY", summary.MeanY), ("StdDevY", summary.StdDevY),
                ("MeanZ", summary.MeanZ), ("StdDevZ", summary.StdDevZ));
            diagnostics.RestartWindow(nowTicks);
        }
        catch
        {
            // Optional summary formatting and time sampling must never disable a production sensor reader.
            diagnostics.Reset();
            diagnosticsActive = false;
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

    internal static int ReadBackoffMilliseconds(bool hasData, bool duplicateSensorTimestamp) =>
        !hasData || duplicateSensorTimestamp ? NonFreshReadBackoffMilliseconds : 0;

    private void WaitForReadOutcome(
        CancellationToken cancellationToken,
        bool hasData,
        bool duplicateSensorTimestamp)
    {
        var delayMilliseconds = ReadBackoffMilliseconds(hasData, duplicateSensorTimestamp);
        if (delayMilliseconds > 0)
            _waitForNextRead(cancellationToken, delayMilliseconds);
    }

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

internal struct MsiClawMotionReaderDiagnostics
{
    private const double SummaryWindowSeconds = 10;
    private long _windowStartTicks;
    private bool _started;
    private long _readAttempts;
    private long _freshAcceptedCount;
    private long _duplicateTimestampCount;
    private long _noDataCount;
    private long _missingSensorTimestampCount;
    private long _sensorTimestampNonMonotonicCount;
    private long _sensorAgeNegativeCount;
    private long _gapOver20MsCount;
    private long _gapOver50MsCount;
    private long _gapOver100MsCount;
    private long _previousReceiveTicks;
    private bool _hasPreviousReceiveTicks;
    private DateTimeOffset? _previousSensorTimestamp;
    private RunningStatistics _readDurations;
    private RunningStatistics _freshReceiveIntervals;
    private RunningStatistics _sensorTimestampIntervals;
    private RunningStatistics _sensorAges;
    private RunningStatistics _x;
    private RunningStatistics _y;
    private RunningStatistics _z;

    internal void StartWindow(long startTicks)
    {
        this = default;
        _started = true;
        _windowStartTicks = startTicks;
    }

    internal void Reset() => this = default;

    internal void RestartWindow(long startTicks)
    {
        var wasStarted = _started;
        this = default;
        _started = wasStarted;
        _windowStartTicks = startTicks;
    }

    internal bool IsSummaryDue(long nowTicks, long timestampFrequency) =>
        _started
        && timestampFrequency > 0
        && nowTicks >= _windowStartTicks
        && nowTicks - _windowStartTicks >= timestampFrequency * SummaryWindowSeconds;

    internal void ObserveRead(long durationTicks, long timestampFrequency)
    {
        _readAttempts++;
        if (timestampFrequency > 0 && durationTicks >= 0)
            _readDurations.Add(durationTicks * 1000d / timestampFrequency);
    }

    internal void ObserveNoData() => _noDataCount++;

    internal void ObserveDuplicateTimestamp() => _duplicateTimestampCount++;

    internal void ObserveMissingSensorTimestamp() => _missingSensorTimestampCount++;

    internal void ObserveFresh(
        double x,
        double y,
        double z,
        long receiveTicks,
        DateTimeOffset? sensorTimestamp,
        DateTimeOffset? ageMeasuredAt,
        long timestampFrequency)
    {
        _freshAcceptedCount++;
        _x.Add(x);
        _y.Add(y);
        _z.Add(z);

        if (_hasPreviousReceiveTicks && timestampFrequency > 0 && receiveTicks >= _previousReceiveTicks)
        {
            var intervalMs = (receiveTicks - _previousReceiveTicks) * 1000d / timestampFrequency;
            _freshReceiveIntervals.Add(intervalMs);
            if (intervalMs > 20) _gapOver20MsCount++;
            if (intervalMs > 50) _gapOver50MsCount++;
            if (intervalMs > 100) _gapOver100MsCount++;
        }
        _previousReceiveTicks = receiveTicks;
        _hasPreviousReceiveTicks = true;

        if (sensorTimestamp is { } currentSensorTimestamp)
        {
            if (_previousSensorTimestamp is { } previousSensorTimestamp)
            {
                var interval = currentSensorTimestamp - previousSensorTimestamp;
                if (interval > TimeSpan.Zero)
                    _sensorTimestampIntervals.Add(interval.TotalMilliseconds);
                else if (interval < TimeSpan.Zero)
                    _sensorTimestampNonMonotonicCount++;
            }
            _previousSensorTimestamp = currentSensorTimestamp;

            if (ageMeasuredAt is { } measuredAt)
            {
                var ageMs = (measuredAt - currentSensorTimestamp).TotalMilliseconds;
                if (ageMs < 0)
                    _sensorAgeNegativeCount++;
                else
                    _sensorAges.Add(ageMs);
            }
        }
        else
        {
            _previousSensorTimestamp = null;
        }
    }

    internal MsiClawMotionReaderDiagnosticSummary CreateSummary(long nowTicks, long timestampFrequency)
    {
        var elapsedTicks = timestampFrequency > 0 && nowTicks >= _windowStartTicks
            ? nowTicks - _windowStartTicks
            : 0;
        var elapsedSeconds = timestampFrequency > 0 ? elapsedTicks / (double)timestampFrequency : 0;
        return new MsiClawMotionReaderDiagnosticSummary(
            elapsedTicks * 1000d / Math.Max(timestampFrequency, 1),
            _readAttempts,
            _freshAcceptedCount,
            _duplicateTimestampCount,
            _noDataCount,
            _missingSensorTimestampCount,
            elapsedSeconds > 0 ? _freshAcceptedCount / elapsedSeconds : 0,
            _readDurations.Mean,
            _readDurations.Maximum,
            _freshReceiveIntervals.Mean,
            _freshReceiveIntervals.Maximum,
            _gapOver20MsCount,
            _gapOver50MsCount,
            _gapOver100MsCount,
            _sensorTimestampIntervals.Mean,
            _sensorTimestampIntervals.Maximum,
            _sensorTimestampNonMonotonicCount,
            _sensorAges.Count,
            _sensorAges.Mean,
            _sensorAges.Maximum,
            _sensorAgeNegativeCount,
            _x.Count,
            _x.Mean,
            _x.StandardDeviation,
            _y.Mean,
            _y.StandardDeviation,
            _z.Mean,
            _z.StandardDeviation);
    }

    private struct RunningStatistics
    {
        private double _mean;
        private double _sumSquaredDelta;
        private double _maximum;

        internal long Count { get; private set; }
        internal double? Mean => Count > 0 && double.IsFinite(_mean) ? _mean : null;
        internal double? Maximum => Count > 0 && double.IsFinite(_maximum) ? _maximum : null;
        internal double? StandardDeviation => Count >= 2 && double.IsFinite(_sumSquaredDelta)
            ? Math.Sqrt(Math.Max(0, _sumSquaredDelta / Count))
            : null;

        internal void Add(double value)
        {
            if (!double.IsFinite(value))
                return;

            Count++;
            var delta = value - _mean;
            _mean += delta / Count;
            _sumSquaredDelta += delta * (value - _mean);
            if (Count == 1 || value > _maximum)
                _maximum = value;
        }
    }
}

internal sealed record MsiClawMotionReaderDiagnosticSummary(
    double WindowMs,
    long ReadAttempts,
    long FreshAcceptedCount,
    long DuplicateTimestampCount,
    long NoDataCount,
    long MissingSensorTimestampCount,
    double ActualFreshHz,
    double? ReadDurationMeanMs,
    double? ReadDurationMaxMs,
    double? FreshReceiveIntervalMeanMs,
    double? FreshReceiveIntervalMaxMs,
    long GapOver20MsCount,
    long GapOver50MsCount,
    long GapOver100MsCount,
    double? SensorTimestampIntervalMeanMs,
    double? SensorTimestampIntervalMaxMs,
    long SensorTimestampNonMonotonicCount,
    long SensorAgeSampleCount,
    double? SensorAgeMeanMs,
    double? SensorAgeMaxMs,
    long SensorAgeNegativeCount,
    long AxisSampleCount,
    double? MeanX,
    double? StdDevX,
    double? MeanY,
    double? StdDevY,
    double? MeanZ,
    double? StdDevZ);
