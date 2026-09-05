using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Prerequisites;

namespace SteamInputAddonforClaw.Diagnostics.EnvironmentDiscovery;

internal sealed record DiscoverySection<T>(IReadOnlyList<T> Items, string? Failure = null);
internal sealed record SystemDiscoveryInfo(string OperatingSystem, string Build, string Architecture, string Manufacturer, string Model, IReadOnlyList<string> Gpus, string AppVersion);
internal sealed record ProcessDiscoveryInfo(string Name, int ProcessId, string ExecutablePath, string FileDescription, string ProductName, string ProductVersion, string CompanyName);
internal sealed record ServiceDiscoveryInfo(string ServiceName, string DisplayName, string Status, string StartType, string ImagePath);
internal sealed record InstalledApplicationDiscoveryInfo(string Source, string KeyName, string DisplayName, string DisplayVersion, string Publisher, string InstallLocation);
internal sealed record AppPackageDiscoveryInfo(string Name, string PackageFamilyName, string PackageFullName, string Publisher, string InstalledLocation, string Version);
internal sealed record StartupRegistrationDiscoveryInfo(string Source, string Name, string Value);
internal sealed record ScheduledTaskDiscoveryInfo(string TaskPath, string TaskName, string Enabled, string State, string Executable);
internal sealed record EnvironmentDiscoverySnapshot(
    DateTimeOffset CapturedAt,
    SystemDiscoveryInfo System,
    DiscoverySection<ProcessDiscoveryInfo> Processes,
    DiscoverySection<ServiceDiscoveryInfo> Services,
    DiscoverySection<InstalledApplicationDiscoveryInfo> InstalledApplications,
    DiscoverySection<AppPackageDiscoveryInfo> AppPackages,
    DiscoverySection<StartupRegistrationDiscoveryInfo> StartupRegistrations,
    DiscoverySection<ScheduledTaskDiscoveryInfo> ScheduledTasks,
    DiscoverySection<ControllerDeviceInfo> Devices,
    DiscoverySection<RuntimePrerequisiteAssessment> Prerequisites,
    MotionSensorDiscoverySnapshot MotionSensors);

internal sealed record MotionSensorDiscoverySnapshot(
    WinRtSensorDiscoveryInfo WinRtGyrometer,
    WinRtSensorDiscoveryInfo WinRtAccelerometer,
    LegacySensorQueryInfo LegacyCategoryAll,
    IReadOnlyList<LegacySensorQueryInfo> LegacyDirectTypeQueries);

internal sealed record WinRtSensorDiscoveryInfo(bool Available, string? DeviceId, uint? MinimumReportIntervalMs, string? Failure);

internal sealed record LegacySensorQueryInfo(
    string QueryKind,
    string QueryGuid,
    string? Label,
    bool Succeeded,
    int? HResult,
    string? Failure,
    IReadOnlyList<LegacySensorCandidateInfo> Candidates);

internal sealed record LegacySensorCandidateInfo(
    string FriendlyName,
    string SensorId,
    string TypeGuid,
    string CategoryGuid,
    string State,
    string Manufacturer,
    string Model,
    string PersistentUniqueId,
    string DevicePath,
    string MinimumReportInterval,
    string HidUsage,
    string SupportsCustomX,
    string SupportsCustomY,
    string SupportsCustomZ);

internal interface IEnvironmentDiscoverySnapshotSource { EnvironmentDiscoverySnapshot Capture(DateTimeOffset capturedAt); }
internal interface IEnvironmentDiscoveryReportStore { string Write(DateTimeOffset capturedAt, string content); string DirectoryPath { get; } }
internal interface IEnvironmentDiscoveryReportGenerator { Task<EnvironmentDiscoveryReportResult> GenerateAsync(CancellationToken cancellationToken = default); }
internal sealed record EnvironmentDiscoveryReportResult(string ReportPath, string ReportFileName, string DirectoryPath);
