using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics.EnvironmentDiscovery;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Status;
using Xunit;

namespace SteamInputAddonforClaw.Tests;

public sealed class EnvironmentDiscoveryReportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SteamInputAddonforClaw.EnvironmentDiscovery.{Guid.NewGuid():N}");

    [Fact]
    public void Writer_UsesVersionedSectionsAndDeterministicOrdering()
    {
        var report = new EnvironmentDiscoveryReportWriter().Write(Snapshot(
            processes: [new("zeta", 9, "C:\\z.exe", "", "", "", ""), new("Alpha", 3, "C:\\a.exe", "", "", "", "")],
            installed: [new("HKLM64", "b", "Zeta", "", "", ""), new("HKLM64", "a", "Alpha", "", "", "")]));

        Assert.Contains("SnapshotVersion: 1", report);
        Assert.True(report.IndexOf("=== SYSTEM ===", StringComparison.Ordinal) < report.IndexOf("=== CURRENT DETECTION ===", StringComparison.Ordinal));
        Assert.True(report.IndexOf("Name=Alpha; PID=3", StringComparison.Ordinal) < report.IndexOf("Name=zeta; PID=9", StringComparison.Ordinal));
        Assert.True(report.IndexOf("DisplayName=Alpha", StringComparison.Ordinal) < report.IndexOf("DisplayName=Zeta", StringComparison.Ordinal));
        Assert.DoesNotContain("CommandLine", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Writer_PreservesPartialFailureWithoutDroppingOtherSections()
    {
        var snapshot = Snapshot(processes: []) with { Services = new DiscoverySection<ServiceDiscoveryInfo>([], "UnauthorizedAccessException") };

        var report = new EnvironmentDiscoveryReportWriter().Write(snapshot);

        Assert.Contains("=== WINDOWS SERVICES ===\r\n<InspectionFailed: UnauthorizedAccessException>", report);
        Assert.Contains("=== ROUTING PREREQUISITES ===", report);
    }

    [Fact]
    public async Task Generator_WritesNewReportForTimestampCollision()
    {
        var timestamp = new DateTimeOffset(2026, 8, 9, 17, 30, 12, TimeSpan.Zero);
        var generator = new EnvironmentDiscoveryReportGenerator(new FakeSource(Snapshot(processes: [])), new EnvironmentDiscoveryReportStore(_directory), new EnvironmentDiscoveryReportWriter(), () => timestamp);

        var first = await generator.GenerateAsync();
        var second = await generator.GenerateAsync();

        Assert.Equal("EnvironmentDiscovery-20260809-173012.log", first.ReportFileName);
        Assert.Equal("EnvironmentDiscovery-20260809-173012-2.log", second.ReportFileName);
        Assert.True(File.Exists(first.ReportPath));
        Assert.True(File.Exists(second.ReportPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static EnvironmentDiscoverySnapshot Snapshot(IReadOnlyList<ProcessDiscoveryInfo> processes, IReadOnlyList<InstalledApplicationDiscoveryInfo>? installed = null) => new(
        new DateTimeOffset(2026, 8, 9, 17, 30, 12, TimeSpan.Zero),
        new SystemDiscoveryInfo("Windows 11", "26100", "x64", "MSI", "Claw", ["Intel Arc"], "1.0.0"),
        new DiscoverySection<CurrentDetectionDiscoveryInfo>([new CurrentDetectionDiscoveryInfo(
            [new ControllerSoftwareStatus(ControllerSoftwareKind.MsiCenterM, "MSI Center M", SoftwareInstallationStatus.Installed, SoftwareRuntimeStatus.Running, "Running")],
            new ControllerEnvironment(ControllerEnvironmentMode.StockCenterM, ClawTweaksState.NotInstalled), ControllerEnvironmentReadiness.Stable)]),
        new DiscoverySection<ProcessDiscoveryInfo>(processes),
        new DiscoverySection<ServiceDiscoveryInfo>([new("svc", "Service", "Running", "Automatic", "C:\\svc.exe")]),
        new DiscoverySection<InstalledApplicationDiscoveryInfo>(installed ?? []),
        new DiscoverySection<AppPackageDiscoveryInfo>([new("Package", "Family", "Full", "Publisher", "C:\\Package", "1.0")]),
        new DiscoverySection<StartupRegistrationDiscoveryInfo>([new("HKCU\\Run", "Startup", "C:\\Startup.exe")]),
        new DiscoverySection<ScheduledTaskDiscoveryInfo>([new("\\", "Task", "True", "Ready", "C:\\Task.exe")]),
        new DiscoverySection<ControllerDeviceInfo>([]),
        new DiscoverySection<ExternalControllerAssessment>([new(ExternalControllerAssessmentStatus.Clear, 0, [])]),
        new DiscoverySection<RuntimePrerequisiteAssessment>([new(new(PrerequisiteKind.HidHide, PrerequisiteStatus.Missing, "Missing"), new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Missing, "Missing"), new(PrerequisiteKind.Viiper, PrerequisiteStatus.Missing, "Missing"))]));

    private sealed class FakeSource(EnvironmentDiscoverySnapshot snapshot) : IEnvironmentDiscoverySnapshotSource
    {
        public EnvironmentDiscoverySnapshot Capture(DateTimeOffset capturedAt) => snapshot with { CapturedAt = capturedAt };
    }
}
