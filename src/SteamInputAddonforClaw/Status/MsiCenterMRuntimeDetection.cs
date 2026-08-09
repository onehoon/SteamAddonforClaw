using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Management.Deployment;
using SteamInputAddonforClaw.Diagnostics;

namespace SteamInputAddonforClaw.Status;

internal interface IMsiCenterMRuntimeDetector
{
    MsiCenterMRuntimeAssessment Detect();
}

internal interface IMsiCenterMRuntimeSignalSource
{
    MsiCenterMRuntimeSignals Capture();
}

internal sealed record MsiCenterMRuntimeAssessment(
    SoftwareRuntimeStatus Status,
    string Reason,
    bool FoundationServiceRunning,
    bool ServerRunning,
    bool ControlModeRunning,
    bool QuickSettingsInstalled,
    bool QuickSettingsWidgetRunning,
    bool DesktopUiRunning);

internal sealed record MsiCenterMRuntimeSignals(
    bool FoundationServiceRunning,
    bool ServerRunning,
    bool ControlModeRunning,
    bool QuickSettingsInstalled,
    bool QuickSettingsWidgetRunning,
    bool DesktopUiRunning);

internal sealed class MsiCenterMRuntimeDetector(IMsiCenterMRuntimeSignalSource? signalSource = null) : IMsiCenterMRuntimeDetector
{
    private readonly IMsiCenterMRuntimeSignalSource _signalSource = signalSource ?? new WindowsMsiCenterMRuntimeSignalSource();

    public MsiCenterMRuntimeAssessment Detect()
    {
        try
        {
            var signals = _signalSource.Capture();
            var result = Assess(signals);
            AppLog.Info("CenterM", "MSI Center M runtime assessment completed.",
                ("Status", result.Status), ("Reason", result.Reason),
                ("FoundationService", result.FoundationServiceRunning), ("Server", result.ServerRunning),
                ("ControlMode", result.ControlModeRunning), ("QuickSettingsInstalled", result.QuickSettingsInstalled),
                ("QuickSettingsWidget", result.QuickSettingsWidgetRunning), ("DesktopUi", result.DesktopUiRunning));
            return result;
        }
        catch (Exception exception)
        {
            AppLog.Warn("CenterM", "MSI Center M runtime inspection failed.", exception, ("Reason", "MsiCenterMInspectionFailed"));
            return new(SoftwareRuntimeStatus.Indeterminate, "MsiCenterMInspectionFailed", false, false, false, false, false, false);
        }
    }

    internal static MsiCenterMRuntimeAssessment Assess(MsiCenterMRuntimeSignals signals)
    {
        if (signals.FoundationServiceRunning && signals.ServerRunning && signals.ControlModeRunning && signals.QuickSettingsInstalled && signals.QuickSettingsWidgetRunning)
            return From(signals, SoftwareRuntimeStatus.Running, "MsiCenterMOperational");

        if (!signals.FoundationServiceRunning && !signals.ServerRunning && !signals.ControlModeRunning && !signals.QuickSettingsWidgetRunning)
            return From(signals, SoftwareRuntimeStatus.NotRunning, "MsiCenterMNotRunning");

        var reason = !signals.FoundationServiceRunning ? "MsiCenterMFoundationServiceNotReady"
            : !signals.ServerRunning ? "MsiCenterMBackendNotReady"
            : !signals.ControlModeRunning ? "MsiCenterMControlModeNotReady"
            : !signals.QuickSettingsInstalled ? "MsiCenterMQuickSettingsNotReady"
            : "MsiCenterMWidgetNotReady";
        return From(signals, SoftwareRuntimeStatus.Starting, reason);
    }

    private static MsiCenterMRuntimeAssessment From(MsiCenterMRuntimeSignals signals, SoftwareRuntimeStatus status, string reason) =>
        new(status, reason, signals.FoundationServiceRunning, signals.ServerRunning, signals.ControlModeRunning, signals.QuickSettingsInstalled, signals.QuickSettingsWidgetRunning, signals.DesktopUiRunning);
}

internal sealed class WindowsMsiCenterMRuntimeSignalSource : IMsiCenterMRuntimeSignalSource
{
    public MsiCenterMRuntimeSignals Capture()
    {
        var quickSettings = new PackageManager().FindPackagesForUser(string.Empty)
            .FirstOrDefault(package => string.Equals(package.Id.Name, MsiCenterMIdentity.QuickSettingsPackageName, StringComparison.OrdinalIgnoreCase));
        var quickSettingsRoot = quickSettings?.InstalledLocation?.Path;

        return new(
            IsServiceRunning(MsiCenterMIdentity.FoundationServiceName),
            IsProcessRunning(MsiCenterMIdentity.ServerProcessName),
            IsProcessRunning(MsiCenterMIdentity.ControlModeProcessName),
            quickSettings is not null,
            quickSettingsRoot is not null && IsQuickSettingsWidgetRunning(quickSettingsRoot),
            IsProcessRunning(MsiCenterMIdentity.DesktopUiProcessName));
    }

    internal static bool IsPackageOwnedWidget(string? installedLocation, string? processPath)
    {
        if (string.IsNullOrWhiteSpace(installedLocation) || string.IsNullOrWhiteSpace(processPath)) return false;
        var root = Path.GetFullPath(installedLocation);
        var executable = Path.GetFullPath(processPath);
        var rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return executable.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(executable), MsiCenterMIdentity.QuickSettingsWidgetFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsQuickSettingsWidgetRunning(string? installedLocation)
    {
        foreach (var process in Process.GetProcessesByName(MsiCenterMIdentity.QuickSettingsWidgetProcessName))
        {
            using (process)
            {
                var path = process.MainModule?.FileName;
                if (IsPackageOwnedWidget(installedLocation, path)) return true;
            }
        }
        return false;
    }

    private static bool IsProcessRunning(string name)
    {
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process) return true;
        }
        return false;
    }

    private static bool IsServiceRunning(string serviceName)
    {
        var manager = OpenSCManagerW(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open the Service Control Manager.");
        try
        {
            var service = OpenServiceW(manager, serviceName, ServiceQueryStatus);
            if (service == IntPtr.Zero)
            {
                if (Marshal.GetLastWin32Error() == ErrorServiceDoesNotExist) return false;
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to open service '{serviceName}'.");
            }
            try
            {
                var status = new ServiceStatusProcess();
                var size = (uint)Marshal.SizeOf<ServiceStatusProcess>();
                var buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (!QueryServiceStatusEx(service, ScStatusProcessInfo, buffer, size, out _))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to query service '{serviceName}'.");
                    status = Marshal.PtrToStructure<ServiceStatusProcess>(buffer);
                    return status.CurrentState == ServiceRunning;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }

    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const int ScStatusProcessInfo = 0;
    private const uint ServiceRunning = 4;
    private const int ErrorServiceDoesNotExist = 1060;

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

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenServiceW(IntPtr serviceManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel, IntPtr buffer, uint bufferSize, out uint bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr serviceHandle);
}
