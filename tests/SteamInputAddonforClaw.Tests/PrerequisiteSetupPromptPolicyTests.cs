using SteamInputAddonforClaw.Prerequisites;
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
}
