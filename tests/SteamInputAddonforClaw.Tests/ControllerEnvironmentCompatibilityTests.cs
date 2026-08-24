using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ControllerEnvironmentCompatibilityTests
{
    private readonly CurrentControllerEnvironmentCompatibilityPolicy _policy = new();

    [Fact]
    public void StockCenterMOnly_IsSupported()
    {
        var assessment = _policy.Evaluate([Status(SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.Running)]);

        Assert.Equal(ControllerEnvironmentCompatibilityStatus.Supported, assessment.Status);
        Assert.Equal(ControllerEnvironmentCompatibilityReason.StockCenterMOnlySupported, assessment.Reason);
    }

    [Theory]
    [InlineData((int)SoftwareInstallationStatus.NotInstalled, (int)SoftwareRuntimeStatus.NotRunning, (int)ControllerEnvironmentCompatibilityStatus.Unsupported, (int)ControllerEnvironmentCompatibilityReason.MsiCenterMRequired)]
    [InlineData((int)SoftwareInstallationStatus.Installed, (int)SoftwareRuntimeStatus.NotRunning, (int)ControllerEnvironmentCompatibilityStatus.Unsupported, (int)ControllerEnvironmentCompatibilityReason.MsiCenterMNotOperational)]
    [InlineData((int)SoftwareInstallationStatus.Installed, (int)SoftwareRuntimeStatus.Starting, (int)ControllerEnvironmentCompatibilityStatus.Indeterminate, (int)ControllerEnvironmentCompatibilityReason.MsiCenterMStarting)]
    [InlineData((int)SoftwareInstallationStatus.Indeterminate, (int)SoftwareRuntimeStatus.Indeterminate, (int)ControllerEnvironmentCompatibilityStatus.Indeterminate, (int)ControllerEnvironmentCompatibilityReason.ControllerSoftwareStateIndeterminate)]
    public void CenterMStates_AreFailClosed(int installationValue, int runtimeValue, int expectedStatusValue, int expectedReasonValue)
    {
        var assessment = _policy.Evaluate([Status((SoftwareInstallationStatus)installationValue, (SoftwareRuntimeStatus)runtimeValue)]);

        Assert.Equal((ControllerEnvironmentCompatibilityStatus)expectedStatusValue, assessment.Status);
        Assert.Equal((ControllerEnvironmentCompatibilityReason)expectedReasonValue, assessment.Reason);
    }

    [Fact]
    public void MissingCenterM_IsIndeterminate()
    {
        var assessment = _policy.Evaluate([]);

        Assert.Equal(ControllerEnvironmentCompatibilityStatus.Indeterminate, assessment.Status);
        Assert.False(assessment.AllowsMutation);
    }

    [Fact]
    public void UnsupportedManagerClassificationRemainsFailClosedWhenReached()
    {
        var classification = new ControllerManagerClassification((ControllerManagerKind)999, ControllerManagerClassificationReason.ControllerManagerStateIndeterminate);
        var assessment = CurrentControllerEnvironmentCompatibilityPolicy.MapClassification(classification, Status(SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.Running));

        Assert.Equal(ControllerEnvironmentCompatibilityStatus.Indeterminate, assessment.Status);
        Assert.False(assessment.AllowsMutation);
    }

    private static ControllerSoftwareStatus Status(SoftwareInstallationStatus installation, SoftwareRuntimeStatus runtime) =>
        new(ControllerSoftwareKind.MsiCenterM, "MSI Center M", installation, runtime, "test");
}
