using SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;
using SteamInputAddonforClaw.Diagnostics.EnvironmentDiscovery;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Devices.Abstractions;
using System.Text.Json;
using Xunit;

namespace SteamInputAddonforClaw.Tests.ClawSensorProbe;

public sealed class ClawSensorProbeTests
{
    [Fact] public void Workflow_StartsIdleAndReachesReadyBeforeSession()
    {
        var workflow = new ClawSensorProbeWorkflow();
        Assert.Equal(ClawSensorProbeState.Idle, workflow.State);
        workflow.Discovering(); workflow.Ready();
        Assert.Equal(ClawSensorProbeState.Ready, workflow.State);
        Assert.Empty(workflow.Visits);
    }
    [Fact] public void Discovery_FailsClosedForAmbiguousCandidates()
    {
        var result = ClawSensorDiscovery.Select([new("Physical Gyrometer", "g1", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true), new("Physical Gyrometer", "g2", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true), new("Physical Accelerometer", "a1", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true), new("Other", "o", "t", "c")]);
        Assert.False(result.IsValid); Assert.Null(result.Gyroscope); Assert.NotNull(result.Accelerometer); Assert.Contains(result.Sensors, x => x.FriendlyName == "Other");
    }
    [Fact] public void Discovery_FailsClosedWhenEitherRequiredSensorIsMissing()
    {
        Assert.False(ClawSensorDiscovery.Select([new("Physical Gyrometer", "g", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true)]).IsValid);
        Assert.False(ClawSensorDiscovery.Select([new("Physical Accelerometer", "a", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true)]).IsValid);
    }
    [Fact] public void Discovery_PreservesAllMetadataForUnrelatedSensors()
    {
        var result = ClawSensorDiscovery.Select([
            new("Physical Gyrometer", "g", "gyro-type", "gyro-category", "M", "G", "g-persist", "10", "usage-g", SupportsX: true, SupportsY: true, SupportsZ: true),
            new("Physical Accelerometer", "a", "accel-type", "accel-category", "M", "A", "a-persist", "20", "usage-a", SupportsX: true, SupportsY: true, SupportsZ: true),
            new("Magnetometer", "m", "mag-type", "mag-category", "M", "M", "m-persist", "30", "usage-m")]);
        Assert.True(result.IsValid);
        Assert.Equal("m-persist", result.Sensors.Single(x => x.SensorId == "m").PersistentUniqueId);
        Assert.Equal("usage-g", result.Gyroscope?.CustomUsage);
    }
    [Fact] public void Candidate_DoesNotTreatSensorIdAsPersistentUniqueId()
    {
        var result = ClawSensorDiscovery.Select([new("Physical Gyrometer", "sensor-id", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true), new("Physical Accelerometer", "a", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true)]);
        Assert.Equal("Unavailable", result.Gyroscope?.PersistentUniqueId);
    }

    [Fact] public void Discovery_RejectsLegacyCandidateMissingRequiredXyzSupport()
    {
        var result = ClawSensorDiscovery.Select([
            new("Physical Gyrometer", "g", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: false),
            new("Physical Accelerometer", "a", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true)]);

        Assert.False(result.IsValid);
        Assert.Null(result.Gyroscope);
        Assert.NotNull(result.Accelerometer);
        Assert.Contains(result.Errors, x => x.Contains("Gyrometer", StringComparison.Ordinal));
    }

    [Fact] public void Discovery_RejectsLegacyCandidateWithExplicitUnusableState()
    {
        var result = ClawSensorDiscovery.Select([
            new("Physical Gyrometer", "g", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true, State: "AccessDenied"),
            new("Physical Accelerometer", "a", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true)]);

        Assert.False(result.IsValid);
        Assert.Null(result.Gyroscope);
        Assert.NotNull(result.Accelerometer);
    }

    [Fact] public void Discovery_RejectsDirectTypeCandidateWithExplicitUnusableState()
    {
        var result = ClawSensorDiscovery.Select([
            new("Physical Gyrometer", "g", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true),
            new("Physical Accelerometer", "a1", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true, IsDirectTypeMatch: true, State: "Error"),
            new("Physical Accelerometer", "a2", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true)]);

        Assert.True(result.IsValid);
        Assert.Equal("a2", result.Accelerometer?.SensorId);
    }
    [Fact] public void Statistics_CalculatesRateAndBounds()
    {
        var stats = new ClawSensorProbeStatistics(); stats.Add(10); stats.Add(20);
        Assert.Equal(2, stats.SampleCount); Assert.Equal(15, stats.AverageIntervalMs); Assert.Equal(10, stats.MinimumIntervalMs); Assert.Equal(20, stats.MaximumIntervalMs); Assert.Equal(1000d / 15, stats.EffectiveHz, 6);
    }
    [Fact] public async Task Writer_RecordsTransitionAndDroppedSamplesInReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-transition-" + Guid.NewGuid().ToString("N"));
        await using (var writer = new ClawSensorProbeSessionWriter(root, "session"))
        {
            writer.WriteTransition(ClawSensorProbePhase.REST, 1, 5.5);
            writer.Write(new(1, DateTimeOffset.UtcNow, 6, ClawSensorProbePhase.REST, 1, "GYRO", 1, 2, 3, 1));
        }
        var csv = await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-live.csv"));
        Assert.Contains("TRANSITION", csv);
        Assert.Contains("capture_mode", csv.Split('\n')[0]);
        Assert.Contains("sensor_timestamp", csv.Split('\n')[0]);
        Assert.Contains(",1,", csv);
        Assert.Contains("\"DroppedSampleCount\": 0", await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-report.json")));
        Directory.Delete(root, true);
    }
    [Fact] public async Task Writer_RecordsPhaseEndEvenWhenEndArrivesBeforeQueuedSample()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-phase-" + Guid.NewGuid().ToString("N"));
        await using (var writer = new ClawSensorProbeSessionWriter(root, "session"))
        {
            writer.EndPhase(ClawSensorProbePhase.ROLL_LEFT, 1, 25);
            writer.Write(new(1, DateTimeOffset.UtcNow, 10, ClawSensorProbePhase.ROLL_LEFT, 1, "GYRO", 1, 2, 3, 1));
        }
            var report = await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-report.json"));
            Assert.Contains("\"end_elapsed_ms\": 25", report);
        Directory.Delete(root, true);
    }
    [Fact] public async Task Writer_PreservesRepeatedPhasePassesSeparately()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-repeated-phase-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var writer = new ClawSensorProbeSessionWriter(root, "session"))
            {
                writer.WriteTransition(ClawSensorProbePhase.ROLL_LEFT, 1, 1);
                writer.Write(new(1, DateTimeOffset.UtcNow, 2, ClawSensorProbePhase.ROLL_LEFT, 1, "GYRO", 1, 2, 3, 1));
                writer.EndPhase(ClawSensorProbePhase.ROLL_LEFT, 1, 3);
                writer.WriteTransition(ClawSensorProbePhase.ROLL_LEFT, 2, 4);
                writer.Write(new(2, DateTimeOffset.UtcNow, 5, ClawSensorProbePhase.ROLL_LEFT, 2, "GYRO", 4, 5, 6, 1));
            }

            var report = await File.ReadAllTextAsync(Directory.GetFiles(root, "claw-sensor-report.json", SearchOption.AllDirectories).Single());
            Assert.Contains("\"pass\": 1", report);
            Assert.Contains("\"pass\": 2", report);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    [Fact] public async Task Writer_ExcludesTransitionRowsFromSensorStatistics()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-transition-stats-" + Guid.NewGuid().ToString("N"));
        try
        {
            var writer = new ClawSensorProbeSessionWriter(root, "session");
            await using (writer)
            {
                writer.WriteTransition(ClawSensorProbePhase.REST, 1, 1);
                writer.Write(new(1, DateTimeOffset.UtcNow, 2, ClawSensorProbePhase.REST, 1, "GYRO", 1, 2, 3, 10));
            }

            Assert.Equal(1, writer.GyroscopeSummary.SampleCount);
            Assert.Equal(0, writer.AccelerometerSummary.SampleCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    [Fact] public async Task Writer_AssignsUniqueGlobalSequenceAcrossSensors()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-sequence-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var writer = new ClawSensorProbeSessionWriter(root, "session"))
            {
                writer.Write(new(1, DateTimeOffset.UtcNow, 1, ClawSensorProbePhase.REST, 1, "GYRO", 1, 2, 3, 1));
                writer.Write(new(1, DateTimeOffset.UtcNow, 2, ClawSensorProbePhase.REST, 1, "ACCEL", 4, 5, 6, 1));
                writer.WriteTransition(ClawSensorProbePhase.REST, 1, 3);
            }

            var lines = (await File.ReadAllLinesAsync(Path.Combine(root, "session", "claw-sensor-live.csv"))).Skip(1).ToArray();
            Assert.Equal(3, lines.Length);
            Assert.Equal(new[] { "1", "2", "3" }, lines.Select(line => line.Split(',')[0]).ToArray());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    [Fact] public void Workflow_FailureIsTerminal()
    {
        var workflow = new ClawSensorProbeWorkflow(); workflow.Ready(); workflow.Start(); workflow.Fail();
        Assert.Equal(ClawSensorProbeState.Failed, workflow.State);
    }

    [Fact] public void Workflow_UsesRequiredOrderAndManualTransitions()
    {
        var workflow = new ClawSensorProbeWorkflow(); workflow.Ready(); workflow.Start(); workflow.BeginRecording();
        foreach (var _ in Enumerable.Range(0, ClawSensorProbeWorkflow.Phases.Count - 1)) { workflow.Next(); workflow.BeginRecording(); }
        Assert.Equal(ClawSensorProbeWorkflow.Phases, workflow.Visits.Select(x => x.Phase));
        workflow.Next(); Assert.Equal(ClawSensorProbeState.Completed, workflow.State);
    }
    [Fact] public void Workflow_BackCreatesDistinctVisitPass()
    {
        var workflow = new ClawSensorProbeWorkflow(); workflow.Ready(); workflow.Start(); workflow.BeginRecording();
        workflow.Next(); workflow.BeginRecording(); workflow.Back();
        Assert.Equal(ClawSensorProbePhase.REST, workflow.Visits.Last().Phase);
        Assert.Equal(2, workflow.Visits.Last().Pass);
        Assert.Equal(ClawSensorProbeState.Countdown, workflow.State);
    }

    [Fact] public void Statistics_EmptyInputHasNoRateOrBounds()
    {
        var stats = new ClawSensorProbeStatistics();
        Assert.Equal(0, stats.SampleCount);
        Assert.Equal(0, stats.EffectiveHz);
        Assert.Equal(0, stats.MinimumIntervalMs);
        Assert.Equal(0, stats.MaximumIntervalMs);
    }

    [Fact] public async Task Writer_UsesInvariantSeparateRowsAndFinalizes()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-" + Guid.NewGuid().ToString("N"));
        await using (var writer = new ClawSensorProbeSessionWriter(root, "session"))
        {
            writer.Write(new(1, DateTimeOffset.Parse("2026-08-12T03:35:01.1234567Z"), 10.124, ClawSensorProbePhase.REST, 1, "GYRO", 0.123, -0.045, 0.231, 10.124));
            writer.Write(new(2, DateTimeOffset.UtcNow, 10.770, ClawSensorProbePhase.REST, 1, "ACCEL", 0.012, -0.998, 0.041, 10.770));
        }
        var csv = await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-live.csv"));
        Assert.Contains("GYRO,0.123,-0.045,0.231", csv); Assert.Contains("ACCEL,0.012,-0.998,0.041", csv); Assert.True(File.Exists(Path.Combine(root, "session", "claw-sensor-report.json")));
        Directory.Delete(root, true);
    }

    [Fact] public async Task Coordinator_StopIsIdempotentAndRejectsRecordsAfterFinalize()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-coordinator-" + Guid.NewGuid().ToString("N"));
        var coordinator = new ClawSensorProbeCoordinator(); coordinator.Prepare(); coordinator.Start(root); coordinator.BeginRecording();
        await coordinator.StopAsync(); await coordinator.StopAsync();
        coordinator.Write(new(1, DateTimeOffset.UtcNow, 1, ClawSensorProbePhase.REST, 1, "GYRO", 1, 2, 3, 1));
        await coordinator.DisposeAsync();
        Assert.True(File.Exists(Directory.GetFiles(root, "claw-sensor-report.json", SearchOption.AllDirectories).Single()));
        Directory.Delete(root, true);
    }
    [Fact] public async Task Coordinator_StopClosesActivePhaseInReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-stop-phase-" + Guid.NewGuid().ToString("N"));
        try
        {
            var coordinator = new ClawSensorProbeCoordinator();
            coordinator.Prepare(); coordinator.Start(root); coordinator.BeginRecording();
            coordinator.Write(new(1, DateTimeOffset.UtcNow, 10, ClawSensorProbePhase.REST, 1, "GYRO", 1, 2, 3, 1));
            await coordinator.StopAsync();
            var report = await File.ReadAllTextAsync(Directory.GetFiles(root, "claw-sensor-report.json", SearchOption.AllDirectories).Single());
            Assert.Contains("\"end_elapsed_ms\":", report);
            await coordinator.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    [Fact] public async Task Coordinator_FailFinalizesReportBeforeRecording()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-fail-before-recording-" + Guid.NewGuid().ToString("N"));
        try
        {
            var coordinator = new ClawSensorProbeCoordinator();
            coordinator.Prepare(); coordinator.Start(root);
            await coordinator.FailAsync("Sensor discovery failed.");
            Assert.Equal(ClawSensorProbeState.Failed, coordinator.State);
            var report = await File.ReadAllTextAsync(Directory.GetFiles(root, "claw-sensor-report.json", SearchOption.AllDirectories).Single());
            Assert.Contains("Sensor discovery failed.", report);
            await coordinator.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    [Fact] public async Task Coordinator_FailFinalizesReportEvenWhenPassedATokenLinkedToItsOwnLifecycleCancellation()
    {
        // PR #290 re-review: FailCoreAsync cancels _lifecycleCancellation FIRST, then reuses the
        // caller's cancellationToken for ShutdownReadersAndApiAsync's own final
        // cancellationToken.ThrowIfCancellationRequested(). If a caller passes a token linked to
        // LifecycleCancellation into FailAsync/FailOnReaderFaultAsync, that final check now observes
        // the cancellation FailAsync itself just triggered and throws OperationCanceledException --
        // skipping FinalizeAsync() and leaving the workflow Failed with no report ever written. This
        // reproduces that trap directly against the coordinator (no reader/hardware fault needed) and
        // proves finalization still completes.
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-fail-self-cancel-" + Guid.NewGuid().ToString("N"));
        try
        {
            var coordinator = new ClawSensorProbeCoordinator();
            coordinator.Prepare(); coordinator.Start(root);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None, coordinator.LifecycleCancellation);

            await coordinator.FailAsync("Reader fault.", linked.Token);

            Assert.Equal(ClawSensorProbeState.Failed, coordinator.State);
            Assert.True(coordinator.HasReport);
            var report = await File.ReadAllTextAsync(Directory.GetFiles(root, "claw-sensor-report.json", SearchOption.AllDirectories).Single());
            Assert.Contains("Reader fault.", report);
            await coordinator.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    [Fact] public void Coordinator_DoesNotStartWhenOutputDirectoryCannotBeCreated()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-output-file-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(root, "not a directory");
        try
        {
            var coordinator = new ClawSensorProbeCoordinator();
            coordinator.Prepare();
            Assert.Throws<IOException>(() => coordinator.Start(root));
            Assert.Equal(ClawSensorProbeState.Ready, coordinator.State);
        }
        finally
        {
            if (File.Exists(root)) File.Delete(root);
            else if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    [Fact] public async Task Coordinator_ReportsOutputAvailabilityOnlyAfterFinalize()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-output-availability-" + Guid.NewGuid().ToString("N"));
        try
        {
            var coordinator = new ClawSensorProbeCoordinator();
            coordinator.Prepare(); coordinator.Start(root);
            Assert.False(coordinator.HasReport);
            await coordinator.StopAsync();
            Assert.True(coordinator.HasReport);
            await coordinator.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    [Fact] public async Task Writer_PreservesExplicitErrorsInFinalReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-errors-" + Guid.NewGuid().ToString("N"));
        await using (var writer = new ClawSensorProbeSessionWriter(root, "session"))
        {
            writer.AddError("Sensor reader shutdown exceeded the bounded wait.");
        }
        var report = await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-report.json"));
        Assert.Contains("Sensor reader shutdown exceeded the bounded wait", report);
        Directory.Delete(root, true);
    }
    [Fact] public void Statistics_TracksDroppedSamples()
    {
        var stats = new ClawSensorProbeStatistics(); stats.AddDropped(); stats.AddDropped();
        Assert.Equal(2, stats.DroppedSampleCount);
    }
    [Fact] public async Task Writer_RecordsShutdownTimeoutInReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-timeout-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var writer = new ClawSensorProbeSessionWriter(root, "session"))
            {
                writer.MarkShutdownTimedOut();
            }

            var report = await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-report.json"));
            Assert.Contains("\"ShutdownTimedOut\": true", report);
            Assert.Contains("Sensor reader shutdown exceeded the bounded wait.", report);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    [Fact] public async Task Writer_RecordsSensorTimestampAsIsoUtcValue()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-timestamp-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var writer = new ClawSensorProbeSessionWriter(root, "session"))
            {
                writer.Write(new(1, DateTimeOffset.UtcNow, 1, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.REST, 1, "GYRO", 1, 2, 3, 1, DateTimeOffset.Parse("2026-08-12T01:02:03.004Z")));
            }

            var csv = await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-live.csv"));
            Assert.Contains("2026-08-12T01:02:03.0040000Z", csv);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    [Fact] public async Task ProbeWriter_IsIndependentOfAppLogMinimumLevel()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-independent-" + Guid.NewGuid().ToString("N"));
        var previousLevel = AppLog.MinimumLevelOverride;
        try
        {
            AppLog.MinimumLevelOverride = AppLogLevel.Fatal;
            await using (var writer = new ClawSensorProbeSessionWriter(root, "session"))
            {
                writer.Write(new(1, DateTimeOffset.UtcNow, 1, ClawSensorProbePhase.REST, 1, "GYRO", 1.25, 2.5, 3.75, 1));
            }
            var directory = Path.Combine(root, "session");
            Assert.Contains("GYRO,1.25,2.5,3.75", await File.ReadAllTextAsync(Path.Combine(directory, "claw-sensor-live.csv")));
            Assert.True(File.Exists(Path.Combine(directory, "claw-sensor-report.json")));
        }
        finally
        {
            AppLog.MinimumLevelOverride = previousLevel;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public void SensorApi_DeclaresVerifiedComSlots()
    {
        Assert.Equal(3, ClawSensorProbeSensorApi.SensorGetIdSlot);
        Assert.Equal(4, ClawSensorProbeSensorApi.SensorGetCategorySlot);
        Assert.Equal(5, ClawSensorProbeSensorApi.SensorGetTypeSlot);
        Assert.Equal(6, ClawSensorProbeSensorApi.SensorGetFriendlyNameSlot);
        Assert.Equal(7, ClawSensorProbeSensorApi.SensorGetPropertySlot);
        Assert.Equal(13, ClawSensorProbeSensorApi.SensorGetDataSlot);
        Assert.Equal(4, ClawSensorProbeSensorApi.ReportGetSensorValueSlot);
    }

    [Fact] public void SessionClock_ProvidesOneMonotonicTimeDomainForMarkersAndSamples()
    {
        var clock = new ClawSensorProbeSessionClock();
        var marker = clock.ElapsedMs;
        Thread.Sleep(2);
        var sample = ClawSensorProbeSessionClock.TicksToMilliseconds(clock.ElapsedTicks);
        Assert.True(sample >= marker);
    }

    [Fact] public void ReportReadResult_SupportsNoDataAndTimestampedDataContracts()
    {
        Assert.False(ClawSensorReportReadResult.NoData().HasData);
        var timestamp = DateTimeOffset.Parse("2026-08-12T01:02:03Z");
        var data = ClawSensorReportReadResult.Data(1, 2, 3, timestamp);
        Assert.True(data.HasData);
        Assert.Equal(timestamp, data.SensorTimestamp);
    }

    [Fact] public void ReportDeduplicator_AcceptsOnlyNewTimestampedReports()
    {
        var deduplicator = new ClawSensorReportDeduplicator();
        var t1 = DateTimeOffset.Parse("2026-08-12T01:02:03Z");
        var t2 = DateTimeOffset.Parse("2026-08-12T01:02:03.010Z");
        var accepted = new[]
        {
            ClawSensorReportReadResult.NoData(),
            ClawSensorReportReadResult.Data(1, 2, 3, t1),
            ClawSensorReportReadResult.Data(4, 5, 6, t1),
            ClawSensorReportReadResult.NoData(),
            ClawSensorReportReadResult.Data(7, 8, 9, t2)
        }.Where(deduplicator.ShouldAccept).ToArray();

        Assert.Equal(2, accepted.Length);
        Assert.Equal([t1, t2], accepted.Select(x => x.SensorTimestamp).ToArray());
    }

    [Fact] public void Reader_UsesBoundedStaleWarningThreshold()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), ClawSensorProbeReaders.StaleWarningThreshold);
    }

    [Fact] public void TimingStatistics_ClassifiesFreshDuplicateNoDataAndFailureIndependently()
    {
        var timing = new ClawSensorProbeTimingStatistics();
        timing.Observe(ClawSensorReadOutcome.Fresh, 2, 0, 10);
        timing.Observe(ClawSensorReadOutcome.Fresh, 3, 0, 10);
        timing.Observe(ClawSensorReadOutcome.Duplicate, 1, 5);
        timing.Observe(ClawSensorReadOutcome.NoData, 1, 6);
        timing.Observe(ClawSensorReadOutcome.Failure, 0);

        Assert.Equal(2, timing.FreshCount);
        Assert.Equal(1, timing.DuplicateCount);
        Assert.Equal(1, timing.NoDataCount);
        Assert.Equal(1, timing.ReadFailureCount);
        Assert.Equal(10, timing.AverageFreshIntervalMs);
        Assert.Equal(100, timing.EffectiveFreshHz);
    }

    [Fact] public void TimingStatistics_QuietOrDuplicateReportsAccumulatePastFiveSecondsWithoutThrowing()
    {
        var timing = new ClawSensorProbeTimingStatistics();
        for (var i = 0; i < 6000; i++) timing.Observe(ClawSensorReadOutcome.NoData, 1, i);

        Assert.Equal(6000, timing.NoDataCount);
        Assert.True(timing.MaxFreshAgeMs >= (double)ClawSensorProbeReaders.StaleWarningThreshold.TotalMilliseconds);
    }

    [Fact] public void TimingStatistics_TracksMaxReadDurationAndLongReadCount()
    {
        var timing = new ClawSensorProbeTimingStatistics();
        timing.Observe(ClawSensorReadOutcome.Fresh, 5, 0, 10);
        timing.Observe(ClawSensorReadOutcome.Fresh, 150, 0, 10);

        Assert.Equal(150, timing.MaxReadDurationMs);
        Assert.Equal(150, timing.LastReadDurationMs);
        Assert.Equal(1, timing.LongReadCount);
    }

    [Fact] public void Discovery_PrefersUniqueWinRtGyroscopeOverAmbiguousLegacyCandidates()
    {
        var result = ClawSensorDiscovery.Select([
            new("Physical Gyrometer", "g1", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true),
            new("Physical Gyrometer", "g2", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true),
            new("WinRT Gyrometer", "winrt-gyro", "Unavailable", "Unavailable", Backend: ClawSensorProbeBackend.WinRtGyrometer),
            new("Physical Accelerometer", "a", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true)]);

        Assert.Equal(ClawSensorProbeBackend.WinRtGyrometer, result.Gyroscope?.Backend);
        Assert.True(result.IsValid);
    }

    [Fact] public void Discovery_PrefersDirectTypeValidatedAccelerometerOverBroadEnumerationMatch()
    {
        var result = ClawSensorDiscovery.Select([
            new("Physical Gyrometer", "g", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true),
            new("Physical Accelerometer", "a1", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true),
            new("Physical Accelerometer", "a2", "t2", "c2", IsDirectTypeMatch: true, SupportsX: true, SupportsY: true, SupportsZ: true)]);

        Assert.Equal("a2", result.Accelerometer?.SensorId);
        Assert.True(result.Accelerometer?.IsDirectTypeMatch);
    }

    [Fact] public void Discovery_AllowsDifferentBackendsForGyroscopeAndAccelerometer()
    {
        var result = ClawSensorDiscovery.Select([
            new("WinRT Gyrometer", "winrt-gyro", "Unavailable", "Unavailable", Backend: ClawSensorProbeBackend.WinRtGyrometer),
            new("Physical Accelerometer", "a", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true)]);

        Assert.Equal(ClawSensorProbeBackend.WinRtGyrometer, result.Gyroscope?.Backend);
        Assert.Equal(ClawSensorProbeBackend.LegacySensorApi, result.Accelerometer?.Backend);
        Assert.True(result.IsValid);
    }

    [Fact] public void Discovery_ReportsPartialDiscoveryWhenOnlyOneRoleIsResolvable()
    {
        var result = ClawSensorDiscovery.Select([new("WinRT Gyrometer", "winrt-gyro", "Unavailable", "Unavailable", Backend: ClawSensorProbeBackend.WinRtGyrometer)]);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Gyroscope);
        Assert.Null(result.Accelerometer);
        Assert.Contains(result.Errors, x => x.Contains("Accelerometer", StringComparison.Ordinal));
    }

    [Fact] public void Discovery_PreservesBroadQueryFailureAlongsideSuccessfulDirectTypeLookup()
    {
        var categoryAll = new LegacySensorQueryInfo("CategoryAll", "C317C286-C468-4288-9975-D4C4587C442C", null, false, unchecked((int)0x80070490), "COMException", []);
        var direct = new LegacySensorQueryInfo("DirectType", "E83AF229-8640-4D18-A213-E22675EBB2C3", "A2VM reference custom accelerometer type", true, 0, null, []);
        var winRtGyro = ClawSensorProbeWinRtEvidence.Unavailable;
        var winRtAccel = ClawSensorProbeWinRtEvidence.Unavailable;

        var result = ClawSensorDiscovery.Select(
            [new("Physical Gyrometer", "g", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true), new("Physical Accelerometer", "a", "t2", "c2", IsDirectTypeMatch: true, SupportsX: true, SupportsY: true, SupportsZ: true)],
            categoryAll, [direct], winRtGyro, winRtAccel);

        Assert.True(result.IsValid);
        Assert.Same(categoryAll, result.LegacyCategoryAll);
        Assert.False(result.LegacyCategoryAll!.Succeeded);
        Assert.Equal(unchecked((int)0x80070490), result.LegacyCategoryAll.HResult);
        Assert.Same(direct, result.LegacyDirectTypeQueries!.Single());
        Assert.True(result.LegacyDirectTypeQueries!.Single().Succeeded);
    }

    [Fact] public async Task Writer_PreservesBroadQueryFailureAndDirectTypeSuccessInFinalReport()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-discovery-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var hresult = unchecked((int)0x80070490);
            var categoryAll = new LegacySensorQueryInfo("CategoryAll", "C317C286-C468-4288-9975-D4C4587C442C", null, false, hresult, "COMException", []);
            var direct = new LegacySensorQueryInfo("DirectType", "E83AF229-8640-4D18-A213-E22675EBB2C3", "A2VM reference custom accelerometer type", true, 0, null, []);
            var discovery = ClawSensorDiscovery.Select(
                [new("Physical Gyrometer", "g", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true), new("Physical Accelerometer", "a", "t2", "c2", IsDirectTypeMatch: true, SupportsX: true, SupportsY: true, SupportsZ: true)],
                categoryAll, [direct], ClawSensorProbeWinRtEvidence.Unavailable, ClawSensorProbeWinRtEvidence.Unavailable);

            var writer = new ClawSensorProbeSessionWriter(root, "session");
            writer.SetDiscovery(discovery);
            await writer.DisposeAsync();

            var report = await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-report.json"));
            Assert.Contains($"\"HResult\": {hresult}", report);
            Assert.Contains("\"LegacyDirectTypeQueries\"", report);
            Assert.Contains("\"LegacyCategoryAll\"", report);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public void ComputeSensorAgeMs_ReflectsActualSensorTimestampAgeNotInterSampleInterval()
    {
        var receiveUtc = DateTimeOffset.Parse("2026-08-12T01:02:03.500Z");
        var sensorTimestamp = DateTimeOffset.Parse("2026-08-12T01:02:03.300Z");

        var age = ClawSensorProbeReaders.ComputeSensorAgeMs(receiveUtc, sensorTimestamp);

        Assert.Equal(200, age);
    }

    [Fact] public void ComputeSensorAgeMs_ReturnsNullForDefaultSensorTimestamp()
    {
        Assert.Null(ClawSensorProbeReaders.ComputeSensorAgeMs(DateTimeOffset.UtcNow, default));
    }

    [Theory]
    [InlineData(1.0, 2.0, 3.0, true)]
    [InlineData(double.NaN, 2.0, 3.0, false)]
    [InlineData(1.0, double.PositiveInfinity, 3.0, false)]
    [InlineData(1.0, 2.0, double.NegativeInfinity, false)]
    public void WinRtDiscovery_IsFiniteRejectsNaNAndInfiniteValues(double x, double y, double z, bool expected)
    {
        Assert.Equal(expected, ClawSensorProbeWinRtDiscovery.IsFinite(x, y, z));
    }

    [Fact] public void TimingStatistics_FailureOutcomeStillContributesToMaxReadDurationAndLongReadCount()
    {
        // Reproduces the fix: a backend read that blocks for a while and then throws must not lose its
        // read-duration evidence -- ClawSensorProbeReaders.RunAsync() now measures the failing attempt
        // and calls Observe(Failure, readDurationMs, freshAgeMs) with the real duration before rethrowing.
        var timing = new ClawSensorProbeTimingStatistics();

        timing.Observe(ClawSensorReadOutcome.Failure, 150, 5);

        Assert.Equal(1, timing.ReadFailureCount);
        Assert.Equal(150, timing.MaxReadDurationMs);
        Assert.Equal(150, timing.LastReadDurationMs);
        Assert.Equal(1, timing.LongReadCount);
    }

    [Fact] public void TimingStatistics_FreshOutcomeResetsCurrentAgeWhilePreservingMaxFreshAge()
    {
        // A prior stale/quiet gap must remain visible via MaxFreshAgeMs, but the CURRENT freshness age
        // (FreshAgeMs) must reflect that the source just reported -- otherwise a snapshot taken right
        // after recovery would still claim the source is 10 seconds stale.
        var timing = new ClawSensorProbeTimingStatistics();
        timing.Observe(ClawSensorReadOutcome.NoData, 1, 10_000);

        timing.Observe(ClawSensorReadOutcome.Fresh, 2, 10_000, 5);

        Assert.Equal(0, timing.FreshAgeMs);
        Assert.Equal(10_000, timing.MaxFreshAgeMs);
    }

    [Fact] public async Task Writer_SerializesEnumsAsNamedValuesNotNumericOrdinals()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-enum-names-" + Guid.NewGuid().ToString("N"));
        try
        {
            var winRtGyro = new ClawSensorProbeCandidate("WinRT Gyrometer", "winrt-gyro", "Unavailable", "Unavailable", Backend: ClawSensorProbeBackend.WinRtGyrometer, UnitBasis: ClawSensorProbeUnitBasis.DegreesPerSecond);
            var discovery = ClawSensorDiscovery.Select([winRtGyro, new("Physical Accelerometer", "a", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true)]);

            var writer = new ClawSensorProbeSessionWriter(root, "session");
            writer.SetDiscovery(discovery);
            await writer.DisposeAsync();

            var report = await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-report.json"));
            Assert.Contains("\"Backend\": \"WinRtGyrometer\"", report);
            Assert.Contains("\"UnitBasis\": \"DegreesPerSecond\"", report);
            Assert.DoesNotContain("\"Backend\": 1", report);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public void Discovery_SetsSelectionReasonOnLegacyGyroscopeFallback()
    {
        var result = ClawSensorDiscovery.Select([
            new("Physical Gyrometer", "g", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true),
            new("Physical Accelerometer", "a", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true)]);

        Assert.False(string.IsNullOrEmpty(result.Gyroscope?.SelectionReason));
    }

    [Fact] public void Discovery_SetsSelectionReasonOnLegacyAccelerometerBroadFallback()
    {
        var result = ClawSensorDiscovery.Select([
            new("Physical Gyrometer", "g", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true),
            new("Physical Accelerometer", "a", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true)]);

        Assert.False(string.IsNullOrEmpty(result.Accelerometer?.SelectionReason));
    }

    [Fact] public async Task Writer_OmitsAccelerometerMagnitudeWhenSelectedUnitBasisIsUnknown()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-magnitude-unknown-" + Guid.NewGuid().ToString("N"));
        try
        {
            var discovery = ClawSensorDiscovery.Select([
                new("Physical Gyrometer", "g", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true),
                new("Physical Accelerometer", "a", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true)]);

            var writer = new ClawSensorProbeSessionWriter(root, "session");
            writer.SetDiscovery(discovery);
            writer.Write(new(1, DateTimeOffset.UtcNow, 1, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.REST, 1, "ACCEL", 0, 0, 1, 1));
            await writer.DisposeAsync();

            var report = await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-report.json"));
            Assert.DoesNotContain("MagnitudeGMean", report);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task Writer_IncludesAccelerometerMagnitudeWhenSelectedUnitBasisIsG()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-magnitude-g-" + Guid.NewGuid().ToString("N"));
        try
        {
            var winRtAccel = new ClawSensorProbeCandidate("WinRT Accelerometer", "winrt-accel", "Unavailable", "Unavailable", Backend: ClawSensorProbeBackend.WinRtAccelerometer, UnitBasis: ClawSensorProbeUnitBasis.G);
            var discovery = ClawSensorDiscovery.Select([
                new("Physical Gyrometer", "g", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true),
                winRtAccel]);

            var writer = new ClawSensorProbeSessionWriter(root, "session");
            writer.SetDiscovery(discovery);
            writer.Write(new(1, DateTimeOffset.UtcNow, 1, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.REST, 1, "ACCEL", 0, 0, 1, 1));
            await writer.DisposeAsync();

            var report = await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-report.json"));
            Assert.Contains("MagnitudeGMean", report);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task Writer_ScopesCustomDataKeysToLegacyBackendInsteadOfGlobalField()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-legacy-keys-" + Guid.NewGuid().ToString("N"));
        try
        {
            var writer = new ClawSensorProbeSessionWriter(root, "session");
            await writer.DisposeAsync();

            var report = await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-report.json"));
            Assert.Contains("\"LegacyCustomDataKeys\"", report);
            Assert.DoesNotContain("\"DataKeys\"", report);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task Writer_EmitsSchemaVersionTwoWithTimingSummaryAndNoMisleadingBackendField()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-schema-v2-" + Guid.NewGuid().ToString("N"));
        try
        {
            var writer = new ClawSensorProbeSessionWriter(root, "session");
            var timing = new ClawSensorProbeTimingStatistics();
            timing.Observe(ClawSensorReadOutcome.Fresh, 2, 0, 10);
            writer.SetTiming(timing.Snapshot(), new ClawSensorProbeTimingStatistics().Snapshot());
            await writer.DisposeAsync();

            var report = await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-report.json"));
            Assert.Contains("\"SchemaVersion\": 2", report);
            Assert.Contains("\"TimingSummary\"", report);
            Assert.Contains("\"FreshCount\": 1", report);
            Assert.DoesNotContain("Windows Sensor API / ISensorManager", report);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task Writer_FreezesTimingSnapshotSoLateObserveAfterTeardownCannotCorruptFinalization()
    {
        // Reproduces the bounded-teardown race: a reader worker can still be running (still blocked in a
        // backend read) after ClawSensorProbeReaders.DisposeAsync() returns on the timeout path. The
        // coordinator must snapshot before handing timing to the writer, so a later Observe() call on the
        // still-live statistics object must not affect what was already frozen for finalization.
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-timing-freeze-" + Guid.NewGuid().ToString("N"));
        try
        {
            var timing = new ClawSensorProbeTimingStatistics();
            timing.Observe(ClawSensorReadOutcome.Fresh, 2, 0, 10);
            var frozen = timing.Snapshot();

            // Simulate the worker still running after the coordinator already captured its snapshot.
            timing.Observe(ClawSensorReadOutcome.Fresh, 3, 0, 12);
            timing.Observe(ClawSensorReadOutcome.Duplicate, 1, 5);

            var writer = new ClawSensorProbeSessionWriter(root, "session");
            writer.SetTiming(frozen, new ClawSensorProbeTimingStatistics().Snapshot());
            await writer.DisposeAsync();

            Assert.Equal(1, frozen.FreshCount);
            Assert.Equal(2, timing.FreshCount);
            var report = await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-report.json"));
            Assert.Contains("\"FreshCount\": 1", report);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task TimingStatistics_ConcurrentObserveAndSnapshotDoNotThrow()
    {
        var timing = new ClawSensorProbeTimingStatistics();
        using var stop = new CancellationTokenSource();
        var mutator = Task.Run(() =>
        {
            var i = 0;
            while (!stop.IsCancellationRequested) timing.Observe(ClawSensorReadOutcome.Fresh, 1, 0, ++i % 50 + 1);
        });

        for (var i = 0; i < 2000; i++) _ = timing.Snapshot();

        stop.Cancel();
        await mutator;
    }

    [Fact] public async Task ClawSensorProbeStatistics_ConcurrentAddAndReadDoNotThrow()
    {
        // WriteLoopAsync() calls Add() at sensor cadence while the frontend's ~200ms poll concurrently
        // reads AverageIntervalMs/MinimumIntervalMs/MaximumIntervalMs -- previously backed by a raw
        // List<double> enumerated on every read, so a normal live probe could intermittently throw
        // "Collection was modified" (PR B review finding #3). This is real producer/poller
        // concurrency, not a theoretical race.
        var statistics = new ClawSensorProbeStatistics();
        using var stop = new CancellationTokenSource();
        var mutator = Task.Run(() =>
        {
            var i = 0;
            while (!stop.IsCancellationRequested) statistics.Add(++i % 20 + 1);
        });

        for (var i = 0; i < 2000; i++)
        {
            _ = statistics.SampleCount;
            _ = statistics.AverageIntervalMs;
            _ = statistics.MinimumIntervalMs;
            _ = statistics.MaximumIntervalMs;
            _ = statistics.EffectiveHz;
        }

        stop.Cancel();
        await mutator;
    }

    [Fact] public void ClawSensorProbeStatistics_MinimumAndMaximumIntervalMsReflectAllAddedIntervals()
    {
        var statistics = new ClawSensorProbeStatistics();
        statistics.Add(5);
        statistics.Add(1);
        statistics.Add(9);
        statistics.Add(0); // non-positive intervals do not count toward min/max/average

        Assert.Equal(4, statistics.SampleCount);
        Assert.Equal(1, statistics.MinimumIntervalMs);
        Assert.Equal(9, statistics.MaximumIntervalMs);
        Assert.Equal(5, statistics.AverageIntervalMs);
    }

    [Fact] public async Task Writer_CsvHeaderAndRowsIncludeBackendReadDurationAndSensorAge()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-backend-columns-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var writer = new ClawSensorProbeSessionWriter(root, "session"))
            {
                writer.Write(new(1, DateTimeOffset.UtcNow, 1, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.REST, 1, "GYRO", 1, 2, 3, 1, null, "WinRtGyrometer", 4.5, 12.5));
            }

            var csv = await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-live.csv"));
            Assert.Contains("backend,read_duration_ms,sensor_age_ms", csv.Split('\n')[0]);
            Assert.Contains("WinRtGyrometer,4.5,12.5", csv);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task Writer_EmitsSourceConfigurationDistinguishingRequestedFromEffectiveInterval()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-source-config-" + Guid.NewGuid().ToString("N"));
        try
        {
            var writer = new ClawSensorProbeSessionWriter(root, "session");
            writer.SetSourceConfiguration(
                new ClawSensorProbeSourceConfiguration(ClawSensorProbeBackend.WinRtGyrometer, 5, 5, 8),
                new ClawSensorProbeSourceConfiguration(ClawSensorProbeBackend.LegacySensorApi, null, null, null));
            await writer.DisposeAsync();

            var report = await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-report.json"));
            Assert.Contains("\"SourceConfiguration\"", report);
            Assert.Contains("\"MinimumReportIntervalMs\": 5", report);
            Assert.Contains("\"RequestedReportIntervalMs\": 5", report);
            Assert.Contains("\"EffectiveReportIntervalMs\": 8", report);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task AdvancePhase_ClearsRecordingContextBeforeMovingToNextPhase()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-context-" + Guid.NewGuid().ToString("N"));
        var coordinator = new ClawSensorProbeCoordinator();
        try
        {
            coordinator.Prepare();
            coordinator.Start(root);
            coordinator.BeginRecording();
            Assert.Equal(ClawSensorCaptureMode.Recording, coordinator.CaptureContext.Mode);
            coordinator.AdvancePhase();
            Assert.Equal(ClawSensorCaptureMode.Transition, coordinator.CaptureContext.Mode);
            Assert.Equal(ClawSensorProbePhase.ROLL_LEFT, coordinator.CaptureContext.Phase);
        }
        finally
        {
            await coordinator.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public void PropVariant_ConvertsOnlySupportedScalarSensorTypes()
    {
        Assert.Equal(-2, ClawSensorProbeSensorApi.ConvertPropVariantForTest(new() { VarType = 3, Int32 = -2 }));
        Assert.Equal(4u, ClawSensorProbeSensorApi.ConvertPropVariantForTest(new() { VarType = 19, UInt32 = 4 }));
        Assert.Equal(1.25, ClawSensorProbeSensorApi.ConvertPropVariantForTest(new() { VarType = 4, Float = 1.25f }), 6);
        Assert.Equal(2.5, ClawSensorProbeSensorApi.ConvertPropVariantForTest(new() { VarType = 5, Double = 2.5 }), 6);
        Assert.Equal(0, ClawSensorProbeSensorApi.ConvertPropVariantForTest(new() { VarType = 11, VariantBool = 0 }));
        Assert.Equal(1, ClawSensorProbeSensorApi.ConvertPropVariantForTest(new() { VarType = 11, VariantBool = -1 }));
        Assert.Throws<InvalidOperationException>(() => ClawSensorProbeSensorApi.ConvertPropVariantForTest(new() { VarType = 31 }));
    }

    [Fact] public void PropVariantClear_UsesOle32EntryPoint()
    {
        var value = new ClawSensorProbeSensorApi.PropVariant();
        Assert.Equal(0, ClawSensorProbeSensorApi.ClearPropVariantForTest(ref value));
    }

    [Theory]
    [InlineData((int)HardwareCompatibilityStatus.Supported, true)]
    [InlineData((int)HardwareCompatibilityStatus.Unsupported, true)]
    [InlineData((int)HardwareCompatibilityStatus.Indeterminate, true)]
    public void DiagnosticEligibility_AllowsRecognizedClawFamilyRegardlessOfProductionModelStatus(int status, bool expected)
    {
        var hardware = new HardwareCompatibilityAssessment((HardwareCompatibilityStatus)status, new HandheldDeviceId("msi.claw"), null, "test");
        Assert.Equal(expected, ClawSensorProbeCoordinator.AllowsReadOnlyDiagnostic(hardware));
    }

    [Fact] public void DiagnosticEligibility_RejectsNonClawHardware()
    {
        var hardware = new HardwareCompatibilityAssessment(HardwareCompatibilityStatus.Unsupported, null, null, "No handheld-device adapter matched.");
        Assert.False(ClawSensorProbeCoordinator.AllowsReadOnlyDiagnostic(hardware));
    }

    [Fact] public async Task Writer_DoesNotLetTransitionSamplesContaminateRestSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-rest-transition-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var writer = new ClawSensorProbeSessionWriter(root, "session"))
            {
                writer.Write(new(1, DateTimeOffset.UtcNow, 1, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.REST, 1, "GYRO", 1, 1, 1, 1));
                writer.Write(new(2, DateTimeOffset.UtcNow, 2, ClawSensorCaptureMode.Transition, ClawSensorProbePhase.ROLL_LEFT, 1, "GYRO", 100, 100, 100, 1));
            }

            var report = await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-report.json"));
            Assert.Contains("\"SampleCount\": 1", report);
            Assert.DoesNotContain("\"X\": 100", report);
            Assert.DoesNotContain("\"Y\": 100", report);
            Assert.DoesNotContain("\"Z\": 100", report);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    // ---- SD6A PR B: capture modes and characterization summaries ----

    [Fact] public void Workflow_AxisStartStillVisitsFirstPhaseAndCountdown()
    {
        var workflow = new ClawSensorProbeWorkflow();
        workflow.Ready();
        workflow.Start(ClawSensorProbeMode.AxisCharacterization);
        Assert.Single(workflow.Visits);
        Assert.Equal(ClawSensorProbePhase.REST, workflow.Visits[0].Phase);
        Assert.Equal(ClawSensorProbeState.Countdown, workflow.State);
        Assert.Equal(0, workflow.CurrentIndex);
    }

    [Fact] public void Workflow_LiveStartDoesNotCreatePhaseVisitOrCountdown()
    {
        var workflow = new ClawSensorProbeWorkflow();
        workflow.Ready();
        workflow.Start(ClawSensorProbeMode.LiveSanity);
        Assert.Empty(workflow.Visits);
        Assert.Equal(-1, workflow.CurrentIndex);
        Assert.Equal(ClawSensorProbeState.Starting, workflow.State);
        workflow.BeginRecording();
        Assert.Equal(ClawSensorProbeState.RecordingPhase, workflow.State);
    }

    [Fact] public void Workflow_BiasStartDoesNotCreatePhaseVisitOrCountdown()
    {
        var workflow = new ClawSensorProbeWorkflow();
        workflow.Ready();
        workflow.Start(ClawSensorProbeMode.StationaryBias);
        Assert.Empty(workflow.Visits);
        Assert.Equal(-1, workflow.CurrentIndex);
        Assert.Equal(ClawSensorProbeState.Starting, workflow.State);
        workflow.BeginRecording();
        Assert.Equal(ClawSensorProbeState.RecordingPhase, workflow.State);
    }

    [Fact] public void Workflow_NextThrowsOutsideAxisMode()
    {
        var workflow = new ClawSensorProbeWorkflow();
        workflow.Ready(); workflow.Start(ClawSensorProbeMode.LiveSanity); workflow.BeginRecording();
        Assert.Throws<InvalidOperationException>(() => workflow.Next());
    }

    [Fact] public void Workflow_BackDoesNotMutateOutsideAxisMode()
    {
        var workflow = new ClawSensorProbeWorkflow();
        workflow.Ready(); workflow.Start(ClawSensorProbeMode.StationaryBias); workflow.BeginRecording();
        workflow.Back();
        Assert.Equal(ClawSensorProbeState.RecordingPhase, workflow.State);
        Assert.Equal(-1, workflow.CurrentIndex);
    }

    [Fact] public async Task Coordinator_LiveModeBeginsRecordingImmediatelyWithoutPhaseVisit()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-live-mode-" + Guid.NewGuid().ToString("N"));
        var coordinator = new ClawSensorProbeCoordinator();
        try
        {
            coordinator.Prepare();
            coordinator.Start(ClawSensorProbeMode.LiveSanity, root);
            coordinator.BeginRecording();
            Assert.Equal(ClawSensorProbeState.RecordingPhase, coordinator.State);
            Assert.Equal(-1, coordinator.Workflow.CurrentIndex);
            Assert.Empty(coordinator.Workflow.Visits);
        }
        finally
        {
            await coordinator.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task Coordinator_BiasModeStopDoesNotAssumeAPhaseVisitExists()
    {
        // Reproduces the fix: StopCoreAsync used to unconditionally call Workflow.Visits.Last(), which
        // throws on an empty list for Live/Bias sessions that never create a phase visit.
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-bias-stop-" + Guid.NewGuid().ToString("N"));
        var coordinator = new ClawSensorProbeCoordinator();
        try
        {
            coordinator.Prepare();
            coordinator.Start(ClawSensorProbeMode.StationaryBias, root);
            coordinator.BeginRecording();
            coordinator.Write(new(1, DateTimeOffset.UtcNow, 1, ClawSensorProbePhase.REST, 1, "GYRO", 1, 2, 3, 1));
            await coordinator.StopAsync();

            Assert.Equal(ClawSensorProbeState.Completed, coordinator.State);
            var report = await File.ReadAllTextAsync(Directory.GetFiles(root, "claw-sensor-report.json", SearchOption.AllDirectories).Single());
            Assert.Contains("\"CaptureMode\": \"StationaryBias\"", report);
            Assert.Contains("\"Phases\": []", report);
        }
        finally
        {
            await coordinator.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task Coordinator_LiveModePhaseNavigationDoesNotMutateSession()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-live-nav-" + Guid.NewGuid().ToString("N"));
        var coordinator = new ClawSensorProbeCoordinator();
        try
        {
            coordinator.Prepare();
            coordinator.Start(ClawSensorProbeMode.LiveSanity, root);
            coordinator.BeginRecording();

            await coordinator.AdvancePhaseAsync(_ => Task.CompletedTask, _ => "n/a", () => { }, CancellationToken.None);
            Assert.Equal(ClawSensorProbeState.RecordingPhase, coordinator.State);
            Assert.Equal(-1, coordinator.Workflow.CurrentIndex);

            await coordinator.RevisitPreviousPhaseAsync(_ => Task.CompletedTask, _ => "n/a", () => { }, CancellationToken.None);
            Assert.Equal(ClawSensorProbeState.RecordingPhase, coordinator.State);
            Assert.Equal(-1, coordinator.Workflow.CurrentIndex);
        }
        finally
        {
            await coordinator.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task Writer_LiveModeReportShapeIsEmptyPhasesAndNullBiasSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-live-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var writer = new ClawSensorProbeSessionWriter(root, "session", ClawSensorProbeMode.LiveSanity))
            {
                writer.Write(new(1, DateTimeOffset.UtcNow, 1, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.REST, 1, "GYRO", 1, 2, 3, 1));
                writer.Write(new(2, DateTimeOffset.UtcNow, 2, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.REST, 1, "ACCEL", 4, 5, 6, 1));
            }

            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-report.json")));
            var root2 = report.RootElement;
            Assert.Equal(2, root2.GetProperty("SchemaVersion").GetInt32());
            Assert.Equal("LiveSanity", root2.GetProperty("CaptureMode").GetString());
            Assert.Equal(0, root2.GetProperty("Phases").GetArrayLength());
            Assert.Equal(0, root2.GetProperty("PerPhaseSummaries").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, root2.GetProperty("StationaryBiasSummary").ValueKind);
            // Live Sanity's REST placeholder phase must not leak into RestSummary either.
            Assert.Equal(0, root2.GetProperty("RestSummary").GetProperty("Gyroscope").GetProperty("SampleCount").GetInt32());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task Writer_AxisModePerPhaseSummaryComputesMeanMinMaxSpanDurationAndRate()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-axis-summary-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var writer = new ClawSensorProbeSessionWriter(root, "session", ClawSensorProbeMode.AxisCharacterization))
            {
                writer.Write(new(1, DateTimeOffset.UtcNow, 10, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.ROLL_LEFT, 1, "GYRO", 1, 2, 3, 0));
                writer.Write(new(2, DateTimeOffset.UtcNow, 20, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.ROLL_LEFT, 1, "GYRO", 3, 4, 1, 10));
            }

            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-report.json")));
            var summary = report.RootElement.GetProperty("PerPhaseSummaries").EnumerateArray()
                .Single(e => e.GetProperty("Phase").GetString() == "ROLL_LEFT" && e.GetProperty("Pass").GetInt32() == 1 && e.GetProperty("Sensor").GetString() == "GYRO");

            Assert.Equal(2, summary.GetProperty("SampleCount").GetInt64());
            Assert.Equal(2, summary.GetProperty("MeanX").GetDouble());
            Assert.Equal(3, summary.GetProperty("MeanY").GetDouble());
            Assert.Equal(2, summary.GetProperty("MeanZ").GetDouble());
            Assert.Equal(1, summary.GetProperty("MinX").GetDouble());
            Assert.Equal(3, summary.GetProperty("MaxX").GetDouble());
            Assert.Equal(2, summary.GetProperty("SpanX").GetDouble());
            Assert.Equal(10, summary.GetProperty("DurationMs").GetDouble());
            Assert.Equal(100, summary.GetProperty("EffectiveHz").GetDouble());
            Assert.Equal(10, summary.GetProperty("StartElapsedMs").GetDouble());
            Assert.Equal(20, summary.GetProperty("EndElapsedMs").GetDouble());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task Writer_AxisModeRevisitedPhasePassesRemainDistinctInPerPhaseSummaries()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-axis-passes-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var writer = new ClawSensorProbeSessionWriter(root, "session", ClawSensorProbeMode.AxisCharacterization))
            {
                writer.Write(new(1, DateTimeOffset.UtcNow, 1, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.ROLL_LEFT, 1, "GYRO", 1, 1, 1, 0));
                writer.Write(new(2, DateTimeOffset.UtcNow, 2, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.ROLL_LEFT, 2, "GYRO", 9, 9, 9, 0));
            }

            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-report.json")));
            var summaries = report.RootElement.GetProperty("PerPhaseSummaries").EnumerateArray()
                .Where(e => e.GetProperty("Phase").GetString() == "ROLL_LEFT" && e.GetProperty("Sensor").GetString() == "GYRO")
                .ToArray();

            Assert.Equal(2, summaries.Length);
            Assert.Contains(summaries, e => e.GetProperty("Pass").GetInt32() == 1 && e.GetProperty("MeanX").GetDouble() == 1);
            Assert.Contains(summaries, e => e.GetProperty("Pass").GetInt32() == 2 && e.GetProperty("MeanX").GetDouble() == 9);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task Writer_AxisModePerPhaseSummaryIncludesZeroSampleSensorRatherThanDroppingIt()
    {
        // A source can be quiet/duplicate/no-data for an entire visit while the other source still
        // produces Fresh samples -- a realistic sensor behavior this diagnostic exists to characterize
        // (PR B review finding #2). The visit's GYRO summary must still appear with an explicit
        // SampleCount=0 rather than being silently absent from PerPhaseSummaries.
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-zero-sensor-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var writer = new ClawSensorProbeSessionWriter(root, "session", ClawSensorProbeMode.AxisCharacterization))
            {
                writer.BeginRecordingPhase(ClawSensorProbePhase.REST, 1, 10);
                writer.Write(new(1, DateTimeOffset.UtcNow, 20, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.REST, 1, "ACCEL", 0, 0, 1, 10));
                writer.EndPhase(ClawSensorProbePhase.REST, 1, 30);
            }

            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-report.json")));
            var summaries = report.RootElement.GetProperty("PerPhaseSummaries").EnumerateArray()
                .Where(e => e.GetProperty("Phase").GetString() == "REST" && e.GetProperty("Pass").GetInt32() == 1)
                .ToArray();

            Assert.Equal(2, summaries.Length);
            var gyro = Assert.Single(summaries, e => e.GetProperty("Sensor").GetString() == "GYRO");
            Assert.Equal(0, gyro.GetProperty("SampleCount").GetInt64());
            var accel = Assert.Single(summaries, e => e.GetProperty("Sensor").GetString() == "ACCEL");
            Assert.Equal(1, accel.GetProperty("SampleCount").GetInt64());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task Writer_BiasModeComputesGyroMeanStdDevMinMaxSpanAndAccelStability()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-bias-summary-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var writer = new ClawSensorProbeSessionWriter(root, "session", ClawSensorProbeMode.StationaryBias))
            {
                writer.Write(new(1, DateTimeOffset.UtcNow, 0, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.REST, 1, "GYRO", 1, 1, 1, 0));
                writer.Write(new(2, DateTimeOffset.UtcNow, 100, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.REST, 1, "GYRO", 3, 3, 3, 100));
                writer.Write(new(3, DateTimeOffset.UtcNow, 0, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.REST, 1, "ACCEL", 0, 0, 10, 0));
                writer.Write(new(4, DateTimeOffset.UtcNow, 100, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.REST, 1, "ACCEL", 0, 0, 12, 100));
            }

            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-report.json")));
            var bias = report.RootElement.GetProperty("StationaryBiasSummary");
            var gyro = bias.GetProperty("Gyroscope");
            Assert.Equal(2, gyro.GetProperty("SampleCount").GetInt64());
            Assert.Equal(2, gyro.GetProperty("MeanX").GetDouble());
            Assert.Equal(1, gyro.GetProperty("StandardDeviationX").GetDouble());
            Assert.Equal(1, gyro.GetProperty("MinX").GetDouble());
            Assert.Equal(3, gyro.GetProperty("MaxX").GetDouble());
            Assert.Equal(2, gyro.GetProperty("SpanX").GetDouble());

            var accel = bias.GetProperty("Accelerometer");
            Assert.Equal(2, accel.GetProperty("SampleCount").GetInt64());
            Assert.Equal(10, accel.GetProperty("MinZ").GetDouble());
            Assert.Equal(12, accel.GetProperty("MaxZ").GetDouble());
            Assert.Equal(2, accel.GetProperty("SpanZ").GetDouble());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task Writer_BiasModeIncludesAccelMagnitudeOnlyWhenUnitBasisIsG()
    {
        var rootKnownG = Path.Combine(Path.GetTempPath(), "claw-probe-bias-mag-g-" + Guid.NewGuid().ToString("N"));
        var rootUnknown = Path.Combine(Path.GetTempPath(), "claw-probe-bias-mag-unknown-" + Guid.NewGuid().ToString("N"));
        try
        {
            var winRtAccel = new ClawSensorProbeCandidate("WinRT Accelerometer", "winrt-accel", "Unavailable", "Unavailable", Backend: ClawSensorProbeBackend.WinRtAccelerometer, UnitBasis: ClawSensorProbeUnitBasis.G);
            var discoveryG = ClawSensorDiscovery.Select([
                new("Physical Gyrometer", "g", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true),
                winRtAccel]);
            var writerG = new ClawSensorProbeSessionWriter(rootKnownG, "session", ClawSensorProbeMode.StationaryBias);
            writerG.SetDiscovery(discoveryG);
            writerG.Write(new(1, DateTimeOffset.UtcNow, 0, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.REST, 1, "ACCEL", 0, 0, 1, 0));
            await writerG.DisposeAsync();
            var reportG = await File.ReadAllTextAsync(Path.Combine(rootKnownG, "session", "claw-sensor-report.json"));
            using var reportGDocument = JsonDocument.Parse(reportG);
            Assert.True(reportGDocument.RootElement.GetProperty("StationaryBiasSummary").GetProperty("Accelerometer").TryGetProperty("MagnitudeGMean", out _));

            var discoveryUnknown = ClawSensorDiscovery.Select([
                new("Physical Gyrometer", "g", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true),
                new("Physical Accelerometer", "a", "t", "c", SupportsX: true, SupportsY: true, SupportsZ: true)]);
            var writerUnknown = new ClawSensorProbeSessionWriter(rootUnknown, "session", ClawSensorProbeMode.StationaryBias);
            writerUnknown.SetDiscovery(discoveryUnknown);
            writerUnknown.Write(new(1, DateTimeOffset.UtcNow, 0, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.REST, 1, "ACCEL", 0, 0, 1, 0));
            await writerUnknown.DisposeAsync();
            var reportUnknown = await File.ReadAllTextAsync(Path.Combine(rootUnknown, "session", "claw-sensor-report.json"));
            using var reportUnknownDocument = JsonDocument.Parse(reportUnknown);
            Assert.False(reportUnknownDocument.RootElement.GetProperty("StationaryBiasSummary").GetProperty("Accelerometer").TryGetProperty("MagnitudeGMean", out _));
        }
        finally
        {
            if (Directory.Exists(rootKnownG)) Directory.Delete(rootKnownG, true);
            if (Directory.Exists(rootUnknown)) Directory.Delete(rootUnknown, true);
        }
    }

    [Fact] public async Task Writer_CsvHasProbeModeColumnDistinctFromCaptureMode()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-probe-mode-column-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var writer = new ClawSensorProbeSessionWriter(root, "session", ClawSensorProbeMode.AxisCharacterization))
            {
                writer.Write(new(1, DateTimeOffset.UtcNow, 1, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.REST, 1, "GYRO", 1, 2, 3, 1));
            }

            var csv = await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-live.csv"));
            var header = csv.Split('\n')[0];
            Assert.Contains("probe_mode", header);
            Assert.Contains("capture_mode", header);
            Assert.True(header.IndexOf("probe_mode", StringComparison.Ordinal) < header.IndexOf("capture_mode", StringComparison.Ordinal));
            Assert.Contains("AxisCharacterization,RECORDING,REST,1,GYRO", csv);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task Writer_LiveAndBiasModeCsvBlanksPhaseAndPhasePass()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-live-csv-blank-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var writer = new ClawSensorProbeSessionWriter(root, "session", ClawSensorProbeMode.LiveSanity))
            {
                writer.Write(new(1, DateTimeOffset.UtcNow, 1, ClawSensorCaptureMode.Recording, ClawSensorProbePhase.REST, 1, "GYRO", 1, 2, 3, 1));
            }

            var csv = await File.ReadAllTextAsync(Path.Combine(root, "session", "claw-sensor-live.csv"));
            Assert.Contains("LiveSanity,RECORDING,,,GYRO", csv);
            Assert.DoesNotContain("REST", csv);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task Coordinator_RecordingElapsedMsIsZeroBeforeRecordingBegins()
    {
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-recording-elapsed-zero-" + Guid.NewGuid().ToString("N"));
        var coordinator = new ClawSensorProbeCoordinator();
        try
        {
            coordinator.Prepare();
            coordinator.Start(ClawSensorProbeMode.LiveSanity, root);
            Assert.Equal(0, coordinator.RecordingElapsedMs);
        }
        finally
        {
            await coordinator.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task Coordinator_RecordingElapsedMsFreezesAtStopInsteadOfContinuingToGrow()
    {
        // The frontend labels this "elapsed capture time" -- it must exclude pre-recording discovery
        // time and stop advancing once the session has completed, rather than growing on every
        // subsequent ~200ms poll while the state is already Completed (PR B review follow-up finding #2).
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-recording-elapsed-freeze-" + Guid.NewGuid().ToString("N"));
        var coordinator = new ClawSensorProbeCoordinator();
        try
        {
            coordinator.Prepare();
            coordinator.Start(ClawSensorProbeMode.LiveSanity, root);
            coordinator.BeginRecording();
            await Task.Delay(20);
            await coordinator.StopAsync();

            var frozen = coordinator.RecordingElapsedMs;
            Assert.True(frozen >= 0);
            await Task.Delay(50);
            Assert.Equal(frozen, coordinator.RecordingElapsedMs);
        }
        finally
        {
            await coordinator.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task Coordinator_StartOverloadWithoutModeDefaultsToAxisCharacterization()
    {
        // Existing PR-A call sites that omit the mode argument must keep exercising exactly the
        // original seven-phase Axis behavior.
        var root = Path.Combine(Path.GetTempPath(), "claw-probe-default-mode-" + Guid.NewGuid().ToString("N"));
        var coordinator = new ClawSensorProbeCoordinator();
        try
        {
            coordinator.Prepare();
            coordinator.Start(root);
            Assert.Equal(ClawSensorProbeMode.AxisCharacterization, coordinator.Mode);
            Assert.Equal(ClawSensorProbeState.Countdown, coordinator.State);
        }
        finally
        {
            await coordinator.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
