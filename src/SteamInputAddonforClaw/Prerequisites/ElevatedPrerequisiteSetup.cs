using System.Diagnostics;
using System.Security.Cryptography;
using SteamInputAddonforClaw.HidHide;
using SteamInputAddonforClaw.Install;
using SteamInputAddonforClaw.Controllers.Detection;
using SteamInputAddonforClaw.Status;
using SteamInputAddonforClaw.Steam;
using SteamInputAddonforClaw.Startup;
using SteamInputAddonforClaw.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using SteamInputAddonforClaw.Recovery;
using SteamInputAddonforClaw.Devices.MSI.Claw;

namespace SteamInputAddonforClaw.Prerequisites;

internal static class ElevatedPrerequisiteSetup
{
    internal const string Argument = "--elevated-prerequisite-setup";
    internal enum ResultKind { Ready, Installed, RebootRequired, Cancelled, Blocked, Failed, AlreadyInProgress }
    internal static ResultKind TranslateExitCode(ElevatedProcessResult result) => result.Kind switch
    {
        ElevatedProcessResultKind.CancelledBeforeStart => ResultKind.Cancelled,
        ElevatedProcessResultKind.Completed when result.ExitCode == 0 => ResultKind.Installed,
        ElevatedProcessResultKind.Completed when result.ExitCode == 3010 => ResultKind.RebootRequired,
        ElevatedProcessResultKind.Completed when result.ExitCode == 2 => ResultKind.AlreadyInProgress,
        ElevatedProcessResultKind.Completed when result.ExitCode == 3 => ResultKind.Blocked,
        _ => ResultKind.Failed
    };
    public static int Run()
    {
        Mutex mutex;
        bool created;
        try { mutex = CreateSetupMutex(out created); }
        catch (Exception exception)
        {
            AppLog.Error("PrerequisiteSetup", "Prerequisite setup mutex could not be opened safely.", exception, ("Reason", "SetupMutexUnavailable"));
            return 1;
        }
        using (mutex)
        {
        if (!created)
        {
            AppLog.Warn("PrerequisiteSetup", "Prerequisite setup was blocked because another helper is active.", null, ("Reason", "SetupMutexAlreadyHeld"));
            return 2;
        }
        try
        {
            AppLog.Info("PrerequisiteSetup", "Elevated prerequisite setup started.", ("PID", Environment.ProcessId), ("ReceiptDirectory", VelopackAppPaths.ProvisioningStateDirectory));
            var storage = ProvisioningStorageSecurity.EnsureTrustedStorage(VelopackAppPaths.ProvisioningStateDirectory);
            AppLog.Info("PrerequisiteSetup", "Provisioning receipt storage assessed.", ("Status", storage.Status), ("Reason", storage.Reason));
            if (storage.Status != ProvisioningStorageStatus.Trusted)
                return 1;
            var hidStore = new HidHideProvisioningReceiptStore(VelopackAppPaths.HidHideProvisioningReceiptPath);
            var usbStore = new UsbIpWin2ProvisioningReceiptStore(VelopackAppPaths.UsbIpWin2ProvisioningReceiptPath);
            if (!LogAndAllowSafetyGate("Initial")) return 1;
            var restartRequired = false;
            var hidHide = new WindowsHidHidePackageProbe().Inspect();
            AppLog.Info("PrerequisiteSetup", "HidHide package probe completed.", ("Installed", hidHide.Installed), ("Version", hidHide.Version), ("InspectionSucceeded", hidHide.InspectionSucceeded));
            if (!hidHide.InspectionSucceeded) return 1;
            var hidPrerequisite = new HidHidePrerequisiteInspector(new HidHideDriverClient()).Inspect();
            AppLog.Info("PrerequisiteSetup", "HidHide prerequisite probe completed.", ("Status", hidPrerequisite.Status), ("Reason", hidPrerequisite.Reason));
            ReconcileHidHideReceipt(hidStore, hidHide, hidPrerequisite);
            var hidReceipt = hidStore.Load().Receipt;
            var hidPendingReboot = hidReceipt?.State == HidHideProvisioningReceiptState.InstalledPendingReboot;
            var hidAction = PrerequisiteSetupExecutionPolicy.SelectAction(hidHide.Installed, hidPrerequisite.Status, hidPendingReboot, hidReceipt?.State == HidHideProvisioningReceiptState.InstallStarted);
            if (hidAction == PrerequisiteComponentAction.RestartRequired) restartRequired = true;
            if (hidAction == PrerequisiteComponentAction.Blocked)
            {
                AppLog.Warn("PrerequisiteSetup", "HidHide is installed but its control device is not ready; reinstall is forbidden.", null, ("Reason", "HidHidePackagePresentPrerequisiteUnusable"), ("PrerequisiteStatus", hidPrerequisite.Status));
                return 3;
            }
            if (!hidHide.Installed)
            {
                if (File.Exists(VelopackAppPaths.LegacyHidHideProvisioningReceiptPath))
                {
                    AppLog.Warn("PrerequisiteSetup", "HidHide installation was blocked by an untrusted legacy receipt.", null, ("Reason", "LegacyHidHideReceiptPresent"));
                    return 1;
                }
                var receipt = new HidHideProvisioningReceipt(1, HidHideProvisioningReceiptState.InstallStarted, Guid.NewGuid(), HidHidePackageMetadata.BundledVersion.ToString(), HidHidePackageMetadata.InstallerSha256, PrerequisiteStatus.Missing, DateTimeOffset.UtcNow, null, null);
                hidStore.Save(receipt);
                AppLog.Info("PrerequisiteSetup", "HidHide installation receipt persisted.", ("AttemptId", receipt.AttemptId), ("State", receipt.State), ("Version", receipt.InstallerVersion));
                if (!LogAndAllowSafetyGate("BeforeHidHideInstall"))
                {
                    hidStore.Save(receipt with { State = HidHideProvisioningReceiptState.AttemptCancelled, CompletedAtUtc = DateTimeOffset.UtcNow, FailureReason = "SafetyGateBlockedBeforeHidHideInstall" });
                    return 1;
                }
                var code = RunChild("HidHide", HidHidePackageMetadata.InstallerPath, "/exenoui /qn /norestart", HidHidePackageMetadata.InstallerSha256);
                var after = new WindowsHidHidePackageProbe().Inspect();
                var afterPrerequisite = new HidHidePrerequisiteInspector(new HidHideDriverClient()).Inspect();
                var outcome = PrerequisiteSetupExecutionPolicy.EvaluatePostInstall(code, after.InspectionSucceeded, after.Installed, after.Version, receipt.InstallerVersion, afterPrerequisite.Status);
                var state = outcome.IsProvisioned ? HidHideProvisioningReceiptState.Provisioned : outcome.RequiresRestart ? HidHideProvisioningReceiptState.InstalledPendingReboot : HidHideProvisioningReceiptState.AttemptFailed;
                hidStore.Save(receipt with { State = state, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = after.Version, FailureReason = outcome.Reason, InstallerExitCode = code });
                AppLog.Info("PrerequisiteSetup", "HidHide installation result recorded.", ("AttemptId", receipt.AttemptId), ("ExitCode", code), ("ReceiptState", state), ("PackageInstalled", after.Installed), ("PackageVersion", after.Version), ("PrerequisiteStatus", afterPrerequisite.Status));
                if (!outcome.IsProvisioned && !outcome.RequiresRestart) return 1;
                restartRequired |= code == 3010;
            }
            var usbIp = new WindowsUsbIpWin2PackageProbe().Inspect();
            AppLog.Info("PrerequisiteSetup", "usbip-win2 package probe completed.", ("Installed", usbIp.Installed), ("Version", usbIp.Version), ("InspectionSucceeded", usbIp.InspectionSucceeded));
            if (!usbIp.InspectionSucceeded) return 1;
            var usbPrerequisite = new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(new WindowsControllerDeviceEnumerator())).Inspect();
            AppLog.Info("PrerequisiteSetup", "usbip-win2 prerequisite probe completed.", ("Status", usbPrerequisite.Status), ("Reason", usbPrerequisite.Reason));
            ReconcileUsbIpReceipt(usbStore, usbIp, usbPrerequisite);
            var usbReceipt = usbStore.Load().Receipt;
            var usbPendingReboot = usbReceipt?.State == UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot;
            var usbAction = PrerequisiteSetupExecutionPolicy.SelectAction(usbIp.Installed, usbPrerequisite.Status, usbPendingReboot, usbReceipt?.State == UsbIpWin2ProvisioningReceiptState.InstallStarted);
            if (usbAction == PrerequisiteComponentAction.RestartRequired) restartRequired = true;
            if (usbAction == PrerequisiteComponentAction.Blocked)
            {
                AppLog.Warn("PrerequisiteSetup", "usbip-win2 is installed but its control device is not ready; reinstall is forbidden.", null, ("Reason", "UsbIpWin2PackagePresentPrerequisiteUnusable"), ("PrerequisiteStatus", usbPrerequisite.Status));
                return 3;
            }
            if (!usbIp.Installed)
            {
                var receipt = new UsbIpWin2ProvisioningReceipt(1, UsbIpWin2ProvisioningReceiptState.InstallStarted, Guid.NewGuid(), UsbIpWin2PackageMetadata.BundledVersion.ToString(), UsbIpWin2PackageMetadata.InstallerSha256, PrerequisiteStatus.Missing, DateTimeOffset.UtcNow, null, null);
                usbStore.Save(receipt);
                AppLog.Info("PrerequisiteSetup", "usbip-win2 installation receipt persisted.", ("AttemptId", receipt.AttemptId), ("State", receipt.State), ("Version", receipt.InstallerVersion));
                if (!LogAndAllowSafetyGate("BeforeUsbIpInstall"))
                {
                    usbStore.Save(receipt with { State = UsbIpWin2ProvisioningReceiptState.AttemptCancelled, CompletedAtUtc = DateTimeOffset.UtcNow, FailureReason = "SafetyGateBlockedBeforeUsbIpInstall" });
                    return 1;
                }
                var code = RunChild("usbip-win2", UsbIpWin2PackageMetadata.InstallerPath, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010 /TYPE=compact /NOICONS", UsbIpWin2PackageMetadata.InstallerSha256);
                var after = new WindowsUsbIpWin2PackageProbe().Inspect();
                var afterPrerequisite = new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(new WindowsControllerDeviceEnumerator())).Inspect();
                var outcome = PrerequisiteSetupExecutionPolicy.EvaluatePostInstall(code, after.InspectionSucceeded, after.Installed, after.Version, receipt.InstallerVersion, afterPrerequisite.Status);
                var state = outcome.IsProvisioned ? UsbIpWin2ProvisioningReceiptState.Provisioned : outcome.RequiresRestart ? UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot : UsbIpWin2ProvisioningReceiptState.AttemptFailed;
                usbStore.Save(receipt with { State = state, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = after.Version, FailureReason = outcome.Reason, InstallerExitCode = code });
                AppLog.Info("PrerequisiteSetup", "usbip-win2 installation result recorded.", ("AttemptId", receipt.AttemptId), ("ExitCode", code), ("ReceiptState", state), ("PackageInstalled", after.Installed), ("PackageVersion", after.Version), ("PrerequisiteStatus", afterPrerequisite.Status));
                if (!outcome.IsProvisioned && !outcome.RequiresRestart) return 1;
                restartRequired |= code == 3010;
            }
            var result = restartRequired ? 3010 : 0;
            AppLog.Info("PrerequisiteSetup", "Elevated prerequisite setup completed.", ("ExitCode", result), ("RestartRequired", restartRequired));
            return result;
        }
        catch (Exception exception)
        {
            AppLog.Error("PrerequisiteSetup", "Elevated prerequisite setup failed unexpectedly.", exception);
            return 1;
        }
        }
    }

    private static Mutex CreateSetupMutex(out bool created)
    {
        var security = new MutexSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), MutexRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), MutexRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), MutexRights.Synchronize | MutexRights.Modify, AccessControlType.Allow));
        return MutexAcl.Create(false, @"Global\SteamInputAddonforClaw.PrerequisiteSetup", out created, security);
    }

    private static int RunChild(string component, string path, string arguments, string expectedHash)
    {
        if (!File.Exists(path))
        {
            AppLog.Error("PrerequisiteSetup", "Bundled prerequisite installer was not found.", new FileNotFoundException(path), ("Component", component), ("Reason", "InstallerMissing"));
            return -1;
        }
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            AppLog.Error("PrerequisiteSetup", "Bundled prerequisite installer hash validation failed.", new InvalidDataException("Installer hash mismatch."), ("Component", component), ("Reason", "InstallerHashMismatch"));
            return -1;
        }
        AppLog.Info("PrerequisiteSetup", "Prerequisite installer launch started.", ("Component", component), ("InstallerPath", path));
        using var process = Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = false });
        if (process is null)
        {
            AppLog.Error("PrerequisiteSetup", "Prerequisite installer process was not created.", new InvalidOperationException("Process.Start returned null."), ("Component", component), ("Reason", "InstallerProcessNotCreated"));
            return -1;
        }
        process.WaitForExit();
        AppLog.Info("PrerequisiteSetup", "Prerequisite installer process exited.", ("Component", component), ("ExitCode", process.ExitCode));
        return process.ExitCode;
    }

    private static void ReconcileHidHideReceipt(HidHideProvisioningReceiptStore store, HidHidePackageState package, PrerequisiteAssessment prerequisite)
    {
        var loaded = store.Load();
        if (loaded.IsCorrupt) throw new InvalidDataException("The HidHide provisioning receipt is corrupt.");
        if (loaded.Receipt is not { State: HidHideProvisioningReceiptState.InstallStarted or HidHideProvisioningReceiptState.InstalledPendingReboot } receipt) return;
        if (!package.Installed)
        {
            AppLog.Warn("PrerequisiteSetup", "HidHide InstallStarted receipt remains unresolved because the package is still missing.", null, ("AttemptId", receipt.AttemptId), ("Reason", "InstallStartedPackageMissing"));
            return;
        }
        var state = package.Installed && prerequisite.Status == PrerequisiteStatus.Ready
            ? HidHideProvisioningReceiptState.Provisioned
            : package.Installed ? HidHideProvisioningReceiptState.InstalledPendingReboot : HidHideProvisioningReceiptState.AttemptFailed;
        store.Save(receipt with { State = state, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = package.Version, FailureReason = state == HidHideProvisioningReceiptState.AttemptFailed ? "HidHidePackageNotDetectedDuringReconciliation" : null });
        AppLog.Info("PrerequisiteSetup", "HidHide receipt reconciled.", ("AttemptId", receipt.AttemptId), ("PreviousState", receipt.State), ("State", state), ("PackageInstalled", package.Installed), ("PackageVersion", package.Version), ("PrerequisiteStatus", prerequisite.Status));
    }

    private static void ReconcileUsbIpReceipt(UsbIpWin2ProvisioningReceiptStore store, UsbIpWin2PackageState package, PrerequisiteAssessment prerequisite)
    {
        var loaded = store.Load();
        if (loaded.IsCorrupt) throw new InvalidDataException("The usbip-win2 provisioning receipt is corrupt.");
        if (loaded.Receipt is not { State: UsbIpWin2ProvisioningReceiptState.InstallStarted or UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot } receipt) return;
        if (!package.Installed)
        {
            AppLog.Warn("PrerequisiteSetup", "usbip-win2 InstallStarted receipt remains unresolved because the package is still missing.", null, ("AttemptId", receipt.AttemptId), ("Reason", "InstallStartedPackageMissing"));
            return;
        }
        var state = package.Installed && prerequisite.Status == PrerequisiteStatus.Ready
            ? UsbIpWin2ProvisioningReceiptState.Provisioned
            : package.Installed ? UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot : UsbIpWin2ProvisioningReceiptState.AttemptFailed;
        store.Save(receipt with { State = state, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = package.Version, FailureReason = state == UsbIpWin2ProvisioningReceiptState.AttemptFailed ? "UsbIpWin2PackageNotDetectedDuringReconciliation" : null });
        AppLog.Info("PrerequisiteSetup", "usbip-win2 receipt reconciled.", ("AttemptId", receipt.AttemptId), ("PreviousState", receipt.State), ("State", state), ("PackageInstalled", package.Installed), ("PackageVersion", package.Version), ("PrerequisiteStatus", prerequisite.Status));
    }

    private static bool LogAndAllowSafetyGate(string stage)
    {
        var result = EvaluateSafetyGate();
        AppLog.Info("PrerequisiteSetup", "Prerequisite safety gate evaluated.", ("Stage", stage), ("Allowed", result.Allowed), ("Reason", result.Reason));
        return result.Allowed;
    }

    private static (bool Allowed, string Reason) EvaluateSafetyGate()
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
            if (!compatibility.AllowsMutation) return (false, "Compatibility" + compatibility.Reason);
            if (new RecoveryJournalStore(VelopackAppPaths.RecoveryJournalPath).Exists()) return (false, "RecoveryJournalPresent");
            var external = DetectExternalControllers(new WindowsControllerDeviceEnumerator());
            if (external.Status != ExternalControllerAssessmentStatus.Clear) return (false, "ExternalController" + external.Status);
            using var runningAppId = new SteamRunningAppIdRegistrySource();
            var steam = SteamSessionState.FromRunningAppId(runningAppId.GetRunningAppId());
            return steam.IsActive ? (false, "SteamSessionActive") : (true, "Allowed");
        }
        catch (Exception exception)
        {
            AppLog.Warn("PrerequisiteSetup", "Prerequisite safety gate inspection failed.", exception, ("Reason", "SafetyGateInspectionFailed"));
            return (false, "SafetyGateInspectionFailed");
        }
    }

    internal static ExternalControllerAssessment DetectExternalControllers(IControllerDeviceEnumerator devices)
    {
        var msiAdapter = new MsiClawDeviceAdapter(devices);
        return new ExternalControllerDetector(
            devices,
            new ControllerDeviceClassifier(msiAdapter.InternalControllerMatcher)).Detect();
    }
}
