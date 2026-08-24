using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ControllerEnvironmentAssessmentProviderTests
{
    [Fact]
    public void StockCenterM_MapsToSupportedStartupEnvironment()
    {
        var assessment = Provider(Stock()).Capture();

        Assert.Equal(ControllerManagerKind.None, assessment.Manager.Kind);
        Assert.True(assessment.Compatibility.AllowsMutation);
        Assert.Equal(ControllerEnvironmentMode.StockCenterM, StartupControllerEnvironmentMapper.Map(assessment).Mode);
    }


    [Theory]
    [InlineData((int)SoftwareInstallationStatus.NotInstalled, (int)SoftwareRuntimeStatus.NotRunning, (int)ControllerEnvironmentCompatibilityStatus.Unsupported)]
    [InlineData((int)SoftwareInstallationStatus.Installed, (int)SoftwareRuntimeStatus.Starting, (int)ControllerEnvironmentCompatibilityStatus.Indeterminate)]
    public void CenterMUnavailable_PreservesStockBaselineEligibilityWhileRuntimeCompatibilityRemainsBlocked(int installationValue, int runtimeValue, int compatibilityValue)
    {
        var centerM = new ControllerSoftwareStatus(ControllerSoftwareKind.MsiCenterM, "MSI Center M", (SoftwareInstallationStatus)installationValue, (SoftwareRuntimeStatus)runtimeValue, "test");
        var assessment = Provider(centerM).Capture();

        Assert.Equal((ControllerEnvironmentCompatibilityStatus)compatibilityValue, assessment.Compatibility.Status);
        Assert.Equal(ControllerEnvironmentMode.StockCenterM, StartupControllerEnvironmentMapper.Map(assessment).Mode);
    }

    private static ControllerEnvironmentAssessmentProvider Provider(params object[] inputs) =>
        new(inputs.Select(input => input switch
        {
            ControllerSoftwareStatus status => (IControllerSoftwareStatusProvider)new FixedProvider(status),
            IControllerSoftwareStatusProvider provider => provider,
            _ => throw new InvalidOperationException()
        }).ToArray());

    private static ControllerSoftwareStatus Stock() => new(ControllerSoftwareKind.MsiCenterM, "MSI Center M", SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.Running, "test");
    private static ControllerSoftwareStatus Absent(ControllerSoftwareKind kind) => new(kind, kind.ToString(), SoftwareInstallationStatus.NotInstalled, SoftwareRuntimeStatus.NotRunning, "test");
    private sealed class FixedProvider(ControllerSoftwareStatus status) : IControllerSoftwareStatusProvider { public ControllerSoftwareStatus Capture() => status; }
}
