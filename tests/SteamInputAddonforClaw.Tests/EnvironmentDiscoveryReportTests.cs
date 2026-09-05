using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Diagnostics.ClawSensorProbe;
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

        Assert.Contains("SnapshotVersion: 2", report);
        Assert.True(report.IndexOf("=== SYSTEM ===", StringComparison.Ordinal) < report.IndexOf("=== RUNNING PROCESSES ===", StringComparison.Ordinal));
        Assert.True(report.IndexOf("Name=Alpha; PID=3", StringComparison.Ordinal) < report.IndexOf("Name=zeta; PID=9", StringComparison.Ordinal));
        Assert.True(report.IndexOf("DisplayName=Alpha", StringComparison.Ordinal) < report.IndexOf("DisplayName=Zeta", StringComparison.Ordinal));
        Assert.True(report.IndexOf("=== CONTROLLER / PNP DEVICES ===", StringComparison.Ordinal) < report.IndexOf("=== WINDOWS MOTION / SENSOR DISCOVERY ===", StringComparison.Ordinal));
        Assert.True(report.IndexOf("=== WINDOWS MOTION / SENSOR DISCOVERY ===", StringComparison.Ordinal) < report.IndexOf("=== ROUTING PREREQUISITES ===", StringComparison.Ordinal));
        Assert.DoesNotContain("CommandLine", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Writer_ReportsWinRtSensorUnavailabilityWithoutFailingReport()
    {
        var snapshot = Snapshot(processes: []) with
        {
            MotionSensors = DefaultMotionSensors() with
            {
                WinRtGyrometer = new WinRtSensorDiscoveryInfo(false, null, null, "TypeLoadException"),
                WinRtAccelerometer = new WinRtSensorDiscoveryInfo(true, "\\\\?\\ACCEL#1", 10, null)
            }
        };

        var report = new EnvironmentDiscoveryReportWriter().Write(snapshot);

        Assert.Contains("WinRT Gyrometer:\r\nAvailable=False\r\nFailure=TypeLoadException", report);
        Assert.Contains("WinRT Accelerometer:\r\nAvailable=True\r\nDeviceId=\\\\?\\ACCEL#1\r\nMinimumReportIntervalMs=10", report);
        Assert.Contains("=== ROUTING PREREQUISITES ===", report);
    }

    [Fact]
    public void Writer_PreservesLegacyCategoryAllHResultIndependentlyOfDirectTypeSuccess()
    {
        var candidate = new LegacySensorCandidateInfo("Physical Accelerometer", "id-1", "type-1", "cat-1", "Ready", "Vendor", "Model", "puid-1", "\\\\?\\ACCEL#1", "10", "0", "True", "True", "True");
        var snapshot = Snapshot(processes: []) with
        {
            MotionSensors = DefaultMotionSensors() with
            {
                LegacyCategoryAll = new LegacySensorQueryInfo("CategoryAll", "C317C286-C468-4288-9975-D4C4587C442C", null, false, unchecked((int)0x80070490), "COMException", []),
                LegacyDirectTypeQueries = [new LegacySensorQueryInfo("DirectType", "E83AF229-8640-4D18-A213-E22675EBB2C3", "A2VM reference custom accelerometer type", true, 0, null, [candidate])]
            }
        };

        var report = new EnvironmentDiscoveryReportWriter().Write(snapshot);

        Assert.Contains("Legacy CategoryAll:\r\nTypeGuid=C317C286-C468-4288-9975-D4C4587C442C\r\nSucceeded=False\r\nHResult=0x80070490", report);
        Assert.Contains("Label=A2VM reference custom accelerometer type", report);
        Assert.Contains("Succeeded=True", report);
        Assert.Contains("FriendlyName=Physical Accelerometer", report);
    }

    [Fact]
    public void Writer_EmitsEmptyCandidateListsExplicitly()
    {
        var snapshot = Snapshot(processes: []) with
        {
            MotionSensors = DefaultMotionSensors() with
            {
                LegacyDirectTypeQueries = [new LegacySensorQueryInfo("DirectType", "E83AF229-8640-4D18-A213-E22675EBB2C3", "A2VM reference custom accelerometer type", true, 0, null, [])]
            }
        };

        var report = new EnvironmentDiscoveryReportWriter().Write(snapshot);

        Assert.Contains("CandidateCount=0", report);
    }

    [Fact]
    public void Writer_DerivesMotionPnPSubsetFromExistingDeviceListWithoutSecondScan()
    {
        var snapshot = Snapshot(
            processes: [],
            devices:
            [
                new("HID\\SENSOR\\1", null, null, [], "HID", [], [], "Sensor", "{Sensor}", "hidsensor", null, null, true, "Intel(R) ISH Sensor", 0x20, 0x0200),
                new("HID\\OTHER\\1", null, null, [], "HID", [], [], "Keyboard", "{Keyboard}", "hidusb", null, null, true, "Some Keyboard", 0x01, 0x0006)
            ]);

        var report = new EnvironmentDiscoveryReportWriter().Write(snapshot);
        var motionSectionStart = report.IndexOf("=== WINDOWS MOTION / SENSOR DISCOVERY ===", StringComparison.Ordinal);
        var motionSectionEnd = report.IndexOf("=== ROUTING PREREQUISITES ===", StringComparison.Ordinal);
        var motionSection = report[motionSectionStart..motionSectionEnd];

        Assert.Contains("PnPRelevantCount: 1", motionSection);
        Assert.Contains("FriendlyName=Intel(R) ISH Sensor", motionSection);
        Assert.DoesNotContain("Some Keyboard", motionSection);
    }

    [Fact]
    public void Writer_DegradesOptionalLegacyMetadataToUnavailableWithoutFailingQuery()
    {
        var candidate = new LegacySensorCandidateInfo("Physical Gyrometer", "id-2", "type-2", "cat-2", "Unavailable", "Unavailable", "Unavailable", "Unavailable", "Unavailable", "Unavailable", "Unavailable", "Unavailable", "Unavailable", "Unavailable");
        var snapshot = Snapshot(processes: []) with
        {
            MotionSensors = DefaultMotionSensors() with
            {
                LegacyCategoryAll = new LegacySensorQueryInfo("CategoryAll", "C317C286-C468-4288-9975-D4C4587C442C", null, true, 0, null, [candidate])
            }
        };

        var report = new EnvironmentDiscoveryReportWriter().Write(snapshot);

        Assert.Contains("Succeeded=True", report);
        Assert.Contains("State=Unavailable", report);
        Assert.Contains("DevicePath=Unavailable", report);
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
    public void ExecutableExtraction_RemovesArgumentsAndSanitizesUserProfileAnywhere()
    {
        var executable = WindowsEnvironmentDiscoverySnapshotSource.ExtractExecutablePath(
            "\"C:\\Users\\TestUser\\AppData\\Local\\Example\\app.exe\" --secret foo",
            "C:\\Users\\TestUser");

        Assert.Equal("%USERPROFILE%\\AppData\\Local\\Example\\app.exe", executable);
        Assert.DoesNotContain("TestUser", executable, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--secret", executable, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("foo", executable, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Writer_DoesNotExposeStartupArgumentsOrUserName()
    {
        var executable = WindowsEnvironmentDiscoverySnapshotSource.ExtractExecutablePath(
            "\"C:\\Users\\TestUser\\AppData\\Local\\Example\\app.exe\" --secret foo",
            "C:\\Users\\TestUser");
        var snapshot = Snapshot(processes: []) with
        {
            StartupRegistrations = new DiscoverySection<StartupRegistrationDiscoveryInfo>([new("HKCU\\Run", "Example", executable)])
        };

        var report = new EnvironmentDiscoveryReportWriter().Write(snapshot);

        Assert.Contains("%USERPROFILE%\\AppData\\Local\\Example\\app.exe", report);
        Assert.DoesNotContain("TestUser", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--secret", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("foo", report, StringComparison.OrdinalIgnoreCase);
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

    private static EnvironmentDiscoverySnapshot Snapshot(IReadOnlyList<ProcessDiscoveryInfo> processes, IReadOnlyList<InstalledApplicationDiscoveryInfo>? installed = null, IReadOnlyList<ControllerDeviceInfo>? devices = null) => new(
        new DateTimeOffset(2026, 8, 9, 17, 30, 12, TimeSpan.Zero),
        new SystemDiscoveryInfo("Windows 11", "26100", "x64", "MSI", "Claw", ["Intel Arc"], "1.0.0"),
        new DiscoverySection<ProcessDiscoveryInfo>(processes),
        new DiscoverySection<ServiceDiscoveryInfo>([new("svc", "Service", "Running", "Automatic", "C:\\svc.exe")]),
        new DiscoverySection<InstalledApplicationDiscoveryInfo>(installed ?? []),
        new DiscoverySection<AppPackageDiscoveryInfo>([new("Package", "Family", "Full", "Publisher", "C:\\Package", "1.0")]),
        new DiscoverySection<StartupRegistrationDiscoveryInfo>([new("HKCU\\Run", "Startup", "C:\\Startup.exe")]),
        new DiscoverySection<ScheduledTaskDiscoveryInfo>([new("\\", "Task", "True", "Ready", "C:\\Task.exe")]),
        new DiscoverySection<ControllerDeviceInfo>(devices ?? []),
        new DiscoverySection<RuntimePrerequisiteAssessment>([new(new(PrerequisiteKind.HidHide, PrerequisiteStatus.Missing, "Missing"), new(PrerequisiteKind.UsbIpWin2, PrerequisiteStatus.Missing, "Missing"), new(PrerequisiteKind.Viiper, PrerequisiteStatus.Missing, "Missing"))]),
        DefaultMotionSensors());

    private static MotionSensorDiscoverySnapshot DefaultMotionSensors() => new(
        new WinRtSensorDiscoveryInfo(false, null, null, "Unavailable"),
        new WinRtSensorDiscoveryInfo(false, null, null, "Unavailable"),
        new LegacySensorQueryInfo("CategoryAll", ClawSensorProbeSensorApi.SensorCategoryAll.ToString("D"), null, false, unchecked((int)0x80070490), "COMException", []),
        [new LegacySensorQueryInfo("DirectType", "E83AF229-8640-4D18-A213-E22675EBB2C3", "A2VM reference custom accelerometer type", false, unchecked((int)0x80070490), "COMException", [])]);

    private sealed class FakeSource(EnvironmentDiscoverySnapshot snapshot) : IEnvironmentDiscoverySnapshotSource
    {
        public EnvironmentDiscoverySnapshot Capture(DateTimeOffset capturedAt) => snapshot with { CapturedAt = capturedAt };
    }
}
