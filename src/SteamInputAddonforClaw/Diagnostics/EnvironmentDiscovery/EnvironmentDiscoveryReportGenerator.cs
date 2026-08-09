using System.Diagnostics;
using Microsoft.Win32;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Status;
using Windows.Management.Deployment;

namespace SteamInputAddonforClaw.Diagnostics.EnvironmentDiscovery;

internal sealed class EnvironmentDiscoveryReportGenerator(
    IEnvironmentDiscoverySnapshotSource snapshotSource,
    IEnvironmentDiscoveryReportStore reportStore,
    EnvironmentDiscoveryReportWriter writer,
    Func<DateTimeOffset>? clock = null) : IEnvironmentDiscoveryReportGenerator
{
    public Task<EnvironmentDiscoveryReportResult> GenerateAsync(CancellationToken cancellationToken = default)
    {
        AppLog.Info("EnvironmentDiscovery", "Environment discovery report requested.");
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capturedAt = (clock ?? (() => DateTimeOffset.Now))();
            var snapshot = snapshotSource.Capture(capturedAt);
            cancellationToken.ThrowIfCancellationRequested();
            var reportPath = reportStore.Write(capturedAt, writer.Write(snapshot));
            AppLog.Info("EnvironmentDiscovery", "Environment discovery report generated.", ("ReportFileName", Path.GetFileName(reportPath)));
            return new EnvironmentDiscoveryReportResult(reportPath, Path.GetFileName(reportPath), reportStore.DirectoryPath);
        }, cancellationToken);
    }
}

internal sealed class EnvironmentDiscoveryReportStore(string logRoot) : IEnvironmentDiscoveryReportStore
{
    public string DirectoryPath => Path.Combine(logRoot, "Discovery");

    public string Write(DateTimeOffset capturedAt, string content)
    {
        Directory.CreateDirectory(DirectoryPath);
        var baseName = $"EnvironmentDiscovery-{capturedAt:yyyyMMdd-HHmmss}";
        for (var suffix = 1; ; suffix++)
        {
            var fileName = suffix == 1 ? $"{baseName}.log" : $"{baseName}-{suffix}.log";
            var path = Path.Combine(DirectoryPath, fileName);
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                using var output = new StreamWriter(stream);
                output.Write(content);
                return path;
            }
            catch (IOException) when (File.Exists(path)) { }
        }
    }
}

internal sealed class WindowsEnvironmentDiscoverySnapshotSource : IEnvironmentDiscoverySnapshotSource
{
    public EnvironmentDiscoverySnapshot Capture(DateTimeOffset capturedAt)
    {
        var devices = new WindowsControllerDeviceEnumerator();
        var deviceInfo = new WindowsDeviceInformationProvider(devices).Capture();
        return new EnvironmentDiscoverySnapshot(
            capturedAt,
            new SystemDiscoveryInfo(Environment.OSVersion.VersionString, Environment.OSVersion.Version.Build.ToString(), Environment.Is64BitOperatingSystem ? "x64" : "x86", deviceInfo.Manufacturer, deviceInfo.Model, deviceInfo.GpuModels, AppVersion()),
            Section(() => (IReadOnlyList<CurrentDetectionDiscoveryInfo>)[CaptureCurrentDetection(devices)]),
            Section(CaptureProcesses),
            Section(CaptureServices),
            Section(CaptureInstalledApplications),
            Section(CapturePackages),
            Section(CaptureStartupRegistrations),
            Section(CaptureScheduledTasks),
            Section(devices.EnumeratePresentDevices),
            Section(() => (IReadOnlyList<ExternalControllerAssessment>)[new ExternalControllerDetector(devices, new ControllerDeviceClassifier(new MsiClawInternalControllerMatcher())).Detect()]),
            Section(() => (IReadOnlyList<RuntimePrerequisiteAssessment>)[new RuntimePrerequisiteInspector(new HidHidePrerequisiteInspector(new HidHideDriverClient()), new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(devices)), new ViiperRuntimeInspector()).Inspect()]));
    }

    private static CurrentDetectionDiscoveryInfo CaptureCurrentDetection(IControllerDeviceEnumerator devices)
    {
        var software = new IControllerSoftwareStatusProvider[]
        {
            new MsiCenterMSoftwareStatusProvider(),
            new ClawTweaksSoftwareStatusProvider(new ClawTweaksInstallationProbe(), new ClawTweaksRuntimeDetector()),
            new HandheldCompanionSoftwareStatusProvider(new HandheldCompanionRuntimeDetector())
        }.Select(provider => provider.Capture()).ToArray();
        var environment = new ClawTweaksEnvironmentDetector(devices).Detect();
        var readiness = environment.Mode == ControllerEnvironmentMode.Indeterminate ? ControllerEnvironmentReadiness.Indeterminate : ControllerEnvironmentReadiness.Stable;
        return new CurrentDetectionDiscoveryInfo(software, environment, readiness);
    }

    private static IReadOnlyList<ProcessDiscoveryInfo> CaptureProcesses() => Process.GetProcesses()
        .Select(process =>
        {
            try
            {
                using (process)
                {
                    try
                    {
                        var path = process.MainModule?.FileName;
                        var version = path is null ? null : FileVersionInfo.GetVersionInfo(path);
                        return new ProcessDiscoveryInfo(process.ProcessName, process.Id, SanitizePath(path), version?.FileDescription ?? "<Unavailable>", version?.ProductName ?? "<Unavailable>", version?.ProductVersion ?? "<Unavailable>", version?.CompanyName ?? "<Unavailable>");
                    }
                    catch { return new ProcessDiscoveryInfo(process.ProcessName, process.Id, "<AccessDenied>", "<Unavailable>", "<Unavailable>", "<Unavailable>", "<Unavailable>"); }
                }
            }
            catch { return new ProcessDiscoveryInfo("<Unavailable>", 0, "<AccessDenied>", "<Unavailable>", "<Unavailable>", "<Unavailable>", "<Unavailable>"); }
        }).ToArray();

    private static IReadOnlyList<ServiceDiscoveryInfo> CaptureServices()
    {
        var states = CaptureServiceStates();
        using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services") ?? throw new InvalidOperationException("Services registry key is unavailable.");
        return services.GetSubKeyNames().Select(name =>
        {
            using var service = services.OpenSubKey(name);
            var start = service?.GetValue("Start") as int?;
            return new ServiceDiscoveryInfo(name, Value(service, "DisplayName"), states.TryGetValue(name, out var state) ? state : "Unknown", StartType(start), SanitizePath(Value(service, "ImagePath")));
        }).ToArray();
    }

    private static Dictionary<string, string> CaptureServiceStates()
    {
        using var process = Process.Start(new ProcessStartInfo("sc.exe", "query state= all") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }) ?? throw new InvalidOperationException("Unable to query service state.");
        var lines = process.StandardOutput.ReadToEnd().Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        process.WaitForExit();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? name = null;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("SERVICE_NAME:", StringComparison.OrdinalIgnoreCase)) name = trimmed[13..].Trim();
            else if (name is not null && trimmed.StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                result[name] = parts.LastOrDefault() ?? "Unknown";
                name = null;
            }
        }
        return result;
    }

    private static IReadOnlyList<InstalledApplicationDiscoveryInfo> CaptureInstalledApplications()
    {
        var values = new List<InstalledApplicationDiscoveryInfo>();
        AddUninstallEntries(values, RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM64");
        AddUninstallEntries(values, RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM32");
        AddUninstallEntries(values, RegistryHive.CurrentUser, RegistryView.Default, "HKCU");
        return values;
    }

    private static void AddUninstallEntries(List<InstalledApplicationDiscoveryInfo> values, RegistryHive hive, RegistryView view, string source)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        if (uninstall is null) return;
        foreach (var keyName in uninstall.GetSubKeyNames())
        {
            using var entry = uninstall.OpenSubKey(keyName);
            values.Add(new InstalledApplicationDiscoveryInfo(source, keyName, Value(entry, "DisplayName"), Value(entry, "DisplayVersion"), Value(entry, "Publisher"), SanitizePath(Value(entry, "InstallLocation"))));
        }
    }

    private static IReadOnlyList<AppPackageDiscoveryInfo> CapturePackages() => new PackageManager().FindPackagesForUser(string.Empty)
        .Select(package => new AppPackageDiscoveryInfo(package.Id.Name, package.Id.FamilyName, package.Id.FullName, package.PublisherDisplayName, SanitizePath(package.InstalledLocation?.Path), package.Id.Version.ToString() ?? "<Unavailable>")).ToArray();

    private static IReadOnlyList<StartupRegistrationDiscoveryInfo> CaptureStartupRegistrations()
    {
        var items = new List<StartupRegistrationDiscoveryInfo>();
        AddRunEntries(items, RegistryHive.CurrentUser, RegistryView.Default, "HKCU");
        AddRunEntries(items, RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM64");
        AddRunEntries(items, RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM32");
        AddStartupFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "CurrentUserStartupFolder");
        AddStartupFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "CommonStartupFolder");
        return items;
    }

    private static void AddRunEntries(List<StartupRegistrationDiscoveryInfo> items, RegistryHive hive, RegistryView view, string source)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        foreach (var keyName in new[] { "Run", "RunOnce" })
        {
            using var key = root.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\{keyName}");
            if (key is null) continue;
            foreach (var valueName in key.GetValueNames()) items.Add(new StartupRegistrationDiscoveryInfo($"{source}\\{keyName}", valueName, SanitizePath(key.GetValue(valueName)?.ToString())));
        }
    }

    private static void AddStartupFolder(List<StartupRegistrationDiscoveryInfo> items, string folder, string source)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var path in Directory.EnumerateFiles(folder)) items.Add(new StartupRegistrationDiscoveryInfo(source, Path.GetFileName(path), SanitizePath(path)));
    }

    private static IReadOnlyList<ScheduledTaskDiscoveryInfo> CaptureScheduledTasks()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Task Scheduler COM service is unavailable.");
        dynamic service = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Task Scheduler COM service cannot be created.");
        service.Connect();
        var results = new List<ScheduledTaskDiscoveryInfo>();
        AddTasks(service.GetFolder("\\"), results);
        return results;
    }

    private static void AddTasks(dynamic folder, List<ScheduledTaskDiscoveryInfo> results)
    {
        foreach (dynamic task in folder.GetTasks(1))
        {
            var executable = "<Unavailable>";
            try { executable = string.Join(" | ", ((IEnumerable<dynamic>)task.Definition.Actions).Select(action => (string?)action.Path ?? "<Unavailable>")); } catch { }
            results.Add(new ScheduledTaskDiscoveryInfo((string)task.Path, (string)task.Name, ((bool)task.Enabled).ToString(), task.State.ToString(), SanitizePath(executable)));
        }
        foreach (dynamic child in folder.GetFolders(0)) AddTasks(child, results);
    }

    private static DiscoverySection<T> Section<T>(Func<IReadOnlyList<T>> capture)
    {
        try { return new DiscoverySection<T>(capture()); }
        catch (Exception exception) { return new DiscoverySection<T>([], exception.GetType().Name); }
    }

    private static string AppVersion() => typeof(WindowsEnvironmentDiscoverySnapshotSource).Assembly.GetName().Version?.ToString() ?? "Unknown";
    private static string Value(RegistryKey? key, string name) => key?.GetValue(name)?.ToString() ?? "<Unavailable>";
    private static string StartType(int? value) => value switch { 0 => "Boot", 1 => "System", 2 => "Automatic", 3 => "Manual", 4 => "Disabled", _ => "Unknown" };
    private static string SanitizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "<Unavailable>";
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
        return value.StartsWith(profile, StringComparison.OrdinalIgnoreCase) ? "%USERPROFILE%" + value[profile.Length..] : value;
    }
}
