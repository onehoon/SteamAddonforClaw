using System.Text;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Diagnostics.SteamController1304;

namespace SteamInputAddonforClaw.Diagnostics.EnvironmentDiscovery;

internal sealed class EnvironmentDiscoveryReportWriter
{
    internal const int SnapshotVersion = 1;

    public string Write(EnvironmentDiscoverySnapshot snapshot)
    {
        var text = new StringBuilder();
        text.AppendLine("Environment Discovery Report");
        text.AppendLine($"SnapshotVersion: {SnapshotVersion}");
        text.AppendLine($"CapturedAt: {snapshot.CapturedAt:O}");
        text.AppendLine($"AppVersion: {snapshot.System.AppVersion}");
        Header(text, "REPORT");
        WriteSystem(text, snapshot.System);
        WriteCurrentDetection(text, snapshot.CurrentDetection);
        WriteProcesses(text, snapshot.Processes);
        WriteServices(text, snapshot.Services);
        WriteInstalledApplications(text, snapshot.InstalledApplications);
        WritePackages(text, snapshot.AppPackages);
        WriteStartup(text, snapshot.StartupRegistrations);
        WriteTasks(text, snapshot.ScheduledTasks);
        WriteDevices(text, snapshot.Devices);
        WriteSteamController1304(text, snapshot.SteamController1304);
        WriteExternalControllers(text, snapshot.ExternalControllers);
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

    private static void WriteCurrentDetection(StringBuilder text, DiscoverySection<CurrentDetectionDiscoveryInfo> section)
    {
        Header(text, "CURRENT DETECTION");
        if (Failure(text, section)) return;
        var current = section.Items.Single();
        foreach (var software in current.Software.OrderBy(value => value.Kind))
        {
            text.AppendLine($"{software.DisplayName}: Installation={software.Installation}; Runtime={software.Runtime}; Reason={software.Reason}");
        }
        text.AppendLine($"ControllerEnvironmentMode: {current.Environment.Mode}");
        text.AppendLine($"ClawTweaksState: {current.Environment.ClawTweaksState}");
        text.AppendLine($"EnvironmentReadiness: {current.EnvironmentReadiness}");
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

    private static void WriteExternalControllers(StringBuilder text, DiscoverySection<ExternalControllerAssessment> section)
    {
        Header(text, "EXTERNAL CONTROLLER ASSESSMENT");
        if (Failure(text, section)) return;
        var assessment = section.Items.Single();
        text.AppendLine($"ExternalControllerAssessment={assessment.Status}");
        text.AppendLine($"ExternalControllerCount={assessment.DetectedExternalControllerCount}");
        foreach (var device in assessment.ExternalControllers.OrderBy(value => value.FriendlyName, StringComparer.OrdinalIgnoreCase))
            text.AppendLine($"FriendlyName={Safe(device.FriendlyName)}; VID={FormatHex(device.VendorId)}; PID={FormatHex(device.ProductId)}; InstanceId={Safe(device.InstanceId)}; ContainerId={device.ContainerId}");
    }

    private static void WriteSteamController1304(StringBuilder text, DiscoverySection<SteamController1304DiagnosticSnapshot> section)
    {
        Header(text, "STEAM CONTROLLER 28DE:1304 DIAGNOSTIC");
        if (Failure(text, section)) return;
        var snapshot = section.Items.Single();
        Field(text, "CaptureFailure", snapshot.Failure ?? "None");
        text.AppendLine($"LogicalDeviceCount: {snapshot.Devices.Select(device => device.ContainerId).Distinct().Count()}");
        foreach (var device in snapshot.Devices.OrderBy(device => device.InstanceId, StringComparer.OrdinalIgnoreCase))
        {
            text.AppendLine($"InstanceId={Safe(device.InstanceId)}; ParentInstanceId={Safe(device.ParentInstanceId)}; ContainerId={device.ContainerId}; Class={Safe(device.ClassName)}; Service={Safe(device.Service)}; Enumerator={Safe(device.EnumeratorName)}; UsagePage={FormatHex(device.UsagePage)}; Usage={FormatHex(device.Usage)}; Present={device.Present}; HardwareIds={Safe(string.Join('|', device.HardwareIds))}; CompatibleIds={Safe(string.Join('|', device.CompatibleIds))}");
        }
        text.AppendLine($"RawInputMatchCount: {snapshot.RawInputDevices.Count}");
        foreach (var raw in snapshot.RawInputDevices.OrderBy(item => item.DeviceName, StringComparer.OrdinalIgnoreCase))
            text.AppendLine($"RawInput: Type={raw.Type}; VID={FormatHex(raw.VendorId)}; PID={FormatHex(raw.ProductId)}; UsagePage={FormatHex(raw.UsagePage)}; Usage={FormatHex(raw.Usage)}; DeviceName={Safe(raw.DeviceName)}");
    }

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
