using System.Diagnostics;
using System.Security.Cryptography;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Startup;

namespace SteamInputAddonforClaw.Prerequisites;

internal static class ElevatedPrerequisiteSetup
{
    internal const string Argument = "--elevated-prerequisite-setup";
    public static int Run()
    {
        using var mutex = new Mutex(false, @"Global\SteamInputAddonforClaw.PrerequisiteSetup", out var created);
        if (!created) return 2;
        try
        {
            if (ProvisioningStorageSecurity.EnsureTrustedStorage(VelopackAppPaths.ProvisioningStateDirectory).Status != ProvisioningStorageStatus.Trusted)
                return 1;
            var hidStore = new HidHideProvisioningReceiptStore(VelopackAppPaths.HidHideProvisioningReceiptPath);
            var usbStore = new UsbIpWin2ProvisioningReceiptStore(VelopackAppPaths.UsbIpWin2ProvisioningReceiptPath);
            if (!SafetyAllowsMutation()) return 1;
            var restartRequired = false;
            var hidHide = new WindowsHidHidePackageProbe().Inspect();
            if (!hidHide.InspectionSucceeded) return 1;
            var hidPrerequisite = new HidHidePrerequisiteInspector(new HidHideDriverClient()).Inspect();
            ReconcileHidHideReceipt(hidStore, hidHide, hidPrerequisite);
            if (!hidHide.Installed)
            {
                if (File.Exists(VelopackAppPaths.LegacyHidHideProvisioningReceiptPath)) return 1;
                var receipt = new HidHideProvisioningReceipt(1, HidHideProvisioningReceiptState.InstallStarted, Guid.NewGuid(), HidHidePackageMetadata.BundledVersion.ToString(), HidHidePackageMetadata.InstallerSha256, PrerequisiteStatus.Missing, DateTimeOffset.UtcNow, null, null);
                hidStore.Save(receipt);
                if (!SafetyAllowsMutation())
                {
                    hidStore.Save(receipt with { State = HidHideProvisioningReceiptState.AttemptCancelled, CompletedAtUtc = DateTimeOffset.UtcNow });
                    return 1;
                }
                var code = RunChild(HidHidePackageMetadata.InstallerPath, "/exenoui /qn /norestart", HidHidePackageMetadata.InstallerSha256);
                var after = new WindowsHidHidePackageProbe().Inspect();
                var afterPrerequisite = new HidHidePrerequisiteInspector(new HidHideDriverClient()).Inspect();
                hidStore.Save(receipt with { State = code == 3010 || afterPrerequisite.Status != PrerequisiteStatus.Ready ? HidHideProvisioningReceiptState.InstalledPendingReboot : code == 0 && after.Installed ? HidHideProvisioningReceiptState.Provisioned : HidHideProvisioningReceiptState.AttemptFailed, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = after.Version });
                if (code is not 0 and not 3010) return 1;
                restartRequired |= code == 3010;
            }
            var usbIp = new WindowsUsbIpWin2PackageProbe().Inspect();
            if (!usbIp.InspectionSucceeded) return 1;
            var usbPrerequisite = new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(new WindowsControllerDeviceEnumerator())).Inspect();
            ReconcileUsbIpReceipt(usbStore, usbIp, usbPrerequisite);
            if (!usbIp.Installed)
            {
                var receipt = new UsbIpWin2ProvisioningReceipt(1, UsbIpWin2ProvisioningReceiptState.InstallStarted, Guid.NewGuid(), UsbIpWin2PackageMetadata.BundledVersion.ToString(), UsbIpWin2PackageMetadata.InstallerSha256, PrerequisiteStatus.Missing, DateTimeOffset.UtcNow, null, null);
                usbStore.Save(receipt);
                if (!SafetyAllowsMutation())
                {
                    usbStore.Save(receipt with { State = UsbIpWin2ProvisioningReceiptState.AttemptCancelled, CompletedAtUtc = DateTimeOffset.UtcNow });
                    return 1;
                }
                var code = RunChild(UsbIpWin2PackageMetadata.InstallerPath, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010 /TYPE=compact /NOICONS", UsbIpWin2PackageMetadata.InstallerSha256);
                var after = new WindowsUsbIpWin2PackageProbe().Inspect();
                var afterPrerequisite = new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(new WindowsControllerDeviceEnumerator())).Inspect();
                usbStore.Save(receipt with { State = code == 3010 || afterPrerequisite.Status != PrerequisiteStatus.Ready ? UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot : code == 0 && after.Installed ? UsbIpWin2ProvisioningReceiptState.Provisioned : UsbIpWin2ProvisioningReceiptState.AttemptFailed, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = after.Version });
                if (code is not 0 and not 3010) return 1;
                restartRequired |= code == 3010;
            }
            return restartRequired ? 3010 : 0;
        }
        catch { return 1; }
    }

    private static int RunChild(string path, string arguments, string expectedHash)
    {
        if (!File.Exists(path) || !string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), expectedHash, StringComparison.OrdinalIgnoreCase)) return -1;
        using var process = Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = false });
        if (process is null) return -1;
        process.WaitForExit();
        return process.ExitCode;
    }

    private static void ReconcileHidHideReceipt(HidHideProvisioningReceiptStore store, HidHidePackageState package, PrerequisiteAssessment prerequisite)
    {
        var loaded = store.Load();
        if (loaded.IsCorrupt) throw new InvalidDataException("The HidHide provisioning receipt is corrupt.");
        if (loaded.Receipt is not { State: HidHideProvisioningReceiptState.InstallStarted or HidHideProvisioningReceiptState.InstalledPendingReboot } receipt) return;
        var state = package.Installed && prerequisite.Status == PrerequisiteStatus.Ready
            ? HidHideProvisioningReceiptState.Provisioned
            : package.Installed ? HidHideProvisioningReceiptState.InstalledPendingReboot : HidHideProvisioningReceiptState.AttemptFailed;
        store.Save(receipt with { State = state, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = package.Version });
    }

    private static void ReconcileUsbIpReceipt(UsbIpWin2ProvisioningReceiptStore store, UsbIpWin2PackageState package, PrerequisiteAssessment prerequisite)
    {
        var loaded = store.Load();
        if (loaded.IsCorrupt) throw new InvalidDataException("The usbip-win2 provisioning receipt is corrupt.");
        if (loaded.Receipt is not { State: UsbIpWin2ProvisioningReceiptState.InstallStarted or UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot } receipt) return;
        var state = package.Installed && prerequisite.Status == PrerequisiteStatus.Ready
            ? UsbIpWin2ProvisioningReceiptState.Provisioned
            : package.Installed ? UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot : UsbIpWin2ProvisioningReceiptState.AttemptFailed;
        store.Save(receipt with { State = state, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = package.Version });
    }

    private static bool SafetyAllowsMutation()
    {
        try
        {
            var software = new IControllerSoftwareStatusProvider[]
            {
                new MsiCenterMSoftwareStatusProvider(),
                new ClawTweaksSoftwareStatusProvider(new ClawTweaksInstallationProbe(), new ClawTweaksRuntimeDetector()),
                new HandheldCompanionSoftwareStatusProvider(new HandheldCompanionRuntimeDetector())
            };
            var compatibility = new CurrentControllerEnvironmentCompatibilityPolicy().Evaluate(software.Select(provider => provider.Capture()).ToArray());
            if (!compatibility.AllowsMutation) return false;
            var devices = new WindowsControllerDeviceEnumerator();
            if (new ExternalControllerDetector(devices, new ControllerDeviceClassifier()).Detect().Status != ExternalControllerAssessmentStatus.Clear) return false;
            using var runningAppId = new SteamRunningAppIdRegistrySource();
            return !SteamSessionState.FromRunningAppId(runningAppId.GetRunningAppId()).IsActive;
        }
        catch { return false; }
    }
}
