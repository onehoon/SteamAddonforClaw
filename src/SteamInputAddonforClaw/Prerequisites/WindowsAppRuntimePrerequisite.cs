using System.Diagnostics;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Install;
using Windows.Management.Deployment;

namespace SteamInputAddonforClaw.Prerequisites;

internal enum WindowsAppRuntimeAvailability
{
    Ready,
    Missing,
    UpdateRequired,
    Indeterminate,
}

internal static class WindowsAppRuntimeMetadata
{
    internal static readonly Version MinimumFrameworkVersion = new(2, 3, 1, 0);
    internal const string FrameworkPackageFamilyName = "Microsoft.WindowsAppRuntime.2_8wekyb3d8bbwe";
    internal const string InstallerFileName = "WindowsAppRuntimeInstall-x64.exe";
    internal static readonly Uri InstallerDownloadUri = new("https://aka.ms/windowsappsdk/2.3/2.3.1/windowsappruntimeinstall-x64.exe");
    internal const string InstallerSha256 = "4011748DDF472B7E856D909FDFB4E9B19C3D23FCD8121039AC91F99D5FFA65DB";
    internal const string SilentInstallerArguments = "--quiet";

    internal static PrerequisiteInstallerDescriptor InstallerDescriptor => new(
        "WindowsAppRuntime",
        MinimumFrameworkVersion,
        InstallerFileName,
        InstallerDownloadUri,
        InstallerSha256);
}

internal sealed record WindowsAppRuntimePackageInfo(string FamilyName, string Version, string Architecture);

internal interface IWindowsAppRuntimePackageEnumeration
{
    IReadOnlyList<WindowsAppRuntimePackageInfo> FindCurrentUserPackages();
}

internal interface IWindowsAppRuntimePackageProbe
{
    WindowsAppRuntimeAvailability Inspect();
}

internal sealed class WindowsAppRuntimePackageEnumeration : IWindowsAppRuntimePackageEnumeration
{
    public IReadOnlyList<WindowsAppRuntimePackageInfo> FindCurrentUserPackages() =>
        new PackageManager()
            .FindPackagesForUser(string.Empty)
            .Select(package => new WindowsAppRuntimePackageInfo(
                package.Id.FamilyName,
                new Version(package.Id.Version.Major, package.Id.Version.Minor, package.Id.Version.Build, package.Id.Version.Revision).ToString(),
                package.Id.Architecture.ToString()))
            .ToArray();
}

internal sealed class WindowsAppRuntimePackageProbe : IWindowsAppRuntimePackageProbe
{
    private readonly IWindowsAppRuntimePackageEnumeration _enumeration;

    internal WindowsAppRuntimePackageProbe(IWindowsAppRuntimePackageEnumeration? enumeration = null) =>
        _enumeration = enumeration ?? new WindowsAppRuntimePackageEnumeration();

    public WindowsAppRuntimeAvailability Inspect()
    {
        try
        {
            var family = _enumeration.FindCurrentUserPackages()
                .Where(package => string.Equals(package.FamilyName, WindowsAppRuntimeMetadata.FrameworkPackageFamilyName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (family.Length == 0) return WindowsAppRuntimeAvailability.Missing;

            var x64 = family
                .Where(package => string.Equals(package.Architecture, "X64", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (x64.Length == 0) return WindowsAppRuntimeAvailability.Missing;

            var versions = new List<Version>(x64.Length);
            foreach (var package in x64)
            {
                if (!Version.TryParse(package.Version, out var version))
                    return WindowsAppRuntimeAvailability.Indeterminate;
                versions.Add(version);
            }

            return versions.Max() >= WindowsAppRuntimeMetadata.MinimumFrameworkVersion
                ? WindowsAppRuntimeAvailability.Ready
                : WindowsAppRuntimeAvailability.UpdateRequired;
        }
        catch (Exception exception)
        {
            AppLog.Warn("WindowsAppRuntime", "Windows App Runtime package probe failed; WinUI remains unavailable.", exception,
                ("Reason", "PackageProbeIndeterminate"));
            return WindowsAppRuntimeAvailability.Indeterminate;
        }
    }
}

internal interface IWindowsAppRuntimePrerequisite
{
    WindowsAppRuntimeAvailability Probe();
    Task<bool> EnsureAvailableAsync(CancellationToken cancellationToken = default);
}

internal sealed class WindowsAppRuntimePrerequisite : IWindowsAppRuntimePrerequisite
{
    private readonly IWindowsAppRuntimePackageProbe _packageProbe;
    private readonly IElevatedProcessRunner _processRunner;
    private readonly Func<string?> _executablePathProvider;
    private readonly SemaphoreSlim _setupGate = new(1, 1);

    internal WindowsAppRuntimePrerequisite(
        IWindowsAppRuntimePackageProbe? packageProbe = null,
        IElevatedProcessRunner? processRunner = null,
        Func<string?>? executablePathProvider = null)
    {
        _packageProbe = packageProbe ?? new WindowsAppRuntimePackageProbe();
        _processRunner = processRunner ?? new ElevatedProcessRunner();
        _executablePathProvider = executablePathProvider ?? (() => Environment.ProcessPath);
    }

    public WindowsAppRuntimeAvailability Probe()
    {
        try { return _packageProbe.Inspect(); }
        catch (Exception exception)
        {
            AppLog.Warn("WindowsAppRuntime", "Windows App Runtime package probe threw; WinUI remains unavailable.", exception,
                ("Reason", "PackageProbeIndeterminate"));
            return WindowsAppRuntimeAvailability.Indeterminate;
        }
    }

    public async Task<bool> EnsureAvailableAsync(CancellationToken cancellationToken = default)
    {
        await _setupGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var before = Probe();
            if (before == WindowsAppRuntimeAvailability.Ready)
                return true;
            if (before == WindowsAppRuntimeAvailability.Indeterminate)
            {
                AppLog.Warn("WindowsAppRuntime", "WinUI surface request blocked because Windows App Runtime state is indeterminate.", null,
                    ("Availability", before));
                return false;
            }

            var executablePath = _executablePathProvider();
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                AppLog.Warn("WindowsAppRuntime", "WinUI surface request blocked because the elevated helper executable is unavailable.", null,
                    ("Reason", "ElevatedHelperUnavailable"));
                return false;
            }

            AppLog.Info("WindowsAppRuntime", "Windows App Runtime setup requested for an explicit WinUI surface.",
                ("Availability", before), ("MinimumVersion", WindowsAppRuntimeMetadata.MinimumFrameworkVersion));
            ElevatedProcessResult result;
            try
            {
                result = await _processRunner.RunAsync(executablePath, ElevatedWindowsAppRuntimeSetup.Argument, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                AppLog.Warn("WindowsAppRuntime", "Windows App Runtime elevated helper failed to start.", exception,
                    ("Reason", "ElevatedHelperStartFailed"));
                return false;
            }

            var after = Probe();
            var ready = after == WindowsAppRuntimeAvailability.Ready;
            var helperSucceeded = result.Kind == ElevatedProcessResultKind.Completed && result.ExitCode == 0;
            AppLog.Info("WindowsAppRuntime", ready
                ? "Windows App Runtime availability confirmed after setup."
                : "Windows App Runtime setup did not establish a usable runtime.",
                ("Availability", after), ("Ready", ready), ("HelperSucceeded", helperSucceeded),
                ("Result", result.Kind), ("ExitCode", result.ExitCode));
            return helperSucceeded && ready;
        }
        finally { _setupGate.Release(); }
    }
}

internal static class ElevatedWindowsAppRuntimeSetup
{
    internal const string Argument = "--ensure-windows-app-runtime";

    public static int Run()
    {
        try
        {
            using var acquisition = new PrerequisiteInstallerAcquisition();
            return Execute(
                new WindowsAppRuntimePackageProbe(),
                ProvisioningStorageSecurity.EnsureTrustedStorage,
                (descriptor, directory, token) => acquisition.AcquireAsync(descriptor, directory, token),
                RunInstaller,
                VelopackAppPaths.ProvisioningStateDirectory);
        }
        catch (Exception exception)
        {
            AppLog.Error("WindowsAppRuntime", "Elevated Windows App Runtime setup failed unexpectedly.", exception);
            return 1;
        }
    }

    internal static int Execute(
        IWindowsAppRuntimePackageProbe packageProbe,
        Func<string, ProvisioningStorageAssessment> ensureTrustedStorage,
        Func<PrerequisiteInstallerDescriptor, string, CancellationToken, Task<InstallerAcquisitionResult>> acquireInstaller,
        Func<string, string, int> runInstaller,
        string stagingDirectory,
        Func<string, string, bool>? verifyInstaller = null)
    {
        ArgumentNullException.ThrowIfNull(packageProbe);
        ArgumentNullException.ThrowIfNull(ensureTrustedStorage);
        ArgumentNullException.ThrowIfNull(acquireInstaller);
        ArgumentNullException.ThrowIfNull(runInstaller);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        verifyInstaller ??= PrerequisiteInstallerAcquisition.HasExpectedSha256;

        var before = packageProbe.Inspect();
        if (before == WindowsAppRuntimeAvailability.Ready) return 0;
        if (before is not (WindowsAppRuntimeAvailability.Missing or WindowsAppRuntimeAvailability.UpdateRequired)) return 1;

        var storage = ensureTrustedStorage(stagingDirectory);
        if (storage.Status != ProvisioningStorageStatus.Trusted) return 1;

        var descriptor = WindowsAppRuntimeMetadata.InstallerDescriptor;
        var acquisition = acquireInstaller(descriptor, stagingDirectory, CancellationToken.None).GetAwaiter().GetResult();
        if (!acquisition.Succeeded || string.IsNullOrWhiteSpace(acquisition.InstallerPath)) return 1;

        try
        {
            if (!verifyInstaller(acquisition.InstallerPath, descriptor.InstallerSha256))
                return 1;

            var exitCode = runInstaller(acquisition.InstallerPath, WindowsAppRuntimeMetadata.SilentInstallerArguments);
            var after = packageProbe.Inspect();
            return exitCode == 0 && after == WindowsAppRuntimeAvailability.Ready ? 0 : 1;
        }
        finally { PrerequisiteInstallerAcquisition.TryDeleteStagedInstaller(acquisition.InstallerPath); }
    }

    private static int RunInstaller(string path, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = false });
        if (process is null) return 1;
        process.WaitForExit();
        return process.ExitCode;
    }
}
