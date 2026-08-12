using SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;
using Xunit;

namespace SteamInputAddonforClaw.Tests.ClawSensorProbe;

public sealed class ClawSensorProbeTests
{
    [Fact] public void Discovery_FailsClosedForAmbiguousCandidates()
    {
        var result = ClawSensorDiscovery.Select([new("Physical Gyrometer", "g1", "t", "c"), new("Physical Gyrometer", "g2", "t", "c"), new("Physical Accelerometer", "a1", "t", "c"), new("Other", "o", "t", "c")]);
        Assert.False(result.IsValid); Assert.Null(result.Gyroscope); Assert.NotNull(result.Accelerometer); Assert.Contains(result.Sensors, x => x.FriendlyName == "Other");
    }
    [Fact] public void Discovery_FailsClosedWhenEitherRequiredSensorIsMissing()
    {
        Assert.False(ClawSensorDiscovery.Select([new("Physical Gyrometer", "g", "t", "c")]).IsValid);
        Assert.False(ClawSensorDiscovery.Select([new("Physical Accelerometer", "a", "t", "c")]).IsValid);
    }
    [Fact] public void Candidate_UsesSensorIdAsDocumentedPersistentUniqueId()
    {
        var result = ClawSensorDiscovery.Select([new("Physical Gyrometer", "persistent-id", "t", "c", PersistentUniqueId: "persistent-id"), new("Physical Accelerometer", "a", "t", "c")]);
        Assert.Equal("persistent-id", result.Gyroscope?.PersistentUniqueId);
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
}
