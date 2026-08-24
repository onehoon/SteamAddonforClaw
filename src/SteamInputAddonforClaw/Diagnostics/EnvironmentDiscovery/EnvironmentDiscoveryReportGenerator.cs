using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Devices;
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
        var deviceProbe = new WindowsDeviceProbeContextFactory().Capture();
        var deviceInfo = new WindowsDeviceInformationProvider().Capture(deviceProbe.Context);
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
            Section(() => (IReadOnlyList<RuntimePrerequisiteAssessment>)[new RuntimePrerequisiteInspector(new HidHidePrerequisiteInspector(new HidHideDriverClient()), new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(devices), new WindowsUsbIpWin2PackageProbe()), new ViiperRuntimeInspector()).Inspect()]));
    }

    private static CurrentDetectionDiscoveryInfo CaptureCurrentDetection(IControllerDeviceEnumerator devices)
    {
        var assessment = new ControllerEnvironmentAssessmentProvider(
        [
            new MsiCenterMSoftwareStatusProvider()
        ]).Capture();
        return new CurrentDetectionDiscoveryInfo(assessment.Software, StartupControllerEnvironmentMapper.Map(assessment), "NotEvaluated");
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
            return new ServiceDiscoveryInfo(name, Value(service, "DisplayName"), states.TryGetValue(name, out var state) ? state : "Unknown", StartType(start), ExtractExecutablePath(Value(service, "ImagePath")));
        }).ToArray();
    }

    private static Dictionary<string, string> CaptureServiceStates()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var manager = OpenSCManagerW(null, null, ScManagerEnumerateService);
        if (manager == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open the Service Control Manager.");
        try
        {
            var initialResumeHandle = 0u;
            if (!EnumServicesStatusExW(manager, ScEnumProcessInfo, ServiceWin32, ServiceStateAll, IntPtr.Zero, 0, out var requiredBytes, out _, ref initialResumeHandle, null)
                && Marshal.GetLastWin32Error() != 234)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to size Windows service enumeration.");
            if (requiredBytes == 0) return result;
            var buffer = Marshal.AllocHGlobal((int)requiredBytes);
            try
            {
                var resumeHandle = 0u;
                if (!EnumServicesStatusExW(manager, ScEnumProcessInfo, ServiceWin32, ServiceStateAll, buffer, requiredBytes, out _, out var returned, ref resumeHandle, null))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to enumerate Windows services.");
                var itemSize = Marshal.SizeOf<EnumServiceStatusProcess>();
                for (var index = 0; index < returned; index++)
                {
                    var item = Marshal.PtrToStructure<EnumServiceStatusProcess>(buffer + (index * itemSize));
                    result[Marshal.PtrToStringUni(item.ServiceName) ?? "<Unavailable>"] = ServiceStatus(item.Status.CurrentState);
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { CloseServiceHandle(manager); }
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
            foreach (var valueName in key.GetValueNames()) items.Add(new StartupRegistrationDiscoveryInfo($"{source}\\{keyName}", valueName, ExtractExecutablePath(key.GetValue(valueName)?.ToString())));
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
        string folderPath;
        try { folderPath = folder.Path; }
        catch { folderPath = "<Unavailable>"; }
        try
        {
            foreach (dynamic task in folder.GetTasks(1))
            {
                var executable = "<Unavailable>";
                try { executable = string.Join(" | ", ((IEnumerable<dynamic>)task.Definition.Actions).Select(action => (string?)action.Path ?? "<Unavailable>")); } catch { }
                results.Add(new ScheduledTaskDiscoveryInfo((string)task.Path, (string)task.Name, ((bool)task.Enabled).ToString(), task.State.ToString(), SanitizePath(executable)));
            }
        }
        catch (Exception exception) { results.Add(new ScheduledTaskDiscoveryInfo(folderPath, "<FolderInspectionFailed>", "<Unavailable>", "<Unavailable>", $"<InspectionFailed: {exception.GetType().Name}>")); }
        try
        {
            foreach (dynamic child in folder.GetFolders(0)) AddTasks(child, results);
        }
        catch (Exception exception) { results.Add(new ScheduledTaskDiscoveryInfo(folderPath, "<SubfolderInspectionFailed>", "<Unavailable>", "<Unavailable>", $"<InspectionFailed: {exception.GetType().Name}>")); }
    }

    private static DiscoverySection<T> Section<T>(Func<IReadOnlyList<T>> capture)
    {
        try { return new DiscoverySection<T>(capture()); }
        catch (Exception exception) { return new DiscoverySection<T>([], exception.GetType().Name); }
    }

    private static string AppVersion() => typeof(WindowsEnvironmentDiscoverySnapshotSource).Assembly.GetName().Version?.ToString() ?? "Unknown";
    private static string Value(RegistryKey? key, string name) => key?.GetValue(name)?.ToString() ?? "<Unavailable>";
    private static string StartType(int? value) => value switch { 0 => "Boot", 1 => "System", 2 => "Automatic", 3 => "Manual", 4 => "Disabled", _ => "Unknown" };
    internal static string ExtractExecutablePath(string? value, string? profileRoot = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return "<Unavailable>";
        var command = value.Trim();
        if (command.StartsWith('"'))
        {
            var closingQuote = command.IndexOf('"', 1);
            return SanitizePath(closingQuote > 0 ? command[1..closingQuote] : command, profileRoot);
        }

        var executableEnd = new[] { ".exe", ".com", ".bat", ".cmd" }
            .Select(extension => command.IndexOf(extension, StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0)
            .Select(index => index + 4)
            .DefaultIfEmpty(command.IndexOf(' '))
            .First();
        return SanitizePath(executableEnd > 0 ? command[..executableEnd] : command, profileRoot);
    }

    internal static string SanitizePath(string? value, string? profileRoot = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return "<Unavailable>";
        var profile = (profileRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).TrimEnd('\\');
        return string.IsNullOrWhiteSpace(profile) ? value : value.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    private const uint ScManagerEnumerateService = 0x0004;
    private const int ScEnumProcessInfo = 0;
    private const uint ServiceWin32 = 0x00000030;
    private const uint ServiceStateAll = 0x00000003;
    private static string ServiceStatus(uint status) => status switch
    {
        1 => "Stopped", 2 => "StartPending", 3 => "StopPending", 4 => "Running", 5 => "ContinuePending", 6 => "PausePending", 7 => "Paused", _ => "Unknown"
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumServiceStatusProcess
    {
        public IntPtr ServiceName;
        public IntPtr DisplayName;
        public ServiceStatusProcess Status;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumServicesStatusExW(IntPtr serviceManager, int infoLevel, uint serviceType, uint serviceState, IntPtr services, uint bufferSize, out uint bytesNeeded, out uint servicesReturned, ref uint resumeHandle, string? groupName);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr serviceHandle);
}
