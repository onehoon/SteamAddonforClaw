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
                var state = code is not 0 and not 3010 ? HidHideProvisioningReceiptState.AttemptFailed : code == 3010 || afterPrerequisite.Status != PrerequisiteStatus.Ready ? HidHideProvisioningReceiptState.InstalledPendingReboot : HidHideProvisioningReceiptState.Provisioned;
                hidStore.Save(receipt with { State = state, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = after.Version, FailureReason = state == HidHideProvisioningReceiptState.AttemptFailed ? "HidHideInstallerExitCode" + code : null, InstallerExitCode = code });
                AppLog.Info("PrerequisiteSetup", "HidHide installation result recorded.", ("AttemptId", receipt.AttemptId), ("ExitCode", code), ("ReceiptState", state), ("PackageInstalled", after.Installed), ("PackageVersion", after.Version), ("PrerequisiteStatus", afterPrerequisite.Status));
                if (code is not 0 and not 3010) return 1;
                restartRequired |= code == 3010;
            }
            var usbIp = new WindowsUsbIpWin2PackageProbe().Inspect();
            AppLog.Info("PrerequisiteSetup", "usbip-win2 package probe completed.", ("Installed", usbIp.Installed), ("Version", usbIp.Version), ("InspectionSucceeded", usbIp.InspectionSucceeded));
            if (!usbIp.InspectionSucceeded) return 1;
            var usbPrerequisite = new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(new WindowsControllerDeviceEnumerator())).Inspect();
            AppLog.Info("PrerequisiteSetup", "usbip-win2 prerequisite probe completed.", ("Status", usbPrerequisite.Status), ("Reason", usbPrerequisite.Reason));
            ReconcileUsbIpReceipt(usbStore, usbIp, usbPrerequisite);
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
                var state = code is not 0 and not 3010 ? UsbIpWin2ProvisioningReceiptState.AttemptFailed : code == 3010 || afterPrerequisite.Status != PrerequisiteStatus.Ready ? UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot : UsbIpWin2ProvisioningReceiptState.Provisioned;
                usbStore.Save(receipt with { State = state, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = after.Version, FailureReason = state == UsbIpWin2ProvisioningReceiptState.AttemptFailed ? "UsbIpWin2InstallerExitCode" + code : null, InstallerExitCode = code });
                AppLog.Info("PrerequisiteSetup", "usbip-win2 installation result recorded.", ("AttemptId", receipt.AttemptId), ("ExitCode", code), ("ReceiptState", state), ("PackageInstalled", after.Installed), ("PackageVersion", after.Version), ("PrerequisiteStatus", afterPrerequisite.Status));
                if (code is not 0 and not 3010) return 1;
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
            var devices = new WindowsControllerDeviceEnumerator();
            var external = new ExternalControllerDetector(devices, new ControllerDeviceClassifier()).Detect();
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
}
