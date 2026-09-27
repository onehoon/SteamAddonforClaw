using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Install;

namespace SteamInputAddonforClaw.Prerequisites;

internal sealed record OwnedPrerequisiteUninstallResult(bool Succeeded, bool RestartRequired, string Reason);
internal sealed record RegisteredUninstallCommand(string FileName, string Arguments);

internal interface IUninstallProcessRunner
{
    bool TryRun(string fileName, string arguments, out int exitCode);
}

internal sealed class WindowsUninstallProcessRunner : IUninstallProcessRunner
{
    private const int UninstallerWaitBudgetMilliseconds = 5 * 60 * 1000;

    public bool TryRun(string fileName, string arguments, out int exitCode)
    {
        exitCode = -1;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null) return false;
            if (!process.WaitForExit(UninstallerWaitBudgetMilliseconds))
                return false;
            exitCode = process.ExitCode;
            return true;
        }
        catch (Exception exception)
        {
            AppLog.Warn("Uninstall.Dependency", "Registered dependency uninstaller could not be started or completed.", exception,
                ("Executable", fileName));
            return false;
        }
    }
}

internal sealed class ElevatedOwnedPrerequisiteUninstall
{
    private readonly UsbIpWin2ProvisioningReceiptStore _usbReceiptStore;
    private readonly IHidHideProvisioningReceiptStore _hidHideReceiptStore;
    private readonly Func<UsbIpWin2PackageState> _usbPackageProbe;
    private readonly Func<HidHidePackageState> _hidHidePackageProbe;
    private readonly Func<RegistryView, IReadOnlyList<HidHideUninstallCandidate>> _hidHideCandidates;
    private readonly IUninstallProcessRunner _processRunner;
    private readonly Func<string, bool> _fileExists;
    private readonly string _provisioningDirectory;

    internal ElevatedOwnedPrerequisiteUninstall(
        UsbIpWin2ProvisioningReceiptStore? usbReceiptStore = null,
        IHidHideProvisioningReceiptStore? hidHideReceiptStore = null,
        Func<UsbIpWin2PackageState>? usbPackageProbe = null,
        Func<HidHidePackageState>? hidHidePackageProbe = null,
        Func<RegistryView, IReadOnlyList<HidHideUninstallCandidate>>? hidHideCandidates = null,
        IUninstallProcessRunner? processRunner = null,
        Func<string, bool>? fileExists = null,
        string? provisioningDirectory = null)
    {
        _usbReceiptStore = usbReceiptStore ?? new(VelopackAppPaths.UsbIpWin2ProvisioningReceiptPath);
        _hidHideReceiptStore = hidHideReceiptStore ?? new HidHideProvisioningReceiptStore(VelopackAppPaths.HidHideProvisioningReceiptPath);
        _usbPackageProbe = usbPackageProbe ?? (() => new WindowsUsbIpWin2PackageProbe().Inspect());
        var registry = new WindowsHidHideUninstallRegistry();
        _hidHidePackageProbe = hidHidePackageProbe ?? (() => new WindowsHidHidePackageProbe(registry).Inspect());
        _hidHideCandidates = hidHideCandidates ?? registry.Enumerate;
        _processRunner = processRunner ?? new WindowsUninstallProcessRunner();
        _fileExists = fileExists ?? File.Exists;
        _provisioningDirectory = provisioningDirectory ?? VelopackAppPaths.ProvisioningStateDirectory;
    }

    internal OwnedPrerequisiteUninstallResult Execute()
    {
        var restartRequired = false;
        try
        {
            var usbLoaded = _usbReceiptStore.Load();
            var usbPackage = _usbPackageProbe();
            if (!UninstallOwnedUsbIp(usbLoaded.Receipt, usbLoaded.IsCorrupt, usbPackage, ref restartRequired, out var usbFailure))
                return new(false, restartRequired, usbFailure);

            var hidHideLoaded = _hidHideReceiptStore.Load();
            if (!UninstallOwnedHidHide(hidHideLoaded.Receipt, hidHideLoaded.IsCorrupt, ref restartRequired, out var hidHideFailure))
                return new(false, restartRequired, hidHideFailure);

            if (!RemoveProvisioningDirectory(_provisioningDirectory))
                return new(false, restartRequired, "ProgramDataProvisioningCleanupFailed");

            AppLog.Info("Uninstall.Dependency", "Owned prerequisite cleanup completed.",
                ("RestartRequired", restartRequired));
            return new(true, restartRequired, "OwnedPrerequisiteCleanupCompleted");
        }
        catch (Exception exception)
        {
            AppLog.Error("Uninstall.Dependency", "Owned prerequisite cleanup failed closed; provisioning receipts were retained when possible.", exception);
            return new(false, restartRequired, "OwnedPrerequisiteCleanupFailed:" + exception.GetType().Name);
        }
    }

    private bool UninstallOwnedUsbIp(
        UsbIpWin2ProvisioningReceipt? receipt,
        bool receiptCorrupt,
        UsbIpWin2PackageState package,
        ref bool restartRequired,
        out string failure)
    {
        failure = string.Empty;
        if (receiptCorrupt || receipt is not
            {
                IsValid: true,
                State: UsbIpWin2ProvisioningReceiptState.Provisioned or UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot,
                InstalledByAddon: true
            })
        {
            LogPreserved("usbip-win2", receiptCorrupt ? "ReceiptCorruptOrUntrusted" : "OwnershipNotProven");
            return true;
        }

        if (!package.InspectionSucceeded)
        {
            LogPreserved("usbip-win2", "PackageInspectionUnavailable");
            return true;
        }
        if (!package.PackageEntryPresent)
        {
            // This also covers a prior successful partial uninstall whose Addon-data cleanup later
            // failed. Conservatively retain the restart notice across that retry window.
            restartRequired = true;
            LogPreserved("usbip-win2", "PreviouslyOwnedPackageAlreadyAbsent");
            return true;
        }
        if (!package.Installed
            || string.IsNullOrWhiteSpace(package.Version)
            || !string.Equals(package.Version, receipt.ObservedInstalledVersion, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(package.Version, receipt.InstallerVersion, StringComparison.OrdinalIgnoreCase))
        {
            LogPreserved("usbip-win2", "CurrentPackageIdentityOrVersionChanged");
            return true;
        }

        if (!RegisteredUninstallCommandResolver.TryResolve(package.QuietUninstallString, package.UninstallString, _fileExists, out var command))
        {
            failure = "OwnedUsbIpUninstallEvidenceMissingOrMalformed";
            return false;
        }
        if (!RunUninstaller("usbip-win2", command, out var exitCode))
        {
            failure = "OwnedUsbIpUninstallerFailedToStart";
            return false;
        }
        if (exitCode is not (0 or 3010))
        {
            failure = "OwnedUsbIpUninstallerExitCode:" + exitCode.ToString(CultureInfo.InvariantCulture);
            return false;
        }

        var after = _usbPackageProbe();
        if (!after.InspectionSucceeded || after.PackageEntryPresent)
        {
            failure = "OwnedUsbIpRemovalCouldNotBeVerified";
            return false;
        }
        restartRequired = true;
        AppLog.Info("Uninstall.Dependency", "Proven Addon-owned usbip-win2 package was removed.", ("ExitCode", exitCode));
        return true;
    }

    private bool UninstallOwnedHidHide(
        HidHideProvisioningReceipt? receipt,
        bool receiptCorrupt,
        ref bool restartRequired,
        out string failure)
    {
        failure = string.Empty;
        if (receiptCorrupt || receipt is not
            {
                IsValid: true,
                State: HidHideProvisioningReceiptState.Provisioned or HidHideProvisioningReceiptState.InstalledPendingReboot
            } || receipt.PreProvisioningStatus != PrerequisiteStatus.Missing)
        {
            LogPreserved("HidHide", receiptCorrupt ? "ReceiptCorruptOrUntrusted" : "OwnershipNotProven");
            return true;
        }

        var package = _hidHidePackageProbe();
        if (!package.InspectionSucceeded)
        {
            LogPreserved("HidHide", "PackageInspectionUnavailable");
            return true;
        }
        if (!package.Installed)
        {
            restartRequired = true;
            LogPreserved("HidHide", "PreviouslyOwnedPackageAlreadyAbsent");
            return true;
        }

        IReadOnlyList<HidHideUninstallCandidate> candidates;
        try
        {
            candidates = new[] { RegistryView.Registry64, RegistryView.Registry32 }
                .SelectMany(_hidHideCandidates)
                .Where(WindowsHidHidePackageProbe.IsExactCandidate)
                .ToArray();
        }
        catch
        {
            LogPreserved("HidHide", "ExactUninstallIdentityUnavailable");
            return true;
        }
        if (candidates.Count != 1)
        {
            LogPreserved("HidHide", "PackageIdentityAmbiguous");
            return true;
        }

        var candidate = candidates[0];
        if (!WindowsHidHidePackageProbe.TryNormalizeVersion(candidate.DisplayVersion, out var currentVersion)
            || !HidHidePackageVersionPolicy.AreEquivalent(currentVersion, receipt.ObservedInstalledVersion)
            || !HidHidePackageVersionPolicy.AreEquivalent(currentVersion, receipt.InstallerVersion)
            || !HidHidePackageVersionPolicy.AreEquivalent(currentVersion, package.Version))
        {
            LogPreserved("HidHide", "CurrentPackageIdentityOrVersionChanged");
            return true;
        }

        if (!TryResolveHidHideCommand(candidate, out var command))
        {
            failure = "OwnedHidHideUninstallEvidenceMissingOrMalformed";
            return false;
        }
        if (!RunUninstaller("HidHide", command, out var exitCode))
        {
            failure = "OwnedHidHideUninstallerFailedToStart";
            return false;
        }
        if (exitCode is not (0 or 3010))
        {
            failure = "OwnedHidHideUninstallerExitCode:" + exitCode.ToString(CultureInfo.InvariantCulture);
            return false;
        }

        var after = _hidHidePackageProbe();
        if (!after.InspectionSucceeded || after.Installed)
        {
            failure = "OwnedHidHideRemovalCouldNotBeVerified";
            return false;
        }
        restartRequired = true;
        AppLog.Info("Uninstall.Dependency", "Proven Addon-owned HidHide package was removed.", ("ExitCode", exitCode));
        return true;
    }

    private bool TryResolveHidHideCommand(HidHideUninstallCandidate candidate, out RegisteredUninstallCommand command)
    {
        if (candidate.WindowsInstaller == true
            && Guid.TryParse(candidate.SubKey?.Trim().Trim('{', '}'), out var productCode))
        {
            var msiexec = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe");
            if (_fileExists(msiexec))
            {
                command = new(msiexec, $"/x {{{productCode:D}}} /qn /norestart");
                return true;
            }
        }

        return RegisteredUninstallCommandResolver.TryResolve(candidate.QuietUninstallString, candidate.UninstallString, _fileExists, out command);
    }

    private bool RunUninstaller(string package, RegisteredUninstallCommand command, out int exitCode)
    {
        var started = _processRunner.TryRun(command.FileName, command.Arguments, out exitCode);
        AppLog.Info("Uninstall.Dependency", "Registered prerequisite uninstaller completed.",
            ("Package", package), ("Started", started), ("ExitCode", started ? exitCode : null));
        return started;
    }

    private static bool RemoveProvisioningDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                return false;
            Directory.Delete(fullPath, recursive: true);
        }
        if (Directory.Exists(fullPath)) return false;

        var parent = Directory.GetParent(fullPath)?.FullName;
        if (parent is null || !Directory.Exists(parent)) return true;
        if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) return false;
        if (Directory.EnumerateFileSystemEntries(parent).Any()) return true;
        try { Directory.Delete(parent, recursive: false); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) when (Directory.Exists(parent) && Directory.EnumerateFileSystemEntries(parent).Any()) { }
        return !Directory.Exists(fullPath);
    }

    private static void LogPreserved(string package, string reason) =>
        AppLog.Info("Uninstall.Dependency", "Prerequisite was preserved because Addon ownership was not safely proven.",
            ("Package", package), ("Reason", reason));
}

internal static class RegisteredUninstallCommandResolver
{
    internal static bool TryResolve(
        string? quietUninstallString,
        string? uninstallString,
        Func<string, bool>? fileExists,
        out RegisteredUninstallCommand command)
    {
        fileExists ??= File.Exists;
        command = new(string.Empty, string.Empty);
        var registered = !string.IsNullOrWhiteSpace(quietUninstallString)
            ? quietUninstallString
            : uninstallString;
        if (string.IsNullOrWhiteSpace(registered)) return false;

        var text = registered.Trim();
        string executable;
        string arguments;
        if (text[0] == '"')
        {
            var closingQuote = text.IndexOf('"', 1);
            if (closingQuote <= 1) return false;
            executable = text[1..closingQuote];
            arguments = text[(closingQuote + 1)..].Trim();
        }
        else
        {
            var extensionEnd = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (extensionEnd < 0) return false;
            executable = text[..(extensionEnd + 4)].Trim();
            arguments = text[(extensionEnd + 4)..].Trim();
        }

        executable = Environment.ExpandEnvironmentVariables(executable);
        if (!Path.IsPathFullyQualified(executable) || !fileExists(executable)) return false;
        command = new(Path.GetFullPath(executable), arguments);
        return true;
    }
}
