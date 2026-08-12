using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class ControllerEnvironmentAssessmentProviderTests
{
    [Fact]
    public void StockCenterM_MapsToSupportedStartupEnvironment()
    {
        var assessment = Provider(Stock(), Absent(ControllerSoftwareKind.ClawTweaks), Absent(ControllerSoftwareKind.HandheldCompanion)).Capture();

        Assert.Equal(ControllerManagerKind.None, assessment.Manager.Kind);
        Assert.True(assessment.Compatibility.AllowsMutation);
        Assert.Equal(ControllerEnvironmentMode.StockCenterM, StartupControllerEnvironmentMapper.Map(assessment).Mode);
    }

    [Theory]
    [InlineData((int)ControllerSoftwareKind.ClawTweaks)]
    [InlineData((int)ControllerSoftwareKind.HandheldCompanion)]
    public void UnsupportedManager_MapsToPassiveStartupEnvironment(int managerKindValue)
    {
        var managerKind = (ControllerSoftwareKind)managerKindValue;
        var other = managerKind == ControllerSoftwareKind.ClawTweaks ? ControllerSoftwareKind.HandheldCompanion : ControllerSoftwareKind.ClawTweaks;
        var assessment = Provider(Stock(), Present(managerKind), Absent(other)).Capture();

        Assert.False(assessment.Compatibility.AllowsMutation);
        Assert.NotEqual(ControllerEnvironmentMode.StockCenterM, StartupControllerEnvironmentMapper.Map(assessment).Mode);
    }

    [Fact]
    public void Capture_RecapturesLiveSoftwareState()
    {
        var clawTweaks = new MutableProvider(Absent(ControllerSoftwareKind.ClawTweaks));
        var provider = Provider(Stock(), clawTweaks, Absent(ControllerSoftwareKind.HandheldCompanion));
        Assert.Equal(ControllerEnvironmentMode.StockCenterM, StartupControllerEnvironmentMapper.Map(provider.Capture()).Mode);

        clawTweaks.Status = Present(ControllerSoftwareKind.ClawTweaks);
        var second = provider.Capture();
        Assert.Equal(ControllerManagerKind.ClawTweaks, second.Manager.Kind);
        Assert.Equal(ControllerEnvironmentMode.Unsupported, StartupControllerEnvironmentMapper.Map(second).Mode);
    }

    [Theory]
    [InlineData((int)SoftwareInstallationStatus.NotInstalled, (int)SoftwareRuntimeStatus.NotRunning, (int)ClawTweaksState.NotInstalled)]
    [InlineData((int)SoftwareInstallationStatus.Installed, (int)SoftwareRuntimeStatus.NotRunning, (int)ClawTweaksState.InstalledInactive)]
    [InlineData((int)SoftwareInstallationStatus.Installed, (int)SoftwareRuntimeStatus.Running, (int)ClawTweaksState.Active)]
    public void StartupMapper_PreservesClawTweaksDiagnosticState(int installationValue, int runtimeValue, int expectedStateValue)
    {
        var clawTweaks = new ControllerSoftwareStatus(ControllerSoftwareKind.ClawTweaks, "ClawTweaks", (SoftwareInstallationStatus)installationValue, (SoftwareRuntimeStatus)runtimeValue, "test");
        var assessment = Provider(Stock(), clawTweaks, Absent(ControllerSoftwareKind.HandheldCompanion)).Capture();

        Assert.Equal((ClawTweaksState)expectedStateValue, StartupControllerEnvironmentMapper.Map(assessment).ClawTweaksState);
    }

    [Theory]
    [InlineData((int)SoftwareInstallationStatus.NotInstalled, (int)SoftwareRuntimeStatus.NotRunning, (int)ControllerEnvironmentCompatibilityStatus.Unsupported)]
    [InlineData((int)SoftwareInstallationStatus.Installed, (int)SoftwareRuntimeStatus.Starting, (int)ControllerEnvironmentCompatibilityStatus.Indeterminate)]
    public void CenterMUnavailable_PreservesStockBaselineEligibilityWhileRuntimeCompatibilityRemainsBlocked(int installationValue, int runtimeValue, int compatibilityValue)
    {
        var centerM = new ControllerSoftwareStatus(ControllerSoftwareKind.MsiCenterM, "MSI Center M", (SoftwareInstallationStatus)installationValue, (SoftwareRuntimeStatus)runtimeValue, "test");
        var assessment = Provider(centerM, Absent(ControllerSoftwareKind.ClawTweaks), Absent(ControllerSoftwareKind.HandheldCompanion)).Capture();

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
    private static ControllerSoftwareStatus Present(ControllerSoftwareKind kind) => new(kind, kind.ToString(), SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.NotRunning, "test");
    private sealed class FixedProvider(ControllerSoftwareStatus status) : IControllerSoftwareStatusProvider { public ControllerSoftwareStatus Capture() => status; }
    private sealed class MutableProvider(ControllerSoftwareStatus status) : IControllerSoftwareStatusProvider { public ControllerSoftwareStatus Status { get; set; } = status; public ControllerSoftwareStatus Capture() => Status; }
}
