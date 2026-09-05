using System.Globalization;
using System.Text;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Prerequisites;

namespace SteamInputAddonforClaw.Diagnostics.EnvironmentDiscovery;

internal sealed class EnvironmentDiscoveryReportWriter
{
    internal const int SnapshotVersion = 2;

    public string Write(EnvironmentDiscoverySnapshot snapshot)
    {
        var text = new StringBuilder();
        text.AppendLine("Environment Discovery Report");
        text.AppendLine($"SnapshotVersion: {SnapshotVersion}");
        text.AppendLine($"CapturedAt: {snapshot.CapturedAt:O}");
        text.AppendLine($"AppVersion: {snapshot.System.AppVersion}");
        Header(text, "REPORT");
        WriteSystem(text, snapshot.System);
        WriteProcesses(text, snapshot.Processes);
        WriteServices(text, snapshot.Services);
        WriteInstalledApplications(text, snapshot.InstalledApplications);
        WritePackages(text, snapshot.AppPackages);
        WriteStartup(text, snapshot.StartupRegistrations);
        WriteTasks(text, snapshot.ScheduledTasks);
        WriteDevices(text, snapshot.Devices);
        WriteMotionSensors(text, snapshot);
        WritePrerequisites(text, snapshot.Prerequisites);
        WriteKeywordMatches(text, snapshot);
        return text.ToString();
    }

    private static void WriteSystem(StringBuilder text, SystemDiscoveryInfo system)
    {
        Header(text, "SYSTEM");
        Field(text, "OS", system.OperatingSystem); Field(text, "Build", system.Build); Field(text, "Architecture", system.Architecture);
        Field(text, "Manufacturer", system.Manufacturer); Field(text, "Model", system.Model);
        if (system.Gpus.Count == 0) Field(text, "GPU", "Unknown");
        else foreach (var gpu in system.Gpus.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)) Field(text, "GPU", gpu);
    }

    private static void WriteProcesses(StringBuilder text, DiscoverySection<ProcessDiscoveryInfo> section)
    {
        Header(text, "RUNNING PROCESSES");
        if (Failure(text, section)) return;
        foreach (var item in section.Items.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.ProcessId))
            text.AppendLine($"Name={Safe(item.Name)}; PID={item.ProcessId}; Path={Safe(item.ExecutablePath)}; FileDescription={Safe(item.FileDescription)}; Product={Safe(item.ProductName)}; Version={Safe(item.ProductVersion)}; Company={Safe(item.CompanyName)}");
    }

    private static void WriteServices(StringBuilder text, DiscoverySection<ServiceDiscoveryInfo> section)
    {
        Header(text, "WINDOWS SERVICES");
        if (Failure(text, section)) return;
        foreach (var item in section.Items.OrderBy(value => value.ServiceName, StringComparer.OrdinalIgnoreCase))
            text.AppendLine($"ServiceName={Safe(item.ServiceName)}; DisplayName={Safe(item.DisplayName)}; Status={Safe(item.Status)}; StartType={Safe(item.StartType)}; ImagePath={Safe(item.ImagePath)}");
    }

    private static void WriteInstalledApplications(StringBuilder text, DiscoverySection<InstalledApplicationDiscoveryInfo> section)
    {
        Header(text, "INSTALLED APPLICATIONS");
        if (Failure(text, section)) return;
        foreach (var item in section.Items.OrderBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.Source, StringComparer.OrdinalIgnoreCase))
            text.AppendLine($"Source={Safe(item.Source)}; Key={Safe(item.KeyName)}; DisplayName={Safe(item.DisplayName)}; Version={Safe(item.DisplayVersion)}; Publisher={Safe(item.Publisher)}; InstallLocation={Safe(item.InstallLocation)}");
    }

    private static void WritePackages(StringBuilder text, DiscoverySection<AppPackageDiscoveryInfo> section)
    {
        Header(text, "APPX PACKAGES");
        if (Failure(text, section)) return;
        foreach (var item in section.Items.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase))
            text.AppendLine($"Name={Safe(item.Name)}; Family={Safe(item.PackageFamilyName)}; FullName={Safe(item.PackageFullName)}; Publisher={Safe(item.Publisher)}; InstalledLocation={Safe(item.InstalledLocation)}; Version={Safe(item.Version)}");
    }

    private static void WriteStartup(StringBuilder text, DiscoverySection<StartupRegistrationDiscoveryInfo> section)
    {
        Header(text, "STARTUP REGISTRATIONS");
        if (Failure(text, section)) return;
        foreach (var item in section.Items.OrderBy(value => value.Source, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase))
            text.AppendLine($"Source={Safe(item.Source)}; Name={Safe(item.Name)}; Value={Safe(item.Value)}");
    }

    private static void WriteTasks(StringBuilder text, DiscoverySection<ScheduledTaskDiscoveryInfo> section)
    {
        Header(text, "SCHEDULED TASKS");
        if (Failure(text, section)) return;
        foreach (var item in section.Items.OrderBy(value => value.TaskPath, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.TaskName, StringComparer.OrdinalIgnoreCase))
            text.AppendLine($"TaskPath={Safe(item.TaskPath)}; TaskName={Safe(item.TaskName)}; Enabled={Safe(item.Enabled)}; State={Safe(item.State)}; Executable={Safe(item.Executable)}");
    }

    private static void WriteDevices(StringBuilder text, DiscoverySection<ControllerDeviceInfo> section)
    {
        Header(text, "CONTROLLER / PNP DEVICES");
        if (Failure(text, section)) return;
        foreach (var item in section.Items.OrderBy(value => value.InstanceId, StringComparer.OrdinalIgnoreCase))
            text.AppendLine($"FriendlyName={Safe(item.FriendlyName)}; InstanceId={Safe(item.InstanceId)}; ContainerId={item.ContainerId}; Class={Safe(item.ClassName)}; ClassGuid={Safe(item.ClassGuid)}; Enumerator={Safe(item.EnumeratorName)}; Service={Safe(item.Service)}; VID={FormatHex(item.VendorId)}; PID={FormatHex(item.ProductId)}; HardwareIds={Safe(string.Join('|', item.HardwareIds))}; CompatibleIds={Safe(string.Join('|', item.CompatibleIds))}; Present={item.Present}");
    }

    private static void WriteMotionSensors(StringBuilder text, EnvironmentDiscoverySnapshot snapshot)
    {
        Header(text, "WINDOWS MOTION / SENSOR DISCOVERY");
        var relevant = snapshot.Devices.Items.Where(IsMotionRelevant).OrderBy(item => item.InstanceId, StringComparer.OrdinalIgnoreCase).ToArray();
        text.AppendLine($"PnPRelevantCount: {relevant.Length}");
        foreach (var item in relevant)
            text.AppendLine($"FriendlyName={Safe(item.FriendlyName)}; InstanceId={Safe(item.InstanceId)}; Class={Safe(item.ClassName)}; ClassGuid={Safe(item.ClassGuid)}; Service={Safe(item.Service)}; UsagePage={FormatHex(item.UsagePage)}; Usage={FormatHex(item.Usage)}; HardwareIds={Safe(string.Join('|', item.HardwareIds))}");

        text.AppendLine();
        text.AppendLine("WinRT Gyrometer:");
        WriteWinRtSensor(text, snapshot.MotionSensors.WinRtGyrometer);
        text.AppendLine();
        text.AppendLine("WinRT Accelerometer:");
        WriteWinRtSensor(text, snapshot.MotionSensors.WinRtAccelerometer);
        text.AppendLine();
        text.AppendLine("Legacy CategoryAll:");
        WriteLegacyQuery(text, snapshot.MotionSensors.LegacyCategoryAll);
        foreach (var query in snapshot.MotionSensors.LegacyDirectTypeQueries)
        {
            text.AppendLine();
            text.AppendLine("Legacy DirectType:");
            if (query.Label is not null) text.AppendLine($"Label={Safe(query.Label)}");
            WriteLegacyQuery(text, query);
        }
    }

    private static void WriteWinRtSensor(StringBuilder text, WinRtSensorDiscoveryInfo info)
    {
        text.AppendLine($"Available={info.Available}");
        if (info.Available)
        {
            text.AppendLine($"DeviceId={Safe(info.DeviceId)}");
            text.AppendLine($"MinimumReportIntervalMs={(info.MinimumReportIntervalMs.HasValue ? info.MinimumReportIntervalMs.Value.ToString(CultureInfo.InvariantCulture) : "<Unavailable>")}");
        }
        else text.AppendLine($"Failure={Safe(info.Failure)}");
    }

    private static void WriteLegacyQuery(StringBuilder text, LegacySensorQueryInfo query)
    {
        text.AppendLine($"TypeGuid={query.QueryGuid}");
        text.AppendLine($"Succeeded={query.Succeeded}");
        text.AppendLine($"HResult={FormatHResult(query.HResult)}");
        if (!query.Succeeded) text.AppendLine($"Failure={Safe(query.Failure)}");
        text.AppendLine($"CandidateCount={query.Candidates.Count}");
        foreach (var candidate in query.Candidates)
            text.AppendLine($"FriendlyName={Safe(candidate.FriendlyName)}; SensorId={Safe(candidate.SensorId)}; TypeGuid={Safe(candidate.TypeGuid)}; CategoryGuid={Safe(candidate.CategoryGuid)}; State={Safe(candidate.State)}; Manufacturer={Safe(candidate.Manufacturer)}; Model={Safe(candidate.Model)}; PersistentUniqueId={Safe(candidate.PersistentUniqueId)}; DevicePath={Safe(candidate.DevicePath)}; MinimumReportInterval={Safe(candidate.MinimumReportInterval)}; HidUsage={Safe(candidate.HidUsage)}; SupportsCustomX={Safe(candidate.SupportsCustomX)}; SupportsCustomY={Safe(candidate.SupportsCustomY)}; SupportsCustomZ={Safe(candidate.SupportsCustomZ)}");
    }

    private static bool IsMotionRelevant(ControllerDeviceInfo item) =>
        string.Equals(item.ClassName, "Sensor", StringComparison.OrdinalIgnoreCase)
        || item.UsagePage == 0x20
        || ContainsIshIdentity(item);

    private static bool ContainsIshIdentity(ControllerDeviceInfo item) =>
        ContainsIshKeyword(item.FriendlyName) || ContainsIshKeyword(item.InstanceId) || ContainsIshKeyword(item.Service)
        || item.HardwareIds.Any(ContainsIshKeyword) || item.CompatibleIds.Any(ContainsIshKeyword);

    private static bool ContainsIshKeyword(string? value) => value is not null
        && (value.Contains("ISH", StringComparison.OrdinalIgnoreCase) || value.Contains("Integrated Sensor", StringComparison.OrdinalIgnoreCase));

    private static string FormatHResult(int? value) => value is null ? "<Unavailable>" : $"0x{unchecked((uint)value.Value):X8}";

    private static void WritePrerequisites(StringBuilder text, DiscoverySection<RuntimePrerequisiteAssessment> section)
    {
        Header(text, "ROUTING PREREQUISITES");
        if (Failure(text, section)) return;
        var assessment = section.Items.Single();
        text.AppendLine($"HidHide={assessment.HidHide.Status}; Reason={assessment.HidHide.Reason}");
        text.AppendLine($"UsbIpWin2={assessment.UsbIpWin2.Status}; Reason={assessment.UsbIpWin2.Reason}");
        text.AppendLine($"Viiper={assessment.Viiper.Status}; Reason={assessment.Viiper.Reason}");
        text.AppendLine($"RoutingReady={assessment.IsRoutingReady}");
    }

    private static void WriteKeywordMatches(StringBuilder text, EnvironmentDiscoverySnapshot snapshot)
    {
        Header(text, "KEYWORD MATCHES");
        var lines = snapshot.Processes.Items.Select(item => $"Process: {item.Name} {item.ExecutablePath}")
            .Concat(snapshot.Services.Items.Select(item => $"Service: {item.ServiceName} {item.DisplayName} {item.ImagePath}"))
            .Concat(snapshot.InstalledApplications.Items.Select(item => $"InstalledApplication: {item.DisplayName} {item.InstallLocation}"))
            .Concat(snapshot.AppPackages.Items.Select(item => $"Package: {item.Name} {item.PackageFullName} {item.InstalledLocation}"))
            .Concat(snapshot.StartupRegistrations.Items.Select(item => $"Startup: {item.Name} {item.Value}"))
            .Concat(snapshot.ScheduledTasks.Items.Select(item => $"ScheduledTask: {item.TaskPath} {item.TaskName} {item.Executable}"))
            .ToArray();
        foreach (var keyword in new[] { "MSI", "Center", "Claw", "Handheld", "HHC" })
        {
            text.AppendLine($"Keyword: {keyword}");
            foreach (var line in lines.Where(line => line.Contains(keyword, StringComparison.OrdinalIgnoreCase)).OrderBy(line => line, StringComparer.OrdinalIgnoreCase)) text.AppendLine(line);
        }
    }

    private static void Header(StringBuilder text, string name) { text.AppendLine(); text.AppendLine($"=== {name} ==="); }
    private static bool Failure<T>(StringBuilder text, DiscoverySection<T> section) { if (section.Failure is null) return false; text.AppendLine($"<InspectionFailed: {section.Failure}>"); return true; }
    private static void Field(StringBuilder text, string name, string value) => text.AppendLine($"{name}: {Safe(value)}");
    private static string Safe(string? value) => string.IsNullOrWhiteSpace(value) ? "<Unavailable>" : value.Replace('\r', ' ').Replace('\n', ' ');
    private static string FormatHex(ushort? value) => value is null ? "<Unavailable>" : value.Value.ToString("X4");
}
