using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Input;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

[Collection("AppLog")]
public sealed class ControllerStateDiagnosticsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ControllerStateDiagnosticsTests", Guid.NewGuid().ToString("N"));

    public ControllerStateDiagnosticsTests()
    {
        Directory.CreateDirectory(_directory);
        AppLog.DirectoryOverride = _directory;
        AppLog.MinimumLevelOverride = AppLogLevel.Debug;
    }

    public void Dispose()
    {
        AppLog.DirectoryOverride = null;
        AppLog.MinimumLevelOverride = AppLogLevel.Info;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void DPadStateChangeIsLogged()
    {
        var before = Neutral();
        var after = before with { Buttons = before.Buttons with { DPadUp = true, DPadRight = true } };

        ControllerStateDiagnostics.LogChanges(before, after, session: 424241);

        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("DPadUp=False->True", log);
        Assert.Contains("DPadRight=False->True", log);
    }

    [Fact]
    public void UnchangedControllerStateDoesNotGenerateAChangeEntry()
    {
        var state = Neutral();

        ControllerStateDiagnostics.LogChanges(state, state, session: 424242);

        // No fields changed, so LogChanges never calls AppLog at all -- the log file may not
        // even exist yet in this test's isolated directory.
        var log = File.Exists(AppLog.CurrentLogFilePath) ? File.ReadAllText(AppLog.CurrentLogFilePath) : string.Empty;
        Assert.DoesNotContain("TestSession=424242", log);
    }

    [Fact]
    public void DPadReleaseIsLoggedIndependentlyFromOtherFields()
    {
        var before = Neutral() with { Buttons = Neutral().Buttons with { DPadDown = true } };
        var after = before with { Buttons = before.Buttons with { DPadDown = false, DPadLeft = true } };

        ControllerStateDiagnostics.LogChanges(before, after, session: 424243);

        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("DPadDown=True->False", log);
        Assert.Contains("DPadLeft=False->True", log);
    }

    [Fact]
    public void RawPovChangeIsLogged()
    {
        // Prime with a value distinct from every other value used in this test class, so this
        // assertion does not depend on the shared static "last POV" state left by other tests.
        ControllerStateDiagnostics.LogPovIfChanged(session: 1, pov: -20250001);
        ControllerStateDiagnostics.LogPovIfChanged(session: 1, pov: 9000);

        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains("POV=9000", log);
    }

    [Fact]
    public void RepeatedIdenticalPovDoesNotGenerateARedundantEntry()
    {
        ControllerStateDiagnostics.LogPovIfChanged(session: 2, pov: -20250002);
        ControllerStateDiagnostics.LogPovIfChanged(session: 2, pov: 18000);
        ControllerStateDiagnostics.LogPovIfChanged(session: 2, pov: 18000);
        ControllerStateDiagnostics.LogPovIfChanged(session: 2, pov: 18000);

        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Equal(1, log.Split("POV=18000", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void NewInputSessionAlwaysLogsAnInitialPovEvenIfItMatchesThePreviousSessionsLastValue()
    {
        // Session identity, not just the value, gates the "already logged" suppression -- a
        // brand new input session must never be silently skipped just because the physical
        // stick happens to already be at the same neutral/direction value as the last session.
        ControllerStateDiagnostics.LogPovIfChanged(session: 601, pov: 0);
        ControllerStateDiagnostics.LogPovIfChanged(session: 602, pov: 0);

        var lines = File.ReadAllText(AppLog.CurrentLogFilePath).Split('\n');
        Assert.Contains(lines, line => line.Contains("TestSession=601") && line.Contains("POV=0"));
        Assert.Contains(lines, line => line.Contains("TestSession=602") && line.Contains("POV=0"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4500)]
    [InlineData(13500)]
    [InlineData(22500)]
    [InlineData(27000)]
    [InlineData(31500)]
    public void AllObservedPovValuesIncludingDiagonalsAreLogged(int pov)
    {
        ControllerStateDiagnostics.LogPovIfChanged(session: 3, pov: pov - 1);
        ControllerStateDiagnostics.LogPovIfChanged(session: 3, pov: pov);

        var log = File.ReadAllText(AppLog.CurrentLogFilePath);
        Assert.Contains($"POV={pov}", log);
    }

    private static ControllerState Neutral() => new(default, default, default, default, new AuxiliaryButtonState([false, false]));
}
