using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Input;
using SteamInputAddonforClaw.Routing;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class DiagnosticLoggingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SteamInputAddonforClaw-DiagnosticTests", Guid.NewGuid().ToString("N"));

    public DiagnosticLoggingTests()
    {
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
    }

    [Fact]
    public void ControllerStateDiagnostics_LogsChangedControlsAndAnalogValues()
    {
        var oldState = new ControllerState(new GamepadButtons(), default, default, default, new AuxiliaryButtonState([false, false]));
        var newState = new ControllerState(oldState.Buttons with { A = true, LeftTriggerFull = true }, new(1, 2), new(3, 4), new(5, 6), new AuxiliaryButtonState([false, true]));

        ControllerStateDiagnostics.LogChanges(oldState, newState, 7);

        var log = File.ReadAllText(Directory.GetFiles(_directory)[0]);
        Assert.Contains("A=", log);
        Assert.Contains("LTFull=", log);
        Assert.Contains("M1=", log);
        Assert.Contains("Analog=", log);
        Assert.Contains("TestSession=7", log);
    }

    [Fact]
    public void DiagnosticSession_LogsActualAndEffectiveIdentity()
    {
        var session = DiagnosticSession.Start(123, 123, "Actual");
        DiagnosticSession.Complete(session, ("RecoveryClean", true));

        var log = File.ReadAllText(Directory.GetFiles(_directory)[0]);
        Assert.Contains("Session started", log);
        Assert.Contains("Session completed", log);
        Assert.Contains("RawRunningAppID=123", log);
        Assert.Contains("EffectiveSource=Actual", log);
        Assert.Contains("RecoveryClean=True", log);
    }

    [Fact]
    public void DiagnosticSessionTracker_SplitsDeveloperTestAndActualIdentities()
    {
        var tracker = new DiagnosticSessionTracker();
        tracker.Observe(0, uint.MaxValue, "DeveloperTest");
        tracker.Observe(123, 123, "Actual");
        tracker.Observe(0, uint.MaxValue, "DeveloperTest");
        tracker.Complete();

        var log = File.ReadAllText(Directory.GetFiles(_directory)[0]);
        Assert.Equal(3, log.Split("Session started", StringSplitOptions.None).Length - 1);
        Assert.Equal(3, log.Split("Session completed", StringSplitOptions.None).Length - 1);
        Assert.Contains("EffectiveSource=DeveloperTest", log);
        Assert.Contains("RawRunningAppID=123", log);
    }

    [Fact]
    public async Task RoutingTrace_IsDebugOnlyAndCorrelatesExecutions()
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
        await new RoutingPipelineExecutor([]).ExecuteAsync(RoutingPipelinePlan.AllDisabled, CancellationToken.None);
        await new RoutingPipelineExecutor([]).ExecuteAsync(RoutingPipelinePlan.AllDisabled, CancellationToken.None);

        var log = File.ReadAllText(Directory.GetFiles(_directory)[0]);
        var executions = System.Text.RegularExpressions.Regex.Matches(log, @"RoutingExecution=(\d+)")
            .Select(match => match.Groups[1].Value).Distinct().ToArray();
        Assert.True(executions.Length >= 2);
        Assert.Contains("[DEBUG] [P", log);
        Assert.Contains("[RoutingTrace] Routing activation started.", log);
        Assert.Contains("[RoutingTrace] Routing activation completed.", log);
    }

    [Fact]
    public async Task RoutingTrace_IsFilteredAtInfoLevel()
    {
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        await new RoutingPipelineExecutor([]).ExecuteAsync(RoutingPipelinePlan.AllDisabled, CancellationToken.None);

        Assert.False(Directory.Exists(_directory) && Directory.GetFiles(_directory).Any(file => File.ReadAllText(file).Contains("[RoutingTrace]", StringComparison.Ordinal)));
    }

    public void Dispose()
    {
        AppLog.DirectoryOverride = null;
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
