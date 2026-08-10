using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.HidHide;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class PrerequisiteSetupPromptPolicyTests
{
    [Fact]
    public void RequiredInstallableStateRequestsForegroundAndAllowsInstall()
    {
        var assessment = new FirstTimeSetupAssessment(FirstTimeSetupStatus.Required, FirstTimeSetupReason.MissingComponents, true);

        Assert.True(PrerequisiteSetupPromptPolicy.IsInstallable(assessment));
        Assert.True(PrerequisiteSetupPromptPolicy.RequiresForegroundActivation(assessment));
    }

    [Theory]
    [InlineData((int)FirstTimeSetupStatus.Complete, (int)FirstTimeSetupReason.Complete, false)]
    [InlineData((int)FirstTimeSetupStatus.Blocked, (int)FirstTimeSetupReason.ExternalController, false)]
    [InlineData((int)FirstTimeSetupStatus.Indeterminate, (int)FirstTimeSetupReason.CompatibilityIndeterminate, false)]
    [InlineData((int)FirstTimeSetupStatus.Required, (int)FirstTimeSetupReason.SteamActive, false)]
    public void NonInstallableStateStaysPassive(int status, int reason, bool canInstall)
    {
        var assessment = new FirstTimeSetupAssessment((FirstTimeSetupStatus)status, (FirstTimeSetupReason)reason, canInstall);

        Assert.False(PrerequisiteSetupPromptPolicy.IsInstallable(assessment));
        Assert.False(PrerequisiteSetupPromptPolicy.RequiresForegroundActivation(assessment));
    }

    [Fact]
    public async Task StateChangedBeforeInstall_BlocksRunner()
    {
        var runner = new FakeRunner();
        var latest = new FirstTimeSetupAssessment(FirstTimeSetupStatus.Required, FirstTimeSetupReason.SteamActive, false);

        var result = await PrerequisiteSetupRunnerPolicy.RunIfInstallableAsync(latest, runner, "app.exe", "setup", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task StillInstallableBeforeInstall_RunsRunnerOnce()
    {
        var runner = new FakeRunner();
        var latest = new FirstTimeSetupAssessment(FirstTimeSetupStatus.Required, FirstTimeSetupReason.MissingComponents, true);

        var result = await PrerequisiteSetupRunnerPolicy.RunIfInstallableAsync(latest, runner, "app.exe", "setup", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, runner.CallCount);
    }

    private sealed class FakeRunner : IElevatedProcessRunner
    {
        public int CallCount { get; private set; }
        public Task<ElevatedProcessResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new ElevatedProcessResult(ElevatedProcessResultKind.Completed, 0, null));
        }
    }
}
