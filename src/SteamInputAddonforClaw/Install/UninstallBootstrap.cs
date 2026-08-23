using System.Diagnostics;
using Microsoft.Win32;
using SteamInputAddonforClaw.Diagnostics;
using SteamInputAddonforClaw.Prerequisites;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Lifecycle;
using SteamInputAddonforClaw.VirtualOutput.Viiper;

namespace SteamInputAddonforClaw.Install;

internal static class UninstallBootstrap
{
    internal const string ElevatedArgument = "--elevated-uninstall-cleanup";

    internal static void Run()
    {
        AppLog.Info("Uninstall", "Velopack uninstall cleanup started.", ("FastCallback", true));
        RequestRunningRuntimeShutdown();
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var result = RequestElevatedCleanupAsync(() => new ElevatedProcessRunner().RunAsync(processPath, ElevatedArgument, CancellationToken.None)).GetAwaiter().GetResult();
                AppLog.Info("Uninstall", "Elevated cleanup completed.", ("Result", result.Kind), ("ExitCode", result.ExitCode));
            }
        }
        catch (Exception exception) { AppLog.Warn("Uninstall", "Elevated cleanup could not be completed; continuing safe user cleanup.", exception); }

        try { new WindowsTaskSchedulerStartupManager().Synchronize(false); } catch (Exception exception) { AppLog.Warn("Uninstall", "Startup registration cleanup failed.", exception); }
        TryDeleteDirectory(CenterM.CenterMHelperStaging.RuntimeDirectory);
        SteamCefDebugBootstrap.RemoveOwnedMarker();
        TryDeleteDirectory(VelopackAppPaths.ProvisioningStateDirectory);
        TryDeleteFile(VelopackAppPaths.LegacyHidHideProvisioningReceiptPath);
        TryDeleteDirectory(VelopackAppPaths.CefMarkerOwnershipDirectory);
        AddonDataPaths.DeleteFullResetRoot(VelopackAppPaths.RootAppDirectory);
        AppLog.Info("Uninstall", "User-level Addon cleanup completed.");
    }

    internal static async Task<ElevatedProcessResult> RequestElevatedCleanupAsync(Func<Task<ElevatedProcessResult>> request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await request().ConfigureAwait(false);
    }

    private static void RequestRunningRuntimeShutdown()
    {
        try
        {
            if (!SingleInstanceGate.RequestPrimaryUninstall()) return;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline)
            {
                try
                {
                    using var probe = SingleInstanceGate.CreateForCurrentUser();
                    if (probe.IsPrimaryInstance) return;
                }
                catch { return; }
                Thread.Sleep(100);
            }
            AppLog.Warn("Uninstall", "Running Addon did not release its single-instance ownership before the bounded shutdown wait expired.", null, ("Action", "PreserveDependencySafety"));
        }
        catch (Exception exception) { AppLog.Warn("Uninstall", "Running Addon shutdown request failed.", exception); }
    }

    internal static int RunElevated()
    {
        try
        {
            if (!UninstallSafetyCoordinator.Prepare())
            {
                AppLog.Warn("Uninstall", "Ownership safety gate did not pass; dependency packages were preserved.", null, ("Action", "ContinueUserCleanup"));
                return 0;
            }
            var hid = new HidHideProvisioningReceiptStore(VelopackAppPaths.HidHideProvisioningReceiptPath).Load().Receipt;
            var usb = new UsbIpWin2ProvisioningReceiptStore(VelopackAppPaths.UsbIpWin2ProvisioningReceiptPath).Load().Receipt;
            if (hid is not null) RemoveHidHideIfExact(hid);
            if (usb is not null) RemoveUsbIfExact(usb);
            return 0;
        }
        catch (Exception exception) { AppLog.Error("Uninstall", "Elevated dependency cleanup failed.", exception); return 1; }
    }

    private static void RemoveHidHideIfExact(HidHideProvisioningReceipt receipt)
    {
        if (receipt.PreProvisioningStatus != PrerequisiteStatus.Missing) return;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            foreach (var name in uninstall?.GetSubKeyNames() ?? [])
            {
                using var key = uninstall!.OpenSubKey(name);
                if (key is null || !string.Equals(key.GetValue("DisplayName") as string, "HidHide", StringComparison.Ordinal) ||
                    !string.Equals(key.GetValue("Publisher") as string, "Nefarius Software Solutions e.U.", StringComparison.Ordinal) ||
                    !HidHidePackageVersionPolicy.AreEquivalent(key.GetValue("DisplayVersion") as string, receipt.InstallerVersion) ||
                    !UninstallDependencyOwnershipPolicy.CanRemoveHidHide(receipt, new(true, key.GetValue("DisplayVersion") as string, true))) continue;
                RunUninstaller(key.GetValue("QuietUninstallString") as string, "HidHide");
                var after = new WindowsHidHidePackageProbe().Inspect();
                AppLog.Info("Uninstall", "HidHide package removal was re-probed.", ("Installed", after.Installed), ("Version", after.Version), ("VerifiedRemoved", UninstallPackageRemovalPolicy.IsVerifiedRemoved(null, after.Installed)));
                return;
            }
        }
    }

    private static void RemoveUsbIfExact(UsbIpWin2ProvisioningReceipt receipt)
    {
        if (receipt.PreProvisioningStatus != PrerequisiteStatus.Missing) return;
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{199505b0-b93d-4521-a8c7-897818e0205a}_is1");
        if (key is null || !UninstallDependencyOwnershipPolicy.CanRemoveUsbIp(receipt, new(true, key.GetValue("DisplayVersion") as string, true, true, key.GetValue("QuietUninstallString") as string))) return;
        RunUninstaller(key.GetValue("QuietUninstallString") as string, "usbip-win2");
        var after = new WindowsUsbIpWin2PackageProbe().Inspect();
        AppLog.Info("Uninstall", "usbip-win2 package removal was re-probed.", ("Installed", after.Installed), ("Version", after.Version), ("VerifiedRemoved", UninstallPackageRemovalPolicy.IsVerifiedRemoved(null, after.Installed)));
    }

    private static void RunUninstaller(string? command, string package)
    {
        if (string.IsNullOrWhiteSpace(command)) { AppLog.Warn("Uninstall", "Exact package has no QuietUninstallString; package preserved.", null, ("Package", package)); return; }
        var trimmed = command.Trim();
        var fileName = trimmed.StartsWith('"') ? trimmed[1..trimmed.IndexOf('"', 1)] : trimmed.Split(' ', 2)[0];
        var arguments = trimmed.Length > fileName.Length + (trimmed.StartsWith('"') ? 2 : 0) ? trimmed[(trimmed.StartsWith('"') ? fileName.Length + 2 : fileName.Length)..].Trim() : string.Empty;
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = false });
        process?.WaitForExit();
        AppLog.Info("Uninstall", "Exact dependency uninstaller completed.", ("Package", package), ("ExitCode", process?.ExitCode), ("ReprobeRequired", true));
    }

    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (Exception e) { AppLog.Warn("Uninstall", "Directory cleanup failed.", e, ("Path", path)); } }
    private static void TryDeleteFile(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (Exception e) { AppLog.Warn("Uninstall", "File cleanup failed.", e, ("Path", path)); } }
}

internal static class UninstallPackageRemovalPolicy
{
    internal static bool IsVerifiedRemoved(int? exitCode, bool packageStillPresent) => !packageStillPresent;
}

internal static class UninstallSafetyCoordinator
{
    internal static bool Prepare()
    {
        var journalStore = new RecoveryJournalStore(AddonDataPaths.RecoveryJournalPath);
        var loaded = new RecoveryManager(journalStore).LoadJournal();
        if (loaded.Status == RecoveryStatus.NoRecoveryNeeded)
        {
            try
            {
                var liveVirtual = new WindowsControllerDeviceEnumerator().EnumeratePresentDevices()
                    .Any(device => device.Present && device.VendorId == SteamDeckVirtualDeviceIdentityPolicy.VendorId && device.ProductId == SteamDeckVirtualDeviceIdentityPolicy.ProductId);
                if (liveVirtual)
                {
                    AppLog.Warn("Uninstall", "An exact Steam Deck virtual output is present without recovery ownership evidence; dependency removal is blocked.", null, ("Action", "PreserveDependencySafety"));
                    return false;
                }
            }
            catch (Exception exception)
            {
                AppLog.Warn("Uninstall", "Virtual-output safety probe failed without recovery evidence; dependency removal is blocked.", exception);
                return false;
            }
            return true;
        }
        if (loaded.Status != RecoveryStatus.Success || loaded.Journal is not { } journal) return false;

        var devices = new WindowsControllerDeviceEnumerator();
        if (journal.OriginalDeviceState is not null || journal.Mutations.DeviceNativeStateChanged)
        {
            var adapter = new MsiClawDeviceAdapter(devices);
            if (adapter.NativeState is not MsiClawNativeStateManager nativeState) return false;
            var baseline = new StockCenterMStartupBaseline(nativeState).EstablishAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (!baseline.Succeeded) return false;
        }

        if (journal.Mutations.AddonOwnedVirtualDeviceEntries is { Count: > 0 } virtualEntries)
        {
            var virtualAssessment = new StartupVirtualOutputRecoveryInspector(devices).AssessAsync(virtualEntries, CancellationToken.None).GetAwaiter().GetResult();
            if (!virtualAssessment.SafeToRetire) return false;
        }

        if (StartupHidHideRecoveryCleaner.RequiresCleanup(journal))
        {
            var cleaner = new StartupHidHideRecoveryCleaner(new HidHideDriverClient());
            if (!cleaner.TryClean(journal, out _)) return false;
        }

        journalStore.Delete();
        return !journalStore.Exists();
    }
}

internal static class UninstallDependencyOwnershipPolicy
{
    internal static bool CanRemoveHidHide(HidHideProvisioningReceipt? receipt, HidHidePackageState package) =>
        receipt is { IsValid: true, PreProvisioningStatus: PrerequisiteStatus.Missing } &&
        package.InspectionSucceeded && package.Installed &&
        HidHidePackageVersionPolicy.AreEquivalent(package.Version, receipt.InstallerVersion);

    internal static bool CanRemoveUsbIp(UsbIpWin2ProvisioningReceipt? receipt, UsbIpWin2PackageState package) =>
        receipt is { IsValid: true, PreProvisioningStatus: PrerequisiteStatus.Missing } &&
        package.InspectionSucceeded && package.Installed &&
        Version.TryParse(package.Version, out var current) && Version.TryParse(receipt.InstallerVersion, out var expected) && current == expected;
}
