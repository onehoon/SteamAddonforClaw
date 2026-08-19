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
using SteamInputAddonforClaw.Devices.MSI.Claw;
using SteamInputAddonforClaw.Devices;
using SteamInputAddonforClaw.Settings;
using SteamInputAddonforClaw.CenterM;

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
            var devices = new WindowsControllerDeviceEnumerator();
            var adapter = new MsiClawDeviceAdapter(devices);
            var preflight = ElevatedHardwareProvisioningPreflight.Evaluate(
                new WindowsDeviceProbeContextFactory(new WindowsDeviceIdentitySource(), devices),
                new HardwareCompatibilityEvaluator(new HandheldDeviceRegistry([adapter])),
                () => ProvisioningStorageSecurity.EnsureTrustedStorage(VelopackAppPaths.ProvisioningStateDirectory));
            AppLog.Info("PrerequisiteSetup", "Handheld hardware compatibility evaluated.", ("Status", preflight.Hardware.Status), ("Reason", preflight.Hardware.Reason));
            if (preflight.Hardware.Status != HardwareCompatibilityStatus.Supported)
                return 3;
            var storage = preflight.Storage!;
            AppLog.Info("PrerequisiteSetup", "Provisioning receipt storage assessed.", ("Status", storage.Status), ("Reason", storage.Reason));
            if (storage.Status != ProvisioningStorageStatus.Trusted)
                return 1;
            var hidStore = new HidHideProvisioningReceiptStore(VelopackAppPaths.HidHideProvisioningReceiptPath);
            var usbStore = new UsbIpWin2ProvisioningReceiptStore(VelopackAppPaths.UsbIpWin2ProvisioningReceiptPath);
            if (!LogAndAllowSafetyGate("Initial")) return 1;
            var autoRunBefore = CenterMAutoRunReader.Read();
            if (autoRunBefore == CenterMAutoRunState.Enabled)
            {
                AppLog.Info("PrerequisiteSetup", "Explicit AutoRun setup requested.", ("OriginalAutoRun", 1), ("AppliedAutoRun", 0));
                if (!CenterMAutoRunReader.TryDisableExplicitly(out var confirmedAutoRun, out var originalAutoRun)
                    || confirmedAutoRun != CenterMAutoRunState.Disabled)
                {
                    AppLog.Warn("PrerequisiteSetup", "AutoRun setup could not be confirmed by read-back; aborting prerequisite mutation.", null,
                        ("OriginalAutoRun", originalAutoRun), ("ConfirmedAutoRun", confirmedAutoRun));
                    return 1;
                }
                AppLog.Info("PrerequisiteSetup", "AutoRun setup confirmed by read-back.", ("OriginalAutoRun", originalAutoRun), ("AppliedAutoRun", 0));
                var settingsStore = new SettingsStore(AddonDataPaths.SettingsPath);
                var settings = settingsStore.Load();
                settingsStore.Save(settings with { CenterMAutoRunOwnedByAddon = true, OriginalAutoRun = originalAutoRun, AppliedAutoRun = 0 });
            }
            else if (autoRunBefore == CenterMAutoRunState.Unknown)
            {
                AppLog.Warn("PrerequisiteSetup", "AutoRun state is unknown; refusing registry mutation.", null, ("Reason", "AutoRunUnknown"));
                return 1;
            }
            var restartRequired = false;
            var hidHide = new WindowsHidHidePackageProbe().Inspect();
            AppLog.Info("PrerequisiteSetup", "HidHide package probe completed.", ("Installed", hidHide.Installed), ("Version", hidHide.Version), ("InspectionSucceeded", hidHide.InspectionSucceeded));
            if (!hidHide.InspectionSucceeded) return 1;
            var hidPrerequisite = new HidHidePrerequisiteInspector(new HidHideDriverClient()).Inspect();
            AppLog.Info("PrerequisiteSetup", "HidHide prerequisite probe completed.", ("Status", hidPrerequisite.Status), ("Reason", hidPrerequisite.Reason));
            ReconcileHidHideReceipt(hidStore, hidHide, hidPrerequisite);
            var hidInstallation = ComponentInstallationAssessmentPolicy.AssessHidHide(hidHide, hidPrerequisite, HidHidePackageMetadata.BundledVersion.ToString());
            AppLog.Info("PrerequisiteSetup", "HidHide installation assessment completed.", ("InstallationStatus", hidInstallation.Status), ("InstallationReason", hidInstallation.Reason), ("PackageVersion", hidInstallation.Version), ("RuntimeStatus", hidPrerequisite.Status));
            if (hidInstallation.Status is not (ComponentInstallationStatus.Installed or ComponentInstallationStatus.Missing))
            {
                AppLog.Warn("PrerequisiteSetup", "HidHide setup was blocked by an existing or incompatible installation.", null, ("InstallationStatus", hidInstallation.Status), ("Reason", hidInstallation.Reason));
                return 3;
            }
            var hidReceipt = hidStore.Load().Receipt;
            if (hidReceipt?.State == HidHideProvisioningReceiptState.InstalledPendingReboot) restartRequired = true;
            if (hidInstallation.Status == ComponentInstallationStatus.Missing)
            {
                var shortcutCleanup = new HidHideDesktopShortcutCleanup();
                var shortcutsBeforeInstall = shortcutCleanup.Snapshot();
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
                var (after, afterPrerequisite) = code is 0 or 3010
                    ? WaitForHidHidePostInstallEvidence(receipt.InstallerVersion, code)
                    : (new WindowsHidHidePackageProbe().Inspect(), new HidHidePrerequisiteInspector(new HidHideDriverClient()).Inspect());
                var outcome = PrerequisiteSetupExecutionPolicy.EvaluatePostInstall(code, after.InspectionSucceeded, after.Installed, after.Version, receipt.InstallerVersion, afterPrerequisite.Status);
                var state = outcome.IsProvisioned ? HidHideProvisioningReceiptState.Provisioned : outcome.RequiresRestart ? HidHideProvisioningReceiptState.InstalledPendingReboot : HidHideProvisioningReceiptState.AttemptFailed;
                hidStore.Save(receipt with { State = state, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = after.Version, FailureReason = outcome.Reason, InstallerExitCode = code });
                var exactPackageEstablished = HidHideDesktopShortcutCleanup.IsExactPackageEstablished(after, receipt.InstallerVersion);
                if (exactPackageEstablished) shortcutCleanup.RemoveInstallerCreated(shortcutsBeforeInstall);
                AppLog.Info("PrerequisiteSetup", "HidHide installation result recorded.", ("AttemptId", receipt.AttemptId), ("ExitCode", code), ("ReceiptState", state), ("PackageInstalled", after.Installed), ("PackageVersion", after.Version), ("PrerequisiteStatus", afterPrerequisite.Status));
                if (!outcome.IsProvisioned && !outcome.RequiresRestart) return 1;
                restartRequired |= code == 3010;
            }
            var usbPackageProbe = new WindowsUsbIpWin2PackageProbe();
            var usbIp = usbPackageProbe.Inspect();
            AppLog.Info("PrerequisiteSetup", "usbip-win2 package probe completed.", ("Installed", usbIp.Installed), ("Version", usbIp.Version), ("InspectionSucceeded", usbIp.InspectionSucceeded));
            if (!usbIp.InspectionSucceeded) return 1;
            var usbPrerequisite = new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(new WindowsControllerDeviceEnumerator()), usbPackageProbe).Inspect();
            AppLog.Info("PrerequisiteSetup", "usbip-win2 prerequisite probe completed.", ("Status", usbPrerequisite.Status), ("Reason", usbPrerequisite.Reason));
            ReconcileUsbIpReceipt(usbStore, usbIp, usbPrerequisite);
            var usbInstallation = ComponentInstallationAssessmentPolicy.AssessUsbIp(usbIp, usbPrerequisite, UsbIpWin2PackageMetadata.BundledVersion.ToString());
            AppLog.Info("PrerequisiteSetup", "usbip-win2 installation assessment completed.", ("InstallationStatus", usbInstallation.Status), ("InstallationReason", usbInstallation.Reason), ("PackageVersion", usbInstallation.Version), ("RuntimeStatus", usbPrerequisite.Status));
            if (usbInstallation.Status is not (ComponentInstallationStatus.Installed or ComponentInstallationStatus.Missing))
            {
                AppLog.Warn("PrerequisiteSetup", "usbip-win2 setup was blocked by an existing or incompatible installation.", null, ("InstallationStatus", usbInstallation.Status), ("Reason", usbInstallation.Reason));
                return 3;
            }
            var usbReceipt = usbStore.Load().Receipt;
            if (usbReceipt?.State == UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot) restartRequired = true;
            if (usbInstallation.Status == ComponentInstallationStatus.Missing)
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
                var (after, afterPrerequisite) = code is 0 or 3010
                    ? WaitForUsbIpPostInstallEvidence(receipt.InstallerVersion, code)
                    : InspectUsbIpRuntime();
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

    private static (HidHidePackageState Package, PrerequisiteAssessment Prerequisite) WaitForHidHidePostInstallEvidence(string expectedVersion, int installerExitCode)
        => WaitForHidHidePostInstallEvidence(
            () => new WindowsHidHidePackageProbe().Inspect(),
            () => new HidHidePrerequisiteInspector(new HidHideDriverClient()).Inspect(),
            () => Environment.TickCount64,
            Thread.Sleep,
            expectedVersion,
            installerExitCode);

    internal static (HidHidePackageState Package, PrerequisiteAssessment Prerequisite) WaitForHidHidePostInstallEvidence(
        Func<HidHidePackageState> packageProbe,
        Func<PrerequisiteAssessment> prerequisiteProbe,
        Func<long> clock,
        Action<int> delay,
        string expectedVersion,
        int installerExitCode)
    {
        const int timeoutMs = 15000;
        const int pollIntervalMs = 500;
        var started = clock();
        var attempt = 0;
        AppLog.Info("PrerequisiteSetup", "HidHide post-install verification started.", ("TimeoutMs", timeoutMs), ("PollIntervalMs", pollIntervalMs), ("ExpectedVersion", expectedVersion), ("InstallerExitCode", installerExitCode));
        while (true)
        {
            attempt++;
            var package = packageProbe();
            var prerequisite = prerequisiteProbe();
            var packageEvidence = package.InspectionSucceeded && package.Installed && HidHidePackageVersionPolicy.AreEquivalent(package.Version, expectedVersion);
            AppLog.Debug("PrerequisiteSetup", "HidHide post-install verification poll.", ("Attempt", attempt), ("ElapsedMs", clock() - started), ("PackageInstalled", package.Installed), ("ControlStatus", prerequisite.Reason));
            if (packageEvidence)
            {
                AppLog.Info("PrerequisiteSetup", "HidHide installation established.", ("Attempt", attempt), ("ElapsedMs", clock() - started), ("Evidence", "ExpectedPackage"), ("ControlStatus", prerequisite.Reason), ("Action", "ContinuePrerequisiteSetup"));
                return (package, prerequisite);
            }
            if (clock() - started >= timeoutMs) return (package, prerequisite);
            delay(pollIntervalMs);
        }
    }

    private static (UsbIpWin2PackageState Package, PrerequisiteAssessment Prerequisite) WaitForUsbIpPostInstallEvidence(string expectedVersion, int installerExitCode)
        => WaitForUsbIpPostInstallEvidence(
            () => new WindowsUsbIpWin2PackageProbe().Inspect(),
            () => new UsbIpWin2PrerequisiteInspector(new WindowsUsbIpWin2DeviceProbe(new WindowsControllerDeviceEnumerator()), new WindowsUsbIpWin2PackageProbe()).Inspect(),
            () => Environment.TickCount64,
            Thread.Sleep,
            expectedVersion,
            installerExitCode);

    private static (UsbIpWin2PackageState Package, PrerequisiteAssessment Prerequisite) InspectUsbIpRuntime()
    {
        var packageProbe = new WindowsUsbIpWin2PackageProbe();
        var package = packageProbe.Inspect();
        var prerequisite = new UsbIpWin2PrerequisiteInspector(
            new WindowsUsbIpWin2DeviceProbe(new WindowsControllerDeviceEnumerator()),
            packageProbe).Inspect();
        return (package, prerequisite);
    }

    internal static (UsbIpWin2PackageState Package, PrerequisiteAssessment Prerequisite) WaitForUsbIpPostInstallEvidence(
        Func<UsbIpWin2PackageState> packageProbe,
        Func<PrerequisiteAssessment> prerequisiteProbe,
        Func<long> clock,
        Action<int> delay,
        string expectedVersion,
        int installerExitCode)
    {
        const int timeoutMs = 15000;
        const int pollIntervalMs = 500;
        var started = clock();
        while (true)
        {
            var package = packageProbe();
            var prerequisite = prerequisiteProbe();
            var packageEvidence = package.InspectionSucceeded && package.Installed && string.Equals(package.Version, expectedVersion, StringComparison.OrdinalIgnoreCase);
            AppLog.Debug("PrerequisiteSetup", "usbip-win2 post-install verification poll.", ("ElapsedMs", clock() - started), ("PackageInstalled", package.Installed), ("PackageVersion", package.Version), ("RuntimeStatus", prerequisite.Status), ("InstallerExitCode", installerExitCode));
            if (packageEvidence) return (package, prerequisite);
            if (clock() - started >= timeoutMs) return (package, prerequisite);
            delay(pollIntervalMs);
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
        if (loaded.Receipt is not { State: HidHideProvisioningReceiptState.InstallStarted or HidHideProvisioningReceiptState.InstalledPendingReboot or HidHideProvisioningReceiptState.AttemptFailed } receipt) return;
        if (receipt.State is HidHideProvisioningReceiptState.InstallStarted or HidHideProvisioningReceiptState.AttemptFailed
            && package.InspectionSucceeded
            && package.Installed
            && HidHidePackageVersionPolicy.AreEquivalent(package.Version, receipt.InstallerVersion))
        {
            store.Save(receipt with { State = HidHideProvisioningReceiptState.Provisioned, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = package.Version, FailureReason = null });
            AppLog.Info("PrerequisiteSetup", "HidHide failed receipt reconciled from exact installed package evidence.", ("AttemptId", receipt.AttemptId), ("State", HidHideProvisioningReceiptState.Provisioned), ("PackageInstalled", package.Installed), ("PackageVersion", package.Version), ("PrerequisiteStatus", prerequisite.Status));
            return;
        }
        if (receipt.State == HidHideProvisioningReceiptState.InstalledPendingReboot
            && BootSession.HasChangedSince(receipt.StartedAtUtc)
            && package.InspectionSucceeded
            && package.Installed
            && HidHidePackageVersionPolicy.AreEquivalent(package.Version, receipt.InstallerVersion))
        {
            store.Save(receipt with { State = HidHideProvisioningReceiptState.Provisioned, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = package.Version, FailureReason = null });
            return;
        }
        var decision = ProvisioningReconciliationPolicy.Evaluate(package.InspectionSucceeded, package.Installed, package.Version, receipt.InstallerVersion, prerequisite.Status, receipt.State == HidHideProvisioningReceiptState.InstallStarted);
        if (decision.Action == ProvisioningReconciliationAction.Preserve)
        {
            AppLog.Warn("PrerequisiteSetup", "HidHide receipt remains unresolved after reconciliation.", null, ("AttemptId", receipt.AttemptId), ("Reason", decision.Reason), ("ExpectedVersion", receipt.InstallerVersion), ("ObservedVersion", package.Version));
            return;
        }
        var state = decision.Action == ProvisioningReconciliationAction.Provisioned ? HidHideProvisioningReceiptState.Provisioned : HidHideProvisioningReceiptState.InstalledPendingReboot;
        store.Save(receipt with { State = state, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = package.Version, FailureReason = null });
        AppLog.Info("PrerequisiteSetup", "HidHide receipt reconciled.", ("AttemptId", receipt.AttemptId), ("PreviousState", receipt.State), ("State", state), ("PackageInstalled", package.Installed), ("PackageVersion", package.Version), ("PrerequisiteStatus", prerequisite.Status));
    }

    private static void ReconcileUsbIpReceipt(UsbIpWin2ProvisioningReceiptStore store, UsbIpWin2PackageState package, PrerequisiteAssessment prerequisite)
    {
        var loaded = store.Load();
        if (loaded.IsCorrupt) throw new InvalidDataException("The usbip-win2 provisioning receipt is corrupt.");
        if (loaded.Receipt is not { State: UsbIpWin2ProvisioningReceiptState.InstallStarted or UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot or UsbIpWin2ProvisioningReceiptState.AttemptFailed } receipt) return;
        if (receipt.State is UsbIpWin2ProvisioningReceiptState.InstallStarted or UsbIpWin2ProvisioningReceiptState.AttemptFailed
            && package.InspectionSucceeded
            && package.Installed
            && string.Equals(package.Version, receipt.InstallerVersion, StringComparison.OrdinalIgnoreCase))
        {
            store.Save(receipt with { State = UsbIpWin2ProvisioningReceiptState.Provisioned, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = package.Version, FailureReason = null });
            AppLog.Info("PrerequisiteSetup", "usbip-win2 failed receipt reconciled from exact installed package evidence.", ("AttemptId", receipt.AttemptId), ("PackageVersion", package.Version));
            return;
        }
        if (receipt.State == UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot
            && BootSession.HasChangedSince(receipt.StartedAtUtc)
            && package.InspectionSucceeded
            && package.Installed
            && string.Equals(package.Version, receipt.InstallerVersion, StringComparison.OrdinalIgnoreCase))
        {
            store.Save(receipt with { State = UsbIpWin2ProvisioningReceiptState.Provisioned, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = package.Version, FailureReason = null });
            return;
        }
        var decision = ProvisioningReconciliationPolicy.Evaluate(package.InspectionSucceeded, package.Installed, package.Version, receipt.InstallerVersion, prerequisite.Status, receipt.State == UsbIpWin2ProvisioningReceiptState.InstallStarted);
        if (decision.Action == ProvisioningReconciliationAction.Preserve)
        {
            AppLog.Warn("PrerequisiteSetup", "usbip-win2 receipt remains unresolved after reconciliation.", null, ("AttemptId", receipt.AttemptId), ("Reason", decision.Reason), ("ExpectedVersion", receipt.InstallerVersion), ("ObservedVersion", package.Version));
            return;
        }
        var state = decision.Action == ProvisioningReconciliationAction.Provisioned ? UsbIpWin2ProvisioningReceiptState.Provisioned : UsbIpWin2ProvisioningReceiptState.InstalledPendingReboot;
        store.Save(receipt with { State = state, CompletedAtUtc = DateTimeOffset.UtcNow, ObservedInstalledVersion = package.Version, FailureReason = null });
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
            var hardware = CaptureHardwareCompatibility();
            if (!hardware.AllowsMutation) return (false, "Hardware" + hardware.Status);
            var software = new IControllerSoftwareStatusProvider[]
            {
                new MsiCenterMSoftwareStatusProvider(),
                new ClawTweaksSoftwareStatusProvider(new ClawTweaksInstallationProbe(), new ClawTweaksRuntimeDetector()),
                new HandheldCompanionSoftwareStatusProvider(new HandheldCompanionRuntimeDetector())
            };
            var compatibility = new CurrentControllerEnvironmentCompatibilityPolicy().Evaluate(software.Select(provider => provider.Capture()).ToArray());
            if (!compatibility.AllowsMutation) return (false, "Compatibility" + compatibility.Reason);
            var recoverySafety = new MachineRecoverySafetyInspector().Inspect();
            if (!AllowsRecoverySafeProvisioning(recoverySafety)) return (false, recoverySafety.Reason);
            using var runningAppId = new SteamRunningAppIdRegistrySource();
            var settings = new SettingsStore(AddonDataPaths.SettingsPath);
            var probe = new SteamBigPictureWindowProbe();
            return ElevatedSteamSafetyGate.Evaluate(runningAppId.GetRunningAppId, settings.LoadForSafetyGate, probe.Capture);
        }
        catch (Exception exception)
        {
            AppLog.Warn("PrerequisiteSetup", "Prerequisite safety gate inspection failed.", exception, ("Reason", "SafetyGateInspectionFailed"));
            return (false, "SafetyGateInspectionFailed");
        }
    }

    internal static bool AllowsRecoverySafeProvisioning(RecoverySafetyAssessment assessment) => assessment.Status == RecoverySafetyStatus.Safe;

    internal static HardwareCompatibilityAssessment CaptureHardwareCompatibility(
        IWindowsDeviceProbeContextFactory? probeContextFactory = null,
        IHardwareCompatibilityEvaluator? evaluator = null)
    {
        var devices = new WindowsControllerDeviceEnumerator();
        var adapter = new MsiClawDeviceAdapter(devices);
        return (evaluator ?? new HardwareCompatibilityEvaluator(new HandheldDeviceRegistry([adapter])))
            .Evaluate((probeContextFactory ?? new WindowsDeviceProbeContextFactory(new WindowsDeviceIdentitySource(), devices)).Capture());
    }
}
